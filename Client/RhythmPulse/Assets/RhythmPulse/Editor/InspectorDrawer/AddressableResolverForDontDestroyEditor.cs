using RhythmPulse.Misc;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AddressableResolverForDontDestroy))]
public sealed class AddressableResolverForDontDestroyEditor : Editor
{
    private SerializedProperty _listProp;
    private bool _foldout = true;

    // ── Cached to avoid per-frame allocations in OnInspectorGUI ──
    private static readonly GUIContent s_PrefabLabel    = new GUIContent("Prefab");
    private static readonly GUIContent s_DisplayLabel   = new GUIContent("Display Name");
    private static readonly Color      s_HeaderExpanded  = new Color(0.22f, 0.36f, 0.53f, 1f);
    private static readonly Color      s_HeaderCollapsed = new Color(0.25f, 0.25f, 0.25f, 1f);
    private static GUIStyle            s_HeaderStyle;

    private static GUIStyle HeaderStyle
    {
        get
        {
            if (s_HeaderStyle == null)
            {
                s_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal    = { textColor = Color.white },
                    fontSize  = 12,
                    padding   = new RectOffset(20, 0, 0, 0)
                };
            }
            return s_HeaderStyle;
        }
    }

    private void OnEnable()
    {
        _listProp = serializedObject.FindProperty("entries");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Drag a Prefab into the Prefab field; the editor will automatically record its path and GUID.\n" +
            "DisplayName is for debugging. At runtime, assets are loaded via AssetRef and marked DontDestroyOnLoad.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        // ── Header bar ──
        string header = $"  DontDestroy Prefabs  ({_listProp.arraySize})";
        Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(24));
        EditorGUI.DrawRect(headerRect, _foldout ? s_HeaderExpanded : s_HeaderCollapsed);
        DrawFoldoutArrow(headerRect.x + 6f, headerRect.y + headerRect.height * 0.5f, _foldout);
        EditorGUI.LabelField(headerRect, header, HeaderStyle);
        if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
        {
            _foldout = !_foldout;
            Event.current.Use();
        }

        if (_foldout)
        {
            EditorGUILayout.Space(4);

            for (int i = 0; i < _listProp.arraySize; i++)
            {
                if (!DrawEntry(i))
                    break; // entry deleted, list changed
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Entry", GUILayout.Width(120)))
            {
                int idx = _listProp.arraySize;
                _listProp.InsertArrayElementAtIndex(idx);
                var entry = _listProp.GetArrayElementAtIndex(idx);
                entry.FindPropertyRelative("DisplayName").stringValue = "";
                var prefabProp = entry.FindPropertyRelative("Prefab");
                prefabProp.FindPropertyRelative("m_Location").stringValue = "";
                prefabProp.FindPropertyRelative("m_GUID").stringValue = "";
            }
            EditorGUI.BeginDisabledGroup(_listProp.arraySize == 0);
            if (GUILayout.Button("- Remove Last", GUILayout.Width(120)))
            {
                _listProp.DeleteArrayElementAtIndex(_listProp.arraySize - 1);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ────────────────────────── Entry ──────────────────────────

    /// <returns>false if the entry was deleted</returns>
    private bool DrawEntry(int index)
    {
        var entryProp       = _listProp.GetArrayElementAtIndex(index);
        var displayNameProp = entryProp.FindPropertyRelative("DisplayName");
        var prefabProp      = entryProp.FindPropertyRelative("Prefab");

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ── Row 1: Header + Delete ──
        EditorGUILayout.BeginHorizontal();
        string label = string.IsNullOrEmpty(displayNameProp.stringValue)
            ? $"Entry #{index}"
            : $"#{index}  {displayNameProp.stringValue}";
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("\u2715", GUILayout.Width(24), GUILayout.Height(18)))
        {
            _listProp.DeleteArrayElementAtIndex(index);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return false;
        }
        EditorGUILayout.EndHorizontal();

        // ── Row 2: Prefab (drawn by AssetRefPropertyDrawer — handles drag-drop, GUID, auto-heal) ──
        EditorGUILayout.PropertyField(prefabProp, s_PrefabLabel);

        // Auto-fill DisplayName from asset name when empty
        if (string.IsNullOrEmpty(displayNameProp.stringValue))
        {
            string guid = prefabProp.FindPropertyRelative("m_GUID")?.stringValue;
            if (!string.IsNullOrEmpty(guid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    displayNameProp.stringValue = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                }
            }
        }

        // ── Row 3: Display Name ──
        EditorGUILayout.PropertyField(displayNameProp, s_DisplayLabel);

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
        return true;
    }

    // ────────────────────────── Helpers ──────────────────────────

    private static void DrawFoldoutArrow(float x, float y, bool expanded)
    {
        var oldColor = Handles.color;
        Handles.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        if (expanded)
        {
            Handles.DrawAAConvexPolygon(
                new Vector3(x,        y - 3f, 0),
                new Vector3(x + 10f,  y - 3f, 0),
                new Vector3(x + 5f,   y + 4f, 0));
        }
        else
        {
            Handles.DrawAAConvexPolygon(
                new Vector3(x,       y - 5f, 0),
                new Vector3(x + 7f,  y,      0),
                new Vector3(x,       y + 5f, 0));
        }
        Handles.color = oldColor;
    }
}
