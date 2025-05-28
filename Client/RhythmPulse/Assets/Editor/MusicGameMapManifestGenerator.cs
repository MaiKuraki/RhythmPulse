#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using RhythmPulse.Gameplay; // For GameplayMapStorage constants
using CycloneGames.Logger;

namespace RhythmPulse.Editor
{
    /// <summary>
    /// Generates a manifest file for music maps located in the configured StreamingAssets subfolder.
    /// This manifest is used by GameplayMapStorage at runtime to locate maps on platforms
    /// where direct System.IO directory scanning of StreamingAssets is not possible (e.g., Android, WebGL).
    /// </summary>
    public class MusicGameMapManifestGenerator
    {
        private const string DEBUG_FLAG = "[MusicGameMapManifestGenerator] ";

        // Internal class for JsonUtility serialization.
        // This structure MUST match the 'MapManifest' class within GameplayMapStorage.
        [System.Serializable]
        private class ManifestStructure
        {
            public string basePathInStreamingAssets; // The StreamingAssets subfolder these maps are relative to.
            public List<string> mapUniqueIDs;      // List of folder names (UniqueIDs) of the maps.
        }

        /// <summary>
        /// Generates the map manifest for the default StreamingAssets map subfolder.
        /// This should be called before building the game.
        /// </summary>
        [MenuItem("Tools/RhythmPulse/Generate Map Manifest", priority = 2000)]
        public static void GenerateManifest()
        {
            // These constants are expected to be public in GameplayMapStorage
            string musicMapsSubfolderInSA = GameplayMapStorage.DEFAULT_STREAMING_ASSETS_MAP_SUBFOLDER;
            string manifestFileName = GameplayMapStorage.STREAMING_ASSETS_MANIFEST_FILENAME;
            string mapInfoFileName = GameplayMapStorage.MAP_INFO_FILE_NAME;

            string fullMusicFolderPath = Path.Combine(Application.streamingAssetsPath, musicMapsSubfolderInSA);
            string manifestFilePath = Path.Combine(fullMusicFolderPath, manifestFileName);

            // Ensure the base StreamingAssets subfolder for maps exists.
            if (!Directory.Exists(fullMusicFolderPath))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Music folder not found at: {fullMusicFolderPath}. Attempting to create it.");
                try
                {
                    Directory.CreateDirectory(fullMusicFolderPath);
                    CLogger.LogInfo($"{DEBUG_FLAG} Created directory: {fullMusicFolderPath}");
                }
                catch (System.Exception ex)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to create directory {fullMusicFolderPath}: {ex.Message}");
                    // If directory creation fails, we cannot write the manifest there.
                    EditorUtility.DisplayDialog("Manifest Generation Error", $"Failed to create directory: {fullMusicFolderPath}\n{ex.Message}", "OK");
                    return;
                }
            }

            List<string> mapUniqueIDs = new List<string>();
            DirectoryInfo dirInfo = new DirectoryInfo(fullMusicFolderPath);

            try
            {
                foreach (DirectoryInfo songDir in dirInfo.GetDirectories())
                {
                    // Check if subdirectory contains the map info file (e.g., MapInfo.yaml)
                    if (File.Exists(Path.Combine(songDir.FullName, mapInfoFileName)))
                    {
                        mapUniqueIDs.Add(songDir.Name); // Folder name is used as UniqueID
                    }
                }
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Error scanning directories in {fullMusicFolderPath}: {ex.Message}");
                EditorUtility.DisplayDialog("Manifest Generation Error", $"Error scanning directories: {fullMusicFolderPath}\n{ex.Message}", "OK");
                return;
            }


            ManifestStructure manifest = new ManifestStructure
            {
                // Store the subfolder path relative to StreamingAssets.
                basePathInStreamingAssets = musicMapsSubfolderInSA,
                mapUniqueIDs = mapUniqueIDs
            };

            try
            {
                string json = JsonUtility.ToJson(manifest, true); // Pretty print for readability
                File.WriteAllText(manifestFilePath, json);
                CLogger.LogInfo($"{DEBUG_FLAG} Map manifest generated/updated at: {manifestFilePath} with {mapUniqueIDs.Count} maps for subfolder '{musicMapsSubfolderInSA}'.");
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to write manifest file {manifestFilePath}: {ex.Message}");
                EditorUtility.DisplayDialog("Manifest Generation Error", $"Failed to write manifest file: {manifestFilePath}\n{ex.Message}", "OK");
                return;
            }

            AssetDatabase.Refresh(); // Ensure Unity editor detects new/changed files
        }
    }
}
#endif