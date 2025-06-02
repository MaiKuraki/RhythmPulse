#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using RhythmPulse.Gameplay.Media;

[CustomEditor(typeof(GameplayVideoPlayer))]
public class GameplayVideoPlayerEditor : Editor
{
    private string memoryUsage;
    private string textureResolutionInfo;
    private VideoPlayer cachedVideoPlayer; // Cache the VideoPlayer component

    public void OnEnable()
    {
        GameplayVideoPlayer player = (GameplayVideoPlayer)target;
        cachedVideoPlayer = player.GetComponent<VideoPlayer>();
        UpdateDebugInfo(player); // Initial update
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GameplayVideoPlayer player = (GameplayVideoPlayer)target;
        UpdateDebugInfo(player); // Update info continuously in inspector

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Render Texture Information", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Texture Details:", textureResolutionInfo);
        EditorGUILayout.LabelField("Est. Memory Usage:", memoryUsage);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Memory usage estimates for uncompressed formats:\n" +
            "1080p (1920x1080, ARGB32) = ~8.29 MB\n" +
            "4K (3840x2160, ARGB32) = ~33.18 MB\n\n" +
            "Bits per pixel for common available formats (RenderTextureFormat):\n" +
            "ARGB32, BGRA32: 32 bpp\n" +
            "RGB565, ARGB4444, RHalf: 16 bpp\n" +
            "ARGBHalf, RGFloat: 64 bpp\n" +
            "ARGBFloat: 128 bpp\n" +
            "Note: Availability of specific formats varies by Unity version.",
            MessageType.Info);

        if (GUILayout.Button("Recreate Target Texture"))
        {
            player.CreateTargetTexture();
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(player);
            }
            UpdateDebugInfo(player); // Refresh info
        }

        if (Application.isPlaying && cachedVideoPlayer != null && cachedVideoPlayer.isPlaying)
        {
            Repaint(); // Keep UI updated if video is playing
        }
    }

    private void UpdateDebugInfo(GameplayVideoPlayer componentInstance)
    {
        RenderTexture rt = componentInstance.VideoTexture;

        if (rt == null || !rt.IsCreated())
        {
            memoryUsage = "No texture allocated or not created";
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
            case RenderTextureFormat.RGB565:    // R5G6B5 is often an alias or equivalent if available
            case RenderTextureFormat.RHalf:     // 1x16f
            case RenderTextureFormat.RG16:      // 2x8 fixed point (unsigned normalized integers)
            case RenderTextureFormat.R16:       // 1x16 fixed point (unsigned normalized integers)
                bitsPerPixel = 16;
                break;

            // 32 bpp
            case RenderTextureFormat.ARGB32:    // 4x8 fixed point (e.g., Color32)
            case RenderTextureFormat.BGRA32:    // Common on some platforms
            case RenderTextureFormat.RG32:      // 2x16 fixed point
            case RenderTextureFormat.RFloat:    // 1x32f
            case RenderTextureFormat.RGB111110Float: // Packed float
                bitsPerPixel = 32;
                break;

            // 64 bpp
            case RenderTextureFormat.ARGBHalf:  // 4x16f (e.g., half4)
            case RenderTextureFormat.RGFloat:   // 2x32f
                bitsPerPixel = 64;
                break;

            // 128 bpp
            case RenderTextureFormat.ARGBFloat: // 4x32f (e.g., float4)
                bitsPerPixel = 128;
                break;

            // Default / Unknown or formats not available in this Unity version's enum
            default:
                Debug.LogWarning($"[GameplayVideoPlayerEditor] Unhandled or unavailable RenderTextureFormat '{format}' for memory calculation. Defaulting to 32bpp. This format might be from a newer/different Unity version.");
                bitsPerPixel = 32; // Fallback to a common default
                break;
        }

        long memorySizeBytes = (long)rt.width * rt.height * bitsPerPixel / 8;

        memoryUsage = EditorUtility.FormatBytes(memorySizeBytes);
        textureResolutionInfo = $"{rt.width}x{rt.height} (Format: {format}, ~{bitsPerPixel} bpp)";
    }
}

#endif