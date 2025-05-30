using Cysharp.Threading.Tasks;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime;
using RhythmPulse.GameplayData.Runtime;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.Networking; // For UnityWebRequest, required for LoadBytesFromUriAsync
using UnityEngine; // For Application.exitCancellationToken and Path.HasExtension
using System.IO;
using System;

namespace RhythmPulse.Gameplay
{
    public interface IGameplayMapListManager
    {
        IReadOnlyList<MapInfo> AvailableMaps { get; }
        event Action OnMapsLoaded;
        bool IsLoading { get; }
        bool IsLoaded { get; }

        UniTask LoadAllMapsAsync(CancellationToken cancellationToken);
    }

    public class GameplayMapListManager : IGameplayMapListManager
    {
        private const string DEBUG_FLAG = "[GameplayMapListManager]";
        private readonly IGameplayMapStorage gameplayMapStorage;

        // Internal mutable list for maps
        private List<MapInfo> _availableMaps = new List<MapInfo>();
        // Public read-only view of the maps
        public IReadOnlyList<MapInfo> AvailableMaps => _availableMaps;

        // New: Event and backing fields for loading status
        public event Action OnMapsLoaded;
        public bool IsLoading { get; private set; }
        public bool IsLoaded { get; private set; }

        public GameplayMapListManager(IGameplayMapStorage gameplayMapStorage)
        {
            this.gameplayMapStorage = gameplayMapStorage;
            IsLoading = false; // Initial state
            IsLoaded = false;  // Initial state
        }

        public async UniTask LoadAllMapsAsync(CancellationToken cancellationToken)
        {
            if (gameplayMapStorage == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} GameplayMapListManager not initialized with IGameplayMapStorage. This should be handled by VContainer's dependency injection.");
                IsLoading = false;
                IsLoaded = false;
                return;
            }

            if (IsLoading)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map loading already in progress.");
                return; // Prevent multiple concurrent loading operations
            }

            IsLoading = true; // Set loading flag
            IsLoaded = false; // Reset loaded flag if re-loading
            _availableMaps.Clear(); // Clear existing maps before starting fresh load

            CLogger.LogInfo($"{DEBUG_FLAG} Starting map loading process...");

            try
            {
                await gameplayMapStorage.UpdatePathDictionaryAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Map loading cancelled during path dictionary update.");
                    return;
                }

                await PopulateAvailableMapsListAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Map loading cancelled after populating map list.");
                    return;
                }

                IsLoaded = true; // Mark as loaded on success
                CLogger.LogInfo($"{DEBUG_FLAG} Map loading finished. Total maps found: {_availableMaps.Count}");
                OnMapsLoaded?.Invoke(); // Notify subscribers
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Map loading failed: {ex.Message}");
                IsLoaded = false; // Ensure it's not marked as loaded on failure
                                  // Consider adding an OnMapsLoadFailed event here if specific error handling is needed by consumers.
            }
            finally
            {
                IsLoading = false; // Always clear loading flag
            }
        }

        private async UniTask PopulateAvailableMapsListAsync(CancellationToken cancellationToken)
        {
            // AvailableMaps.Clear() is already called at the start of LoadAllMapsAsync
            var mapIDs = gameplayMapStorage.GetAllMapUniqueIDs();
            CLogger.LogInfo($"{DEBUG_FLAG} Found {mapIDs.Count} potential map IDs. Loading MapInfo for each...");

            foreach (string id in mapIDs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} MapInfo loading cancelled for ID: {id}.");
                    break;
                }

                if (gameplayMapStorage.TryGetMapPathInfo(id, out string mapRootPath, out UnityPathSource source))
                {
                    string mapInfoFilePath;

                    if (source == UnityPathSource.StreamingAssets)
                    {
                        mapInfoFilePath = Path.Combine(mapRootPath, GameplayMapStorage.MAP_INFO_FILE_NAME).Replace(Path.DirectorySeparatorChar, '/');
                    }
                    else
                    {
                        mapInfoFilePath = Path.Combine(mapRootPath, GameplayMapStorage.MAP_INFO_FILE_NAME);
                    }

                    CLogger.LogInfo($"{DEBUG_FLAG} Attempting to load MapInfo for ID '{id}' from '{mapInfoFilePath}' (Source: {source})");

                    byte[] yamlBytes = null;
                    if (source == UnityPathSource.StreamingAssets)
                    {
                        string uri = FilePathUtility.GetUnityWebRequestUri(mapInfoFilePath, source);
                        yamlBytes = await LoadBytesFromUriAsync(uri, cancellationToken);
                    }
                    else
                    {
#if UNITY_WEBGL && !UNITY_EDITOR
                        CLogger.LogWarning($"{DEBUG_FLAG} Direct System.IO access for MapInfo not supported on WebGL builds. Skipping map ID '{id}' from path '{mapInfoFilePath}'.");
#else
                        try
                        {
                            if (System.IO.File.Exists(mapInfoFilePath))
                            {
                                yamlBytes = await System.IO.File.ReadAllBytesAsync(mapInfoFilePath, cancellationToken);
                            }
                            else
                            {
                                CLogger.LogError($"{DEBUG_FLAG} MapInfo file not found at absolute path: {mapInfoFilePath}");
                            }
                        }
                        catch (System.OperationCanceledException)
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Reading MapInfo bytes for ID '{id}' was cancelled.");
                            continue;
                        }
                        catch (System.Exception ex)
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Error reading MapInfo from {mapInfoFilePath}: {ex.Message}");
                        }
#endif
                    }

                    if (yamlBytes != null && yamlBytes.Length > 0)
                    {
                        try
                        {
                            MapInfo mapInfo = VYaml.Serialization.YamlSerializer.Deserialize<MapInfo>(yamlBytes);
                            mapInfo.UniqueID = id;
                            _availableMaps.Add(mapInfo);
                            CLogger.LogInfo($"{DEBUG_FLAG} Successfully loaded MapInfo for '{mapInfo.DisplayName ?? "N/A"}' (ID: {id})");
                        }
                        catch (System.Exception ex)
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Failed to parse MapInfo for ID '{id}' with VYaml: {ex.Message}. Raw content snippet: {System.Text.Encoding.UTF8.GetString(yamlBytes, 0, Mathf.Min(yamlBytes.Length, 200))}");
                        }
                    }
                    else if (yamlBytes == null)
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Failed to load bytes for MapInfo.yaml for ID '{id}' from path '{mapInfoFilePath}'.");
                    }
                }
                else
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Could not get path info for map ID '{id}'. Skipping MapInfo loading.");
                }
            }
        }

        private async UniTask<byte[]> LoadBytesFromUriAsync(string uri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(uri))
            {
                CLogger.LogError($"{DEBUG_FLAG} URI for LoadBytesAsync is null or empty.");
                return null;
            }
            using (UnityWebRequest www = UnityWebRequest.Get(uri))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Loading bytes from URI: {uri}");
                try
                {
                    await www.SendWebRequest().ToUniTask(null, PlayerLoopTiming.Update, cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Loading from {uri} was cancelled.");
                    return null;
                }
                catch (UnityWebRequestException ex)
                {
                    CLogger.LogError($"{DEBUG_FLAG} UnityWebRequest failed for {uri}: {www.error} (Code: {www.responseCode}). Details: {ex.Message}");
                    return null;
                }
                catch (System.Exception ex)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Unexpected error during UnityWebRequest for {uri}: {ex.Message}");
                    return null;
                }

                if (www.result == UnityWebRequest.Result.Success)
                {
                    return www.downloadHandler.data;
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load from {uri}: {www.error}");
                    return null;
                }
            }
        }
    }
}