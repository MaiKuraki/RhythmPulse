using UnityEngine;
using UnityEngine.Video;
using VContainer;

namespace RhythmPulse.Gameplay.Media
{
    public interface IGameplayVideoPlayer
    {
        void Play();
        void Stop();
        void Pause();
        void Resume();
        void SeekTime(long milliSeconds);
    }
    /// <summary>
    /// Handles video playback with configurable render texture settings and memory management 
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    public class GameplayVideoPlayer : MonoBehaviour, IGameplayVideoPlayer
    {
        [Header("Video Settings")]
        public Vector2Int textureResolution = new Vector2Int(1920, 1080);
        public RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        public int depthBuffer = 0;
        public FilterMode filterMode = FilterMode.Bilinear;

        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        public RenderTexture VideoTexture => videoTexture;

        [Inject]
        void Constract()
        {

        }

        private void Awake()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            CreateTargetTexture();
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }
        }

        // Editor-only validation for real-time parameter changes 
#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && videoTexture != null &&
                (videoTexture.width != textureResolution.x || videoTexture.height != textureResolution.y))
            {
                CreateTargetTexture();
            }
        }
#endif 

        /// <summary>
        /// Creates a new render texture with current configuration settings 
        /// </summary>
        public void CreateTargetTexture()
        {
            // Release existing texture if present 
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }

            // Initialize new render texture with configured parameters 
            videoTexture = new RenderTexture(
                textureResolution.x,
                textureResolution.y,
                depthBuffer,
                textureFormat
            );

            // Apply texture filtering and create GPU resource 
            videoTexture.filterMode = filterMode;
            videoTexture.Create();

            // Assign to video player for output 
            videoPlayer.targetTexture = videoTexture;
        }

        public void Play()
        {
            videoPlayer.Play();
        }

        public void Stop()
        {
            videoPlayer.Stop();
        }

        public void Pause()
        {
            videoPlayer.Pause();
        }

        public void Resume()
        {
            videoPlayer.Play();
        }

        public long GetPlaybackTimeMSec()
        {
            return (long)(videoPlayer.time * 1000.0);
        }

        public void SeekTime(long milliSeconds)
        {
            videoPlayer.time = milliSeconds / 1000.0;
        }

        public void InitializeVideoPlayer(string videoUrl)
        {
            videoPlayer.url = videoUrl;
        }
    }
}