#if UNITY_EDITOR 
using UnityEditor;
using UnityEngine;
using CycloneGames.Utility.Runtime;
using RhythmPulse.Gameplay.Media;
using UnityEngine.Video;

[CustomEditor(typeof(GameplayVideoPlayer))]
public class GameplayVideoPlayerEditor : Editor
{
    private string memoryUsage;
    private string textureResolutionInfo;
    private VideoPlayer cachedVideoPlayer;

    public void OnEnable()
    {
        cachedVideoPlayer = ((GameplayVideoPlayer)target).GetComponent<VideoPlayer>();
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        GameplayVideoPlayer player = (GameplayVideoPlayer)target;
        UpdateDebugInfo(player);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Memory Information", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Texture Resolution:", textureResolutionInfo);
        EditorGUILayout.LabelField("Memory Usage:", memoryUsage);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Memory usage scales quadratically with resolution:\n" +
            "1080p (1920x1080) = ~8MB (ARGB32)\n" +
            "4K (3840x2160) = ~32MB (ARGB32)\n" +
            "8K (7680x4320) = ~128MB (ARGB32)\n\n" +
            "Different formats can significantly affect memory usage:\n" +
            "ARGB32: 32 bits per pixel\n" +
            "RGB565: 16 bits per pixel\n" +
            "ARGBFloat: 128 bits per pixel",
            MessageType.Info);

        if (GUILayout.Button("Recreate Texture"))
        {
            player.CreateTargetTexture();
        }
    }

    private void UpdateDebugInfo(GameplayVideoPlayer player)
    {
        var videoTexture = cachedVideoPlayer?.targetTexture;

        if (videoTexture == null)
        {
            memoryUsage = "No texture allocated";
            textureResolutionInfo = "N/A";
            return;
        }

        // Calculate approximate memory footprint based on pixel format 
        int bitsPerPixel = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Color)) * 8;

        // Handle special texture formats 
        switch (videoTexture.format)
        {
            case RenderTextureFormat.ARGBHalf:
            case RenderTextureFormat.RGB565:
            case RenderTextureFormat.ARGB4444:
                bitsPerPixel = 16;
                break;
            case RenderTextureFormat.ARGBFloat:
            case RenderTextureFormat.RGFloat:
                bitsPerPixel = 128;
                break;
        }

        long memorySizeBytes = (long)videoTexture.width * videoTexture.height * bitsPerPixel / 8;

        // Format for human-readable display 
        memoryUsage = memorySizeBytes.ToMemorySizeString();
        textureResolutionInfo = $"{videoTexture.width}x{videoTexture.height}    ({videoTexture.format})";
    }
}
#endif