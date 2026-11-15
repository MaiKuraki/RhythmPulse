using RhythmPulse.AssetManagement;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AssetResolver))]
public sealed class AssetResolverEditor : Editor
{
    private SerializedProperty _listProp;
    private SerializedProperty _waitForDelayedProp;
    private bool _foldout = true;

    // Cached to avoid per-frame allocations in OnInspectorGUI.
    private static readonly GUIContent s_PrefabLabel = new GUIContent("Prefab");
    private static readonly GUIContent s_DisplayLabel = new GUIContent("Display Name");
    private static readonly GUIContent s_EntryIdLabel = new GUIContent("Entry Id", "Stable id used by TryGetInstance / InstantiateEntryAsync at runtime.");
    private static readonly GUIContent s_PreloadLabel = new GUIContent("Preload", "Load the prefab during InitializeAsync even when the entry is not instantiated immediately.");
    private static readonly GUIContent s_ModeLabel = new GUIContent("Mode", "Immediate / Delayed / Manual instantiation policy.");
    private static readonly GUIContent s_DelayLabel = new GUIContent("Delay (s)");
    private static readonly GUIContent s_DelayStartLabel = new GUIContent("Delay Start");
    private static readonly GUIContent s_DependsOnLabel = new GUIContent("Depends On", "Comma separated EntryIds that must exist before this entry instantiates.");
    private static readonly GUIContent s_DependencyModeLabel = new GUIContent("Require");
    private static readonly GUIContent s_AutoDependencyLabel = new GUIContent("Auto Instantiate");
    private static readonly GUIContent s_DependencyWaitLabel = new GUIContent("Wait Timeout (s)");
    private static readonly GUIContent s_DependencyMissingLabel = new GUIContent("Missing Policy");

    private static readonly Color s_HeaderExpanded = new Color(0.22f, 0.36f, 0.53f, 1f);
    private static readonly Color s_HeaderCollapsed = new Color(0.25f, 0.25f, 0.25f, 1f);
    private static GUIStyle s_HeaderStyle;

    private static GUIStyle HeaderStyle
    {
        get
        {
            if (s_HeaderStyle == null)
            {
                s_HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Color.white },
                    fontSize = 12,
                    padding = new RectOffset(20, 0, 0, 0)
                };
            }

            return s_HeaderStyle;
        }
    }

    private void OnEnable()
    {
        _listProp = serializedObject.FindProperty("entries");
        _waitForDelayedProp = serializedObject.FindProperty("waitForDelayedEntries");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Persistent prefabs loaded once and kept across scene transitions (DontDestroyOnLoad).\n" +
            "Entry Id is the runtime handle (TryGetInstance / TryGetComponent); Display Name is for logs.\n" +
            "Drop a prefab in the Prefab field — the path and GUID are recorded automatically by the AssetRef drawer.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(_waitForDelayedProp, new GUIContent("Wait For Delayed Entries",
            "When enabled, InitializeAsync also awaits every delayed entry."));

        EditorGUILayout.Space(4);
        DrawEntriesHeader();

        if (_foldout)
        {
            EditorGUILayout.Space(4);

            for (int i = 0; i < _listProp.arraySize; i++)
            {
                if (!DrawEntry(i))
                {
                    break; // entry deleted, list changed
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Entry", GUILayout.Width(120)))
            {
                AddEntry();
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

    private void DrawEntriesHeader()
    {
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
    }

    /// <returns>false when the entry was deleted and the list layout changed.</returns>
    private bool DrawEntry(int index)
    {
        SerializedProperty entryProp = _listProp.GetArrayElementAtIndex(index);
        SerializedProperty entryIdProp = entryProp.FindPropertyRelative("EntryId");
        SerializedProperty displayNameProp = entryProp.FindPropertyRelative("DisplayName");
        SerializedProperty prefabProp = entryProp.FindPropertyRelative("Prefab");
        SerializedProperty modeProp = entryProp.FindPropertyRelative("InstantiationMode");
        SerializedProperty dependsOnProp = entryProp.FindPropertyRelative("DependsOnIds");

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

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

        EditorGUILayout.PropertyField(prefabProp, s_PrefabLabel);

        bool autoFill = AutoFillNames(displayNameProp, entryIdProp, prefabProp, out string displayName, out string entryId);
        if (autoFill)
        {
            displayNameProp.stringValue = displayName;
            if (string.IsNullOrEmpty(entryIdProp.stringValue))
            {
                entryIdProp.stringValue = entryId;
            }
        }

        EditorGUILayout.PropertyField(entryIdProp, s_EntryIdLabel);
        EditorGUILayout.PropertyField(displayNameProp, s_DisplayLabel);
        EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("PreloadOnInitialize"), s_PreloadLabel);
        EditorGUILayout.PropertyField(modeProp, s_ModeLabel);

        var mode = (AssetResolverInstantiationMode)modeProp.intValue;
        if (mode == AssetResolverInstantiationMode.Delayed)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("DelaySeconds"), s_DelayLabel);
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("DelayStartMode"), s_DelayStartLabel);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(dependsOnProp, s_DependsOnLabel);
        if (!string.IsNullOrWhiteSpace(dependsOnProp.stringValue))
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("DependencyMode"), s_DependencyModeLabel);
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("AutoInstantiateDependencies"), s_AutoDependencyLabel);
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("DependencyWaitTimeoutSeconds"), s_DependencyWaitLabel);
            EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("DependencyMissingPolicy"), s_DependencyMissingLabel);
            EditorGUI.indentLevel--;
        }

        if (mode == AssetResolverInstantiationMode.Manual)
        {
            EditorGUILayout.HelpBox("Manual entries are never instantiated by InitializeAsync. Call InstantiateEntryAsync at runtime.", MessageType.None);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
        return true;
    }

    private static bool AutoFillNames(
        SerializedProperty displayNameProp,
        SerializedProperty entryIdProp,
        SerializedProperty prefabProp,
        out string displayName,
        out string entryId)
    {
        displayName = null;
        entryId = null;

        bool needsDisplay = string.IsNullOrEmpty(displayNameProp.stringValue);
        bool needsEntryId = string.IsNullOrEmpty(entryIdProp.stringValue);
        if (!needsDisplay && !needsEntryId)
        {
            return false;
        }

        string guid = prefabProp.FindPropertyRelative("m_GUID")?.stringValue;
        if (string.IsNullOrEmpty(guid))
        {
            return false;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        string assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        displayName = needsDisplay ? assetName : displayNameProp.stringValue;
        entryId = needsEntryId ? ToKebabCase(assetName) : entryIdProp.stringValue;
        return true;
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == ' ' || character == '_')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            if (char.IsUpper(character))
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private void AddEntry()
    {
        int index = _listProp.arraySize;
        _listProp.InsertArrayElementAtIndex(index);
        SerializedProperty entry = _listProp.GetArrayElementAtIndex(index);

        entry.FindPropertyRelative("EntryId").stringValue = string.Empty;
        entry.FindPropertyRelative("DisplayName").stringValue = string.Empty;
        SerializedProperty prefab = entry.FindPropertyRelative("Prefab");
        prefab.FindPropertyRelative("m_Location").stringValue = string.Empty;
        prefab.FindPropertyRelative("m_GUID").stringValue = string.Empty;
        entry.FindPropertyRelative("PreloadOnInitialize").boolValue = true;
        entry.FindPropertyRelative("InstantiationMode").intValue = (int)AssetResolverInstantiationMode.Immediate;
        entry.FindPropertyRelative("DelaySeconds").floatValue = 0f;
        entry.FindPropertyRelative("DelayStartMode").intValue = 0;
        entry.FindPropertyRelative("DependsOnIds").stringValue = string.Empty;
        entry.FindPropertyRelative("DependencyMode").intValue = 0;
        entry.FindPropertyRelative("AutoInstantiateDependencies").boolValue = false;
        entry.FindPropertyRelative("DependencyWaitTimeoutSeconds").floatValue = 0f;
        entry.FindPropertyRelative("DependencyMissingPolicy").intValue = 0;
    }

    private static void DrawFoldoutArrow(float x, float y, bool expanded)
    {
        Color oldColor = Handles.color;
        Handles.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        if (expanded)
        {
            Handles.DrawAAConvexPolygon(
                new Vector3(x, y - 3f, 0),
                new Vector3(x + 10f, y - 3f, 0),
                new Vector3(x + 5f, y + 4f, 0));
        }
        else
        {
            Handles.DrawAAConvexPolygon(
                new Vector3(x, y - 5f, 0),
                new Vector3(x + 7f, y, 0),
                new Vector3(x, y + 5f, 0));
        }

        Handles.color = oldColor;
    }
}
