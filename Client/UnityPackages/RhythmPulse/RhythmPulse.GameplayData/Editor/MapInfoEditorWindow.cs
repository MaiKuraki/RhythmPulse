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
                UniqueID = System.Guid.NewGuid().ToString(),
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
        }

        void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

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

            EditorGUILayout.LabelField("Map Information", EditorStyles.boldLabel);
            mapInfo.UniqueID = EditorGUILayout.TextField(new GUIContent("Unique ID", "A unique identifier for this map (e.g., a GUID)."), mapInfo.UniqueID);
            
            GUI.enabled = false; // Make LastModifiedTime read-only
            EditorGUILayout.TextField(new GUIContent("Last Modified Time", "Time this MapInfo was last saved (automatically updated)."), string.IsNullOrEmpty(mapInfo.LastModifiedTime) ? "Not saved yet" : mapInfo.LastModifiedTime);
            GUI.enabled = true;

            mapInfo.DisplayName = EditorGUILayout.TextField(new GUIContent("Display Name", "The name of the song/map shown in-game."), mapInfo.DisplayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Media Files", EditorStyles.boldLabel);
            mapInfo.AudioFile = EditorGUILayout.TextField(new GUIContent("Audio File", "Path to the main audio file (e.g., 'audio.ogg'). Should be relative to the MapInfo file or a common root."), mapInfo.AudioFile);
            mapInfo.PreviewAudioFile = EditorGUILayout.TextField(new GUIContent("Preview Audio File", "Optional path to a short audio preview clip."), mapInfo.PreviewAudioFile);
            mapInfo.VideoFile = EditorGUILayout.TextField(new GUIContent("Video File", "Optional path to a background video file."), mapInfo.VideoFile);
            mapInfo.PreviewVideoFile = EditorGUILayout.TextField(new GUIContent("Preview Video File", "Optional path to a short video preview clip."), mapInfo.PreviewVideoFile);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Credits", EditorStyles.boldLabel);
            mapInfo.Vocalist = EditorGUILayout.TextField("Vocalist", mapInfo.Vocalist);
            mapInfo.Composer = EditorGUILayout.TextField("Composer", mapInfo.Composer);
            mapInfo.Arranger = EditorGUILayout.TextField("Arranger", mapInfo.Arranger);
            mapInfo.Lyricist = EditorGUILayout.TextField("Lyricist", mapInfo.Lyricist);
            mapInfo.BeatmapAuthor = EditorGUILayout.TextField(new GUIContent("Beatmap Author", "Creator of this specific beatmap data."), mapInfo.BeatmapAuthor);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("In-Game Background Pictures", EditorStyles.boldLabel);
            for (int i = 0; i < currentBackgroundPictures.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                currentBackgroundPictures[i] = EditorGUILayout.TextField($"Picture {i + 1}", currentBackgroundPictures[i]);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    currentBackgroundPictures.RemoveAt(i);
                    GUI.FocusControl(null);
                    break; 
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            newBackgroundPicturePath = EditorGUILayout.TextField("New Picture Path", newBackgroundPicturePath);
            if (GUILayout.Button("Add Picture", GUILayout.Width(100)) && !string.IsNullOrWhiteSpace(newBackgroundPicturePath))
            {
                currentBackgroundPictures.Add(newBackgroundPicturePath);
                newBackgroundPicturePath = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
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
                
                currentBeatmap.DifficultyFile = EditorGUILayout.TextField(new GUIContent("Difficulty File", "Filename of the beatmap data (e.g., 'mania_hard.yaml'). Relative to MapInfo file location."), currentBeatmap.DifficultyFile);

                EditorGUILayout.BeginHorizontal();
                currentBeatmap.MD5 = EditorGUILayout.TextField(new GUIContent("MD5 Hash", "MD5 hash of the difficulty file. Click 'Generate' to calculate."), currentBeatmap.MD5);
                if (GUILayout.Button("Generate MD5", GUILayout.Width(100)))
                {
                    // Assign to a local variable for the async lambda
                    int local_i = i; 
                    BeatMapInfo beatmapToUpdate = mapInfo.BeatmapDifficultyFiles[local_i];

                    // Asynchronously generate MD5
                    _ = GenerateMD5ForBeatmapAsync(beatmapToUpdate.DifficultyFile, local_i);
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
                    GUI.FocusControl(null);
                    EditorGUILayout.EndVertical(); // Ensure EndVertical is called before break
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
                mapInfo.LastModifiedTime = System.DateTime.UtcNow.ToString("o"); // ISO 8601 format
                mapInfo.InGameBackgroundPictures = currentBackgroundPictures.ToArray();
                GenerateYaml();
            }
            EditorGUILayout.EndScrollView();
        }

        private async Task GenerateMD5ForBeatmapAsync(string difficultyFileName, int beatmapIndex)
        {
            if (string.IsNullOrWhiteSpace(difficultyFileName))
            {
                Debug.LogWarning($"[MapInfoEditorWindow] Difficulty file name for entry #{beatmapIndex + 1} is empty. Cannot generate MD5.");
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
                Debug.LogError($"[MapInfoEditorWindow] Difficulty file not found at: {fullPath}. Cannot generate MD5 for entry #{beatmapIndex + 1}.");
                BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                tempBeatmap.MD5 = "File not found";
                mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                Repaint(); // Update UI
                return;
            }

            try
            {
                // We need a buffer to hold the hash. MD5 is 16 bytes.
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
                }
                else
                {
                    Debug.LogError($"[MapInfoEditorWindow] Failed to compute MD5 for: {fullPath} (Entry #{beatmapIndex + 1}).");
                    BeatMapInfo tempBeatmap = mapInfo.BeatmapDifficultyFiles[beatmapIndex];
                    tempBeatmap.MD5 = "Error computing";
                    mapInfo.BeatmapDifficultyFiles[beatmapIndex] = tempBeatmap;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapInfoEditorWindow] Exception computing MD5 for {fullPath} (Entry #{beatmapIndex + 1}): {ex.ToString()}");
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
                    }

                    newBackgroundPicturePath = "";

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

        private string SanitizeFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
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