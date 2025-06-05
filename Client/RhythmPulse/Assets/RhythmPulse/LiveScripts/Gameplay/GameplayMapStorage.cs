using UnityEngine.Networking;
using System.Collections.Generic;
using System.IO;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime;     // For FilePathUtility, UnityPathSource
using RhythmPulse.GameplayData.Runtime; // For MapInfo struct
using Cysharp.Threading.Tasks;
using UnityEngine;
using System.Threading;

namespace RhythmPulse.Gameplay
{
    public interface IGameplayMapStorage
    {
        void AddBasePath(string logicalPath, UnityPathSource source);
        UniTask UpdatePathDictionaryAsync(bool clearExisting = true, CancellationToken cancellationToken = default); // Added option to not clear

        string GetAudioPath(in MapInfo mapInfo);
        string GetVideoPath(in MapInfo mapInfo);
        string GetPreviewAudioPath(in MapInfo mapInfo);
        string GetPreviewVideoPath(in MapInfo mapInfo);
        string[] GetBackgroundPictures(in MapInfo mapInfo);

        IReadOnlyList<string> GetAllMapUniqueIDs(); // Changed to IReadOnlyList for less GC
        bool TryGetMapPathInfo(string uniqueID, out string path, out UnityPathSource source);
    }

    /// <summary>
    /// Manages storage and retrieval of paths for gameplay map assets across various platforms.
    /// Supports StreamingAssets (via manifest), PersistentDataPath, and absolute file system paths.
    /// Optimized for performance, minimal GC, and robustness.
    /// </summary>
    public class GameplayMapStorage : IGameplayMapStorage
    {
        private const string DEBUG_FLAG = "[GameplayMapStorage]";
        public const string MAP_INFO_FILE_NAME = "MapInfo.yaml"; // Standardized map metadata file
        public const string DEFAULT_STREAMING_ASSETS_MAP_SUBFOLDER = "MusicGameMap";
        public const string STREAMING_ASSETS_MANIFEST_FILENAME = "map_manifest.json";

        [System.Serializable]
        private class MapManifest
        {
            public string basePathInStreamingAssets;
            public List<string> mapUniqueIDs; // Consider using string[] if IDs are fixed post-generation
        }

        // Use a struct for BasePathKey to enable value-based comparison in HashSet for efficiency
        private readonly struct BasePathKey
        {
            public readonly string LogicalPath;
            public readonly UnityPathSource Source;

            public BasePathKey(string logicalPath, UnityPathSource source)
            {
                LogicalPath = logicalPath;
                Source = source;
            }

            public override bool Equals(object obj) =>
                obj is BasePathKey other &&
                LogicalPath == other.LogicalPath &&
                Source == other.Source;

            public override int GetHashCode() =>
                System.HashCode.Combine(LogicalPath, Source); // Use System.HashCode for modern .NET
        }

        private readonly List<BasePathKey> _basePathEntries = new List<BasePathKey>();
        private readonly HashSet<BasePathKey> _addedBasePaths = new HashSet<BasePathKey>(); // For efficient duplicate checking

        // Storing the full URI directly after construction might save repeated concatenations later.
        // Key: UniqueID, Value: (Full URI to map folder, PathSource)
        private readonly Dictionary<string, (string mapRootUri, UnityPathSource pathSource)> _mapAssetRoots =
            new Dictionary<string, (string, UnityPathSource)>();

        private List<string> _cachedUniqueIDs; // Cache for GetAllMapUniqueIDs to reduce allocations

        public GameplayMapStorage()
        {
            // By default, add the standard StreamingAssets location.
            // The user can choose to clear this or add more paths later.
            AddBasePath(DEFAULT_STREAMING_ASSETS_MAP_SUBFOLDER, UnityPathSource.StreamingAssets);
        }

        /// <summary>
        /// Adds a base directory to scan for maps.
        /// </summary>
        /// <param name="logicalPath">
        /// For StreamingAssets and PersistentData, this is a relative path from the respective root.
        /// For AbsoluteOrFullUri, this is the full system path or URI.
        /// An empty string for StreamingAssets/PersistentData refers to their root.
        /// </param>
        /// <param name="source">The source type of the path (StreamingAssets, PersistentData, AbsoluteOrFullUri).</param>
        public void AddBasePath(string logicalPath, UnityPathSource source)
        {
            if (string.IsNullOrEmpty(logicalPath) && source != UnityPathSource.AbsoluteOrFullUri)
            {
                logicalPath = ""; // Normalize for root of SA/PD.
            }
            // For AbsoluteOrFullUri, ensure logicalPath is not null/empty if it's truly meant to be a path.
            else if (string.IsNullOrEmpty(logicalPath) && source == UnityPathSource.AbsoluteOrFullUri)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Attempted to add an empty absolute path. This is usually an error. Path ignored.");
                return;
            }


            var basePathKey = new BasePathKey(logicalPath, source);
            if (_addedBasePaths.Add(basePathKey)) // HashSet.Add returns true if the item was added (not a duplicate)
            {
                _basePathEntries.Add(basePathKey);
                CLogger.LogInfo($"{DEBUG_FLAG} Added base path: '{logicalPath}' (Source: {source})");
                _cachedUniqueIDs = null; // Invalidate cache
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Attempted to add duplicate base path: '{logicalPath}' (Source: {source}). Ignored.");
            }
        }

        /// <summary>
        /// Asynchronously updates the internal dictionary of map asset locations.
        /// Scans all registered base paths.
        /// </summary>
        /// <param name="clearExisting">If true, clears previously found maps before scanning. Set to false to incrementally add maps.</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        public async UniTask UpdatePathDictionaryAsync(bool clearExisting = true, CancellationToken cancellationToken = default)
        {
            if (clearExisting)
            {
                _mapAssetRoots.Clear();
                _cachedUniqueIDs = null;
            }
            CLogger.LogInfo($"{DEBUG_FLAG} Starting to update path dictionary... (Clear existing: {clearExisting})");

            foreach (var basePathEntry in _basePathEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string logicalBasePath = basePathEntry.LogicalPath;
                UnityPathSource currentSource = basePathEntry.Source;

                if (currentSource == UnityPathSource.StreamingAssets)
                {
                    await ProcessStreamingAssetsPathAsync(logicalBasePath, currentSource, cancellationToken);
                }
                else // PersistentData or AbsoluteOrFullUri
                {
                    // System.IO is not available on WebGL.
                    // For WebGL, PersistentData is emulated via IndexedDB and direct file system access is not possible.
                    // This part needs careful handling for WebGL, possibly by pre-generating all paths or using alternative loading.
#if UNITY_WEBGL && !UNITY_EDITOR // System.IO not available on WebGL
                    if (currentSource == UnityPathSource.PersistentData || currentSource == UnityPathSource.AbsoluteOrFullUri)
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} System.IO scanning for {currentSource} is not supported on WebGL. Path '{logicalBasePath}' will be skipped. Consider using StreamingAssets with a manifest for WebGL maps.");
                        continue;
                    }
#endif
                    ProcessFileSystemPath(logicalBasePath, currentSource);
                }
            }
            CLogger.LogInfo($"{DEBUG_FLAG} Path dictionary update finished. Total unique maps found: {_mapAssetRoots.Count}");
            if (_cachedUniqueIDs == null && _mapAssetRoots.Count > 0) // Rebuild cache if invalidated and maps exist
            {
                // Handled by GetAllMapUniqueIDs when first requested
            }
        }

        private async UniTask ProcessStreamingAssetsPathAsync(string logicalBasePath, UnityPathSource source, CancellationToken cancellationToken)
        {
            // Normalize path separators for URI construction
            string manifestRelativePath = Path.Combine(logicalBasePath, STREAMING_ASSETS_MANIFEST_FILENAME).Replace(Path.DirectorySeparatorChar, '/');
            string manifestUri = FilePathUtility.GetUnityWebRequestUri(manifestRelativePath, UnityPathSource.StreamingAssets);

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to load StreamingAssets manifest: {manifestUri}");

            using (UnityWebRequest www = UnityWebRequest.Get(manifestUri))
            {
                try
                {
                    await www.SendWebRequest().ToUniTask(null, PlayerLoopTiming.Update, cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Manifest loading cancelled for: {manifestUri}");
                    return;
                }
                catch (UnityWebRequestException ex) // Catch specific UWR exceptions
                {
                    // This typically covers network errors, but also local file access errors on some platforms.
                    if (ex.ResponseCode == 404 || (www.error != null && (www.error.Contains("Cannot connect to destination host") || www.error.Contains("Unable to_Complete_Login")))) // Common errors for missing files
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Manifest not found at '{manifestUri}' (or network issue). This might be intentional if no maps are bundled here. Skipping this StreamingAssets path.");
                    }
                    else
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Failed to load manifest from '{manifestUri}': {www.error} (Code: {www.responseCode}). Details: {ex.Message}");
                    }
                    return; // Stop processing this manifest if there's an error
                }


                if (www.result == UnityWebRequest.Result.Success)
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Successfully loaded manifest for SA base '{logicalBasePath}'.");
                    MapManifest manifestData = null;
                    try
                    {
                        manifestData = JsonUtility.FromJson<MapManifest>(www.downloadHandler.text);
                    }
                    catch (System.Exception ex)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Failed to parse manifest JSON from '{manifestUri}': {ex.Message}. Content snippet: {www.downloadHandler.text.Substring(0, Mathf.Min(www.downloadHandler.text.Length, 200))}");
                        return; // Stop if manifest is malformed
                    }

                    if (manifestData != null && manifestData.mapUniqueIDs != null && manifestData.mapUniqueIDs.Count > 0)
                    {
                        string effectiveBasePath = string.IsNullOrEmpty(manifestData.basePathInStreamingAssets)
                            ? logicalBasePath // Fallback to the logical path if manifest's own base is empty
                            : manifestData.basePathInStreamingAssets;

                        if (string.IsNullOrEmpty(manifestData.basePathInStreamingAssets) && !string.IsNullOrEmpty(logicalBasePath))
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Manifest for '{logicalBasePath}' has empty 'basePathInStreamingAssets'. Using provided logicalBasePath '{logicalBasePath}' as effective base.");
                        }

                        foreach (string mapFolderUniqueID in manifestData.mapUniqueIDs)
                        {
                            if (string.IsNullOrEmpty(mapFolderUniqueID))
                            {
                                CLogger.LogWarning($"{DEBUG_FLAG} Manifest contains an empty or null map UniqueID. Skipping.");
                                continue;
                            }

                            // Path within StreamingAssets, relative to its root.
                            string songDirRelativePath = Path.Combine(effectiveBasePath, mapFolderUniqueID).Replace(Path.DirectorySeparatorChar, '/');

                            if (_mapAssetRoots.TryAdd(mapFolderUniqueID, (songDirRelativePath, source)))
                            {
                                CLogger.LogInfo($"{DEBUG_FLAG} (Manifest) Added map '{mapFolderUniqueID}', SA relative path: '{songDirRelativePath}'");
                            }
                            else
                            {
                                CLogger.LogWarning($"{DEBUG_FLAG} (Manifest) Duplicate UniqueID '{mapFolderUniqueID}' encountered. Path from manifest: '{songDirRelativePath}'. Keeping first found entry: '{_mapAssetRoots[mapFolderUniqueID].mapRootUri}'.");
                            }
                        }
                        _cachedUniqueIDs = null; // Invalidate cache
                    }
                    else
                    {
                        CLogger.LogInfo($"{DEBUG_FLAG} Manifest from '{manifestUri}' loaded but contains no map entries or data is null. This is acceptable.");
                    }
                }
                // Removed the generic "Failed to load manifest" warning here as specific cases (like 404) are handled above as non-critical.
                // Only log actual errors not covered by specific checks.
                else if (www.responseCode != 404 && !(www.error != null && (www.error.Contains("Cannot connect to destination host") || www.error.Contains("Unable to_Complete_Login"))))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Failed to load manifest from '{manifestUri}'. Result: {www.result}, Error: {www.error}, Response Code: {www.responseCode}");
                }
            }
        }

        private void ProcessFileSystemPath(string logicalBasePath, UnityPathSource currentSource)
        {
            // This method uses System.IO and is not WebGL compatible.
            // A UniTask version might be needed if async file operations become critical here,
            // but for initial scan, synchronous can be acceptable on non-WebGL platforms.
#if UNITY_WEBGL && !UNITY_EDITOR
            CLogger.LogError($"{DEBUG_FLAG} ProcessFileSystemPath called on WebGL. This should not happen. Path: {logicalBasePath}");
            return;
#else
            string actualSystemBasePathToScan;
            if (currentSource == UnityPathSource.PersistentData)
            {
                actualSystemBasePathToScan = Path.Combine(Application.persistentDataPath, logicalBasePath);
            }
            else // AbsoluteOrFullUri (assuming it's a local file system path here)
            {
                actualSystemBasePathToScan = logicalBasePath;
                // For AbsoluteOrFullUri, if it's a remote URI, this direct System.IO approach won't work.
                // This implementation assumes AbsoluteOrFullUri refers to local file paths when not StreamingAssets.
                // If remote URIs need to be "scanned" for a list of maps, a different mechanism (like a manifest at that URI) is required.
            }

            CLogger.LogInfo($"{DEBUG_FLAG} System.IO Scanning: '{actualSystemBasePathToScan}' (Logical: '{logicalBasePath}', Source: {currentSource})");

            if (!Directory.Exists(actualSystemBasePathToScan))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} System.IO Base path '{actualSystemBasePathToScan}' does not exist or is not a directory. Skipping.");
                return;
            }

            string[] songDirectories;
            try
            {
                // SearchOption.TopDirectoryOnly is implied by GetDirectories without it.
                songDirectories = Directory.GetDirectories(actualSystemBasePathToScan);
                if (songDirectories.Length == 0)
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} No subdirectories found in '{actualSystemBasePathToScan}'. This may be intentional.");
                    // return; // Don't return, allow processing other paths.
                }
            }
            catch (System.UnauthorizedAccessException ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Access denied while scanning '{actualSystemBasePathToScan}': {ex.Message}");
                return; // Cannot proceed with this path
            }
            catch (System.IO.IOException ex) // Catch more specific IO errors
            {
                CLogger.LogError($"{DEBUG_FLAG} IO error while scanning '{actualSystemBasePathToScan}': {ex.Message}");
                return; // Cannot proceed with this path
            }
            catch (System.Exception ex) // Catch-all for other unexpected errors
            {
                CLogger.LogError($"{DEBUG_FLAG} Unexpected error during System.IO directory scan of '{actualSystemBasePathToScan}': {ex.Message}");
                return; // Cannot proceed with this path
            }


            foreach (string songDirPathAbsolute in songDirectories)
            {
                string mapInfoFilePath = Path.Combine(songDirPathAbsolute, MAP_INFO_FILE_NAME);
                if (File.Exists(mapInfoFilePath)) // Check for the map info file to identify a valid map folder
                {
                    string uniqueID = Path.GetFileName(songDirPathAbsolute); // Folder name is the UniqueID
                    if (string.IsNullOrEmpty(uniqueID))
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Could not determine UniqueID from directory path '{songDirPathAbsolute}'. Skipping.");
                        continue;
                    }

                    // For PersistentData and Absolute paths, the stored `songPath` in `_mapAssetRoots` will be the absolute system path.
                    // FilePathUtility.GetUnityWebRequestUri will later convert this to a file:/// URI if needed.
                    if (_mapAssetRoots.TryAdd(uniqueID, (songDirPathAbsolute, currentSource)))
                    {
                        CLogger.LogInfo($"{DEBUG_FLAG} (System.IO) Added map '{uniqueID}', Absolute path: '{songDirPathAbsolute}'");
                    }
                    else
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} (System.IO) Duplicate UniqueID '{uniqueID}' found at '{songDirPathAbsolute}'. Keeping first found entry: '{_mapAssetRoots[uniqueID].mapRootUri}'.");
                    }
                }
                // else: It's a directory but not a map folder (no MapInfo.yaml), so ignore it.
            }
            _cachedUniqueIDs = null; // Invalidate cache
#endif
        }


        // Helper for constructing final asset URIs.
        // `mapRootPathInSource` is the relative path for SA, or absolute path for PD/Absolute.
        // `assetFilenameWithExtension` is the name of the asset file, e.g., "audio.ogg".
        private string GetFinalAssetUri(string mapUniqueID, string mapRootPathInSource, UnityPathSource pathSource, string assetFilenameWithExtension)
        {
            if (string.IsNullOrEmpty(assetFilenameWithExtension)) return string.Empty;

            string relativeAssetPath; // Path relative to SA root, or PD root, or the full path for Absolute
            string finalUri;

            if (pathSource == UnityPathSource.StreamingAssets)
            {
                // mapRootPathInSource is already relative to SA root (e.g., "MusicGameMap/MyCoolSong")
                // assetFilenameWithExtension is e.g., "audio.ogg"
                // Resulting pathForFilePathUtility needs to be "MusicGameMaps/MyCoolSong/audio.ogg"
                relativeAssetPath = Path.Combine(mapRootPathInSource, assetFilenameWithExtension).Replace(Path.DirectorySeparatorChar, '/');
                finalUri = FilePathUtility.GetUnityWebRequestUri(relativeAssetPath, UnityPathSource.StreamingAssets);
            }
            else // PersistentData or AbsoluteOrFullUri
            {
                // mapRootPathInSource is an absolute system path (e.g., "/var/mobile/.../Maps/MyCoolSong" or "C:/Users/.../Maps/MyCoolSong")
                string fullAbsoluteAssetSystemPath = Path.Combine(mapRootPathInSource, assetFilenameWithExtension);

#if !UNITY_WEBGL || UNITY_EDITOR // File.Exists is not available on WebGL builds
                // On WebGL, we cannot check File.Exists for PersistentData (IndexedDB) this way. Assume it exists if listed.
                // For other platforms, it's a good check.
                if (!File.Exists(fullAbsoluteAssetSystemPath))
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Asset file not found on disk: '{fullAbsoluteAssetSystemPath}' (Map: '{mapUniqueID}', Asset: '{assetFilenameWithExtension}').");
                    return string.Empty;
                }
#endif

                if (pathSource == UnityPathSource.PersistentData)
                {
                    // FilePathUtility needs a path *relative* to Application.persistentDataPath for PersistentData source.
                    string persistentDataRoot = Application.persistentDataPath;
                    if (!fullAbsoluteAssetSystemPath.StartsWith(persistentDataRoot, System.StringComparison.OrdinalIgnoreCase))
                    {
                        // This should ideally not happen if paths are constructed correctly.
                        CLogger.LogError($"{DEBUG_FLAG} Asset path '{fullAbsoluteAssetSystemPath}' for map '{mapUniqueID}' (PersistentData) is not under PersistentData root '{persistentDataRoot}'. This indicates an internal logic error.");
                        return string.Empty;
                    }
                    // Get the part of the path after persistentDataRoot.
                    relativeAssetPath = fullAbsoluteAssetSystemPath.Substring(persistentDataRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    finalUri = FilePathUtility.GetUnityWebRequestUri(relativeAssetPath, UnityPathSource.PersistentData);
                }
                else // AbsoluteOrFullUri
                {
                    // For AbsoluteOrFullUri, FilePathUtility expects the full path directly.
                    relativeAssetPath = fullAbsoluteAssetSystemPath; // In this context, 'relative' is a bit of a misnomer; it's the direct path.
                    finalUri = FilePathUtility.GetUnityWebRequestUri(relativeAssetPath, UnityPathSource.AbsoluteOrFullUri);
                }
            }
            // CLogger.LogDebug($"{DEBUG_FLAG} Asset URI for '{mapUniqueID}/{assetFilenameWithExtension}': Source='{pathSource}', Root='{mapRootPathInSource}', RelativeToUtil='{relativeAssetPath}', Final='{finalUri}'");
            return finalUri;
        }


        private string GetSpecificAssetPathInternal(in MapInfo mapInfo, string assetFilenameFromYaml, string defaultExtensionIfNotPresent)
        {
            if (string.IsNullOrEmpty(assetFilenameFromYaml)) return string.Empty;

            if (_mapAssetRoots.TryGetValue(mapInfo.UniqueID, out var rootInfo))
            {
                string assetFilenameWithExtension = assetFilenameFromYaml;
                if (!string.IsNullOrEmpty(defaultExtensionIfNotPresent) && !Path.HasExtension(assetFilenameFromYaml))
                {
                    // Using string.Concat for potentially fewer allocations than + operator in a loop, though StringBuilder is better for many concats.
                    // For a single concat, this is fine.
                    assetFilenameWithExtension = string.Concat(assetFilenameFromYaml, defaultExtensionIfNotPresent);
                }
                return GetFinalAssetUri(mapInfo.UniqueID, rootInfo.mapRootUri, rootInfo.pathSource, assetFilenameWithExtension);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map with UniqueID '{mapInfo.UniqueID}' not found in asset roots when trying to get asset '{assetFilenameFromYaml}'. Call UpdatePathDictionaryAsync() if maps were recently added.");
                return string.Empty;
            }
        }

        public string GetAudioPath(in MapInfo mapInfo) => GetSpecificAssetPathInternal(mapInfo, mapInfo.AudioFile, ".ogg");
        public string GetVideoPath(in MapInfo mapInfo) => GetSpecificAssetPathInternal(mapInfo, mapInfo.VideoFile, ".mp4");
        public string GetPreviewAudioPath(in MapInfo mapInfo) => GetSpecificAssetPathInternal(mapInfo, mapInfo.PreviewAudioFile, ".ogg");
        public string GetPreviewVideoPath(in MapInfo mapInfo) => GetSpecificAssetPathInternal(mapInfo, mapInfo.PreviewVideoFile, ".mp4");

        public string[] GetBackgroundPictures(in MapInfo mapInfo)
        {
            if (mapInfo.InGameBackgroundPictures == null || mapInfo.InGameBackgroundPictures.Length == 0)
            {
                return System.Array.Empty<string>(); // Returns a cached empty array, good for GC.
            }

            if (!_mapAssetRoots.TryGetValue(mapInfo.UniqueID, out var rootInfo))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Map with UniqueID '{mapInfo.UniqueID}' not found when trying to get background pictures.");
                return System.Array.Empty<string>();
            }

            // Avoid reallocating the array every time if the content doesn't change frequently.
            // However, MapInfo is passed by 'in', so we can't store derived data in it.
            // The cost of this small array allocation is likely acceptable.
            var bgPicUris = new string[mapInfo.InGameBackgroundPictures.Length];
            for (int i = 0; i < mapInfo.InGameBackgroundPictures.Length; i++)
            {
                string bgFileNameInYaml = mapInfo.InGameBackgroundPictures[i];
                // Assuming background picture filenames in YAML *always* include their extensions.
                // If not, a default extension (e.g., ".png", ".jpg") would be needed for GetSpecificAssetPathInternal.
                bgPicUris[i] = GetFinalAssetUri(mapInfo.UniqueID, rootInfo.mapRootUri, rootInfo.pathSource, bgFileNameInYaml);

                if (string.IsNullOrEmpty(bgPicUris[i]))
                {
                    // GetFinalAssetUri already logs warnings for missing files if applicable.
                    CLogger.LogWarning($"{DEBUG_FLAG} Failed to resolve URI for BG picture '{bgFileNameInYaml}' in map '{mapInfo.UniqueID}'. It might be missing or incorrectly listed.");
                }
            }
            return bgPicUris;
        }

        /// <summary>
        /// Gets a read-only list of all discovered map UniqueIDs.
        /// The list is cached and regenerated only if maps are updated.
        /// </summary>
        public IReadOnlyList<string> GetAllMapUniqueIDs()
        {
            if (_cachedUniqueIDs == null)
            {
                // Keys.ToList() creates a new list, which is what we want for the cache.
                _cachedUniqueIDs = new List<string>(_mapAssetRoots.Keys);
            }
            return _cachedUniqueIDs;
        }

        /// <summary>
        /// Attempts to retrieve the root storage path and source for a given map UniqueID.
        /// For StreamingAssets, 'path' is relative to StreamingAssets root.
        /// For PersistentData and AbsoluteOrFullUri, 'path' is the absolute system path to the map folder.
        /// </summary>
        public bool TryGetMapPathInfo(string uniqueID, out string path, out UnityPathSource source)
        {
            if (string.IsNullOrEmpty(uniqueID))
            {
                path = null;
                source = default;
                return false;
            }
            if (_mapAssetRoots.TryGetValue(uniqueID, out var rootInfo))
            {
                path = rootInfo.mapRootUri; // This is the "base" path for the map's assets
                source = rootInfo.pathSource;
                return true;
            }
            path = null;
            source = default;
            return false;
        }
    }
}