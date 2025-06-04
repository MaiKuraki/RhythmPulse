#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RhythmPulse.Gameplay.Media;

[CustomEditor(typeof(GameplayVideoPlayer))]
public class GameplayVideoPlayerEditor : Editor
{
    private string memoryUsage;
    private string textureResolutionInfo;

    public void OnEnable()
    {
        GameplayVideoPlayer player = (GameplayVideoPlayer)target;
        UpdateDebugInfo(player); // Initial update
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GameplayVideoPlayer player = (GameplayVideoPlayer)target;
        UpdateDebugInfo(player); // Update info continuously in inspector

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Render Texture Information (Current Video)", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Current Texture Details:", textureResolutionInfo);
        EditorGUILayout.LabelField("Est. Memory Usage:", memoryUsage);
        if (player.PreviousFrameTexture != null && player.PreviousFrameTexture.IsCreated())
        {
            EditorGUILayout.LabelField("Previous Frame Texture:", $"{player.PreviousFrameTexture.name} ({player.PreviousFrameTexture.width}x{player.PreviousFrameTexture.height})");
        }
        else
        {
            EditorGUILayout.LabelField("Previous Frame Texture:", "N/A or not created");
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
          "Memory usage estimates for uncompressed formats:\n" +
          "1080p (1920x1080, ARGB32) = ~8.29 MB per texture\n" +
          "4K (3840x2160, ARGB32) = ~33.18 MB per texture\n\n" +
          "This component uses two RenderTextures internally for seamless swapping.\n" +
          "Bits per pixel for common available formats (RenderTextureFormat):\n" +
          "ARGB32, BGRA32: 32 bpp\n" +
          "RGB565, ARGB4444, RHalf: 16 bpp\n" +
          "ARGBHalf, RGFloat: 64 bpp\n" +
          "ARGBFloat: 128 bpp\n" +
          "Note: Availability of specific formats varies by Unity version.",
          MessageType.Info);

        if (GUILayout.Button("Force Recreate All Managed Textures"))
        {
            if (Application.isPlaying)
            {
                player.EditorRecreateAllManagedTextures();
            }
            else
            {
                // In editor mode (not playing), direct manipulation of textures can be tricky
                // if Awake hasn't run or if it's a prefab.
                // The OnValidate method in GameplayVideoPlayer will handle changes to public
                // properties like textureResolution when in play mode.
                // Awake will handle initial creation when entering play mode.
                player.EditorRecreateAllManagedTextures(); // Call it anyway, it has some checks
                EditorUtility.SetDirty(player);
            }
            UpdateDebugInfo(player); // Refresh info
        }

        if (Application.isPlaying && player.IsCurrentVideoPlaying) // Use the new property
        {
            Repaint(); // Keep UI updated if video is playing
        }
    }

    private void UpdateDebugInfo(GameplayVideoPlayer componentInstance)
    {
        // Display info for the CurrentVideoTexture
        RenderTexture rt = componentInstance.CurrentVideoTexture;

        if (rt == null || !rt.IsCreated())
        {
            memoryUsage = "No current texture allocated or not created";
            textureResolutionInfo = "N/A";
            return;
        }

        int bitsPerPixel;
        RenderTextureFormat format = rt.format;

        switch (format)
        {
            // 8 bpp
            case RenderTextureFormat.R8:
                bitsPerPixel = 8;
                break;

            // 16 bpp
            case RenderTextureFormat.ARGB4444:
            case RenderTextureFormat.RGB565:
            case RenderTextureFormat.RHalf:
            case RenderTextureFormat.RG16:
            case RenderTextureFormat.R16:
                bitsPerPixel = 16;
                break;

            // 32 bpp
            case RenderTextureFormat.ARGB32:
            case RenderTextureFormat.BGRA32: // Often an alias or swap for ARGB32
            case RenderTextureFormat.RG32:   // 2x16 fixed point
            case RenderTextureFormat.RFloat: // 1x32f
            case RenderTextureFormat.RGB111110Float: // Packed float, ensure this is a valid and desired comparison
                bitsPerPixel = 32;
                break;

            // 64 bpp
            case RenderTextureFormat.ARGBHalf: // 4x16f
            case RenderTextureFormat.RGFloat:  // 2x32f
                bitsPerPixel = 64;
                break;

            // 128 bpp
            case RenderTextureFormat.ARGBFloat: // 4x32f
                bitsPerPixel = 128;
                break;

            // Add more cases if you use other specific formats like Depth, Shadowmap, YUV, etc.
            // For example, Default might be treated as ARGB32 or a platform specific format.
            case RenderTextureFormat.Default:
                // GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.Default, RenderTextureReadWrite.Default)
                // can give more info, but for a quick estimate:
                bitsPerPixel = 32; // Common default, but can vary.
                Debug.LogWarning($"[GameplayVideoPlayerEditor] RenderTextureFormat.Default used for '{rt.name}'. Estimating 32bpp. Actual bpp depends on platform graphics settings.");
                break;

            default:
                // Attempt to get bits per pixel using Unity's utility if available (Unity 2019.3+)
                // This is a more robust way for unlisted formats
#if UNITY_2019_3_OR_NEWER
                try
                {
                    bitsPerPixel = (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, rt.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)) * 8;
                }
                catch
                {
                    Debug.LogWarning($"[GameplayVideoPlayerEditor] Unhandled RenderTextureFormat '{format}' for memory calculation on '{rt.name}'. Defaulting to 32bpp. Consider adding a case for it.");
                    bitsPerPixel = 32; // Fallback
                }
#else
                Debug.LogWarning($"[GameplayVideoPlayerEditor] Unhandled RenderTextureFormat '{format}' for memory calculation on '{rt.name}'. Defaulting to 32bpp. Consider adding a case for it or upgrading Unity for GraphicsFormatUtility.");
                bitsPerPixel = 32; // Fallback
#endif
                break;
        }

        if (bitsPerPixel == 0)
        { // Should not happen if cases are well-defined
            Debug.LogError($"[GameplayVideoPlayerEditor] bitsPerPixel is 0 for format {format}. This will result in incorrect memory calculation.");
            bitsPerPixel = 32; // Safe fallback
        }

        long memorySizeBytes = (long)rt.width * rt.height * bitsPerPixel / 8;
        if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray || rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D)
        {
            memorySizeBytes *= rt.volumeDepth; // For Texture2DArray or 3D textures
        }
        if (rt.useMipMap)
        {
            // Mipmaps add roughly 1/3 more memory. This is an approximation.
            memorySizeBytes = (long)(memorySizeBytes * 1.33333f);
        }


        memoryUsage = EditorUtility.FormatBytes(memorySizeBytes);
        textureResolutionInfo = $"{rt.name} ({rt.width}x{rt.height}, Format: {format}, ~{bitsPerPixel} bpp, Mips: {rt.useMipMap})";
    }
}

#endif