using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using RhythmPulse.Audio;
using CycloneGames.Utility.Runtime;

[CustomEditor(typeof(AudioManager))]
public class AudioManagerEditor : Editor
{
    // Foldout states for different sections 
    private bool showLoadedClips = true;
    private bool showAudioStates = true;
    private bool showMemoryUsage = true;

    // Dictionary to store expanded state of each key (audio path)
    private Dictionary<string, bool> keyExpandedStates = new Dictionary<string, bool>();

    public override void OnInspectorGUI()
    {
        // Force repaint to keep inspector updated after focus change 
        Repaint();

        // Draw default inspector fields 
        DrawDefaultInspector();

        AudioManager audioManager = (AudioManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("------ Audio Management State ------", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Only show runtime data in Play Mode 
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("AudioManager runtime data is only available in Play Mode.", MessageType.Info);
            return;
        }

        // Display total memory usage at the top 
        EditorGUILayout.BeginHorizontal();
        {
            EditorGUILayout.LabelField("Total Audio Memory Usage:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(audioManager.TotalMemoryUsage.ToMemorySizeString(), EditorStyles.boldLabel);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // Loaded Audio Clips foldout 
        showLoadedClips = EditorGUILayout.Foldout(showLoadedClips, "Loaded Audio Clips", true);
        if (showLoadedClips)
        {
            EditorGUI.indentLevel++;
            DrawDictionaryData(audioManager.GetLoadedClips(), "Loaded", audioManager);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Audio Loading States foldout 
        showAudioStates = EditorGUILayout.Foldout(showAudioStates, "Audio Loading States", true);
        if (showAudioStates)
        {
            EditorGUI.indentLevel++;
            DrawDictionaryData(audioManager.GetAudioStates(), "State", audioManager);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Memory Usage foldout 
        showMemoryUsage = EditorGUILayout.Foldout(showMemoryUsage, "Detailed Memory Usage", true);
        if (showMemoryUsage)
        {
            EditorGUI.indentLevel++;
            DrawMemoryUsageData(audioManager.GetAudioMemoryUsage(), audioManager);
            EditorGUI.indentLevel--;
        }
    }

    /// <summary>
    /// Formats the status text with brackets and pads to fixed width for alignment.
    /// </summary>
    private string FormatStatusText(string status, int fixedWidth = 12)
    {
        string text = $"[{status}]";
        if (text.Length < fixedWidth)
        {
            text = text.PadRight(fixedWidth);
        }
        return text;
    }

    /// <summary>
    /// Draws a key-value pair with the status displayed on the left side of the key.
    /// </summary>
    private void DrawKeyValuePair(string key, string value, Color valueColor, string memoryInfo = null)
    {
        EditorGUILayout.BeginVertical();
        {
            if (!keyExpandedStates.ContainsKey(key))
            {
                keyExpandedStates[key] = false;
            }

            string foldoutLabel = TruncateKey(key);
            string statusText = FormatStatusText(value);

            // Calculate status text width 
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
            statusStyle.normal.textColor = valueColor;
            Vector2 statusSize = statusStyle.CalcSize(new GUIContent(statusText));
            float statusWidth = statusSize.x;

            // Calculate indent width based on current indentLevel 
            float indentWidth = EditorGUI.indentLevel * 15f;

            EditorGUILayout.BeginHorizontal();
            {
                // Leave indent space before status label 
                GUILayout.Space(indentWidth);

                // Draw status label with exact width (no padding)
                GUILayout.Label(statusText, statusStyle, GUILayout.Width(statusWidth));

                // Create a flexible space that will automatically handle the width 
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                {
                    // Draw foldout with truncated key 
                    keyExpandedStates[key] = EditorGUILayout.Foldout(keyExpandedStates[key], foldoutLabel, true);
                }
                EditorGUILayout.EndHorizontal();

                // Add memory info if available 
                if (!string.IsNullOrEmpty(memoryInfo))
                {
                    EditorGUILayout.LabelField(memoryInfo, GUILayout.Width(80));
                }
            }
            EditorGUILayout.EndHorizontal();

            if (keyExpandedStates[key])
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                {
                    // Leave indent space + status text width for alignment 
                    GUILayout.Space(indentWidth + statusWidth);

                    // Vertical layout for full path label and selectable text 
                    EditorGUILayout.BeginVertical();
                    {
                        EditorGUILayout.LabelField("Full Path:");
                        EditorGUILayout.SelectableLabel(key, EditorStyles.textArea, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2));
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
    }
    
    /// <summary>
    /// Truncates a key string to a maximum length, appending ellipsis if truncated.
    /// </summary>
    private string TruncateKey(string key)
    {
        const int maxLength = 30;
        if (key.Length <= maxLength)
            return key;

        return key.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Returns a color based on the audio state.
    /// </summary>
    private Color GetStateColor(AudioManager.AudioLoadState state)
    {
        switch (state)
        {
            case AudioManager.AudioLoadState.Loaded:
                return Color.green;
            case AudioManager.AudioLoadState.Loading:
                return Color.yellow;
            case AudioManager.AudioLoadState.NotLoaded:
                return Color.red;
            case AudioManager.AudioLoadState.Unloading:
                return Color.yellow;
            default:
                return Color.white;
        }
    }

    /// <summary>
    /// Draws dictionary data with appropriate formatting.
    /// </summary>
    private void DrawDictionaryData<T>(Dictionary<string, T> data, string label, AudioManager audioManager)
    {
        foreach (var kvp in new List<KeyValuePair<string, T>>(data))
        {
            string valueText = "null";
            Color valueColor = Color.white;
            string memoryInfo = null;

            if (kvp.Value == null)
            {
                valueText = "null";
                valueColor = Color.red;
            }
            else if (typeof(T) == typeof(AudioClip))
            {
                var clip = kvp.Value as AudioClip;
                string clipName = clip != null ? clip.name : "Null AudioClip";

                // Get corresponding audio state 
                var states = audioManager.GetAudioStates();
                AudioManager.AudioLoadState state = AudioManager.AudioLoadState.NotLoaded;
                if (states.TryGetValue(kvp.Key, out var s))
                {
                    state = s;
                }

                valueText = state.ToString();
                valueColor = GetStateColor(state);

                // Add memory info if loaded 
                if (state == AudioManager.AudioLoadState.Loaded)
                {
                    audioManager.GetAudioMemoryUsage().TryGetValue(kvp.Key, out long memory);
                    memoryInfo = memory.ToMemorySizeString();
                }
            }
            else if (typeof(T) == typeof(AudioManager.AudioLoadState))
            {
                var state = (AudioManager.AudioLoadState)(object)kvp.Value;
                valueText = state.ToString();
                valueColor = GetStateColor(state);

                // Add memory info if loaded 
                if (state == AudioManager.AudioLoadState.Loaded)
                {
                    audioManager.GetAudioMemoryUsage().TryGetValue(kvp.Key, out long memory);
                    memoryInfo = memory.ToMemorySizeString();
                }
            }
            else
            {
                valueText = kvp.Value.ToString();
                valueColor = Color.white;
            }

            DrawKeyValuePair(kvp.Key, valueText, valueColor, memoryInfo);
        }
    }

    /// <summary>
    /// Draws memory usage data in a detailed view.
    /// </summary>
    private void DrawMemoryUsageData(Dictionary<string, long> memoryUsage, AudioManager audioManager)
    {
        // Sort by memory usage (descending)
        var sortedUsage = new List<KeyValuePair<string, long>>(memoryUsage);
        sortedUsage.Sort((a, b) => b.Value.CompareTo(a.Value));

        foreach (var kvp in sortedUsage)
        {
            string memoryText = kvp.Value.ToMemorySizeString();
            string stateText = "Unknown";
            Color stateColor = Color.white;

            // Get the state for coloring 
            if (audioManager.GetAudioStates().TryGetValue(kvp.Key, out var state))
            {
                stateText = state.ToString();
                stateColor = GetStateColor(state);
            }

            DrawKeyValuePair(kvp.Key, stateText, stateColor, memoryText);
        }
    }
}