using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RhythmPulse.GameplayData.Runtime;
using VYaml.Serialization;
using VYaml.Parser;
using System.Reflection; // For reading BeatMapTypeConstant
using System.Threading.Tasks; // For async MD5 calculation
using CycloneGames.Utility.Runtime;
using System; // For FileUtility
using System.Text.RegularExpressions; // For more robust path validation

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
                var constants = typeof(BeatMapTypeConstant)
                    .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
                    .Select(fi => (string)fi.GetRawConstantValue())
                    .ToArray();
                beatMapTypeOptions = constants;

                if (beatMapTypeOptions.Length == 0)
                {
                    Debug.LogWarning("[MapInfoEditorWindow] No string constants found in BeatMapTypeConstant. BeatMapType selection will be empty.");
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
                BeatmapDifficultyFiles = new List<BeatMapInfo>()
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
            mapInfo.AudioFile = EditorGUILayout.TextField(new GUIContent("Audio File", "Path to the main audio file (e.g., 'audio.ogg'). Should be relative to the MapInfo file or a common root."), mapInfo.AudioFile);
            ValidateRelativePath("AudioFile", mapInfo.AudioFile, currentlyLoadedMapInfoPath);
            DisplayValidationMessage("AudioFile");

            mapInfo.PreviewAudioFile = EditorGUILayout.TextField(new GUIContent("Preview Audio File", "Optional path to a short audio preview clip."), mapInfo.PreviewAudioFile);
            ValidateRelativePath("PreviewAudioFile", mapInfo.PreviewAudioFile, currentlyLoadedMapInfoPath, true);
            DisplayValidationMessage("PreviewAudioFile");

            mapInfo.VideoFile = EditorGUILayout.TextField(new GUIContent("Video File", "Optional path to a background video file."), mapInfo.VideoFile);
            ValidateRelativePath("VideoFile", mapInfo.VideoFile, currentlyLoadedMapInfoPath, true);
            DisplayValidationMessage("VideoFile");

            mapInfo.PreviewVideoFile = EditorGUILayout.TextField(new GUIContent("Preview Video File", "Optional path to a short video preview clip."), mapInfo.PreviewVideoFile);
            ValidateRelativePath("PreviewVideoFile", mapInfo.PreviewVideoFile, currentlyLoadedMapInfoPath, true);
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
            EditorGUILayout.LabelField("In-Game Background Pictures", EditorStyles.boldLabel);
            for (int i = 0; i < currentBackgroundPictures.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                currentBackgroundPictures[i] = EditorGUILayout.TextField($"Picture {i + 1}", currentBackgroundPictures[i]);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    currentBackgroundPictures.RemoveAt(i);
                    // Clear validation message for this specific entry after removal
                    validationMessages.Remove($"BackgroundPicture_{i}");
                    validationMessageTypes.Remove($"BackgroundPicture_{i}");
                    GUI.FocusControl(null);
                    break;
                }
                EditorGUILayout.EndHorizontal();
                ValidateRelativePath($"BackgroundPicture_{i}", currentBackgroundPictures[i], currentlyLoadedMapInfoPath);
                DisplayValidationMessage($"BackgroundPicture_{i}");
            }
            EditorGUILayout.BeginHorizontal();
            newBackgroundPicturePath = EditorGUILayout.TextField("New Picture Path", newBackgroundPicturePath);
            if (GUILayout.Button("Add Picture", GUILayout.Width(100)) && !string.IsNullOrWhiteSpace(newBackgroundPicturePath))
            {
                // Basic validation before adding
                if (!PathContainsInvalidChars(newBackgroundPicturePath) && !PathIsReserved(newBackgroundPicturePath))
                {
                    currentBackgroundPictures.Add(newBackgroundPicturePath);
                    newBackgroundPicturePath = "";
                    GUI.FocusControl(null);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Path", "The new background picture path contains invalid characters or is a reserved filename. Please correct it.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // --- Beatmap Difficulty Files ---
            EditorGUILayout.LabelField("Beatmap Difficulty Files", EditorStyles.boldLabel);
            if (mapInfo.BeatmapDifficultyFiles == null)
            {
                mapInfo.BeatmapDifficultyFiles = new List<BeatMapInfo>();
            }

            for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
            {
                BeatMapInfo currentBeatmap = mapInfo.BeatmapDifficultyFiles[i]; // This is a struct, so it's a copy.

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Difficulty Entry #{i + 1}", EditorStyles.miniBoldLabel);

                string newDifficultyFile = EditorGUILayout.TextField(new GUIContent("Difficulty File", "Filename of the beatmap data (e.g., 'mania_hard.yaml'). Relative to MapInfo file location."), currentBeatmap.DifficultyFile);
                if (newDifficultyFile != currentBeatmap.DifficultyFile)
                {
                    currentBeatmap.DifficultyFile = newDifficultyFile;
                    mapInfo.BeatmapDifficultyFiles[i] = currentBeatmap; // Write back immediately to reflect changes
                }
                ValidateRelativePath($"DifficultyFile_{i}", currentBeatmap.DifficultyFile, currentlyLoadedMapInfoPath);
                DisplayValidationMessage($"DifficultyFile_{i}");


                EditorGUILayout.BeginHorizontal();
                currentBeatmap.MD5 = EditorGUILayout.TextField(new GUIContent("MD5 Hash", "MD5 hash of the difficulty file. Click 'Generate' to calculate."), currentBeatmap.MD5);
                if (GUILayout.Button("Generate MD5", GUILayout.Width(100)))
                {
                    // Assign to a local variable for the async lambda
                    int local_i = i;
                    // Ensure the latest value is used
                    string fileName = mapInfo.BeatmapDifficultyFiles[local_i].DifficultyFile;
                    _ = GenerateMD5ForBeatmapAsync(fileName, local_i);
                }
                EditorGUILayout.EndHorizontal();


                if (beatMapTypeOptions != null && beatMapTypeOptions.Length > 0)
                {
                    int currentMask = ConvertStringArrayToMask(currentBeatmap.BeatMapType, beatMapTypeOptions);
                    int newMask = EditorGUILayout.MaskField(
                        new GUIContent("BeatMap Types", "Select the game modes this difficulty applies to."),
                        currentMask, beatMapTypeOptions);
                    if (newMask != currentMask)
                    {
                        currentBeatmap.BeatMapType = ConvertMaskToStringArray(newMask, beatMapTypeOptions);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No BeatMapType options available. Check BeatMapTypeConstant.", MessageType.Warning);
                }
                currentBeatmap.Difficulty = EditorGUILayout.IntField("Difficulty Level", currentBeatmap.Difficulty);

                if (GUILayout.Button("Remove This Difficulty Entry", GUILayout.Height(20)))
                {
                    mapInfo.BeatmapDifficultyFiles.RemoveAt(i);
                    // Clear validation message for this specific entry after removal
                    validationMessages.Remove($"DifficultyFile_{i}");
                    validationMessageTypes.Remove($"DifficultyFile_{i}");
                    GUI.FocusControl(null);
                    break;
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
                mapInfo.BeatmapDifficultyFiles[i] = currentBeatmap; // Write the modified struct back
            }

            if (GUILayout.Button("Add New BeatMap Difficulty Entry"))
            {
                mapInfo.BeatmapDifficultyFiles.Add(new BeatMapInfo
                {
                    DifficultyFile = "GameMode_DifficultyName.yaml",
                    MD5 = "", // Initialize MD5 as empty
                    BeatMapType = new string[0],
                    Difficulty = 1
                });
                GUI.FocusControl(null);
            }

            EditorGUILayout.Space(20);
            if (GUILayout.Button("Save MapInfo to YAML File", GUILayout.Height(30)))
            {
                // Clear existing messages before full validation
                validationMessages.Clear();
                validationMessageTypes.Clear();

                // Pre-save validation
                if (PerformFullValidation())
                {
                    mapInfo.LastModifiedTime = System.DateTime.UtcNow.ToString("o"); // ISO 8601 format
                    mapInfo.InGameBackgroundPictures = currentBackgroundPictures.ToArray();
                    GenerateYaml();
                }
                else
                {
                    EditorUtility.DisplayDialog("Validation Failed", "Please correct the highlighted errors before saving.", "OK");
                    Repaint(); // Force repaint to show all validation messages
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private async Task GenerateMD5ForBeatmapAsync(string difficultyFileName, int beatmapIndex)
        {
            // Clear specific MD5 error message if it exists before recalculating
            validationMessages.Remove($"DifficultyFile_{beatmapIndex}_MD5");
            validationMessageTypes.Remove($"DifficultyFile_{beatmapIndex}_MD5");

            if (string.IsNullOrWhiteSpace(difficultyFileName))
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}", "Difficulty file name is empty. Cannot generate MD5.", MessageType.Error);
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "Filename empty";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                Repaint();
                return;
            }

            string baseDirectory = Application.dataPath; // Default if no mapinfo loaded
            if (!string.IsNullOrEmpty(currentlyLoadedMapInfoPath))
            {
                baseDirectory = Path.GetDirectoryName(currentlyLoadedMapInfoPath);
            }
            else
            {
                Debug.LogWarning($"[MapInfoEditorWindow] MapInfo file not loaded or path unknown. Assuming difficulty file '{difficultyFileName}' is relative to Assets folder. For best results, load the MapInfo file first.");
            }

            string fullPath = Path.Combine(baseDirectory, difficultyFileName);

            if (!File.Exists(fullPath))
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}", $"Difficulty file not found at: {fullPath}. Cannot generate MD5.", MessageType.Error);
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
                    Debug.Log($"[MapInfoEditorWindow] MD5 for '{difficultyFileName}' (Entry #{beatmapIndex + 1}): {md5Hex}");
                    // Optionally, clear the warning if it was previously set for this specific field
                    SetValidationMessage($"DifficultyFile_{beatmapIndex}", "", MessageType.None); // Clear message
                }
                else
                {
                    SetValidationMessage($"DifficultyFile_{beatmapIndex}", $"Failed to compute MD5 for: {fullPath}.", MessageType.Error);
                    BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                    tempBeatmap.MD5 = "Error computing";
                    mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                }
            }
            catch (System.Exception ex)
            {
                SetValidationMessage($"DifficultyFile_{beatmapIndex}", $"Exception computing MD5 for {fullPath}: {ex.Message}", MessageType.Error);
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "Exception";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
            }
            finally
            {
                Repaint(); // Ensure the UI updates after the async operation
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
                    // Ensure LastModifiedTime has a value to display if it was missing in older files
                    if (string.IsNullOrEmpty(mapInfo.LastModifiedTime))
                    {
                        mapInfo.LastModifiedTime = "N/A (loaded)";
                    } else {
                        // Validate loaded LastModifiedTime format (optional, but good for consistency)
                        if (!System.DateTime.TryParse(mapInfo.LastModifiedTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                        {
                            Debug.LogWarning($"[MapInfoEditorWindow] Loaded LastModifiedTime '{mapInfo.LastModifiedTime}' is not in a valid ISO 8601 format. It will be updated on save.");
                            mapInfo.LastModifiedTime = "Invalid format"; // Indicate invalid format in UI
                        }
                    }

                    newBackgroundPicturePath = "";
                    validationMessages.Clear(); // Clear all messages on successful load
                    validationMessageTypes.Clear();

                    Debug.Log($"[MapInfoEditorWindow] MapInfo loaded from: {path}");
                    EditorUtility.DisplayDialog("Load Successful", $"MapInfo data loaded from:\n{path}", "OK");
                    GUI.FocusControl(null);
                    Repaint();
                }
                catch (VYaml.Parser.YamlParserException ype)
                {
                    string detailedMessage = $"Error: {ype.Message}\n\nThis typically indicates a problem with the YAML file's formatting. Please check the file structure around the location indicated in the error message.";
                    Debug.LogError($"[MapInfoEditorWindow] Error parsing YAML syntax in file: {path}\n{ype.Message}\nDetails: {ype.ToString()}");
                    EditorUtility.DisplayDialog("YAML Syntax Error",
                        $"Failed to parse YAML file: {Path.GetFileName(path)}\n\n{detailedMessage}\n\nCheck the console for the full file path and more details.", "OK");
                }
                catch (VYaml.Serialization.YamlSerializerException yse)
                {
                    string detailedMessage = $"Error: {yse.Message}\n\nThis error often occurs if the YAML data doesn't match the expected structure or types.";
                    Debug.LogError($"[MapInfoEditorWindow] Error deserializing YAML data from file: {path}\n{yse.Message}\nDetails: {yse.ToString()}");
                    EditorUtility.DisplayDialog("YAML Data/Structure Error",
                        $"Failed to map YAML data in file: {Path.GetFileName(path)}\n\n{detailedMessage}\n\nCheck the console for the full file path and more details.", "OK");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[MapInfoEditorWindow] Error loading YAML file: {path}\nGeneral Error: {ex.ToString()}");
                    EditorUtility.DisplayDialog("Error Loading File",
                        $"Failed to load YAML file: {Path.GetFileName(path)}\nError: {ex.Message}\n\nCheck the console for the full file path and more details.", "OK");
                }
            }
        }

        private void GenerateYaml()
        {
            string defaultFileName = string.IsNullOrWhiteSpace(mapInfo.DisplayName) ? "mapinfo_untitled" : SanitizeFileName(mapInfo.DisplayName);
            // Default save directory to where the file was loaded from, or Application.dataPath
            string initialSaveDirectory = string.IsNullOrEmpty(currentlyLoadedMapInfoPath) ? Application.dataPath : Path.GetDirectoryName(currentlyLoadedMapInfoPath);
            string path = EditorUtility.SaveFilePanel("Save MapInfo YAML", initialSaveDirectory, defaultFileName + ".yaml", "yaml");

            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // Ensure LastModifiedTime is set before serialization
                    mapInfo.LastModifiedTime = System.DateTime.UtcNow.ToString("o");

                    string yamlStr = VYaml.Serialization.YamlSerializer.SerializeToString(mapInfo);
                    File.WriteAllText(path, yamlStr, System.Text.Encoding.UTF8);
                    currentlyLoadedMapInfoPath = path; // Update loaded path on successful save

                    validationMessages.Clear(); // Clear all messages on successful save
                    validationMessageTypes.Clear();

                    Debug.Log($"[MapInfoEditorWindow] MapInfo YAML saved to: {path}");
                    EditorUtility.DisplayDialog("Save Successful", $"MapInfo YAML saved to:\n{path}", "OK");
                    Repaint(); // To update LastModifiedTime display
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[MapInfoEditorWindow] Error saving YAML: {ex.ToString()}");
                    EditorUtility.DisplayDialog("Error Saving YAML", $"Failed to save YAML file to: {path}\nError: {ex.Message}\n\nCheck console for more details.", "OK");
                }
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
        /// Validates a relative file path. Checks for invalid characters and if the file exists.
        /// </summary>
        /// <param name="fieldNameKey">A unique key for this field's validation message (e.g., "AudioFile", "BackgroundPicture_0").</param>
        /// <param name="relativePath">The relative path to validate.</param>
        /// <param name="basePath">The base path (e.g., directory of the MapInfo file) to resolve the full path.</param>
        /// <param name="allowEmpty">If true, an empty path is considered valid.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private bool ValidateRelativePath(string fieldNameKey, string relativePath, string basePath, bool allowEmpty = false)
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
                string fullPath = "";
                bool pathExists = false;

                if (!string.IsNullOrEmpty(basePath))
                {
                    fullPath = Path.Combine(Path.GetDirectoryName(basePath), relativePath);
                    pathExists = File.Exists(fullPath);
                }
                else
                {
                    // If MapInfo not loaded, assume relative to project Assets.
                    fullPath = Path.Combine(Application.dataPath, relativePath);
                    pathExists = File.Exists(fullPath);
                    if (!pathExists)
                    {
                        // Also try directly resolving from project root if it's an asset path
                        string assetFullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
                        pathExists = File.Exists(assetFullPath);
                        if (pathExists) fullPath = assetFullPath; // Update fullPath if found
                    }
                }

                if (!pathExists)
                {
                    message = $"File not found at: {fullPath}.";
                    type = MessageType.Warning; // Changed to warning for non-existence, error for invalid path
                }
            }

            SetValidationMessage(fieldNameKey, message, type);
            return type != MessageType.Error; // Only return false if it's a hard error
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
            if (!ValidateRelativePath("AudioFile", mapInfo.AudioFile, currentlyLoadedMapInfoPath, false)) allValid = false;
            if (!ValidateRelativePath("PreviewAudioFile", mapInfo.PreviewAudioFile, currentlyLoadedMapInfoPath, true)) allValid = false;
            if (!ValidateRelativePath("VideoFile", mapInfo.VideoFile, currentlyLoadedMapInfoPath, true)) allValid = false;
            if (!ValidateRelativePath("PreviewVideoFile", mapInfo.PreviewVideoFile, currentlyLoadedMapInfoPath, true)) allValid = false;

            // Validate Background Pictures
            for (int i = 0; i < currentBackgroundPictures.Count; i++)
            {
                if (!ValidateRelativePath($"BackgroundPicture_{i}", currentBackgroundPictures[i], currentlyLoadedMapInfoPath, false))
                {
                    allValid = false;
                }
            }

            // Validate Beatmap Difficulty Files
            if (mapInfo.BeatmapDifficultyFiles.Count == 0)
            {
                // This is a warning, not an error that prevents saving.
                // We'll show a dialog to the user about it, but it doesn't set allValid to false.
                EditorUtility.DisplayDialog("Validation Warning", "No Beatmap Difficulty Files are defined. A map usually requires at least one difficulty.", "OK");
            }
            else
            {
                for (int i = 0; i < mapInfo.BeatmapDifficultyFiles.Count; i++)
                {
                    if (!ValidateRelativePath($"DifficultyFile_{i}", mapInfo.BeatmapDifficultyFiles[i].DifficultyFile, currentlyLoadedMapInfoPath, false))
                    {
                        allValid = false;
                    }
                }
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