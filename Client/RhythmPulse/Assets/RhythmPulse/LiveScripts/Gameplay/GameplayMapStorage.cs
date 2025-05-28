using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime; // For FilePathUtility, UnityPathSource, and UnityWebRequestHelper
using RhythmPulse.GameplayData.Runtime; // For MapInfo struct

namespace RhythmPulse.Gameplay
{
    /// <summary>
    /// Manages storage and retrieval of paths for gameplay map assets.
    /// Handles different path sources including StreamingAssets (via manifest),
    /// PersistentDataPath, and absolute file system paths.
    /// </summary>
    public class GameplayMapStorage
    {
        private const string DEBUG_FLAG = "[GameplayMapStorage]";
        public const string MAP_INFO_FILE_NAME = "MapInfo.yaml";
        public const string DEFAULT_STREAMING_ASSETS_MAP_SUBFOLDER = "MusicGameMaps"; // Default subfolder within StreamingAssets.
        public const string STREAMING_ASSETS_MANIFEST_FILENAME = "map_manifest.json";

        /// <summary>
        /// Internal structure for the map manifest JSON file. This MUST match the structure
        /// produced by MusicGameMapManifestGenerator.
        /// </summary>
        [System.Serializable]
        private class MapManifest
        {
            public string basePathInStreamingAssets; // The StreamingAssets subfolder these maps are relative to (e.g., "MusicGameMaps").
            public List<string> mapUniqueIDs;      // List of map folder names (UniqueIDs) within that basePath.
        }

        private readonly List<string> logicalBasePaths = new List<string>();
        private readonly List<UnityPathSource> basePathSources = new List<UnityPathSource>();
        private readonly Dictionary<string, (string songPath, UnityPathSource pathSource)> mapAssetRoots =
            new Dictionary<string, (string, UnityPathSource)>();

        public GameplayMapStorage()
        {
            AddBasePath(DEFAULT_STREAMING_ASSETS_MAP_SUBFOLDER, UnityPathSource.StreamingAssets);
        }

        public void AddBasePath(string logicalPath, UnityPathSource source)
        {
            if (string.IsNullOrEmpty(logicalPath) && source != UnityPathSource.AbsoluteOrFullUri)
            {
                logicalPath = ""; // Normalize for root of SA/PD.
            }

            bool pathExists = false;
            for (int i = 0; i < logicalBasePaths.Count; ++i)
            {
                if (logicalBasePaths[i] == logicalPath && basePathSources[i] == source)
                {
                    pathExists = true;
                    break;
                }
            }

            if (!pathExists)
            {
                logicalBasePaths.Add(logicalPath);
                basePathSources.Add(source);
                CLogger.LogInfo($"{DEBUG_FLAG} Added base path: '{logicalPath}' (Source: {source})");
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Attempted to add duplicate base path: '{logicalPath}' (Source: {source})");
            }
        }

        public async Task UpdatePathDictionaryAsync()
        {
            mapAssetRoots.Clear();
            CLogger.LogInfo($"{DEBUG_FLAG} Starting to update path dictionary...");

            for (int i = 0; i < logicalBasePaths.Count; i++)
            {
                string logicalBasePath = logicalBasePaths[i];
                UnityPathSource currentSource = basePathSources[i];

                if (currentSource == UnityPathSource.StreamingAssets)
                {
                    string manifestRelativePathToSARoot = Path.Combine(logicalBasePath, STREAMING_ASSETS_MANIFEST_FILENAME).Replace(Path.DirectorySeparatorChar, '/');
                    string manifestUri = FilePathUtility.GetUnityWebRequestUri(manifestRelativePathToSARoot, UnityPathSource.StreamingAssets);

                    CLogger.LogInfo($"{DEBUG_FLAG} Attempting to load manifest from URI: {manifestUri} (for SA logical base: '{logicalBasePath}')");

                    using (UnityWebRequest www = UnityWebRequest.Get(manifestUri))
                    {
                        await www.SendWebRequest();

                        if (www.result == UnityWebRequest.Result.Success)
                        {
                            CLogger.LogInfo($"{DEBUG_FLAG} Successfully loaded manifest for SA logical base '{logicalBasePath}'.");
                            try
                            {
                                MapManifest manifestData = JsonUtility.FromJson<MapManifest>(www.downloadHandler.text);
                                if (manifestData != null && manifestData.mapUniqueIDs != null)
                                {
                                    string effectiveBasePathForManifestEntries = manifestData.basePathInStreamingAssets;
                                    if (string.IsNullOrEmpty(effectiveBasePathForManifestEntries))
                                    {
                                        // Fallback if manifest doesn't specify its own base, though it should.
                                        effectiveBasePathForManifestEntries = logicalBasePath;
                                        CLogger.LogWarning($"{DEBUG_FLAG} Manifest for '{logicalBasePath}' has empty 'basePathInStreamingAssets'. Using logicalBasePath '{logicalBasePath}' as effective base.");
                                    }
                                    // Optional: Further validation if effectiveBasePathForManifestEntries drastically differs from logicalBasePath.

                                    foreach (string mapFolderUniqueID in manifestData.mapUniqueIDs)
                                    {
                                        string songDirectoryRelativeToSARoot = Path.Combine(effectiveBasePathForManifestEntries, mapFolderUniqueID).Replace(Path.DirectorySeparatorChar, '/');
                                        if (mapAssetRoots.ContainsKey(mapFolderUniqueID))
                                        {
                                            CLogger.LogWarning($"{DEBUG_FLAG} (Manifest) Duplicate UniqueID '{mapFolderUniqueID}'. Keeping first found entry.");
                                        }
                                        else
                                        {
                                            mapAssetRoots[mapFolderUniqueID] = (songDirectoryRelativeToSARoot, currentSource);
                                            CLogger.LogInfo($"{DEBUG_FLAG} (Manifest) Added map '{mapFolderUniqueID}', SA relative path: '{songDirectoryRelativeToSARoot}'");
                                        }
                                    }
                                }
                                else
                                {
                                    CLogger.LogWarning($"{DEBUG_FLAG} Manifest from '{manifestUri}' parsed but data or mapUniqueIDs list is null.");
                                }
                            }
                            catch (System.Exception ex)
                            {
                                CLogger.LogError($"{DEBUG_FLAG} Failed to parse manifest from '{manifestUri}': {ex.Message}. Content: {www.downloadHandler.text}");
                            }
                        }
                        else
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Failed to load manifest from '{manifestUri}': {www.error}");
                        }
                    }
                }
                else // PersistentData or AbsoluteOrFullUri
                {
                    string actualSystemBasePathToScan;
                    if (currentSource == UnityPathSource.PersistentData)
                    {
                        actualSystemBasePathToScan = Path.Combine(Application.persistentDataPath, logicalBasePath);
                    }
                    else // AbsoluteOrFullUri
                    {
                        actualSystemBasePathToScan = logicalBasePath;
                    }

                    CLogger.LogInfo($"{DEBUG_FLAG} Scanning with System.IO: '{actualSystemBasePathToScan}' (Logical: '{logicalBasePath}', Source: {currentSource})");
                    if (!Directory.Exists(actualSystemBasePathToScan))
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} System.IO Base path '{actualSystemBasePathToScan}' does not exist. Skipping.");
                        continue;
                    }

                    string[] songDirectories;
                    try { songDirectories = Directory.GetDirectories(actualSystemBasePathToScan); }
                    catch (System.Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error System.IO scanning '{actualSystemBasePathToScan}': {ex.Message}"); continue; }

                    foreach (string songDirPathAbsolute in songDirectories)
                    {
                        string mapInfoFilePath = Path.Combine(songDirPathAbsolute, MAP_INFO_FILE_NAME);
                        if (File.Exists(mapInfoFilePath))
                        {
                            string uniqueID = Path.GetFileName(songDirPathAbsolute);
                            if (string.IsNullOrEmpty(uniqueID)) { CLogger.LogWarning($"{DEBUG_FLAG} Could not determine UniqueID from path '{songDirPathAbsolute}'. Skipping."); continue; }

                            if (mapAssetRoots.ContainsKey(uniqueID))
                            {
                                CLogger.LogWarning($"{DEBUG_FLAG} (System.IO) Duplicate UniqueID '{uniqueID}' found at '{songDirPathAbsolute}'. Keeping first found entry.");
                            }
                            else
                            {
                                mapAssetRoots[uniqueID] = (songDirPathAbsolute, currentSource);
                                CLogger.LogInfo($"{DEBUG_FLAG} (System.IO) Added map '{uniqueID}', absolute path: '{songDirPathAbsolute}'");
                            }
                        }
                    }
                }
            }
            CLogger.LogInfo($"{DEBUG_FLAG} Path dictionary update finished. Found {mapAssetRoots.Count} maps.");
        }

        private string GetSpecificAssetPathInternal(in MapInfo mapInfo, string assetFilenameWithExtension)
        {
            if (string.IsNullOrEmpty(assetFilenameWithExtension)) return string.Empty;

            if (mapAssetRoots.TryGetValue(mapInfo.UniqueID, out var rootInfo))
            {
                string pathForFilePathUtility;
                UnityPathSource sourceForFilePathUtility = rootInfo.pathSource;

                if (sourceForFilePathUtility == UnityPathSource.StreamingAssets)
                {
                    pathForFilePathUtility = Path.Combine(rootInfo.songPath, assetFilenameWithExtension).Replace(Path.DirectorySeparatorChar, '/');
                }
                else
                {
                    string fullAbsoluteAssetSystemPath = Path.Combine(rootInfo.songPath, assetFilenameWithExtension);
                    if (!File.Exists(fullAbsoluteAssetSystemPath))
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Asset file '{fullAbsoluteAssetSystemPath}' (map '{mapInfo.UniqueID}', asset '{assetFilenameWithExtension}') not found on disk.");
                        return string.Empty;
                    }

                    if (sourceForFilePathUtility == UnityPathSource.PersistentData)
                    {
                        string persistentDataRoot = Application.persistentDataPath;
                        if (!fullAbsoluteAssetSystemPath.StartsWith(persistentDataRoot, System.StringComparison.OrdinalIgnoreCase))
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Asset path '{fullAbsoluteAssetSystemPath}' for map '{mapInfo.UniqueID}' is not under PersistentData root '{persistentDataRoot}'.");
                            return string.Empty;
                        }
                        pathForFilePathUtility = fullAbsoluteAssetSystemPath.Substring(persistentDataRoot.Length)
                                                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    else // AbsoluteOrFullUri
                    {
                        pathForFilePathUtility = fullAbsoluteAssetSystemPath;
                    }
                }
                return FilePathUtility.GetUnityWebRequestUri(pathForFilePathUtility, sourceForFilePathUtility);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map with UniqueID '{mapInfo.UniqueID}' not found for asset '{assetFilenameWithExtension}'. Call UpdatePathDictionaryAsync().");
                return string.Empty;
            }
        }

        private string GetAssetPathWithPotentialExtension(in MapInfo mapInfo, string baseAssetFileNameInYaml, string expectedExtension)
        {
            if (string.IsNullOrEmpty(baseAssetFileNameInYaml)) return string.Empty;
            string assetFilenameWithExtension = Path.HasExtension(baseAssetFileNameInYaml) ? baseAssetFileNameInYaml : string.Concat(baseAssetFileNameInYaml, expectedExtension);
            return GetSpecificAssetPathInternal(mapInfo, assetFilenameWithExtension);
        }

        public string GetAudioPath(in MapInfo mapInfo) => GetAssetPathWithPotentialExtension(mapInfo, mapInfo.AudioFile, ".ogg");
        public string GetVideoPath(in MapInfo mapInfo) => GetAssetPathWithPotentialExtension(mapInfo, mapInfo.VideoFile, ".mp4");
        public string GetPreviewAudioPath(in MapInfo mapInfo) => GetAssetPathWithPotentialExtension(mapInfo, mapInfo.PreviewAudioFile, ".ogg");
        public string GetPreviewVideoPath(in MapInfo mapInfo) => GetAssetPathWithPotentialExtension(mapInfo, mapInfo.PreviewVideoFile, ".mp4");

        public string[] GetBackgroundPictures(in MapInfo mapInfo)
        {
            if (mapInfo.InGameBackgroundPictures == null || mapInfo.InGameBackgroundPictures.Length == 0) return System.Array.Empty<string>();
            if (!mapAssetRoots.ContainsKey(mapInfo.UniqueID))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map with UniqueID '{mapInfo.UniqueID}' not found for background pictures.");
                return System.Array.Empty<string>();
            }

            string[] bgPicUris = new string[mapInfo.InGameBackgroundPictures.Length];
            for (int i = 0; i < mapInfo.InGameBackgroundPictures.Length; i++)
            {
                string bgFileNameInYaml = mapInfo.InGameBackgroundPictures[i];
                bgPicUris[i] = GetSpecificAssetPathInternal(mapInfo, bgFileNameInYaml); // Assuming BG filenames have extensions
                if (string.IsNullOrEmpty(bgPicUris[i]))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Failed to get URI for BG picture '{bgFileNameInYaml}' in map '{mapInfo.UniqueID}'.");
                }
            }
            return bgPicUris;
        }

        public List<string> GetAllMapUniqueIDs() => new List<string>(mapAssetRoots.Keys);

        public bool TryGetMapPathInfo(string uniqueID, out string path, out UnityPathSource source)
        {
            if (mapAssetRoots.TryGetValue(uniqueID, out var rootInfo))
            {
                path = rootInfo.songPath;
                source = rootInfo.pathSource;
                return true;
            }
            path = null;
            source = default;
            return false;
        }
    }
}