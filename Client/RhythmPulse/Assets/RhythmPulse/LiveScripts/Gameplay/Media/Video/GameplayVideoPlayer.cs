using System;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Video;

namespace RhythmPulse.Gameplay.Media
{
    public interface IGameplayVideoPlayer
    {
        RenderTexture VideoTexture { get; }
        void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null);
        void Play();
        void Stop();
        void Pause();
        void Resume();
        long GetPlaybackTimeMSec();
        void SeekTime(long milliSeconds);
        void CreateTargetTexture(); // Exposed for manual recreation if needed
    }

    /// <summary>
    /// Handles video playback with configurable render texture settings and memory management.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    public class GameplayVideoPlayer : MonoBehaviour, IGameplayVideoPlayer
    {
        private const string DEBUG_FLAG = "[GameplayVideoPlayer]";

        [Header("Video Settings")]
        public Vector2Int textureResolution = new Vector2Int(1920, 1080);
        public RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        public int depthBuffer = 0; // 0 for no depth buffer (common for video), 16 or 24 if needed for 3D effects.
        public FilterMode filterMode = FilterMode.Bilinear;

        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        public RenderTexture VideoTexture => videoTexture;

        private Action onVideoPreparedCallback;

        private void Awake()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer component not found on this GameObject. Disabling component.");
                enabled = false;
                return;
            }

            CreateTargetTexture(); // Initial texture creation
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoTexture; // Assign after creation
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
                // Unsubscribe from all events to prevent errors if destroyed while processing.
                videoPlayer.prepareCompleted -= OnVideoPreparedInternal;
                videoPlayer.errorReceived -= OnVideoError;
                videoPlayer.loopPointReached -= OnVideoLoopPointReached;
            }

            if (videoTexture != null)
            {
                if (videoTexture.IsCreated())
                {
                    videoTexture.Release();
                }
                Destroy(videoTexture); // Destroy the RenderTexture asset.
                videoTexture = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Called in the editor when a script property is changed.
            // Re-assign videoPlayer if it's null (e.g., after script recompile in editor).
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
            }

            // If parameters change during play mode, recreate the render texture.
            if (Application.isPlaying && videoPlayer != null && videoTexture != null &&
                (videoTexture.width != textureResolution.x ||
                 videoTexture.height != textureResolution.y ||
                 videoTexture.format != textureFormat ||
                 videoTexture.depth != depthBuffer ||
                 videoTexture.filterMode != filterMode))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Video texture parameters changed in Inspector. Recreating texture.");
                CreateTargetTexture();
                // Ensure the video player uses the new texture if it was validly created
                if (videoPlayer.targetTexture != videoTexture && videoTexture != null && videoTexture.IsCreated())
                {
                    videoPlayer.targetTexture = videoTexture;
                }
            }
        }
#endif

        /// <summary>
        /// Creates or recreates the render texture based on current configuration settings.
        /// </summary>
        public void CreateTargetTexture()
        {
            // Release existing texture if present.
            if (videoTexture != null)
            {
                if (videoPlayer != null && videoPlayer.targetTexture == videoTexture)
                {
                    videoPlayer.targetTexture = null; // Detach from player before releasing.
                }
                if (videoTexture.IsCreated())
                {
                    videoTexture.Release();
                }
                Destroy(videoTexture);
                videoTexture = null;
            }

            videoTexture = new RenderTexture(
                textureResolution.x,
                textureResolution.y,
                depthBuffer,
                textureFormat
            );

            videoTexture.filterMode = filterMode;
            videoTexture.name = "GameplayVideoRenderTexture"; // Useful for debugging.

            if (!videoTexture.Create())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create RenderTexture. Video output may not work.");
                Destroy(videoTexture); // Clean up the failed texture asset.
                videoTexture = null;
                // Ensure player doesn't point to a failed texture if it was already assigned.
                if (videoPlayer != null && videoPlayer.targetTexture != null && !videoPlayer.targetTexture.IsCreated())
                {
                    videoPlayer.targetTexture = null;
                }
                return; // Exit if creation failed.
            }

            // Assign to video player for output if videoPlayer is available and texture is valid.
            if (videoPlayer != null)
            {
                videoPlayer.targetTexture = videoTexture;
            }
            else if (Application.isPlaying)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} VideoPlayer component not yet available to assign targetTexture. Ensure Awake has run or assign manually.");
            }
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            if (videoPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer is not initialized. Cannot initialize.");
                return;
            }

            if (videoPlayer.isPlaying || videoPlayer.isPaused || videoPlayer.isPrepared || videoPlayer.isLooping)
            {
                videoPlayer.Stop();
            }

            // Always unsubscribe before subscribing to prevent multiple event registrations.
            videoPlayer.prepareCompleted -= OnVideoPreparedInternal;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.loopPointReached -= OnVideoLoopPointReached;

            this.onVideoPreparedCallback = OnPrepared;

            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoUrl;
            videoPlayer.isLooping = bLoop;

            videoPlayer.prepareCompleted += OnVideoPreparedInternal;
            videoPlayer.errorReceived += OnVideoError;
            videoPlayer.loopPointReached += OnVideoLoopPointReached;

            CLogger.LogInfo($"{DEBUG_FLAG} Preparing video: {videoUrl}");
            videoPlayer.Prepare();
        }

        private void OnVideoPreparedInternal(VideoPlayer source)
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Video prepared successfully: {source.url}");
            source.prepareCompleted -= OnVideoPreparedInternal; // Unsubscribe to ensure one-shot execution per Prepare().

            onVideoPreparedCallback?.Invoke();
            onVideoPreparedCallback = null; // Clear callback after invoking.
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            CLogger.LogError($"{DEBUG_FLAG} VideoPlayer Error: {message} (URL: {source.url})");
            // If an error occurs, preparation might have failed or will not complete as expected.
            source.prepareCompleted -= OnVideoPreparedInternal; // Unsubscribe from prepare if an error occurs.
            onVideoPreparedCallback = null; // Clear pending prepared callback.
            // Optionally, invoke a specific error callback to the user here.
        }

        private void OnVideoLoopPointReached(VideoPlayer source)
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Video loop point reached: {source.url}");
            // Custom logic for loop point can be added here if needed beyond VideoPlayer's isLooping.
        }

        public void Play()
        {
            if (videoPlayer != null && videoPlayer.isPrepared)
            {
                videoPlayer.Play();
                // CLogger.LogInfo($"{DEBUG_FLAG} Play called.");
            }
            else if (videoPlayer != null && !videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but VideoPlayer is not prepared. Call InitializeVideoPlayer and wait for OnPrepared callback.");
            }
            else
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer is null. Cannot play.");
            }
        }

        public void Stop()
        {
            if (videoPlayer != null && (videoPlayer.isPlaying || videoPlayer.isPaused || videoPlayer.isPrepared))
            {
                videoPlayer.Stop();
                // After Stop(), VideoPlayer is no longer in a prepared state.
                // Prepare() must be called again before playing.
            }
        }

        public void Pause()
        {
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
                // CLogger.LogInfo($"{DEBUG_FLAG} Pause called.");
            }
        }

        public void Resume()
        {
            if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPaused)
            {
                videoPlayer.Play(); // VideoPlayer.Play() resumes if paused.
                // CLogger.LogInfo($"{DEBUG_FLAG} Resume (via Play) called.");
            }
            else if (videoPlayer != null && videoPlayer.isPrepared && !videoPlayer.isPlaying && !videoPlayer.isPaused)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but video is not playing or paused (e.g. stopped or at end). Call Play() to start from beginning or after seeking.");
            }
            else if (videoPlayer != null && !videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but VideoPlayer is not prepared.");
            }
        }

        public long GetPlaybackTimeMSec()
        {
            if (videoPlayer != null)
            {
                return (long)(videoPlayer.time * 1000.0);
            }
            return 0;
        }

        public void SeekTime(long milliSeconds)
        {
            if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.canSetTime)
            {
                double newTime = milliSeconds / 1000.0;

                // Clamp time to valid video duration.
                if (newTime < 0) newTime = 0;
                // Seeking beyond length is usually clamped by VideoPlayer, but explicit clamping is safer.
                if (videoPlayer.length > 0 && newTime > videoPlayer.length) newTime = videoPlayer.length;

                videoPlayer.time = newTime;
                // CLogger.LogInfo($"{DEBUG_FLAG} SeekTime to {milliSeconds}ms ({newTime:F3}s).");
            }
            else if (videoPlayer != null && !videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but VideoPlayer is not prepared.");
            }
            else if (videoPlayer != null && !videoPlayer.canSetTime)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but VideoPlayer cannot be seeked currently (e.g. streaming HLS without seekable ranges).");
            }
        }
    }
}