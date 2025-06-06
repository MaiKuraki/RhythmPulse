using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RhythmPulse.GameplayData.Runtime;
using VYaml.Serialization;
using VYaml.Parser;
using System.Reflection;
using System.Threading.Tasks;
using CycloneGames.Utility.Runtime;
using System;
using System.Text.RegularExpressions;
using CycloneGames.Logger;

namespace RhythmPulse.GameplayData.Editor
{
    public class MapInfoEditorWindow : EditorWindow
    {
        private MapInfo mapInfo;
        private Vector2 scrollPosition;

        private static string[] beatMapTypeOptions;
        private List<string> currentBackgroundPictures = new List<string>();
        private string newBackgroundPicturePath = "";
        private string currentlyLoadedMapInfoPath = ""; // To resolve relative paths for difficulty files

        // Dictionary to hold validation messages for specific fields
        private Dictionary<string, string> validationMessages = new Dictionary<string, string>();
        private Dictionary<string, MessageType> validationMessageTypes = new Dictionary<string, MessageType>();


        // Common invalid file/directory characters for Windows, Linux, macOS
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        // Reserved filenames on Windows (case-insensitive)
        private static readonly string[] ReservedWindowsNames = new string[]
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };


        [MenuItem("Tools/RhythmPulse/MapInfo Editor")]
        public static void ShowWindow()
        {
            MapInfoEditorWindow window = GetWindow<MapInfoEditorWindow>("MapInfo Editor");
            window.minSize = new Vector2(500, 400);
        }

        private void OnEnable()
        {
            InitializeMapInfo();
            if (beatMapTypeOptions == null)
            {
                beatMapTypeOptions = BeatMapUtility.ValidBeatMapTypes;

                if (beatMapTypeOptions.Length == 0)
                {
                    Debug.LogWarning("[MapInfoEditorWindow] No string constants found in BeatMapTypeConstant via BeatMapUtility. BeatMapType selection will be empty.");
                }
                else if (beatMapTypeOptions.Length > 32)
                {
                    Debug.LogWarning("[MapInfoEditorWindow] BeatMapTypeConstant has more than 32 options. MaskField may not support all of them if more are added.");
                }
            }
        }

        private void InitializeMapInfo()
        {
            mapInfo = new MapInfo
            {
                UniqueID = System.Guid.NewGuid().ToString("N"), // Use "N" for a cleaner GUID without hyphens, suitable for folder names
                LastModifiedTime = "", // Initialized as empty, will be set on save
                DisplayName = "Vocalist - NewSongName (BeatMapAuthor)",
                AudioFile = "audio.ogg",
                VideoFile = "",
                PreviewAudioFile = "",
                PreviewVideoFile = "",
                InGameBackgroundPictures = new string[0],
                Vocalist = "Anonymous",
                Composer = "Anonymous",
                Arranger = "Anonymous",
                Lyricist = "Anonymous",
                BeatmapAuthor = "YourName",
                BeatmapDifficultyFiles = new List<BeatMapInfo>(),
                MediaOverrides = new List<BeatMapTypeMediaOverride>()
            };
            currentBackgroundPictures = new List<string>(mapInfo.InGameBackgroundPictures);
            newBackgroundPicturePath = "";
            currentlyLoadedMapInfoPath = ""; // Reset loaded path
            validationMessages.Clear(); // Clear validation messages on new/reset
            validationMessageTypes.Clear();
        }

        void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // --- File Operations ---
            EditorGUILayout.LabelField("File Operations", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load MapInfo from YAML"))
            {
                LoadYamlFromFile();
            }
            if (GUILayout.Button("Reset/New MapInfo"))
            {
                if (EditorUtility.DisplayDialog("Confirm Reset", "Are you sure you want to discard current changes and reset all fields to default values?", "Yes", "No"))
                {
                    InitializeMapInfo();
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            // --- Map Information ---
            EditorGUILayout.LabelField("Map Information", EditorStyles.boldLabel);
            string newUniqueID = EditorGUILayout.TextField(new GUIContent("Unique ID", "A unique identifier for this map (e.g., a GUID). This should be a valid folder name."), mapInfo.UniqueID);
            if (newUniqueID != mapInfo.UniqueID)
            {
                mapInfo.UniqueID = newUniqueID;
                // Validate immediately to show feedback
                ValidateUniqueID(mapInfo.UniqueID);
            }
            DisplayValidationMessage("UniqueID"); // Display message below the field

            GUI.enabled = false; // Make LastModifiedTime read-only
            EditorGUILayout.TextField(new GUIContent("Last Modified Time", "Time this MapInfo was last saved (automatically updated)."), string.IsNullOrEmpty(mapInfo.LastModifiedTime) ? "Not saved yet" : mapInfo.LastModifiedTime);
            GUI.enabled = true;

            mapInfo.DisplayName = EditorGUILayout.TextField(new GUIContent("Display Name", "The name of the song/map shown in-game."), mapInfo.DisplayName);

            EditorGUILayout.Space();

            // --- Media Files ---
            EditorGUILayout.LabelField("Media Files", EditorStyles.boldLabel);
            mapInfo.AudioFile = EditorGUILayout.TextField(new GUIContent("Full Audio File", "Path to the main audio file (e.g., 'audio.ogg'). Should be relative to the MapInfo's unique ID folder."), mapInfo.AudioFile);
            ValidateRelativePath("AudioFile", mapInfo.AudioFile, false); // AudioFile is mandatory
            DisplayValidationMessage("AudioFile");

            mapInfo.VideoFile = EditorGUILayout.TextField(new GUIContent("Full Video File", "Optional path to a background video file."), mapInfo.VideoFile);
            ValidateRelativePath("VideoFile", mapInfo.VideoFile, true);
            DisplayValidationMessage("VideoFile");

            mapInfo.PreviewAudioFile = EditorGUILayout.TextField(new GUIContent("Preview Audio File", "Optional path to a short audio preview clip."), mapInfo.PreviewAudioFile);
            ValidateRelativePath("PreviewAudioFile", mapInfo.PreviewAudioFile, true);
            DisplayValidationMessage("PreviewAudioFile");

            mapInfo.PreviewVideoFile = EditorGUILayout.TextField(new GUIContent("Preview Video File", "Optional path to a short video preview clip."), mapInfo.PreviewVideoFile);
            ValidateRelativePath("PreviewVideoFile", mapInfo.PreviewVideoFile, true);
            DisplayValidationMessage("PreviewVideoFile");


            EditorGUILayout.Space();

            // --- Credits ---
            EditorGUILayout.LabelField("Credits", EditorStyles.boldLabel);
            mapInfo.Vocalist = EditorGUILayout.TextField("Vocalist", mapInfo.Vocalist);
            mapInfo.Composer = EditorGUILayout.TextField("Composer", mapInfo.Composer);
            mapInfo.Arranger = EditorGUILayout.TextField("Arranger", mapInfo.Arranger);
            mapInfo.Lyricist = EditorGUILayout.TextField("Lyricist", mapInfo.Lyricist);
            mapInfo.BeatmapAuthor = EditorGUILayout.TextField(new GUIContent("Beatmap Author", "Creator of this specific beatmap data."), mapInfo.BeatmapAuthor);

            EditorGUILayout.Space();

            // --- In-Game Background Pictures ---
            DrawBackgroundsGUI();

            // --- Media Overrides ---
            DrawMediaOverridesGUI();

            // --- Beatmap Difficulty Files ---
            DrawBeatmapDifficultyFilesGUI();

            EditorGUILayout.Space(20);
            if (GUILayout.Button("Save MapInfo to YAML File", GUILayout.Height(30)))
            {
                // Clear existing messages before full validation
                validationMessages.Clear();
                validationMessageTypes.Clear();

                // Perform uniqueness check across all entries before full validation
                PerformBeatmapUniquenessValidation();
                PerformMediaOverrideUniquenessValidation();

                // Pre-save validation
                if (PerformFullValidation())
                {
                    mapInfo.LastModifiedTime = System.DateTime.UtcNow.ToString("o"); // ISO 8601 format
                    mapInfo.InGameBackgroundPictures = currentBackgroundPictures.ToArray();
                    GenerateYaml(); // Now prompts for save location
                }
                else
                {
                    EditorUtility.DisplayDialog("Validation Failed", "Please correct the highlighted errors before saving.", "OK");
                    Repaint(); // Force repaint to show all validation messages
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Clears all validation messages associated with a specific beatmap entry index.
        /// </summary>
        private void ClearBeatmapValidationMessages(int index)
        {
            SetValidationMessage($"Version_{index}", "", MessageType.None);
            SetValidationMessage($"BeatMapType_{index}", "", MessageType.None);
            SetValidationMessage($"DifficultyLevel_{index}", "", MessageType.None);
            SetValidationMessage($"GeneratedDifficultyFile_{index}", "", MessageType.None);
            SetValidationMessage($"DifficultyFile_{index}_MD5", "", MessageType.None);
            SetValidationMessage($"DuplicateEntry_{index}", "", MessageType.None); // Clear duplicate message
        }

        /// <summary>
        /// Performs a uniqueness validation check on all BeatmapDifficultyFiles entries.
        /// Sets error messages for any duplicate (Type, Difficulty, Version) combinations.
        /// </summary>
        private void PerformBeatmapUniquenessValidation()
        {
            // Using a dictionary to track unique keys and the index of their *first* occurrence
            Dictionary<(string type, int difficulty, string version), int> seenEntries = new Dictionary<(string, int, string), int>();

            for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
            {
                BeatMapInfo beatmap = mapInfo.BeatmapDifficultyFiles[i];
                string beatMapType = beatmap.BeatMapType != null && beatmap.BeatMapType.Length > 0 ? beatmap.BeatMapType[0] : "";

                var currentKey = (beatMapType, beatmap.Difficulty, beatmap.Version);

                if (seenEntries.ContainsKey(currentKey))
                {
                    // This is a duplicate. Mark both the current one and the first one found.
                    SetValidationMessage($"DuplicateEntry_{i}", "This difficulty entry (Type, Level, Version) is a duplicate of another entry.", MessageType.Error);
                    // Also mark the original duplicate entry if it wasn't already
                    int originalIndex = seenEntries[currentKey];
                    SetValidationMessage($"DuplicateEntry_{originalIndex}", "This difficulty entry (Type, Level, Version) is a duplicate of another entry.", MessageType.Error);
                }
                else
                {
                    // If it's not a duplicate, add it to seen entries and clear any previous duplicate message
                    seenEntries.Add(currentKey, i);
                    SetValidationMessage($"DuplicateEntry_{i}", "", MessageType.None);
                }
            }
        }

        /// <summary>
        /// Draws the GUI for managing Media Overrides.
        /// </summary>
        private void DrawMediaOverridesGUI()
        {
            EditorGUILayout.LabelField("Mode-Specific Media Overrides", EditorStyles.boldLabel);
            if (mapInfo.MediaOverrides == null)
            {
                mapInfo.MediaOverrides = new List<BeatMapTypeMediaOverride>();
            }

            int overrideToRemove = -1; // Defer removal to after the loop

            for (int i = 0; i < mapInfo.MediaOverrides.Count; i++)
            {
                var currentOverride = mapInfo.MediaOverrides[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Override Entry #{i + 1}", EditorStyles.miniBoldLabel);

                // BeatMapType selection and other fields... (no changes needed here)
                int currentSelectionIndex = string.IsNullOrEmpty(currentOverride.BeatMapType) ? -1 : Array.IndexOf(beatMapTypeOptions, currentOverride.BeatMapType);
                int newSelectionIndex = EditorGUILayout.Popup(new GUIContent("Game Mode", "Select game mode to override."), currentSelectionIndex, beatMapTypeOptions);
                if (newSelectionIndex != currentSelectionIndex)
                {
                    currentOverride.BeatMapType = beatMapTypeOptions[newSelectionIndex];
                }
                DisplayValidationMessage($"OverrideBeatMapType_{i}");
                DisplayValidationMessage($"DuplicateOverride_{i}");

                currentOverride.AudioFile = EditorGUILayout.TextField(new GUIContent("Override Full Audio File", "Leave empty to use default."), currentOverride.AudioFile);
                ValidateRelativePath($"OverrideAudio_{i}", currentOverride.AudioFile, true);
                DisplayValidationMessage($"OverrideAudio_{i}");

                currentOverride.VideoFile = EditorGUILayout.TextField(new GUIContent("Override Full Video File", "Leave empty to use default."), currentOverride.VideoFile);
                ValidateRelativePath($"OverrideVideo_{i}", currentOverride.VideoFile, true);
                DisplayValidationMessage($"OverrideVideo_{i}");

                currentOverride.PreviewAudioFile = EditorGUILayout.TextField(new GUIContent("Override Preview Audio", "Leave empty to use default."), currentOverride.PreviewAudioFile);
                ValidateRelativePath($"OverridePreviewAudio_{i}", currentOverride.PreviewAudioFile, true);
                DisplayValidationMessage($"OverridePreviewAudio_{i}");

                currentOverride.PreviewVideoFile = EditorGUILayout.TextField(new GUIContent("Override Preview Video", "Leave empty to use default."), currentOverride.PreviewVideoFile);
                ValidateRelativePath($"OverridePreviewVideo_{i}", currentOverride.PreviewVideoFile, true);
                DisplayValidationMessage($"OverridePreviewVideo_{i}");

                mapInfo.MediaOverrides[i] = currentOverride;

                if (GUILayout.Button("Remove This Override", GUILayout.Height(20)))
                {
                    // Instead of removing here, mark the index for removal
                    overrideToRemove = i;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            // Perform the removal safely outside the drawing loop
            if (overrideToRemove > -1)
            {
                ClearMediaOverrideValidationMessages(overrideToRemove);
                mapInfo.MediaOverrides.RemoveAt(overrideToRemove);
            }

            if (GUILayout.Button("Add New Media Override"))
            {
                mapInfo.MediaOverrides.Add(new BeatMapTypeMediaOverride());
                GUI.FocusControl(null);
            }
            EditorGUILayout.Space();
        }

        /// <summary>
        /// Draws the GUI for managing In-Game Background Pictures.
        /// </summary>
        private void DrawBackgroundsGUI()
        {
            EditorGUILayout.LabelField("In-Game Background Pictures", EditorStyles.boldLabel);
            int backgroundToRemove = -1; // Defer removal to after the loop

            for (int i = 0; i < currentBackgroundPictures.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                string newPicPath = EditorGUILayout.TextField($"Picture {i + 1}", currentBackgroundPictures[i]);
                if (newPicPath != currentBackgroundPictures[i])
                {
                    currentBackgroundPictures[i] = newPicPath;
                    ValidateRelativePath($"BackgroundPicture_{i}", newPicPath, false);
                }
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    // Instead of removing here, mark the index for removal
                    backgroundToRemove = i;
                }
                EditorGUILayout.EndHorizontal();
                DisplayValidationMessage($"BackgroundPicture_{i}");
            }

            // Perform the removal safely outside the drawing loop
            if (backgroundToRemove > -1)
            {
                ClearValidationMessage($"BackgroundPicture_{backgroundToRemove}");
                currentBackgroundPictures.RemoveAt(backgroundToRemove);
            }

            EditorGUILayout.BeginHorizontal();
            newBackgroundPicturePath = EditorGUILayout.TextField("New Picture Path", newBackgroundPicturePath);
            if (GUILayout.Button("Add Picture", GUILayout.Width(100)) && !string.IsNullOrWhiteSpace(newBackgroundPicturePath))
            {
                if (!PathContainsInvalidChars(newBackgroundPicturePath) && !PathIsReserved(newBackgroundPicturePath))
                {
                    currentBackgroundPictures.Add(newBackgroundPicturePath);
                    newBackgroundPicturePath = "";
                    GUI.FocusControl(null);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Path", "The path contains invalid characters or is a reserved filename.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// Draws the GUI for managing Beatmap Difficulty Files.
        /// </summary>
        private void DrawBeatmapDifficultyFilesGUI()
        {
            EditorGUILayout.LabelField("Beatmap Difficulty Files", EditorStyles.boldLabel);
            if (mapInfo.BeatmapDifficultyFiles == null)
            {
                mapInfo.BeatmapDifficultyFiles = new List<BeatMapInfo>();
            }

            // Track existing difficulty entries for uniqueness validation
            HashSet<(string type, int difficulty, string version)> existingDifficultyEntries = new HashSet<(string, int, string)>();

            for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
            {
                BeatMapInfo currentBeatmap = mapInfo.BeatmapDifficultyFiles[i]; // This is a struct, so it's a copy.

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Difficulty Entry #{i + 1}", EditorStyles.miniBoldLabel);

                // Version string input
                string newVersion = EditorGUILayout.TextField(new GUIContent("Version String", "A descriptive version (alphanumeric, hyphens, max 24 chars)."), currentBeatmap.Version);
                if (newVersion != currentBeatmap.Version)
                {
                    currentBeatmap.Version = newVersion;
                    mapInfo.BeatmapDifficultyFiles[i] = currentBeatmap; // Write back immediately
                    ValidateVersionString($"Version_{i}", currentBeatmap.Version);
                }
                DisplayValidationMessage($"Version_{i}");

                // Beatmap Type selection - Changed from MaskField to Popup for single selection
                if (beatMapTypeOptions != null && beatMapTypeOptions.Length > 0)
                {
                    // Find the index of the current BeatMapType in the options
                    int currentSelectionIndex = -1;
                    if (currentBeatmap.BeatMapType != null && currentBeatmap.BeatMapType.Length > 0)
                    {
                        currentSelectionIndex = Array.IndexOf(beatMapTypeOptions, currentBeatmap.BeatMapType[0]);
                    }

                    int newSelectionIndex = EditorGUILayout.Popup(
                        new GUIContent("BeatMap Type", "Select the single game mode this difficulty applies to."),
                        currentSelectionIndex, beatMapTypeOptions);

                    if (newSelectionIndex != currentSelectionIndex)
                    {
                        currentBeatmap.BeatMapType = new string[] { beatMapTypeOptions[newSelectionIndex] };
                        mapInfo.BeatmapDifficultyFiles[i] = currentBeatmap; // Write back immediately
                        ValidateBeatMapTypeSelection($"BeatMapType_{i}", currentBeatmap.BeatMapType);
                    }
                    DisplayValidationMessage($"BeatMapType_{i}");
                }
                else
                {
                    EditorGUILayout.HelpBox("No BeatMapType options available. Check BeatMapTypeConstant.", MessageType.Warning);
                }

                // Difficulty Level input
                int newDifficulty = EditorGUILayout.IntField("Difficulty Level", currentBeatmap.Difficulty);
                if (newDifficulty != currentBeatmap.Difficulty)
                {
                    currentBeatmap.Difficulty = newDifficulty;
                    mapInfo.BeatmapDifficultyFiles[i] = currentBeatmap; // Write back immediately
                    ValidateDifficultyLevel($"DifficultyLevel_{i}", currentBeatmap.Difficulty);
                }
                DisplayValidationMessage($"DifficultyLevel_{i}");

                // Add to hash set for uniqueness check
                if (currentBeatmap.BeatMapType != null && currentBeatmap.BeatMapType.Length > 0)
                {
                    var entry = (currentBeatmap.BeatMapType[0], currentBeatmap.Difficulty, currentBeatmap.Version);
                    if (!existingDifficultyEntries.Add(entry))
                    {
                        SetValidationMessage($"DuplicateEntry_{i}", "This difficulty entry (Type, Level, Version) is a duplicate of another entry.", MessageType.Error);
                    }
                    else
                    {
                        // Clear message if it was a duplicate and now it's unique (e.g., after changing a field)
                        SetValidationMessage($"DuplicateEntry_{i}", "", MessageType.None);
                    }
                }
                DisplayValidationMessage($"DuplicateEntry_{i}");


                // Display the generated Difficulty File name (read-only)
                string displayBeatMapType = currentBeatmap.BeatMapType != null && currentBeatmap.BeatMapType.Length > 0
                                            ? currentBeatmap.BeatMapType[0]
                                            : "N/A"; // Fallback for display if no type selected

                string generatedFileName = BeatMapUtility.GetBeatMapFile(
                    displayBeatMapType,
                    currentBeatmap.Difficulty,
                    currentBeatmap.Version
                );
                GUI.enabled = false; // Make the text field read-only
                EditorGUILayout.TextField(new GUIContent("Generated File", "The full filename for this beatmap difficulty (read-only)."), generatedFileName);
                GUI.enabled = true; // Re-enable GUI

                // Validate the generated path (existence check)
                ValidateGeneratedBeatmapFilePath($"GeneratedDifficultyFile_{i}", generatedFileName);
                DisplayValidationMessage($"GeneratedDifficultyFile_{i}");

                EditorGUILayout.BeginHorizontal();
                currentBeatmap.MD5 = EditorGUILayout.TextField(new GUIContent("MD5 Hash", "MD5 hash of the difficulty file. Click 'Generate' to calculate."), currentBeatmap.MD5);
                if (GUILayout.Button("Generate MD5", GUILayout.Width(100)))
                {
                    int local_i = i;
                    // Pass the actual type for MD5 generation
                    string actualBeatMapTypeForMD5 = mapInfo.BeatmapDifficultyFiles[local_i].BeatMapType != null && mapInfo.BeatmapDifficultyFiles[local_i].BeatMapType.Length > 0
                                                     ? mapInfo.BeatmapDifficultyFiles[local_i].BeatMapType[0]
                                                     : "N/A";
                    string fileNameForMD5 = BeatMapUtility.GetBeatMapFile(
                        actualBeatMapTypeForMD5,
                        mapInfo.BeatmapDifficultyFiles[local_i].Difficulty,
                        mapInfo.BeatmapDifficultyFiles[local_i].Version
                    );
                    _ = GenerateMD5ForBeatmapAsync(fileNameForMD5, local_i);
                }
                EditorGUILayout.EndHorizontal();
                DisplayValidationMessage($"DifficultyFile_{i}_MD5"); // Display MD5 validation message

                // New: Auto-create file button
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Auto-Create File", GUILayout.Width(120), GUILayout.Height(20)))
                {
                    CreateBeatmapFile(currentBeatmap, i);
                }
                if (GUILayout.Button("Remove This Difficulty Entry", GUILayout.Height(20)))
                {
                    mapInfo.BeatmapDifficultyFiles.RemoveAt(i);
                    // Clear validation messages for this specific entry after removal
                    ClearBeatmapValidationMessages(i);
                    GUI.FocusControl(null);
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
                // No need to write back currentBeatmap explicitly here, as it's already assigned on changes.
            }

            if (GUILayout.Button("Add New BeatMap Difficulty Entry"))
            {
                mapInfo.BeatmapDifficultyFiles.Add(new BeatMapInfo
                {
                    MD5 = "", // Initialize MD5 as empty
                    BeatMapType = new string[0], // Start with no type selected
                    Difficulty = 1,
                    Version = "Default" // Initialize with a default version
                });
                GUI.FocusControl(null);
            }
        }

        /// <summary>
        /// Attempts to create a new empty beatmap file with the generated filename.
        /// </summary>
        private void CreateBeatmapFile(BeatMapInfo beatmap, int index)
        {
            // First, ensure the BeatMapInfo is valid enough to generate a filename.
            if (!ValidateVersionString($"Version_{index}", beatmap.Version) ||
                !ValidateBeatMapTypeSelection($"BeatMapType_{index}", beatmap.BeatMapType) ||
                !ValidateDifficultyLevel($"DifficultyLevel_{index}", beatmap.Difficulty))
            {
                EditorUtility.DisplayDialog("Cannot Create File", "Please correct validation errors in BeatMap Type, Difficulty, and Version before creating the file.", "OK");
                return;
            }

            string beatMapTypeForFile = beatmap.BeatMapType.Length > 0 ? beatmap.BeatMapType[0] : "InvalidType"; // Should be caught by validation
            string generatedFileName = BeatMapUtility.GetBeatMapFile(beatMapTypeForFile, beatmap.Difficulty, beatmap.Version);

            if (string.IsNullOrWhiteSpace(generatedFileName))
            {
                EditorUtility.DisplayDialog("Cannot Create File", "Failed to generate a valid filename. Check BeatMap Type, Difficulty, and Version.", "OK");
                return;
            }

            string fullPath = Path.Combine(GetMapInfoBaseDirectory(), generatedFileName);

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    CLogger.LogInfo($"[MapInfoEditorWindow] Created directory: {directory}");
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Error Creating Directory", $"Failed to create directory '{directory}': {e.Message}", "OK");
                    CLogger.LogError($"[MapInfoEditorWindow] Failed to create directory '{directory}': {e.Message}");
                    return;
                }
            }

            // Check if file already exists
            if (File.Exists(fullPath))
            {
                if (!EditorUtility.DisplayDialog("File Exists", $"A file with the name '{generatedFileName}' already exists at '{directory}'. Do you want to overwrite it?", "Overwrite", "Cancel"))
                {
                    return; // User chose not to overwrite
                }
            }

            try
            {
                // Create an empty YAML file. You might want to put a minimal YAML structure here.
                File.WriteAllText(fullPath, "# Empty Beatmap File\n", System.Text.Encoding.UTF8);
                AssetDatabase.Refresh(); // Refresh Project window to show the new file
                CLogger.LogInfo($"[MapInfoEditorWindow] Created beatmap file: {fullPath}");
                EditorUtility.DisplayDialog("File Created", $"Beatmap file created successfully:\n{fullPath}", "OK");

                // Clear any previous "file not found" messages for this entry
                SetValidationMessage($"GeneratedDifficultyFile_{index}", "", MessageType.None);
                SetValidationMessage($"DifficultyFile_{index}_MD5", "", MessageType.None);

                // Optionally generate MD5 after creation
                _ = GenerateMD5ForBeatmapAsync(generatedFileName, index);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error Creating File", $"Failed to create file '{fullPath}': {e.Message}", "OK");
                CLogger.LogError($"[MapInfoEditorWindow] Failed to create file '{fullPath}': {e.Message}");
            }
        }


        private async Task GenerateMD5ForBeatmapAsync(string difficultyFileName, int beatmapIndex)
        {
            // Clear specific MD5 error message if it exists before recalculating
            SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", "", MessageType.None);

            if (string.IsNullOrWhiteSpace(difficultyFileName))
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", "Generated difficulty file name is empty. Cannot generate MD5.", MessageType.Error);
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "Filename empty";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                Repaint();
                return;
            }

            string baseDirectory = GetMapInfoBaseDirectory();
            string fullPath = Path.Combine(baseDirectory, difficultyFileName);


            if (!File.Exists(fullPath))
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", $"Difficulty file not found at: {fullPath}. Cannot generate MD5.", MessageType.Error);
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "File not found";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                Repaint(); // Update UI
                return;
            }

            try
            {
                byte[] hashBytes = new byte[FileUtility.GetHashSizeInBytes(HashAlgorithmType.MD5)];
                Memory<byte> hashMemory = new Memory<byte>(hashBytes);

                bool success = await FileUtility.ComputeFileHashAsync(fullPath, HashAlgorithmType.MD5, hashMemory, System.Threading.CancellationToken.None);

                if (success)
                {
                    string md5Hex = FileUtility.ToHexString(hashBytes);
                    BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                    tempBeatmap.MD5 = md5Hex;
                    mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                    CLogger.LogInfo($"[MapInfoEditorWindow] MD5 for '{difficultyFileName}' (Entry #{beatmapIndex + 1}): {md5Hex}");
                    SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", "", MessageType.None); // Clear message
                }
                else
                {
                    SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", $"Failed to compute MD5 for: {fullPath}.", MessageType.Error);
                    BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                    tempBeatmap.MD5 = "Error computing";
                    mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                }
            }
            catch (System.Exception ex)
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}_MD5", $"Exception computing MD5 for {fullPath}: {ex.Message}", MessageType.Error);
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "Exception";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
            }
            finally
            {
                Repaint(); // Ensure the UI updates after the async operation
            }
        }

        private void ClearValidationMessage(string fieldKey)
        {
            validationMessages.Remove(fieldKey);
            validationMessageTypes.Remove(fieldKey);
        }

        private void ClearMediaOverrideValidationMessages(int index)
        {
            ClearValidationMessage($"OverrideBeatMapType_{index}");
            ClearValidationMessage($"DuplicateOverride_{index}");
            ClearValidationMessage($"OverrideAudio_{index}");
            ClearValidationMessage($"OverrideVideo_{index}");
            ClearValidationMessage($"OverridePreviewAudio_{index}");
            ClearValidationMessage($"OverridePreviewVideo_{index}");
        }

        /// <summary>
        /// Performs uniqueness validation for Media Overrides.
        /// </summary>
        private void PerformMediaOverrideUniquenessValidation()
        {
            if (mapInfo.MediaOverrides == null) return;

            Dictionary<string, int> seenOverrideTypes = new Dictionary<string, int>();
            for (int i = 0; i < mapInfo.MediaOverrides.Count; i++)
            {
                string beatMapType = mapInfo.MediaOverrides[i].BeatMapType;
                if (string.IsNullOrEmpty(beatMapType))
                {
                    SetValidationMessage($"OverrideBeatMapType_{i}", "A game mode must be selected.", MessageType.Error);
                    continue;
                }

                if (seenOverrideTypes.ContainsKey(beatMapType))
                {
                    int originalIndex = seenOverrideTypes[beatMapType];
                    string errorMsg = $"Duplicate override for game mode '{beatMapType}'. Each mode can only have one override.";
                    SetValidationMessage($"DuplicateOverride_{i}", errorMsg, MessageType.Error);
                    SetValidationMessage($"DuplicateOverride_{originalIndex}", errorMsg, MessageType.Error);
                }
                else
                {
                    seenOverrideTypes.Add(beatMapType, i);
                    SetValidationMessage($"DuplicateOverride_{i}", "", MessageType.None);
                }
            }
        }

        private void LoadYamlFromFile()
        {
            string path = EditorUtility.OpenFilePanel("Load MapInfo YAML", Application.dataPath, "yaml");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string yamlString = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    byte[] yamlBytes = System.Text.Encoding.UTF8.GetBytes(yamlString);
                    mapInfo = VYaml.Serialization.YamlSerializer.Deserialize<MapInfo>(yamlBytes);
                    currentlyLoadedMapInfoPath = path; // Store the path

                    if (mapInfo.InGameBackgroundPictures != null)
                    {
                        currentBackgroundPictures = new List<string>(mapInfo.InGameBackgroundPictures);
                    }
                    else
                    {
                        mapInfo.InGameBackgroundPictures = new string[0];
                        currentBackgroundPictures = new List<string>();
                    }
                    if (mapInfo.BeatmapDifficultyFiles == null)
                    {
                        mapInfo.BeatmapDifficultyFiles = new List<BeatMapInfo>();
                    }
                    if (mapInfo.MediaOverrides == null)
                    {
                        mapInfo.MediaOverrides = new List<BeatMapTypeMediaOverride>();
                    }

                    // Ensure LastModifiedTime has a value to display if it was missing in older files
                    if (string.IsNullOrEmpty(mapInfo.LastModifiedTime))
                    {
                        mapInfo.LastModifiedTime = "N/A (loaded)";
                    }
                    else
                    {
                        // Validate loaded LastModifiedTime format (optional, but good for consistency)
                        if (!System.DateTime.TryParse(mapInfo.LastModifiedTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                        {
                            CLogger.LogWarning($"[MapInfoEditorWindow] Loaded LastModifiedTime '{mapInfo.LastModifiedTime}' is not in a valid ISO 8601 format. It will be updated on save.");
                            mapInfo.LastModifiedTime = "Invalid format"; // Indicate invalid format in UI
                        }
                    }

                    newBackgroundPicturePath = "";
                    validationMessages.Clear(); // Clear all messages on successful load
                    validationMessageTypes.Clear();

                    // NEW: Post-load validation for difficulty files existence
                    PerformPostLoadBeatmapFileValidation();

                    CLogger.LogInfo($"[MapInfoEditorWindow] MapInfo loaded from: {path}");
                    EditorUtility.DisplayDialog("Load Successful", $"MapInfo data loaded from:\n{path}", "OK");
                    GUI.FocusControl(null);
                    Repaint();
                }
                catch (VYaml.Parser.YamlParserException ype)
                {
                    string detailedMessage = $"Error: {ype.Message}\n\nThis typically indicates a problem with the YAML file's formatting. Please check the file structure around the location indicated in the error message.";
                    CLogger.LogError($"[MapInfoEditorWindow] Error parsing YAML syntax in file: {path}\n{ype.Message}\nDetails: {ype.ToString()}");
                    EditorUtility.DisplayDialog("YAML Syntax Error",
                        $"Failed to parse YAML file: {Path.GetFileName(path)}\n\n{detailedMessage}\n\nCheck the console for the full file path and more details.", "OK");
                }
                catch (VYaml.Serialization.YamlSerializerException yse)
                {
                    string detailedMessage = $"Error: {yse.Message}\n\nThis error often occurs if the YAML data doesn't match the expected structure or types.";
                    CLogger.LogError($"[MapInfoEditorWindow] Error deserializing YAML data from file: {path}\n{yse.Message}\nDetails: {yse.ToString()}");
                    EditorUtility.DisplayDialog("YAML Data/Structure Error",
                        $"Failed to map YAML data in file: {Path.GetFileName(path)}\n\n{detailedMessage}\n\nCheck the console for the full file path and more details.", "OK");
                }
                catch (System.Exception ex)
                {
                    CLogger.LogError($"[MapInfoEditorWindow] Error loading YAML file: {path}\nGeneral Error: {ex.ToString()}");
                    EditorUtility.DisplayDialog("Error Loading File",
                        $"Failed to load YAML file: {Path.GetFileName(path)}\nError: {ex.Message}\n\nCheck the console for the full file path and more details.", "OK");
                }
            }
        }

        /// <summary>
        /// Performs validation after loading a MapInfo file to check for missing beatmap difficulty files.
        /// If a file is missing, prompts the user to either remove the entry or keep it (with a warning).
        /// </summary>
        private void PerformPostLoadBeatmapFileValidation()
        {
            List<int> indicesToRemove = new List<int>();
            string baseDir = GetMapInfoBaseDirectory();

            for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
            {
                BeatMapInfo beatmap = mapInfo.BeatmapDifficultyFiles[i];
                string beatMapTypeForFile = beatmap.BeatMapType != null && beatmap.BeatMapType.Length > 0 ? beatmap.BeatMapType[0] : "N/A";

                // Use BeatMapUtility's internal validation first to get a clean filename
                string generatedFileName = BeatMapUtility.GetBeatMapFile(beatMapTypeForFile, beatmap.Difficulty, beatmap.Version);

                // Check if GetBeatMapFile returned an empty string, meaning invalid components
                if (string.IsNullOrEmpty(generatedFileName))
                {
                    string message = $"Beatmap difficulty entry #{i + 1} has invalid components (Type: '{beatMapTypeForFile}', Difficulty: {beatmap.Difficulty}, Version: '{beatmap.Version}') preventing filename generation.\n\nDo you want to remove this entry from the MapInfo?";
                    if (EditorUtility.DisplayDialog("Invalid Beatmap Entry", message, "Remove Entry", "Keep (with warning)"))
                    {
                        indicesToRemove.Add(i);
                    }
                    else
                    {
                        SetValidationMessage($"GeneratedDifficultyFile_{i}", "Invalid components, filename could not be generated.", MessageType.Error);
                        // Make sure to also set error for specific fields if they were invalid initially
                        ValidateVersionString($"Version_{i}", beatmap.Version);
                        ValidateBeatMapTypeSelection($"BeatMapType_{i}", beatmap.BeatMapType);
                        ValidateDifficultyLevel($"DifficultyLevel_{i}", beatmap.Difficulty);
                    }
                }
                else if (!File.Exists(Path.Combine(baseDir, generatedFileName)))
                {
                    string message = $"Beatmap difficulty file '{generatedFileName}' (Entry #{i + 1}) referenced in MapInfo is missing from disk at '{Path.Combine(baseDir, generatedFileName)}'.\n\nDo you want to remove this entry from the MapInfo?";
                    if (EditorUtility.DisplayDialog("Missing Beatmap File", message, "Remove Entry", "Keep (with warning)"))
                    {
                        indicesToRemove.Add(i);
                    }
                    else
                    {
                        SetValidationMessage($"GeneratedDifficultyFile_{i}", $"File missing: {generatedFileName}", MessageType.Warning);
                    }
                }
            }

            // Remove entries in reverse order to avoid index shifting issues
            for (int i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                int originalIndex = indicesToRemove[i];
                mapInfo.BeatmapDifficultyFiles.RemoveAt(originalIndex);
                ClearBeatmapValidationMessages(originalIndex); // Also clear any messages for the removed entry
                CLogger.LogWarning($"[MapInfoEditorWindow] Removed missing/invalid beatmap entry #{originalIndex + 1} from MapInfo.");
            }

            if (indicesToRemove.Count > 0)
            {
                EditorUtility.DisplayDialog("Beatmap Files Checked", $"Finished checking beatmap difficulty files. {indicesToRemove.Count} missing/invalid entries were removed (or kept with warnings).", "OK");
            }
        }


        private void GenerateYaml()
        {
            string defaultFileName = "MapInfo.yaml";
            string defaultDirectory;

            // If a file was already loaded, suggest saving in the same directory.
            // Otherwise, suggest the default MapInfos/UniqueID folder.
            if (!string.IsNullOrEmpty(currentlyLoadedMapInfoPath))
            {
                defaultDirectory = Path.GetDirectoryName(currentlyLoadedMapInfoPath);
                defaultFileName = Path.GetFileName(currentlyLoadedMapInfoPath); // Retain original filename if loading an existing file
            }
            else
            {
                // Suggest a default directory based on the UniqueID if it's a new map
                defaultDirectory = Path.Combine(Application.dataPath, "MapInfos", mapInfo.UniqueID);
            }

            // Allow the user to choose the save location and filename
            string savePath = EditorUtility.SaveFilePanel(
                "Save MapInfo YAML",
                defaultDirectory,
                defaultFileName,
                "yaml"
            );

            if (string.IsNullOrEmpty(savePath))
            {
                CLogger.LogInfo("[MapInfoEditorWindow] Save operation cancelled by user.");
                return; // User cancelled the save dialog
            }

            // Ensure the directory exists for the chosen save path
            string directory = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    CLogger.LogInfo($"[MapInfoEditorWindow] Created directory: {directory}");
                }
                catch (Exception e)
                {
                    CLogger.LogError($"[MapInfoEditorWindow] Failed to create directory for MapInfo: {directory}. Error: {e.Message}");
                    EditorUtility.DisplayDialog("Error Creating Directory", $"Failed to create directory:\n{directory}\n\nError: {e.Message}\n\nSaving aborted.", "OK");
                    return; // Abort save if directory cannot be created
                }
            }

            try
            {
                // Ensure LastModifiedTime is set before serialization
                mapInfo.LastModifiedTime = System.DateTime.UtcNow.ToString("o");

                string yamlStr = VYaml.Serialization.YamlSerializer.SerializeToString(mapInfo);
                File.WriteAllText(savePath, yamlStr, System.Text.Encoding.UTF8); // Use File.WriteAllText which overwrites by default

                currentlyLoadedMapInfoPath = savePath; // Update loaded path to the new save location

                validationMessages.Clear(); // Clear all messages on successful save
                validationMessageTypes.Clear();

                AssetDatabase.Refresh(); // Refresh Project window to ensure Unity sees the new/updated file.

                CLogger.LogInfo($"[MapInfoEditorWindow] MapInfo YAML saved to: {savePath}");
                EditorUtility.DisplayDialog("Save Successful", $"MapInfo YAML saved to:\n{savePath}", "OK");
                Repaint(); // To update LastModifiedTime display
            }
            catch (Exception ex)
            {
                CLogger.LogError($"[MapInfoEditorWindow] Error saving YAML to {savePath}: {ex.ToString()}");
                EditorUtility.DisplayDialog("Error Saving YAML", $"Failed to save YAML file to:\n{savePath}\n\nError: {ex.Message}\n\nCheck console for more details.", "OK");
            }
        }

        /// <summary>
        /// Sanitizes a string to be a valid filename, removing invalid characters and handling reserved names.
        /// </summary>
        /// <param name="name">The input string.</param>
        /// <returns>A sanitized filename.</returns>
        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "untitled";
            }

            // Remove invalid characters
            string sanitized = new string(name.Where(c => !InvalidFileNameChars.Contains(c)).ToArray());

            // Handle reserved names for Windows (case-insensitive)
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                foreach (string reserved in ReservedWindowsNames)
                {
                    if (sanitized.Equals(reserved, StringComparison.OrdinalIgnoreCase))
                    {
                        sanitized += "_"; // Append underscore to avoid conflict
                        break;
                    }
                }
            }
            return sanitized;
        }

        /// <summary>
        /// Validates the UniqueID as a safe folder name across platforms.
        /// Displays a warning if invalid.
        /// </summary>
        /// <param name="id">The UniqueID string to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private bool ValidateUniqueID(string id)
        {
            string message = "";
            MessageType type = MessageType.None;

            if (string.IsNullOrWhiteSpace(id))
            {
                message = "Unique ID cannot be empty or whitespace.";
                type = MessageType.Error;
            }
            else if (PathContainsInvalidChars(id, true)) // Treat UniqueID as a directory name
            {
                message = $"Unique ID contains invalid characters for a folder name (e.g., {string.Join(", ", InvalidPathChars.Where(c => id.Contains(c)).Select(c => $"'{c}'"))}).";
                type = MessageType.Error;
            }
            else if (PathIsReserved(id))
            {
                message = $"Unique ID '{id}' is a reserved name on Windows. This might cause issues.";
                type = MessageType.Warning;
            }

            SetValidationMessage("UniqueID", message, type);
            return type != MessageType.Error; // Return false only if it's an error
        }

        /// <summary>
        /// Gets the base directory for relative paths (where the MapInfo YAML is or would be saved).
        /// </summary>
        /// <returns>The full path to the MapInfo's unique ID folder.</returns>
        private string GetMapInfoBaseDirectory()
        {
            // If we have a currently loaded path, use its directory.
            // Otherwise, assume the standard new mapinfo path (Assets/MapInfos/UniqueID).
            // This ensures that relative paths for media and beatmap files are resolved correctly
            // either to the loaded file's location or the *intended* save location for a new map.
            if (!string.IsNullOrEmpty(currentlyLoadedMapInfoPath))
            {
                return Path.GetDirectoryName(currentlyLoadedMapInfoPath);
            }
            // Fallback for new MapInfo before initial save
            return Path.Combine(Application.dataPath, "MapInfos", mapInfo.UniqueID);
        }

        /// <summary>
        /// Validates a relative file path for media files. Checks for invalid characters and if the file exists.
        /// The path is relative to the MapInfo's unique ID folder.
        /// </summary>
        /// <param name="fieldNameKey">A unique key for this field's validation message (e.g., "AudioFile", "BackgroundPicture_0").</param>
        /// <param name="relativePath">The relative path to validate (e.g., "audio.ogg").</param>
        /// <param name="allowEmpty">If true, an empty path is considered valid.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private bool ValidateRelativePath(string fieldNameKey, string relativePath, bool allowEmpty = false)
        {
            string message = "";
            MessageType type = MessageType.None;

            if (allowEmpty && string.IsNullOrEmpty(relativePath))
            {
                SetValidationMessage(fieldNameKey, "", MessageType.None); // Clear any previous message
                return true;
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                message = "Path cannot be empty or whitespace.";
                type = MessageType.Error;
            }
            else if (PathContainsInvalidChars(relativePath))
            {
                message = "Path contains invalid characters.";
                type = MessageType.Error;
            }
            else if (PathIsReserved(relativePath))
            {
                message = $"Uses a reserved filename '{Path.GetFileName(relativePath)}' on Windows.";
                type = MessageType.Warning;
            }
            else
            {
                string fullPath = Path.Combine(GetMapInfoBaseDirectory(), relativePath);

                if (!File.Exists(fullPath))
                {
                    message = $"File not found at: {fullPath}.";
                    type = MessageType.Warning; // Changed to warning for non-existence, error for invalid path
                }
            }

            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error; // Only return false if it's a hard error
        }

        /// <summary>
        /// Validates a generated beatmap difficulty file path for existence.
        /// This is distinct from ValidateRelativePath because the filename itself is generated.
        /// </summary>
        private bool ValidateGeneratedBeatmapFilePath(string fieldNameKey, string generatedFileName)
        {
            string message = "";
            MessageType type = MessageType.None;

            if (string.IsNullOrWhiteSpace(generatedFileName))
            {
                message = "Generated filename is empty. This usually means the BeatMapType or Version is invalid.";
                type = MessageType.Error;
            }
            else
            {
                string fullPath = Path.Combine(GetMapInfoBaseDirectory(), generatedFileName);

                if (!File.Exists(fullPath))
                {
                    message = $"Generated difficulty file not found at: {fullPath}. Please ensure the file exists or create it using the 'Auto-Create File' button.";
                    type = MessageType.Warning; // Changed to warning to allow saving if user accepts missing file
                }
            }

            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error;
        }


        /// <summary>
        /// Validates the version string for BeatMapInfo entries using BeatMapUtility's internal logic.
        /// </summary>
        private bool ValidateVersionString(string fieldNameKey, string version)
        {
            string message = "";
            MessageType type = MessageType.None;

            if (string.IsNullOrWhiteSpace(version))
            {
                message = "Version string cannot be empty or whitespace.";
                type = MessageType.Error;
            }
            else if (version.Length > 24)
            {
                message = $"Version string '{version}' exceeds maximum length of 24 characters.";
                type = MessageType.Error;
            }
            else if (!Regex.IsMatch(version, @"^[a-zA-Z0-9\-]+$"))
            {
                message = $"Version string '{version}' contains invalid characters. Only alphanumeric characters and hyphens are allowed.";
                type = MessageType.Error;
            }

            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error;
        }

        /// <summary>
        /// Validates that exactly one BeatMapType is selected.
        /// </summary>
        private bool ValidateBeatMapTypeSelection(string fieldNameKey, string[] selectedTypes)
        {
            string message = "";
            MessageType type = MessageType.None;

            // Now, we expect exactly one selection.
            if (selectedTypes == null || selectedTypes.Length != 1)
            {
                message = "Exactly one BeatMap Type must be selected.";
                type = MessageType.Error;
            }
            // Optional: Also check if the selected type is actually a valid constant
            else if (!BeatMapUtility.ValidBeatMapTypes.Contains(selectedTypes[0]))
            {
                message = $"Selected BeatMap Type '{selectedTypes[0]}' is not a valid constant.";
                type = MessageType.Error;
            }


            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error;
        }

        /// <summary>
        /// Validates the difficulty level is within the valid range (0-99).
        /// </summary>
        private bool ValidateDifficultyLevel(string fieldNameKey, int difficulty)
        {
            string message = "";
            MessageType type = MessageType.None;

            if (difficulty < 0 || difficulty > 99)
            {
                message = "Difficulty must be between 0 and 99.";
                type = MessageType.Error;
            }

            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error;
        }

        /// <summary>
        /// Stores a validation message and its type for a given field.
        /// </summary>
        private void SetValidationMessage(string fieldKey, string message, MessageType type)
        {
            if (string.IsNullOrEmpty(message) || type == MessageType.None)
            {
                validationMessages.Remove(fieldKey);
                validationMessageTypes.Remove(fieldKey);
            }
            else
            {
                validationMessages[fieldKey] = message;
                validationMessageTypes[fieldKey] = type;
            }
        }

        /// <summary>
        /// Displays a HelpBox for a given field if a validation message exists.
        /// </summary>
        private void DisplayValidationMessage(string fieldKey)
        {
            if (validationMessages.TryGetValue(fieldKey, out string message))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    validationMessageTypes.TryGetValue(fieldKey, out MessageType type);
                    EditorGUILayout.HelpBox(message, type);
                }
            }
        }


        /// <summary>
        /// Checks if a given path string contains any characters illegal for paths.
        /// </summary>
        /// <param name="path">The path string to check.</param>
        /// <param name="checkDirectoryNameSpecific">If true, checks for directory name specific invalid characters like ':' for drives.</param>
        /// <returns>True if invalid characters are found, false otherwise.</returns>
        private bool PathContainsInvalidChars(string path, bool checkDirectoryNameSpecific = false)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Check for general invalid path characters
            if (path.Any(c => InvalidPathChars.Contains(c)))
            {
                return true;
            }

            // For directory names, also check characters specific to drive letters like ':' if it's not part of a valid drive specification.
            // Simplified check: if it contains ':', but not as part of "C:\", it's likely invalid.
            if (checkDirectoryNameSpecific && path.Contains(':'))
            {
                // Simple regex to check for ':' not followed by '\' (typical drive letter)
                // Or ':' anywhere else in the string
                if (Regex.IsMatch(path, @"(?<![a-zA-Z]):(?!\\)") || Regex.IsMatch(path, @"^[^:]+:$"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the filename part of a path is a reserved name on Windows.
        /// This method is less strict than PathContainsInvalidChars, it's specific to Windows reserved names.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the filename is a reserved Windows name, false otherwise.</returns>
        private bool PathIsReserved(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                foreach (string reserved in ReservedWindowsNames)
                {
                    if (fileName.Equals(reserved, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Performs a full validation of all critical fields before saving.
        /// </summary>
        /// <returns>True if all validations pass, false otherwise.</returns>
        private bool PerformFullValidation()
        {
            bool allValid = true;

            // Validate UniqueID
            if (!ValidateUniqueID(mapInfo.UniqueID)) allValid = false;

            // Validate Media Files
            if (!ValidateRelativePath("AudioFile", mapInfo.AudioFile, false)) allValid = false;
            if (!ValidateRelativePath("PreviewAudioFile", mapInfo.PreviewAudioFile, true)) allValid = false;
            if (!ValidateRelativePath("VideoFile", mapInfo.VideoFile, true)) allValid = false;
            if (!ValidateRelativePath("PreviewVideoFile", mapInfo.PreviewVideoFile, true)) allValid = false;

            // Validate Background Pictures
            for (int i = 0; i < currentBackgroundPictures.Count; i++)
            {
                if (!ValidateRelativePath($"BackgroundPicture_{i}", currentBackgroundPictures[i], false))
                {
                    allValid = false;
                }
            }

            // Validate Media Overrides
            if (mapInfo.MediaOverrides != null)
            {
                for (int i = 0; i < mapInfo.MediaOverrides.Count; i++)
                {
                    var o = mapInfo.MediaOverrides[i];
                    if (!ValidateRelativePath($"OverrideAudio_{i}", o.AudioFile, true)) allValid = false;
                    if (!ValidateRelativePath($"OverrideVideo_{i}", o.VideoFile, true)) allValid = false;
                    if (!ValidateRelativePath($"OverridePreviewAudio_{i}", o.PreviewAudioFile, true)) allValid = false;
                    if (!ValidateRelativePath($"OverridePreviewVideo_{i}", o.PreviewVideoFile, true)) allValid = false;
                }
            }

            // Validate Beatmap Difficulty Files
            if (mapInfo.BeatmapDifficultyFiles.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Warning", "No Beatmap Difficulty Files are defined. A map usually requires at least one difficulty.", "OK");
            }
            else
            {
                for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
                {
                    BeatMapInfo beatmap = mapInfo.BeatmapDifficultyFiles[i]; // Use a local copy for consistency

                    // Validate the version string
                    if (!ValidateVersionString($"Version_{i}", beatmap.Version))
                    {
                        allValid = false;
                    }

                    // Validate that exactly one BeatMapType is selected
                    if (!ValidateBeatMapTypeSelection($"BeatMapType_{i}", beatmap.BeatMapType))
                    {
                        allValid = false;
                    }

                    // Validate difficulty range
                    if (!ValidateDifficultyLevel($"DifficultyLevel_{i}", beatmap.Difficulty))
                    {
                        allValid = false;
                    }

                    // Only generate and validate filename if basic components are valid for generation
                    string generatedFileName = string.Empty;

                    // Only attempt to generate if BeatMapType is valid (length check is part of ValidateBeatMapTypeSelection)
                    if (beatmap.BeatMapType != null && beatmap.BeatMapType.Length == 1)
                    {
                        generatedFileName = BeatMapUtility.GetBeatMapFile(
                            beatmap.BeatMapType[0],
                            beatmap.Difficulty,
                            beatmap.Version
                        );

                        // If GetBeatMapFile returned an empty string, it means an internal error occurred
                        if (string.IsNullOrEmpty(generatedFileName))
                        {
                            SetValidationMessage($"GeneratedDifficultyFile_{i}", "Failed to generate filename due to invalid BeatMap Type, Difficulty, or Version.", MessageType.Error);
                            allValid = false;
                        }
                        else if (!ValidateGeneratedBeatmapFilePath($"GeneratedDifficultyFile_{i}", generatedFileName))
                        {
                            // ValidateGeneratedBeatmapFilePath will set a Warning if file is just missing
                            // but will set Error if the filename itself is invalid.
                            // If it's an Error (e.g., invalid chars), then `allValid` should be false.
                            // If it's a Warning (file not found), `allValid` can still be true, but we prompt the user.
                            MessageType currentType;
                            if (validationMessageTypes.TryGetValue($"GeneratedDifficultyFile_{i}", out currentType) && currentType == MessageType.Error)
                            {
                                allValid = false;
                            }
                        }
                    }
                    else
                    {
                        // If BeatMapType is not exactly one, SetValidationMessage is already called by ValidateBeatMapTypeSelection,
                        // and it will likely be an Error, setting allValid to false.
                        // No additional specific message needed here.
                    }
                }
            }

            // Important: Re-run uniqueness validation as a final check before deciding on `allValid`
            // This ensures that any duplicates identified during GUI updates are properly flagged
            // and contribute to the `allValid` status.
            PerformBeatmapUniquenessValidation();
            if (validationMessageTypes.Any(kvp => kvp.Value == MessageType.Error && kvp.Key.StartsWith("DuplicateEntry_")))
            {
                allValid = false;
            }

            // After all individual validations, check if there are any *Error* messages globally.
            // If there are errors, we prevent saving.
            if (validationMessageTypes.Any(kvp => kvp.Value == MessageType.Error))
            {
                allValid = false;
            }

            return allValid;
        }

        private int ConvertStringArrayToMask(string[] selectedTypes, string[] allOptions)
        {
            if (selectedTypes == null || selectedTypes.Length == 0 || allOptions == null || allOptions.Length == 0) return 0;
            int mask = 0;
            for (int i = 0; i < allOptions.Length; i++)
            {
                if (selectedTypes.Contains(allOptions[i])) mask |= (1 << i);
            }
            return mask;
        }

        private string[] ConvertMaskToStringArray(int mask, string[] allOptions)
        {
            if (allOptions == null || allOptions.Length == 0) return new string[0];
            List<string> selected = new List<string>();
            for (int i = 0; i < allOptions.Length; i++)
            {
                if ((mask & (1 << i)) != 0) selected.Add(allOptions[i]);
            }
            return selected.ToArray();
        }
    }
}