using VContainer;
using VContainer.Unity;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Gameplay;
using Cysharp.Threading.Tasks;
using CycloneGames.Utility.Runtime;
using CycloneGames.Logger;
using RhythmPulse.GameplayData.Runtime;
using System.Collections.Generic;
using System.Threading;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeLobbyScene : SceneBaseLifetimeScope
    {
        private const string DEBUG_FLAG = "[LifetimeScopeLobbyScene]";
        IGameplayMapStorage mapStorage;
        public List<MapInfo> AvailableMaps { get; private set; } = new List<MapInfo>(); // TODO: maybe keep it as a data model in singleton?

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleLobbyScene>();

            mapStorage = Parent.Container.Resolve<IGameplayMapStorage>();

            mapStorage.AddBasePath("DownloadedMap", UnityPathSource.PersistentData);
            InitializeMapListAsync(UnityEngine.Application.exitCancellationToken).Forget();
        }

        public async UniTask InitializeMapListAsync(CancellationToken cancellationToken)
        {
            await mapStorage.UpdatePathDictionaryAsync();
            if (cancellationToken.IsCancellationRequested) return;
            await LoadAllMapInfosAsync();
            CLogger.LogInfo($"{DEBUG_FLAG} Loaded {AvailableMaps.Count} maps.");
        }

        private async UniTask LoadAllMapInfosAsync()
        {
            AvailableMaps.Clear();
            var mapIDs = mapStorage.GetAllMapUniqueIDs();
            CLogger.LogInfo($"{DEBUG_FLAG} Found {mapIDs.Count} potential map IDs. Loading MapInfo for each...");

            foreach (string id in mapIDs)
            {
                if (mapStorage.TryGetMapPathInfo(id, out string songPath, out UnityPathSource source))
                {
                    // Construct the path to MapInfo.yaml within the song's folder.
                    // songPath is relative for SA, absolute for PD/Absolute.
                    string mapInfoFileRelativeOrAbsolute = System.IO.Path.Combine(songPath, GameplayMapStorage.MAP_INFO_FILE_NAME);

                    if (source == UnityPathSource.StreamingAssets)
                    {
                        mapInfoFileRelativeOrAbsolute = mapInfoFileRelativeOrAbsolute.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                    }
                    CLogger.LogInfo($"{DEBUG_FLAG} Attempting to load MapInfo for ID '{id}' from '{mapInfoFileRelativeOrAbsolute}' (Source: {source})");

                    byte[] yamlBytes = null;
                    if (source == UnityPathSource.StreamingAssets)
                    {
                        // mapInfoFileRelativeOrAbsolute is already the correct relative path for FilePathUtility here.
                        string uri = FilePathUtility.GetUnityWebRequestUri(mapInfoFileRelativeOrAbsolute, source);
                        yamlBytes = await LoadBytesFromUriAsync(uri);
                    }
                    else // PersistentData or AbsoluteOrFullUri
                    {
                        try
                        {
                            if (System.IO.File.Exists(mapInfoFileRelativeOrAbsolute))
                            {
                                // For potentially large files or many files, consider File.ReadAllBytesAsync if available/needed.
                                yamlBytes = await System.IO.File.ReadAllBytesAsync(mapInfoFileRelativeOrAbsolute);
                            }
                            else
                            {
                                CLogger.LogError($"{DEBUG_FLAG} MapInfo file not found at absolute path: {mapInfoFileRelativeOrAbsolute}");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Error reading MapInfo from {mapInfoFileRelativeOrAbsolute}: {ex.Message}");
                        }
                    }

                    if (yamlBytes != null && yamlBytes.Length > 0)
                    {
                        try
                        {
                            MapInfo mapInfo = VYaml.Serialization.YamlSerializer.Deserialize<MapInfo>(yamlBytes);

                            mapInfo.UniqueID = id; // Ensure UniqueID from folder name overrides/sets it.

                            AvailableMaps.Add(mapInfo);
                            CLogger.LogInfo($"{DEBUG_FLAG} Loaded MapInfo for '{mapInfo.DisplayName ?? "N/A"}' (ID: {id})");
                        }
                        catch (System.Exception ex)
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Failed to parse MapInfo for ID '{id}' with VYaml: {ex.Message}");
                        }
                    }
                    else if (yamlBytes == null) // Only log if bytes are null, not if empty (though empty YAML is invalid for MapInfo).
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Failed to load bytes for MapInfo.yaml for ID '{id}' from path '{mapInfoFileRelativeOrAbsolute}'.");
                    }
                }
                else
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Could not get path info for map ID '{id}'.");
                }
            }
        }

        private async UniTask<byte[]> LoadBytesFromUriAsync(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                CLogger.LogError($"{DEBUG_FLAG} URI for LoadBytesAsync is null or empty.");
                return null;
            }
            using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequest.Get(uri))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Loading bytes from URI: {uri}");
                await www.SendWebRequest().ToUniTask(null, PlayerLoopTiming.Update, UnityEngine.Application.exitCancellationToken);
                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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