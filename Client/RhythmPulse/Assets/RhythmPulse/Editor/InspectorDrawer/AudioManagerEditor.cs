using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using RhythmPulse.Audio;

[CustomEditor(typeof(AudioManager))]
public class AudioManagerEditor : Editor
{
    // Foldout states for different sections
    private bool showPoolSizes = true;
    private bool showLoadedClips = true;
    private bool showAudioStates = true;
    private bool showAudioClipPool = true;
    private bool showPlaybackStates = true;

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
        EditorGUILayout.LabelField("Audio Management State", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Only show runtime data in Play Mode
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("AudioManager runtime data is only available in Play Mode.", MessageType.Info);
            return;
        }

        // Pool Sizes foldout
        showPoolSizes = EditorGUILayout.Foldout(showPoolSizes, "Pool Sizes", true);
        if (showPoolSizes)
        {
            EditorGUI.indentLevel++;
            // Defensive copy of keys before iteration to avoid collection modified exception
            foreach (var category in new List<AudioManager.AudioCategory>(audioManager.poolSizes.Keys))
            {
                audioManager.poolSizes[category] = EditorGUILayout.IntField($"{category} Pool Size", audioManager.poolSizes[category]);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Loaded Audio Clips foldout
        showLoadedClips = EditorGUILayout.Foldout(showLoadedClips, "Loaded Audio Clips", true);
        if (showLoadedClips)
        {
            EditorGUI.indentLevel++;
            DrawCategoryData(audioManager.GetLoadedClips(), "Loaded");
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
            DrawCategoryData(audioManager.GetAudioStates(), "State");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Audio Clip Pool foldout
        showAudioClipPool = EditorGUILayout.Foldout(showAudioClipPool, "Audio Clip Pool", true);
        if (showAudioClipPool)
        {
            EditorGUI.indentLevel++;
            DrawCategoryData(audioManager.GetAudioClipPool(), "Cached");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Playback States foldout
        showPlaybackStates = EditorGUILayout.Foldout(showPlaybackStates, "Playback States", true);
        if (showPlaybackStates)
        {
            EditorGUI.indentLevel++;
            DrawCategoryData(audioManager.GetIsPlayingMap(), "Playing");
            EditorGUI.indentLevel--;
        }
    }

    /// <summary>
    /// Formats the status text with brackets and pads to fixed width for alignment.
    /// </summary>
    /// <param name="status">Status string</param>
    /// <param name="fixedWidth">Fixed width for padding</param>
    /// <returns>Formatted status string</returns>
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
    /// <param name="key">The key string (audio path)</param>
    /// <param name="value">The status string</param>
    /// <param name="valueColor">Color of the status text</param>
    private void DrawKeyValuePair(string key, string value, Color valueColor)
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

                // Draw foldout with truncated key
                keyExpandedStates[key] = EditorGUILayout.Foldout(keyExpandedStates[key], foldoutLabel, true);

                GUILayout.FlexibleSpace();
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
    /// <param name="key">The original key string</param>
    /// <returns>Truncated key string</returns>
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
    /// <param name="state">AudioState enum value</param>
    /// <returns>Color representing the state</returns>
    private Color GetStateColor(AudioManager.AudioState state)
    {
        switch (state)
        {
            case AudioManager.AudioState.Loaded:
                return Color.green;
            case AudioManager.AudioState.Loading:
                return Color.yellow;
            case AudioManager.AudioState.NotLoaded:
                return Color.red;
            case AudioManager.AudioState.Unloading:
                return Color.yellow;
            default:
                return Color.white;
        }
    }

    /// <summary>
    /// Draws dictionary data grouped by AudioCategory.
    /// Uses defensive copying to avoid collection modification exceptions during enumeration.
    /// For AudioClip type, status is retrieved from audioStates dictionary.
    /// </summary>
    /// <typeparam name="T">Type of the dictionary values</typeparam>
    /// <param name="data">Dictionary of AudioCategory to dictionary of key-value pairs</param>
    /// <param name="label">Label to display for each category</param>
    private void DrawCategoryData<T>(Dictionary<AudioManager.AudioCategory, Dictionary<string, T>> data, string label)
    {
        AudioManager audioManager = (AudioManager)target;

        foreach (var category in new List<AudioManager.AudioCategory>(data.Keys))
        {
            EditorGUILayout.LabelField($"{category} {label}", EditorStyles.boldLabel);

            var categoryDict = data[category];
            foreach (var kvp in new List<KeyValuePair<string, T>>(categoryDict))
            {
                string valueText = "null";
                Color valueColor = Color.white;

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
                    AudioManager.AudioState state = AudioManager.AudioState.NotLoaded;
                    if (states.TryGetValue(category, out var stateDict))
                    {
                        if (stateDict.TryGetValue(kvp.Key, out var s))
                        {
                            state = s;
                        }
                    }

                    valueText = state.ToString();
                    valueColor = GetStateColor(state);
                }
                else if (typeof(T) == typeof(AudioManager.AudioState))
                {
                    var state = (AudioManager.AudioState)(object)kvp.Value;
                    valueText = state.ToString();
                    valueColor = GetStateColor(state);
                }
                else if (typeof(T) == typeof(bool))
                {
                    bool isPlaying = (bool)(object)kvp.Value;
                    valueText = isPlaying ? "Playing" : "Not Playing";
                    valueColor = isPlaying ? Color.green : Color.red;
                }
                else if (typeof(T) == typeof((AudioClip, float)))
                {
                    var poolEntry = ((AudioClip, float))(object)kvp.Value;
                    valueText = poolEntry.Item1 != null ? "Cached" : "Not Cached";
                    valueColor = poolEntry.Item1 != null ? Color.green : Color.red;
                }
                else
                {
                    valueText = kvp.Value.ToString();
                    valueColor = Color.white;
                }

                DrawKeyValuePair(kvp.Key, valueText, valueColor);
            }
        }
    }
}