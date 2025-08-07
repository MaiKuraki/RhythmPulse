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

        /// <summary>
        /// Gets a list of available maps, excluding any map that is exclusively of the specified beatmap type.
        /// </summary>
        /// <param name="beatMapTypeToExclude">The single beatmap type to exclude.</param>
        /// <returns>A read-only list of maps that are not exclusively of the specified type.</returns>
        IReadOnlyList<MapInfo> GetAvailableMapsExcludingType(string beatMapTypeToExclude);
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
        // Global cache for fast lookups that do not distinguish by BeatMapType.
        private readonly Dictionary<string, List<MapInfo>> _globalMapsByVocalist = new();
        private readonly Dictionary<int, List<MapInfo>> _globalMapsByDifficulty = new();
        // Hierarchical cache for secondary filtering after a BeatMapType lookup.
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
        /// Gets a list of maps, excluding those that are *only* of a specific type.
        /// Maps that contain the excluded type alongside other types will still be included.
        /// </summary>
        /// <param name="beatMapTypeToExclude">The beatmap type to check for exclusion.</param>
        public IReadOnlyList<MapInfo> GetAvailableMapsExcludingType(string beatMapTypeToExclude)
        {
            if (string.IsNullOrEmpty(beatMapTypeToExclude))
            {
                return _availableMaps; // If no type is specified to exclude, return all maps.
            }

            var filteredMaps = new List<MapInfo>();
            var uniqueTypesForMap = new HashSet<string>();

            foreach (var map in _availableMaps)
            {
                uniqueTypesForMap.Clear();
                if (map.BeatmapDifficultyFiles != null)
                {
                    foreach (var difficultyInfo in map.BeatmapDifficultyFiles)
                    {
                        if (difficultyInfo.BeatMapType != null)
                        {
                            foreach (string type in difficultyInfo.BeatMapType)
                            {
                                uniqueTypesForMap.Add(type);
                            }
                        }
                    }
                }

                // If the map has only one type and that type is the one we want to exclude,
                // then we skip this map. Otherwise, we add it.
                if (uniqueTypesForMap.Count == 1 && uniqueTypesForMap.Contains(beatMapTypeToExclude))
                {
                    continue; // Skip this map as it is exclusively the type to be excluded.
                }

                filteredMaps.Add(map);
            }

            return filteredMaps;
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

        /// <summary>
        /// Builds all filter caches in a single pass for improved performance.
        /// This method iterates through each map once, populating global and hierarchical caches simultaneously.
        /// </summary>
        private void PrecomputeAllFilterCaches()
        {
            var uniqueDifficultiesForMap = new HashSet<int>();

            foreach (var map in _availableMaps)
            {
                // Populate global vocalist cache
                if (!string.IsNullOrEmpty(map.Vocalist))
                {
                    if (!_globalMapsByVocalist.TryGetValue(map.Vocalist, out var globalVocalistList))
                    {
                        globalVocalistList = new List<MapInfo>();
                        _globalMapsByVocalist[map.Vocalist] = globalVocalistList;
                    }
                    globalVocalistList.Add(map);
                }

                if (map.BeatmapDifficultyFiles == null) continue;

                uniqueDifficultiesForMap.Clear();

                // Process all difficulties and types for the current map
                foreach (var difficultyInfo in map.BeatmapDifficultyFiles)
                {
                    uniqueDifficultiesForMap.Add(difficultyInfo.Difficulty);

                    if (difficultyInfo.BeatMapType == null) continue;

                    foreach (string type in difficultyInfo.BeatMapType)
                    {
                        // Get or create the hierarchical cache for this type
                        if (!_mapsCache.TryGetValue(type, out var typeCache))
                        {
                            typeCache = new BeatMapTypeCache();
                            _mapsCache[type] = typeCache;
                        }

                        // Add to this type's difficulty-specific list
                        if (!typeCache.MapsByDifficulty.TryGetValue(difficultyInfo.Difficulty, out var mapsForDifficulty))
                        {
                            mapsForDifficulty = new List<MapInfo>();
                            typeCache.MapsByDifficulty[difficultyInfo.Difficulty] = mapsForDifficulty;
                        }
                        
                        // A map should only be in a specific difficulty list once. A check prevents
                        // issues if data is structured unexpectedly, though it's often redundant.
                        if (!mapsForDifficulty.Contains(map))
                        {
                            mapsForDifficulty.Add(map);
                        }
                    }
                }

                // Populate global difficulty cache using the collected unique difficulties
                foreach (var difficulty in uniqueDifficultiesForMap)
                {
                    if (!_globalMapsByDifficulty.TryGetValue(difficulty, out var globalDifficultyList))
                    {
                        globalDifficultyList = new List<MapInfo>();
                        _globalMapsByDifficulty[difficulty] = globalDifficultyList;
                    }
                    globalDifficultyList.Add(map);
                }
            }

            // Post-process to build the remaining hierarchical caches (AllMaps, MapsByVocalist)
            // This is done after the main loop to avoid adding the same map multiple times to these lists.
            foreach (var kvp in _mapsCache)
            {
                var typeCache = kvp.Value;
                var uniqueMapsInCache = new HashSet<MapInfo>();

                // Gather all unique maps for this type from the difficulty lists we just built
                foreach (var mapList in typeCache.MapsByDifficulty.Values)
                {
                    uniqueMapsInCache.UnionWith(mapList);
                }
                
                // Now populate the AllMaps list and the vocalist sub-cache from the unique set
                typeCache.AllMaps.AddRange(uniqueMapsInCache);
                
                foreach (var map in uniqueMapsInCache)
                {
                    if (!string.IsNullOrEmpty(map.Vocalist))
                    {
                        if (!typeCache.MapsByVocalist.TryGetValue(map.Vocalist, out var mapsForVocalist))
                        {
                            mapsForVocalist = new List<MapInfo>();
                            typeCache.MapsByVocalist[map.Vocalist] = mapsForVocalist;
                        }
                        mapsForVocalist.Add(map);
                    }
                }
            }
        }

        #region File Loading

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