#if UNITY_EDITOR
using System.Text;
using RhythmPulse.Media;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UnityVideoProvider))]
public sealed class UnityVideoProviderEditor : Editor
{
    private string _currentTextureInfo;
    private string _previousTextureInfo;
    private string _currentMemory;
    private string _previousMemory;
    private string _totalMemory;

    private bool _showMemoryGuide;
    private double _lastUpdateTime;
    private const double UpdateInterval = 0.1;

    private readonly StringBuilder _sb = new(256);

    // Cached styles
    private static GUIStyle s_BoxStyle;
    private static GUIStyle s_HeaderStyle;
    private static GUIStyle s_TotalMemoryStyle;
    private static GUIStyle s_MemoryStyle;
    private static GUIStyle s_InfoStyle;
    private static GUIStyle s_RichTextStyle;
    private static bool s_StylesInitialized;

    private static readonly Color HeaderBgColor = new(0.2f, 0.2f, 0.2f, 0.3f);
    private static readonly Color SectionBgColor = new(0.15f, 0.15f, 0.15f, 0.2f);

    private static void EnsureStyles()
    {
        if (s_StylesInitialized) return;

        s_BoxStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(8, 8, 6, 6),
            margin = new RectOffset(0, 0, 2, 2)
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

        s_MemoryStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            fontSize = 11,
            normal = { textColor = new Color(0.5f, 0.75f, 1f) }
        };

        s_InfoStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            fontSize = 10,
            padding = new RectOffset(2, 2, 1, 1)
        };

        s_RichTextStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            richText = true,
            fontSize = 10
        };

        s_StylesInitialized = true;
    }

    private void OnEnable()
    {
        UpdateDebugInfo((UnityVideoProvider)target);
    }

    public override void OnInspectorGUI()
    {
        EnsureStyles();
        DrawDefaultInspector();

        var provider = (UnityVideoProvider)target;

        // Throttle updates
        double currentTime = EditorApplication.timeSinceStartup;
        if (currentTime - _lastUpdateTime > UpdateInterval)
        {
            UpdateDebugInfo(provider);
            _lastUpdateTime = currentTime;
        }

        EditorGUILayout.Space(10);

        // Header with total memory
        DrawHeader(provider);

        EditorGUILayout.Space(6);

        // Textures info
        DrawTexturesSection(provider);

        EditorGUILayout.Space(4);

        // Memory guide foldout
        _showMemoryGuide = EditorGUILayout.BeginFoldoutHeaderGroup(_showMemoryGuide, "Memory Reference Guide");
        if (_showMemoryGuide)
        {
            DrawMemoryGuide();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);

        // Actions
        DrawActions(provider);

        if (Application.isPlaying && provider.IsCurrentVideoPlaying)
            Repaint();
    }

    private void DrawHeader(UnityVideoProvider provider)
    {
        var rect = EditorGUILayout.BeginVertical(s_BoxStyle);
        EditorGUI.DrawRect(rect, HeaderBgColor);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Video Provider", s_HeaderStyle);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(_totalMemory ?? "N/A", s_TotalMemoryStyle, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawTexturesSection(UnityVideoProvider provider)
    {
        EditorGUILayout.BeginVertical(s_BoxStyle);

        // Current texture
        DrawTextureEntry("Current Texture", _currentTextureInfo, _currentMemory, true);

        EditorGUILayout.Space(4);

        // Previous texture
        if (provider.PreviousFrameTexture != null && provider.PreviousFrameTexture.IsCreated())
        {
            DrawTextureEntry("Previous Texture", _previousTextureInfo, _previousMemory, false);
        }
        else
        {
            DrawTextureEntry("Previous Texture", "Not allocated", null, false);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTextureEntry(string title, string info, string memory, bool isPrimary)
    {
        EditorGUILayout.BeginVertical();

        // Title row with memory
        EditorGUILayout.BeginHorizontal();
        {
            // Status indicator
            string indicator = (info != null && !info.Contains("Not allocated")) ? "●" : "○";
            var indicatorStyle = (info != null && !info.Contains("Not allocated"))
                ? new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.3f, 0.85f, 0.3f) }, fontSize = 11 }
                : new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }, fontSize = 11 };
            
            GUILayout.Label(indicator, indicatorStyle, GUILayout.Width(16));
            EditorGUILayout.LabelField(title, isPrimary ? EditorStyles.boldLabel : EditorStyles.label);
            
            GUILayout.FlexibleSpace();
            
            if (!string.IsNullOrEmpty(memory))
            {
                EditorGUILayout.LabelField(memory, s_MemoryStyle, GUILayout.Width(70));
            }
        }
        EditorGUILayout.EndHorizontal();

        // Info line
        if (!string.IsNullOrEmpty(info))
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(info, s_InfoStyle);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawMemoryGuide()
    {
        EditorGUILayout.BeginVertical(s_BoxStyle);
        GUILayout.Label(
            "<b>Memory estimates (uncompressed):</b>\n" +
            "  • 1080p ARGB32 ≈ <b>8.29 MB</b>\n" +
            "  • 4K ARGB32 ≈ <b>33.18 MB</b>\n\n" +
            "<b>Format bits per pixel:</b>\n" +
            "  • ARGB32/BGRA32: 32 bpp\n" +
            "  • RGB565/ARGB4444: 16 bpp\n" +
            "  • ARGBHalf: 64 bpp\n" +
            "  • ARGBFloat: 128 bpp\n\n" +
            "<b>Note:</b> MSAA multiplies color buffer memory.\n" +
            "This component uses <b>2 RenderTextures</b> for seamless swapping.",
            s_RichTextStyle
        );
        EditorGUILayout.EndVertical();
    }

    private void DrawActions(UnityVideoProvider provider)
    {
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Force Recreate Textures", GUILayout.Height(24)))
        {
            provider.EditorRecreateAllManagedTextures();
            UpdateDebugInfo(provider);
            EditorUtility.SetDirty(provider);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void UpdateDebugInfo(UnityVideoProvider provider)
    {
        var rt = provider.CurrentVideoTexture;
        var prev = provider.PreviousFrameTexture;

        long currentBytes = 0;
        long prevBytes = 0;

        if (rt != null && rt.IsCreated())
        {
            currentBytes = EstimateMemory(rt);
            _currentTextureInfo = BuildTextureInfo(rt);
            _currentMemory = EditorUtility.FormatBytes(currentBytes);
        }
        else
        {
            _currentTextureInfo = "Not allocated";
            _currentMemory = null;
        }

        if (prev != null && prev.IsCreated())
        {
            prevBytes = EstimateMemory(prev);
            _previousTextureInfo = BuildTextureInfo(prev);
            _previousMemory = EditorUtility.FormatBytes(prevBytes);
        }
        else
        {
            _previousTextureInfo = "Not allocated";
            _previousMemory = null;
        }

        long total = currentBytes + prevBytes;
        _totalMemory = total > 0 ? EditorUtility.FormatBytes(total) : "0 B";
    }

    private string BuildTextureInfo(RenderTexture rt)
    {
        _sb.Clear();
        _sb.Append(rt.width).Append('x').Append(rt.height)
           .Append("  ").Append(rt.format)
           .Append("  AA:").Append(Mathf.Max(rt.antiAliasing, 1));
        return _sb.ToString();
    }

    private static long EstimateMemory(RenderTexture rt)
    {
        int bpp = GetBitsPerPixel(rt.format);
        long bytes = (long)rt.width * rt.height * bpp / 8;

        if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray ||
            rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D)
        {
            bytes *= rt.volumeDepth;
        }

        if (rt.useMipMap)
            bytes = (long)(bytes * 1.33333f);

        bytes *= Mathf.Max(rt.antiAliasing, 1);
        return bytes;
    }

    private static int GetBitsPerPixel(RenderTextureFormat format)
    {
        return format switch
        {
            RenderTextureFormat.R8 => 8,
            RenderTextureFormat.ARGB4444 or RenderTextureFormat.RGB565 or
            RenderTextureFormat.RHalf or RenderTextureFormat.RG16 or RenderTextureFormat.R16 => 16,
            RenderTextureFormat.ARGB32 or RenderTextureFormat.BGRA32 or
            RenderTextureFormat.RG32 or RenderTextureFormat.RFloat or
            RenderTextureFormat.RGB111110Float or RenderTextureFormat.Default => 32,
            RenderTextureFormat.ARGBHalf or RenderTextureFormat.RGFloat => 64,
            RenderTextureFormat.ARGBFloat => 128,
            _ => TryGetBppFromGraphicsFormat(format)
        };
    }

    private static int TryGetBppFromGraphicsFormat(RenderTextureFormat format)
    {
#if UNITY_2019_3_OR_NEWER
        try
        {
            var gfmt = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, RenderTextureReadWrite.Default);
            return (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(gfmt) * 8;
        }
        catch { return 32; }
#else
        return 32;
#endif
    }
}
#endif