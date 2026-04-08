using System.Collections.Generic;
using CycloneGames.Utility.Runtime;
using RhythmPulse.Audio;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AudioManager))]
public sealed class AudioManagerEditor : Editor
{
    private SerializedProperty _singletonProp;
    private SerializedProperty _audioSourcePrefabRefProp;

    private bool _showLoadedClips = true;
    private bool _showAudioStates = true;
    private bool _showMemoryUsage = true;

    private readonly HashSet<string> _expandedKeys = new(32);
    private readonly List<KeyValuePair<string, AudioClip>> _clipBuffer = new(32);
    private readonly List<KeyValuePair<string, AudioManager.AudioLoadState>> _stateBuffer = new(32);
    private readonly List<KeyValuePair<string, long>> _memoryBuffer = new(32);

    private static GUIStyle s_BoxStyle;
    private static GUIStyle s_PathStyle;
    private static GUIStyle s_StatusLoaded;
    private static GUIStyle s_StatusLoading;
    private static GUIStyle s_StatusError;
    private static GUIStyle s_MemoryStyle;
    private static GUIStyle s_HeaderStyle;
    private static GUIStyle s_TotalMemoryStyle;
    private static GUIStyle s_FileNameStyle;
    private static bool s_StylesInitialized;

    private static readonly Color HeaderBgColor = new(0.2f, 0.2f, 0.2f, 0.3f);
    private static readonly Color EntryBgColorA = new(0.18f, 0.18f, 0.18f, 0.3f);
    private static readonly Color EntryBgColorB = new(0.12f, 0.12f, 0.12f, 0.15f);

    private void OnEnable()
    {
        _singletonProp = serializedObject.FindProperty("_singleton");
        _audioSourcePrefabRefProp = serializedObject.FindProperty("_audioSourcePrefabRef");
    }

    private static void EnsureStyles()
    {
        if (s_StylesInitialized) return;

        s_BoxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(6, 6, 4, 4),
            margin = new RectOffset(0, 0, 2, 2)
        };

        s_PathStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            fontSize = 10,
            padding = new RectOffset(2, 2, 1, 1)
        };

        s_StatusLoaded = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            normal = { textColor = new Color(0.3f, 0.85f, 0.3f) }
        };

        s_StatusLoading = new GUIStyle(s_StatusLoaded)
        {
            normal = { textColor = new Color(0.95f, 0.75f, 0.2f) }
        };

        s_StatusError = new GUIStyle(s_StatusLoaded)
        {
            normal = { textColor = new Color(0.9f, 0.35f, 0.35f) }
        };

        s_MemoryStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            fontSize = 10,
            normal = { textColor = new Color(0.5f, 0.75f, 1f) }
        };

        s_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            padding = new RectOffset(4, 4, 4, 4)
        };

        s_TotalMemoryStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.85f, 0.4f) }
        };

        s_FileNameStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Normal,
            fontSize = 11,
            clipping = TextClipping.Clip
        };

        s_StylesInitialized = true;
    }

    public override void OnInspectorGUI()
    {
        EnsureStyles();
        serializedObject.Update();

        DrawConfigurationSection();

        serializedObject.ApplyModifiedProperties();

        var audioManager = (AudioManager)target;

        EditorGUILayout.Space(10);
        DrawHeader(audioManager);

        if (!Application.isPlaying)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Runtime data only available in Play Mode.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6);

        _showLoadedClips = EditorGUILayout.BeginFoldoutHeaderGroup(_showLoadedClips, $"Loaded Clips ({audioManager.EditorGetLoadedClips().Count})");
        if (_showLoadedClips) DrawLoadedClipsSection(audioManager);
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(2);

        _showAudioStates = EditorGUILayout.BeginFoldoutHeaderGroup(_showAudioStates, $"All States ({audioManager.EditorGetAudioStates().Count})");
        if (_showAudioStates) DrawStatesSection(audioManager);
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(2);

        _showMemoryUsage = EditorGUILayout.BeginFoldoutHeaderGroup(_showMemoryUsage, "Memory Usage (by size)");
        if (_showMemoryUsage) DrawMemorySection(audioManager);
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (Application.isPlaying) Repaint();
    }

    private void DrawConfigurationSection()
    {
        EditorGUILayout.BeginVertical(s_BoxStyle);
        EditorGUILayout.LabelField("Configuration", s_HeaderStyle);
        EditorGUILayout.PropertyField(_singletonProp);
        EditorGUILayout.PropertyField(_audioSourcePrefabRefProp, new GUIContent("Audio Source Prefab"));

        var guidProp = _audioSourcePrefabRefProp?.FindPropertyRelative("m_GUID");
        var locationProp = _audioSourcePrefabRefProp?.FindPropertyRelative("m_Location");
        bool isValid = guidProp != null && locationProp != null &&
                       !string.IsNullOrEmpty(guidProp.stringValue) &&
                       !string.IsNullOrEmpty(locationProp.stringValue);

        if (!isValid)
        {
            EditorGUILayout.HelpBox("Assign Audio Source Prefab via AssetRef to ensure stable GUID-based reference.", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawHeader(AudioManager audioManager)
    {
        var rect = EditorGUILayout.BeginVertical(s_BoxStyle);
        EditorGUI.DrawRect(rect, HeaderBgColor);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Audio Manager", s_HeaderStyle);
        GUILayout.FlexibleSpace();
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField(audioManager.TotalMemoryUsage.ToMemorySizeString(), s_TotalMemoryStyle, GUILayout.Width(100));
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawLoadedClipsSection(AudioManager audioManager)
    {
        var clips = audioManager.EditorGetLoadedClips();
        var states = audioManager.EditorGetAudioStates();
        var memory = audioManager.EditorGetAudioMemoryUsage();

        _clipBuffer.Clear();
        foreach (var kvp in clips) _clipBuffer.Add(kvp);

        if (_clipBuffer.Count == 0)
        {
            DrawEmptyMessage("No clips loaded");
            return;
        }

        EditorGUILayout.BeginVertical(s_BoxStyle);
        for (int i = 0; i < _clipBuffer.Count; i++)
        {
            var kvp = _clipBuffer[i];
            states.TryGetValue(kvp.Key, out var state);
            memory.TryGetValue(kvp.Key, out var mem);
            DrawAudioEntry(kvp.Key, state, mem, i % 2 == 0);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawStatesSection(AudioManager audioManager)
    {
        var states = audioManager.EditorGetAudioStates();
        var memory = audioManager.EditorGetAudioMemoryUsage();

        _stateBuffer.Clear();
        foreach (var kvp in states) _stateBuffer.Add(kvp);

        if (_stateBuffer.Count == 0)
        {
            DrawEmptyMessage("No audio tracked");
            return;
        }

        EditorGUILayout.BeginVertical(s_BoxStyle);
        for (int i = 0; i < _stateBuffer.Count; i++)
        {
            var kvp = _stateBuffer[i];
            memory.TryGetValue(kvp.Key, out var mem);
            DrawAudioEntry(kvp.Key, kvp.Value, mem, i % 2 == 0);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawMemorySection(AudioManager audioManager)
    {
        var memory = audioManager.EditorGetAudioMemoryUsage();
        var states = audioManager.EditorGetAudioStates();

        _memoryBuffer.Clear();
        foreach (var kvp in memory) _memoryBuffer.Add(kvp);

        if (_memoryBuffer.Count == 0)
        {
            DrawEmptyMessage("No memory tracked");
            return;
        }

        _memoryBuffer.Sort((a, b) => b.Value.CompareTo(a.Value));

        EditorGUILayout.BeginVertical(s_BoxStyle);
        for (int i = 0; i < _memoryBuffer.Count; i++)
        {
            var kvp = _memoryBuffer[i];
            states.TryGetValue(kvp.Key, out var state);
            DrawAudioEntry(kvp.Key, state, kvp.Value, i % 2 == 0);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawEmptyMessage(string message)
    {
        EditorGUILayout.BeginVertical(s_BoxStyle);
        EditorGUILayout.LabelField(message, EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawAudioEntry(string path, AudioManager.AudioLoadState state, long memoryBytes, bool altRow)
    {
        bool isExpanded = _expandedKeys.Contains(path);

        var entryRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(entryRect, altRow ? EntryBgColorA : EntryBgColorB);

        // Row 1: Status icon | Filename | Memory (on separate line if expanded)
        EditorGUILayout.BeginHorizontal();
        {
            // Status badge - fixed width with spacing
            GUILayout.Space(4);
            DrawStatusBadge(state);
            GUILayout.Space(8);

            // Filename - use remaining space with clipping
            string filename = GetFileName(path);
            
            // Calculate available width for filename
            float memoryWidth = memoryBytes > 0 ? 65f : 0f;
            float statusWidth = 24f;
            float spacing = 20f;
            float availableWidth = EditorGUIUtility.currentViewWidth - statusWidth - memoryWidth - spacing - 40f;
            
            // Foldout with filename - offset down 2px to align with status circle
            GUILayout.BeginVertical();
            GUILayout.Space(2);
            bool newExpanded = GUILayout.Toggle(isExpanded, "", EditorStyles.foldout, GUILayout.Width(12));
            GUILayout.EndVertical();
            EditorGUILayout.LabelField(filename, s_FileNameStyle, GUILayout.MaxWidth(Mathf.Max(100, availableWidth)));
            
            if (newExpanded != isExpanded)
            {
                if (newExpanded) _expandedKeys.Add(path);
                else _expandedKeys.Remove(path);
            }

            // Memory - right aligned, fixed width
            if (memoryBytes > 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(memoryBytes.ToMemorySizeString(), s_MemoryStyle, GUILayout.Width(65));
            }
        }
        EditorGUILayout.EndHorizontal();

        // Expanded: show full path on new line
        if (_expandedKeys.Contains(path))
        {
            EditorGUILayout.Space(2);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Path:", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(path, s_PathStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight * 2));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(1);
    }

    private void DrawStatusBadge(AudioManager.AudioLoadState state)
    {
        GUIStyle style;
        string label;

        switch (state)
        {
            case AudioManager.AudioLoadState.Loaded:
                style = s_StatusLoaded;
                label = "●";
                break;
            case AudioManager.AudioLoadState.Loading:
                style = s_StatusLoading;
                label = "◐";
                break;
            case AudioManager.AudioLoadState.Unloading:
                style = s_StatusLoading;
                label = "◑";
                break;
            default:
                style = s_StatusError;
                label = "○";
                break;
        }

        GUILayout.Label(label, style, GUILayout.Width(16), GUILayout.Height(16));
    }

    private static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "(empty)";

        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0) lastSlash = path.LastIndexOf('\\');

        if (lastSlash >= 0 && lastSlash < path.Length - 1)
            return path.Substring(lastSlash + 1);

        return path;
    }
}