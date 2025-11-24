#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RhythmPulse.Gameplay.Media;

[CustomEditor(typeof(UnityVideoProvider))]
public class GameplayVideoPlayerEditor : Editor
{
	 private string memoryUsage;
	 private string textureResolutionInfo;
	 private string previousMemoryUsage;
	 private string previousTextureInfo;
	 private string totalMemoryUsage;
	 private bool showMemoryGuide;

	 // Cached styles to improve readability and wrap long lines in narrow inspectors
	 private static GUIStyle s_WordWrapLabel;
	 private static GUIStyle s_WordWrapRichLabel;
	 private static bool s_StylesInitialized;

	 private static void EnsureStyles()
	 {
		 if (s_StylesInitialized) return;
		 s_WordWrapLabel = new GUIStyle(EditorStyles.label)
		 {
			 wordWrap = true,
			 richText = false
		 };
		 s_WordWrapRichLabel = new GUIStyle(EditorStyles.label)
		 {
			 wordWrap = true,
			 richText = true,
		 };
		 s_StylesInitialized = true;
	 }

    public void OnEnable()
    {
        UnityVideoProvider provider = (UnityVideoProvider)target;
        UpdateDebugInfo(provider); // Initial update
    }

    public override void OnInspectorGUI()
    {
		 EnsureStyles();
        DrawDefaultInspector();

        UnityVideoProvider provider = (UnityVideoProvider)target;
        UpdateDebugInfo(provider); // Update info continuously in inspector

        EditorGUILayout.Space();
		 EditorGUILayout.LabelField("Render Texture Information", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		 EditorGUILayout.LabelField("Current Texture", EditorStyles.boldLabel);
		 GUILayout.Label(textureResolutionInfo, s_WordWrapLabel);
		 EditorGUILayout.LabelField("Est. Memory Usage (Current):", memoryUsage);
        if (provider.PreviousFrameTexture != null && provider.PreviousFrameTexture.IsCreated())
        {
			 EditorGUILayout.Space(4);
			 EditorGUILayout.LabelField("Previous Texture", EditorStyles.boldLabel);
			 GUILayout.Label(previousTextureInfo, s_WordWrapLabel);
			 EditorGUILayout.LabelField("Est. Memory Usage (Previous):", previousMemoryUsage);
			 EditorGUILayout.LabelField("Est. Memory Usage (Total):", totalMemoryUsage);
        }
        else
        {
            EditorGUILayout.LabelField("Previous Frame Texture:", "N/A or not created");
			 EditorGUILayout.LabelField("Est. Memory Usage (Total):", memoryUsage);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
		 showMemoryGuide = EditorGUILayout.BeginFoldoutHeaderGroup(showMemoryGuide, "Memory usage reference");
		 if (showMemoryGuide)
		 {
			 EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			 // Use rich text for slightly larger and clearer reference text
			 string guide =
				 "<b><size=12>Memory usage estimates (uncompressed):</size></b>\n" +
				 "- 1080p (1920x1080, ARGB32) ≈ 8.29 MB per texture\n" +
				 "- 4K (3840x2160, ARGB32) ≈ 33.18 MB per texture\n\n" +
				 "<b><size=12>Notes:</size></b>\n" +
				 "- This component uses two RenderTextures for seamless swapping.\n" +
				 "- Bits per pixel examples:\n" +
				 "  • ARGB32/BGRA32: 32 bpp\n" +
				 "  • RGB565/ARGB4444/RHalf: 16 bpp\n" +
				 "  • ARGBHalf/RGFloat: 64 bpp\n" +
				 "  • ARGBFloat: 128 bpp\n" +
				 "- MSAA multiplies color buffer memory by antiAliasing.\n" +
				 "- Depth buffer memory is not included in these estimates.\n" +
				 "- Actual memory may vary by platform/graphics API.";
			 GUILayout.Label(guide, s_WordWrapRichLabel);
			 EditorGUILayout.EndVertical();
		 }
		 EditorGUILayout.EndFoldoutHeaderGroup();

        if (GUILayout.Button("Force Recreate All Managed Textures"))
        {
            if (Application.isPlaying)
            {
                provider.EditorRecreateAllManagedTextures();
            }
            else
            {
                // In editor mode (not playing), direct manipulation of textures can be tricky
                // if Awake hasn't run or if it's a prefab.
                // The OnValidate method in GameplayVideoPlayer will handle changes to public
                // properties like textureResolution when in play mode.
                // Awake will handle initial creation when entering play mode.
                provider.EditorRecreateAllManagedTextures(); // Call it anyway, it has some checks
                EditorUtility.SetDirty(provider);
            }
            UpdateDebugInfo(provider); // Refresh info
        }

        if (Application.isPlaying && provider.IsCurrentVideoPlaying) // Use the new property
        {
            Repaint(); // Keep UI updated if video is playing
        }
    }

    private void UpdateDebugInfo(UnityVideoProvider componentInstance)
    {
        // Display info for the CurrentVideoTexture
        RenderTexture rt = componentInstance.CurrentVideoTexture;
		 RenderTexture prev = componentInstance.PreviousFrameTexture;

        if (rt == null || !rt.IsCreated())
        {
            memoryUsage = "No current texture allocated or not created";
			 textureResolutionInfo = "N/A";
			 previousTextureInfo = (prev != null && prev.IsCreated())
			 	? $"{prev.name} ({prev.width}x{prev.height}, Format: {prev.format}, Mips: {prev.useMipMap}, AA: {Mathf.Max(prev.antiAliasing, 1)})"
			 	: "N/A";
			 previousMemoryUsage = (prev != null && prev.IsCreated())
			 	? EditorUtility.FormatBytes(EstimateRenderTextureMemoryBytes(prev))
			 	: "N/A";
			 totalMemoryUsage = "N/A";
            return;
        }

		 long currentBytes = EstimateRenderTextureMemoryBytes(rt);
		 memoryUsage = EditorUtility.FormatBytes(currentBytes);
		 int bpp = GetBitsPerPixel(rt.format);
		 int aa = Mathf.Max(rt.antiAliasing, 1);
		 textureResolutionInfo =
			 $"{rt.name}\n" +
			 $"Size: {rt.width}x{rt.height}\n" +
			 $"Format: {rt.format} (~{bpp} bpp)\n" +
			 $"Mips: {rt.useMipMap}    AA: {aa}";

		 if (prev != null && prev.IsCreated())
		 {
			 long prevBytes = EstimateRenderTextureMemoryBytes(prev);
			 previousMemoryUsage = EditorUtility.FormatBytes(prevBytes);
			 int prevBpp = GetBitsPerPixel(prev.format);
			 int prevAa = Mathf.Max(prev.antiAliasing, 1);
			 previousTextureInfo =
				 $"{prev.name}\n" +
				 $"Size: {prev.width}x{prev.height}\n" +
				 $"Format: {prev.format} (~{prevBpp} bpp)\n" +
				 $"Mips: {prev.useMipMap}    AA: {prevAa}";
			 totalMemoryUsage = EditorUtility.FormatBytes(currentBytes + prevBytes);
		 }
		 else
		 {
			 previousTextureInfo = "N/A or not created";
			 previousMemoryUsage = "N/A";
			 totalMemoryUsage = memoryUsage;
		 }
    }

	 private static int GetBitsPerPixel(RenderTextureFormat format)
	 {
		 switch (format)
		 {
			 case RenderTextureFormat.R8: return 8;
			 case RenderTextureFormat.ARGB4444:
			 case RenderTextureFormat.RGB565:
			 case RenderTextureFormat.RHalf:
			 case RenderTextureFormat.RG16:
			 case RenderTextureFormat.R16:
				 return 16;
			 case RenderTextureFormat.ARGB32:
			 case RenderTextureFormat.BGRA32:
			 case RenderTextureFormat.RG32:
			 case RenderTextureFormat.RFloat:
			 case RenderTextureFormat.RGB111110Float:
				 return 32;
			 case RenderTextureFormat.ARGBHalf:
			 case RenderTextureFormat.RGFloat:
				 return 64;
			 case RenderTextureFormat.ARGBFloat:
				 return 128;
			 case RenderTextureFormat.Default:
				 // Conservative default; actual may vary by graphics API
				 return 32;
			 default:
#if UNITY_2019_3_OR_NEWER
				 try
				 {
					 var gfmt = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, RenderTextureReadWrite.Default);
					 // GetBlockSize returns bytes per texel for uncompressed formats; multiply to bits
					 return (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(gfmt) * 8;
				 }
				 catch
				 {
					 return 32;
				 }
#else
				 return 32;
#endif
		 }
	 }

	 private static long EstimateRenderTextureMemoryBytes(RenderTexture rt)
	 {
		 int bpp = GetBitsPerPixel(rt.format);
		 long bytes = (long)rt.width * rt.height * bpp / 8;
		 if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray || rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D)
		 {
			 bytes *= rt.volumeDepth;
		 }
		 if (rt.useMipMap)
		 {
			 bytes = (long)(bytes * 1.33333f); // Approx mip overhead
		 }
		 int aa = Mathf.Max(rt.antiAliasing, 1);
		 bytes *= aa; // MSAA color buffer overhead approximation
		 return bytes;
	 }
}

#endif