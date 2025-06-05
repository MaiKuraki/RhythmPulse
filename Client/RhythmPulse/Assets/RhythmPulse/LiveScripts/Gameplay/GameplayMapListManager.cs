using Cysharp.Threading.Tasks;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime;
using RhythmPulse.GameplayData.Runtime;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.Networking;
using System.IO;
using System;
using VYaml.Serialization;

namespace RhythmPulse.Gameplay
{
    public interface IGameplayMapListManager
    {
        IReadOnlyList<MapInfo> AvailableMaps { get; }
        event Action OnMapsLoaded;
        bool IsLoading { get; }
        bool IsLoaded { get; }

        UniTask LoadAllMapsAsync(CancellationToken cancellationToken);

        // --- Global Filter Methods (All Modes) ---
        IReadOnlyList<MapInfo> GetAvailableMapsByVocalist(string vocalist);
        IReadOnlyList<MapInfo> GetAvailableMapsByDifficulty(int difficulty);

        // --- Hierarchical Filter Methods (Mode First) ---
        IReadOnlyList<MapInfo> GetAvailableMapsByBeatMapType(string beatMapType);
        IReadOnlyList<MapInfo> GetAvailableMapsByVocalist(string beatMapType, string vocalist);
        IReadOnlyList<MapInfo> GetAvailableMapsByDifficulty(string beatMapType, int difficulty);
    }

    public class GameplayMapListManager : IGameplayMapListManager
    {
        private const string DEBUG_FLAG = "[GameplayMapListManager]";
        private readonly IGameplayMapStorage gameplayMapStorage;

        /// <summary>
        /// Contains caches that are further sorted by BeatMapType.
        /// </summary>
        private class BeatMapTypeCache
        {
            public readonly List<MapInfo> AllMaps = new();
            public readonly Dictionary<string, List<MapInfo>> MapsByVocalist = new();
            public readonly Dictionary<int, List<MapInfo>> MapsByDifficulty = new();
        }

        private readonly List<MapInfo> _availableMaps = new();
        private static readonly IReadOnlyList<MapInfo> EmptyMapList = Array.Empty<MapInfo>();

        // --- Cache Structures ---
        // 1. Global cache for fast lookups that do not distinguish by BeatMapType.
        private readonly Dictionary<string, List<MapInfo>> _globalMapsByVocalist = new();
        private readonly Dictionary<int, List<MapInfo>> _globalMapsByDifficulty = new();
        // 2. Hierarchical cache for secondary filtering after a BeatMapType lookup.
        private readonly Dictionary<string, BeatMapTypeCache> _mapsCache = new();

        public IReadOnlyList<MapInfo> AvailableMaps => _availableMaps;
        public event Action OnMapsLoaded;
        public bool IsLoading { get; private set; }
        public bool IsLoaded { get; private set; }

        public GameplayMapListManager(IGameplayMapStorage gameplayMapStorage)
        {
            this.gameplayMapStorage = gameplayMapStorage ?? throw new ArgumentNullException(nameof(gameplayMapStorage));
        }

        public async UniTask LoadAllMapsAsync(CancellationToken cancellationToken)
        {
            if (IsLoading)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map loading is already in progress.");
                return;
            }

            IsLoading = true;
            IsLoaded = false;

            // Clear all caches
            _availableMaps.Clear();
            _mapsCache.Clear();
            _globalMapsByVocalist.Clear();
            _globalMapsByDifficulty.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} Starting map loading process...");

            try
            {
                await gameplayMapStorage.UpdatePathDictionaryAsync(true, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await PopulateAvailableMapsListAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                PrecomputeAllFilterCaches();

                IsLoaded = true;
                CLogger.LogInfo($"{DEBUG_FLAG} Map loading finished. Total maps: {_availableMaps.Count}. All filter caches generated.");
                OnMapsLoaded?.Invoke();
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map loading was cancelled.");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} An error occurred during map loading: {ex.Message}");
                IsLoaded = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        #region Public Getters

        /// <summary>
        /// [Global] Gets available maps across all modes by vocalist.
        /// </summary>
        public IReadOnlyList<MapInfo> GetAvailableMapsByVocalist(string vocalist)
        {
            return _globalMapsByVocalist.TryGetValue(vocalist, out var maps) ? maps : EmptyMapList;
        }

        /// <summary>
        /// [Global] Gets available maps across all modes by difficulty.
        /// </summary>
        public IReadOnlyList<MapInfo> GetAvailableMapsByDifficulty(int difficulty)
        {
            return _globalMapsByDifficulty.TryGetValue(difficulty, out var maps) ? maps : EmptyMapList;
        }

        /// <summary>
        /// [Hierarchical] Gets all available maps for a specific game mode.
        /// </summary>
        public IReadOnlyList<MapInfo> GetAvailableMapsByBeatMapType(string beatMapType)
        {
            return _mapsCache.TryGetValue(beatMapType, out var cache) ? cache.AllMaps : EmptyMapList;
        }

        /// <summary>
        /// [Hierarchical] Gets all maps for a specific game mode, sung by a specific vocalist.
        /// </summary>
        public IReadOnlyList<MapInfo> GetAvailableMapsByVocalist(string beatMapType, string vocalist)
        {
            if (_mapsCache.TryGetValue(beatMapType, out var cache))
            {
                return cache.MapsByVocalist.TryGetValue(vocalist, out var maps) ? maps : EmptyMapList;
            }
            return EmptyMapList;
        }

        /// <summary>
        /// [Hierarchical] Gets all maps for a specific game mode with a specific difficulty.
        /// </summary>
        public IReadOnlyList<MapInfo> GetAvailableMapsByDifficulty(string beatMapType, int difficulty)
        {
            if (_mapsCache.TryGetValue(beatMapType, out var cache))
            {
                return cache.MapsByDifficulty.TryGetValue(difficulty, out var maps) ? maps : EmptyMapList;
            }
            return EmptyMapList;
        }

        #endregion

        private void PrecomputeAllFilterCaches()
        {
            var tempMapsByType = new Dictionary<string, HashSet<MapInfo>>();
            var uniqueDifficultiesForMap = new HashSet<int>();

            // --- Pass 1: Populate global caches and the temporary hierarchical cache ---
            foreach (var map in _availableMaps)
            {
                // 1. Populate the global vocalist cache
                if (!string.IsNullOrEmpty(map.Vocalist))
                {
                    if (!_globalMapsByVocalist.TryGetValue(map.Vocalist, out var list))
                    {
                        list = new List<MapInfo>();
                        _globalMapsByVocalist[map.Vocalist] = list;
                    }
                    list.Add(map);
                }

                if (map.BeatmapDifficultyFiles == null) continue;

                uniqueDifficultiesForMap.Clear();

                // 2. Populate the temporary hierarchical cache (by BeatMapType) and collect the map's unique difficulties
                foreach (var difficultyInfo in map.BeatmapDifficultyFiles)
                {
                    uniqueDifficultiesForMap.Add(difficultyInfo.Difficulty);
                    if (difficultyInfo.BeatMapType == null) continue;

                    foreach (string type in difficultyInfo.BeatMapType)
                    {
                        if (!tempMapsByType.TryGetValue(type, out var mapSet))
                        {
                            mapSet = new HashSet<MapInfo>();
                            tempMapsByType[type] = mapSet;
                        }
                        mapSet.Add(map);
                    }
                }

                // 3. Populate the global difficulty cache
                foreach (int difficulty in uniqueDifficultiesForMap)
                {
                    if (!_globalMapsByDifficulty.TryGetValue(difficulty, out var list))
                    {
                        list = new List<MapInfo>();
                        _globalMapsByDifficulty[difficulty] = list;
                    }
                    list.Add(map);
                }
            }

            // --- Pass 2: Build the final hierarchical cache structure ---
            foreach (var kvp in tempMapsByType)
            {
                string beatMapType = kvp.Key;
                var mapsForThisType = kvp.Value;

                var newCache = new BeatMapTypeCache();
                _mapsCache[beatMapType] = newCache;

                newCache.AllMaps.AddRange(mapsForThisType);

                // Within this BeatMapType's subset, categorize again by vocalist and difficulty
                foreach (var map in newCache.AllMaps)
                {
                    if (!string.IsNullOrEmpty(map.Vocalist))
                    {
                        if (!newCache.MapsByVocalist.TryGetValue(map.Vocalist, out var list))
                        {
                            list = new List<MapInfo>();
                            newCache.MapsByVocalist[map.Vocalist] = list;
                        }
                        list.Add(map);
                    }

                    foreach (var difficultyInfo in map.BeatmapDifficultyFiles)
                    {
                        if (difficultyInfo.BeatMapType != null && Array.Exists(difficultyInfo.BeatMapType, t => t == beatMapType))
                        {
                            if (!newCache.MapsByDifficulty.TryGetValue(difficultyInfo.Difficulty, out var list))
                            {
                                list = new List<MapInfo>();
                                newCache.MapsByDifficulty[difficultyInfo.Difficulty] = list;
                            }
                            if (!list.Contains(map))
                            {
                                list.Add(map);
                            }
                        }
                    }
                }
            }
        }

        #region File Loading (No changes needed here)

        private async UniTask PopulateAvailableMapsListAsync(CancellationToken cancellationToken)
        {
            var mapIDs = gameplayMapStorage.GetAllMapUniqueIDs();

            foreach (string id in mapIDs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!gameplayMapStorage.TryGetMapPathInfo(id, out string mapRootPath, out UnityPathSource source))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Could not get path info for map ID '{id}'.");
                    continue;
                }

                string mapInfoFileName = GameplayMapStorage.MAP_INFO_FILE_NAME;
                byte[] yamlBytes = null;

                try
                {
                    if (source == UnityPathSource.StreamingAssets)
                    {
                        string relativePath = Path.Combine(mapRootPath, mapInfoFileName).Replace('\\', '/');
                        string uri = FilePathUtility.GetUnityWebRequestUri(relativePath, source);
                        yamlBytes = await LoadBytesViaWebRequestAsync(uri, cancellationToken);
                    }
                    else
                    {
#if UNITY_WEBGL && !UNITY_EDITOR
                        CLogger.LogWarning($"{DEBUG_FLAG} Direct file access is not supported on WebGL. Skipping non-StreamingAssets map: {id}");
                        continue;
#else
                        string absolutePath = Path.Combine(mapRootPath, mapInfoFileName);
                        if (File.Exists(absolutePath))
                        {
                            yamlBytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
                        }
                        else
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} MapInfo file not found at path: {absolutePath}");
                        }
#endif
                    }

                    if (yamlBytes != null && yamlBytes.Length > 0)
                    {
                        var mapInfo = YamlSerializer.Deserialize<MapInfo>(yamlBytes);
                        mapInfo.UniqueID = id;
                        _availableMaps.Add(mapInfo);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load or parse MapInfo for ID '{id}'. Reason: {ex.Message}");
                }
            }
        }

        private async UniTask<byte[]> LoadBytesViaWebRequestAsync(string uri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(uri)) return null;

            using (var www = UnityWebRequest.Get(uri))
            {
                await www.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
                return www.result == UnityWebRequest.Result.Success ? www.downloadHandler.data : null;
            }
        }

        #endregion
    }
}