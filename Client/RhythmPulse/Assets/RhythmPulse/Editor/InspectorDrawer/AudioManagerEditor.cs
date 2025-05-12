using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using RhythmPulse.Audio;

[CustomEditor(typeof(AudioManager))]
public class AudioManagerEditor : Editor
{
    // Foldout states
    private bool showLoadedClips = true;
    private bool showAudioStates = true;
    private bool showAudioClipPool = true;
    private bool showPlaybackStates = true;

    // Record the expanded state of each key
    private Dictionary<string, bool> keyExpandedStates = new Dictionary<string, bool>();

    public override void OnInspectorGUI()
    {
        // Draw the default Inspector
        DrawDefaultInspector();

        // Get the target AudioManager
        AudioManager audioManager = (AudioManager)target;

        // Separator
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio Management State", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Loaded audio clips (foldable)
        showLoadedClips = EditorGUILayout.Foldout(showLoadedClips, "Loaded Audio Clips", true);
        if (showLoadedClips)
        {
            EditorGUI.indentLevel++;
            foreach (var kvp in audioManager.GetLoadedClips())
            {
                DrawKeyValuePair(kvp.Key, kvp.Value != null ? "Loaded" : "Not Loaded", kvp.Value != null ? Color.green : Color.red);
            }
            EditorGUI.indentLevel--;
        }

        // Separator
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Audio loading states (foldable)
        showAudioStates = EditorGUILayout.Foldout(showAudioStates, "Audio Loading States", true);
        if (showAudioStates)
        {
            EditorGUI.indentLevel++;
            foreach (var kvp in audioManager.GetAudioStates())
            {
                string stateText = kvp.Value.ToString();
                Color stateColor = GetStateColor(kvp.Value);
                DrawKeyValuePair(kvp.Key, stateText, stateColor);
            }
            EditorGUI.indentLevel--;
        }

        // Separator
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Audio clip pool (foldable)
        showAudioClipPool = EditorGUILayout.Foldout(showAudioClipPool, "Audio Clip Pool", true);
        if (showAudioClipPool)
        {
            EditorGUI.indentLevel++;
            foreach (var kvp in audioManager.GetAudioClipPool())
            {
                string cacheStatus = kvp.Value.clip != null ? "Cached" : "Not Cached";
                Color cacheColor = kvp.Value.clip != null ? Color.green : Color.red;
                DrawKeyValuePair(kvp.Key, $"{cacheStatus} (Unload Time: {kvp.Value.unloadTime})", cacheColor);
            }
            EditorGUI.indentLevel--;
        }

        // Separator
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        // Playback states (foldable)
        showPlaybackStates = EditorGUILayout.Foldout(showPlaybackStates, "Playback States", true);
        if (showPlaybackStates)
        {
            EditorGUI.indentLevel++;
            foreach (var kvp in audioManager.GetIsPlayingMap())
            {
                string playbackStatus = kvp.Value ? "Playing" : "Not Playing";
                Color playbackColor = kvp.Value ? Color.green : Color.red;
                DrawKeyValuePair(kvp.Key, playbackStatus, playbackColor);
            }
            EditorGUI.indentLevel--;
        }
    }

    // Helper method: Draw Key-Value pair
    private void DrawKeyValuePair(string key, string value, Color valueColor)
    {
        EditorGUILayout.BeginVertical();
        {
            // Check the expanded state of the key
            if (!keyExpandedStates.ContainsKey(key))
            {
                keyExpandedStates[key] = false;
            }

            // Display Key and status label (on the same line)
            EditorGUILayout.BeginHorizontal();
            {
                // Foldout label
                keyExpandedStates[key] = EditorGUILayout.Foldout(keyExpandedStates[key], "Key", true);

                // Display truncated or full key
                if (keyExpandedStates[key])
                {
                    // Display full key (read-only text area)
                    EditorGUILayout.SelectableLabel(key, EditorStyles.textArea, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2));
                }
                else
                {
                    // Display truncated key, left-aligned
                    EditorGUILayout.LabelField(TruncateKey(key), GUILayout.ExpandWidth(true));
                }

                // Display status label (right-aligned, with color)
                GUIStyle valueStyle = new GUIStyle(EditorStyles.label);
                valueStyle.normal.textColor = valueColor;
                valueStyle.alignment = TextAnchor.MiddleRight;
                EditorGUILayout.LabelField(value, valueStyle, GUILayout.Width(100)); // Fixed status label width
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        // Add spacing
        EditorGUILayout.Space();
    }

    // Helper method: Truncate key
    private string TruncateKey(string key)
    {
        const int maxLength = 30;
        if (key.Length <= maxLength)
            return key;

        return key.Substring(0, maxLength) + "...";
    }

    // Helper method: Get color based on state
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
}