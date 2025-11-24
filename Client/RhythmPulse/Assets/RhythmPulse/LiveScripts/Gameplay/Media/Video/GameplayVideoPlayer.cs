using System;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

namespace RhythmPulse.Gameplay.Media
{
    /// <summary>
    /// Implementation of IGameplayVideoPlayer using Unity VideoPlayer.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    public class GameplayVideoPlayer : MonoBehaviour, IGameplayVideoPlayer
    {
        private const string DEBUG_FLAG = "[GameplayVideoPlayer]";

        [Header("Video Settings")]
        [Tooltip("Target resolution for the RenderTextures. On low-end devices, this may be automatically downscaled.")]
        public Vector2Int textureResolution = new Vector2Int(1920, 1080);
        public RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        public int depthBuffer = 0;
        public FilterMode filterMode = FilterMode.Bilinear;
        public VideoTimeUpdateMode timeUpdateMode = VideoTimeUpdateMode.DSPTime;

        [Tooltip("On low-end devices (mobile), drop frames to maintain sync.")]
        public bool skipOnDrop = true;

        [Tooltip("If true, automatically reduces texture resolution on devices with low system memory (< 4GB).")]
        public bool autoDownscaleOnLowEnd = true;

        [Header("Audio Settings")]
        [Tooltip("If true, video audio will be directed to an AudioSource (if attached) or Direct.")]
        public bool enableAudio = false;
        [Range(0f, 1f)]
        public float defaultVolume = 1.0f;

        [Header("Seek Settings")]
        public bool cancelStandbyPrepareOnSeekSameUrl = true;
        public int seekTimeoutMs = 2000;

        [Header("Prepare Settings")]
        // Delays to mitigate freezing on mobile platforms during preparation
        public int preparePreDelayMs = 100;
        public int internalStopToPrepareDelayMs = 100;
        public int preparePostDelayMs = 50;
        public int prepareTimeoutMs = 8000; // Increased for web/network streams

        [Header("Retry Settings")]
        public int maxPrepareRetries = 2;
        public int prepareRetryDelayMs = 500;

        private VideoPlayer[] _videoPlayers = new VideoPlayer[2];
        private VideoPlayer _currentVideoPlayer;
        private VideoPlayer _standbyVideoPlayer;
        private RenderTexture[] _renderTextures = new RenderTexture[2];

        private RenderTexture _currentVideoTexture;
        private RenderTexture _previousFrameTexture;

        public RenderTexture CurrentVideoTexture => _currentVideoTexture;
        public RenderTexture PreviousFrameTexture => _previousFrameTexture;

        // Cancellation Handling
        private CancellationTokenSource _masterPrepareCts;
        private Action _currentUserOnPreparedCallback;
        private string _currentVideoUrlBeingPreparedOnStandby;
        private CancellationToken _activeAsyncOperationToken = CancellationToken.None;
        private CancellationTokenSource _seekCts;

        // Helper for low-end detection
        private bool _isLowEndDevice = false;
        // Backup original resolution to support toggling low-end mode
        private Vector2Int _baseTextureResolution;

        private void Awake()
        {
            _baseTextureResolution = textureResolution;
            CheckLowEndDevice();
            InitializePlayers();
            AdjustDelayForPlatform();
        }

        private void CheckLowEndDevice()
        {
            // Simple heuristic: System Memory < 4GB or typical mobile GPU limits could be used.
            // Here we assume < 4GB RAM implies a lower-end device.
            if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 4096)
            {
                _isLowEndDevice = true;
            }

            // Apply settings
            if (_isLowEndDevice && autoDownscaleOnLowEnd)
            {
                textureResolution = _baseTextureResolution / 2;
                CLogger.LogInfo($"{DEBUG_FLAG} Low-end device detected & AutoDownscale enabled. Resolution set to {textureResolution}");
            }
            else
            {
                // Ensure we restore or keep original if condition not met
                textureResolution = _baseTextureResolution;
            }
        }

        private void InitializePlayers()
        {
            _videoPlayers = GetComponents<VideoPlayer>();

            // Ensure we have exactly 2 players
            if (_videoPlayers.Length < 2)
            {
                int missing = 2 - _videoPlayers.Length;
                var currentList = new System.Collections.Generic.List<VideoPlayer>(_videoPlayers);
                for (int i = 0; i < missing; i++)
                {
                    currentList.Add(gameObject.AddComponent<VideoPlayer>());
                }
                _videoPlayers = currentList.ToArray();
            }

            _currentVideoPlayer = _videoPlayers[0];
            _standbyVideoPlayer = _videoPlayers[1];

            // Create textures
            CreateAndAssignTargetTexture(0, ref _renderTextures[0], _currentVideoPlayer);
            CreateAndAssignTargetTexture(1, ref _renderTextures[1], _standbyVideoPlayer);

            _currentVideoTexture = _renderTextures[0];
            _previousFrameTexture = _renderTextures[1];

            ConfigureVideoPlayer(_currentVideoPlayer, _currentVideoTexture);
            ConfigureVideoPlayer(_standbyVideoPlayer, _previousFrameTexture);
        }

        private void ConfigureVideoPlayer(VideoPlayer player, RenderTexture targetTexture)
        {
            if (player == null) return;

            player.timeUpdateMode = timeUpdateMode;
            player.skipOnDrop = skipOnDrop;
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = targetTexture;
            player.waitForFirstFrame = false; // Important for async handling
            player.isLooping = false;

            // Audio Configuration
            if (enableAudio)
            {
                player.audioOutputMode = VideoAudioOutputMode.Direct;
                player.SetDirectAudioVolume(0, defaultVolume);
                // Note: If you need AudioSource routing, change to VideoAudioOutputMode.AudioSource 
                // and assign player.SetTargetAudioSource(0, yourSource);
            }
            else
            {
                player.audioOutputMode = VideoAudioOutputMode.None;
            }

            player.Stop();

            // Events
            player.loopPointReached -= OnVideoLoopPointReachedHandler;
            player.loopPointReached += OnVideoLoopPointReachedHandler;
        }

        private void CreateAndAssignTargetTexture(int index, ref RenderTexture textureField, VideoPlayer targetPlayer)
        {
            ReleaseRenderTexture(ref textureField, targetPlayer);

            textureField = new RenderTexture(textureResolution.x, textureResolution.y, depthBuffer, textureFormat)
            {
                filterMode = filterMode,
                name = $"GameplayVideoRT_{index}",
                autoGenerateMips = false, // Disable mips for video to save memory/perf
                useMipMap = false
            };

            if (!textureField.Create())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create RenderTexture {index}.");
                return;
            }

            if (targetPlayer != null)
            {
                targetPlayer.targetTexture = textureField;
            }
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            InitializeVideoPlayerAsync(videoUrl, bLoop).Forget(e =>
            {
                if (e is not OperationCanceledException)
                {
                    CLogger.LogError($"{DEBUG_FLAG} InitializeVideoPlayer failed: {e}");
                }
            });
        }

        // Unified Async Entry Point
        public async UniTask InitializeVideoPlayerAsync(string videoUrl, bool bLoop = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(videoUrl))
            {
                CLogger.LogError($"{DEBUG_FLAG} Video URL is empty.");
                return;
            }

            // Check if already playing/prepared on current
            if (_currentVideoPlayer.url == videoUrl && _currentVideoPlayer.isPrepared &&
                !(_currentVideoUrlBeingPreparedOnStandby == videoUrl && IsStandbyActivelyPreparing()))
            {
                _currentUserOnPreparedCallback?.Invoke();
                return;
            }

            // Check if already preparing on standby
            if (IsStandbyActivelyPreparing() && _currentVideoUrlBeingPreparedOnStandby == videoUrl)
            {
                // If calling Async, we usually await the existing task, but here we just join the state.
                // For simplicity, we let the new call take over or piggyback. 
                // But to ensure strict contract, we'll cancel old and start new for full control.
            }

            CancelCurrentMasterPreparation(true, "New Request");

            // Setup new context
            _masterPrepareCts = new CancellationTokenSource();
            _currentVideoUrlBeingPreparedOnStandby = videoUrl;

            // Create linked token (Destroy + Global Cancel + Param Token)
            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                _masterPrepareCts.Token,
                this.GetCancellationTokenOnDestroy(),
                cancellationToken
            );

            var capturedToken = linkedSource.Token; // Capture token before disposal risk
            _activeAsyncOperationToken = capturedToken;

            try
            {
                await LaunchMasterPrepareAsync(_standbyVideoPlayer, videoUrl, bLoop, capturedToken);
                // Callback is invoked inside LaunchMasterPrepareAsync upon success
            }
            finally
            {
                linkedSource.Dispose();
                // Use local variable capturedToken instead of linkedSource.Token to avoid ObjectDisposedException
                if (_activeAsyncOperationToken == capturedToken)
                {
                    _activeAsyncOperationToken = CancellationToken.None;
                    _currentVideoUrlBeingPreparedOnStandby = null;
                }
            }
        }

        // Original Callback-style overload (mapped to Async)
        void IGameplayVideoPlayer.InitializeVideoPlayer(in string videoUrl, bool bLoop, Action OnPrepared)
        {
            // We store the callback to be invoked by the async process
            _currentUserOnPreparedCallback = OnPrepared;
            InitializeVideoPlayerAsync(videoUrl, bLoop).Forget();
        }

        private async UniTask LaunchMasterPrepareAsync(VideoPlayer player, string url, bool loop, CancellationToken token)
        {
            bool success = false;
            int attempt = 0;

            while (attempt <= maxPrepareRetries && !success)
            {
                token.ThrowIfCancellationRequested();
                if (attempt > 0) await UniTask.Delay(prepareRetryDelayMs, cancellationToken: token);

                var status = await TryPrepareAttemptAsync(player, url, loop, token);

                if (status == PrepareAttemptStatus.Success)
                {
                    PerformSwap(player);
                    success = true;
                    _currentUserOnPreparedCallback?.Invoke();
                }
                else if (status == PrepareAttemptStatus.Error || status == PrepareAttemptStatus.Cancelled)
                {
                    break;
                }
                // Timeout -> Retry
                attempt++;
            }

            if (!success && !token.IsCancellationRequested)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to prepare '{url}' after retries.");
            }
        }

        private enum PrepareAttemptStatus { Success, Timeout, Error, Cancelled }

        private async UniTask<PrepareAttemptStatus> TryPrepareAttemptAsync(VideoPlayer player, string url, bool loop, CancellationToken token)
        {
            var completionSource = new UniTaskCompletionSource<bool>();

            // Local event handlers to avoid leaks and allocations
            VideoPlayer.EventHandler onPrepare = (source) =>
            {
                if (source == player && source.url == url) completionSource.TrySetResult(true);
            };

            VideoPlayer.ErrorEventHandler onError = (source, msg) =>
            {
                if (source == player)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Video Error: {msg}");
                    completionSource.TrySetResult(false);
                }
            };

            try
            {
                // Platform specific delays
                if (preparePreDelayMs > 0) await UniTask.Delay(preparePreDelayMs, cancellationToken: token);

                player.Stop();
                player.url = null; // Reset URL to force fresh state

                // Ensure Texture is valid
                if (player.targetTexture == null || !player.targetTexture.IsCreated())
                {
                    // Should ideally recreate, but we'll assume InitializePlayers handled it.
                    // Log and fail safely.
                    CLogger.LogError($"{DEBUG_FLAG} TargetTexture missing/released. Cannot prepare.");
                    return PrepareAttemptStatus.Error;
                }

                if (internalStopToPrepareDelayMs > 0) await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: token);
                else await UniTask.Yield(PlayerLoopTiming.Update, token);

                player.source = VideoSource.Url;
                player.url = url;
                player.isLooping = loop;
                player.prepareCompleted += onPrepare;
                player.errorReceived += onError;

                player.Prepare();

                // Wait with timeout
                bool result = await completionSource.Task.Timeout(TimeSpan.FromMilliseconds(prepareTimeoutMs));
                return result ? PrepareAttemptStatus.Success : PrepareAttemptStatus.Error;
            }
            catch (TimeoutException)
            {
                // CLogger.LogWarning($"{DEBUG_FLAG} Prepare timeout for {url}");
                player.Stop();
                return PrepareAttemptStatus.Timeout;
            }
            catch (OperationCanceledException)
            {
                player.Stop();
                return PrepareAttemptStatus.Cancelled;
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Prepare exception: {ex}");
                player.Stop();
                return PrepareAttemptStatus.Error;
            }
            finally
            {
                player.prepareCompleted -= onPrepare;
                player.errorReceived -= onError;
            }
        }

        private void PerformSwap(VideoPlayer newPlayer)
        {
            if (_currentVideoPlayer.isPlaying)
            {
                _currentVideoPlayer.Pause();
            }

            // Swap references
            var oldPlayer = _currentVideoPlayer;
            _currentVideoPlayer = newPlayer;
            _standbyVideoPlayer = oldPlayer;

            if (_currentVideoPlayer.targetTexture == _renderTextures[0])
            {
                _currentVideoTexture = _renderTextures[0];
                _previousFrameTexture = _renderTextures[1];
            }
            else
            {
                _currentVideoTexture = _renderTextures[1];
                _previousFrameTexture = _renderTextures[0];
            }
        }

        public void Play()
        {
            if (_currentVideoPlayer != null && _currentVideoPlayer.isPrepared)
                _currentVideoPlayer.Play();
        }

        public void Stop()
        {
            CancelCurrentMasterPreparation(true, "Stop");
            _seekCts?.Cancel();
            _currentVideoPlayer?.Stop();
            _standbyVideoPlayer?.Stop();
        }

        public void Pause()
        {
            if (_currentVideoPlayer != null && _currentVideoPlayer.isPlaying)
                _currentVideoPlayer.Pause();
        }

        public void Resume()
        {
            if (_currentVideoPlayer != null && _currentVideoPlayer.isPrepared && !_currentVideoPlayer.isPlaying)
                _currentVideoPlayer.Play();
        }

        public void SetVolume(float volume)
        {
            defaultVolume = Mathf.Clamp01(volume);
            if (enableAudio && _currentVideoPlayer != null)
            {
                _currentVideoPlayer.SetDirectAudioVolume(0, defaultVolume);
            }
        }

        public long GetPlaybackTimeMSec()
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared) return 0;
            return (long)(_currentVideoPlayer.time * 1000.0);
        }

        public void SeekTime(long milliSeconds)
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared || !_currentVideoPlayer.canSetTime) return;

            // Check standby conflict
            if (cancelStandbyPrepareOnSeekSameUrl && IsStandbyActivelyPreparing() && _currentVideoUrlBeingPreparedOnStandby == _currentVideoPlayer.url)
            {
                CancelCurrentMasterPreparation(true, "Seek Conflict");
            }

            _seekCts?.Cancel();
            _seekCts?.Dispose();
            _seekCts = new CancellationTokenSource();
            var token = CancellationTokenSource.CreateLinkedTokenSource(_seekCts.Token, this.GetCancellationTokenOnDestroy()).Token;

            SeekInternalAsync(milliSeconds, token).Forget();
        }

        private async UniTask SeekInternalAsync(long ms, CancellationToken token)
        {
            try
            {
                double time = ms / 1000.0;
                _currentVideoPlayer.time = time;
                // Wait for seek completion implicitly or via event if strict sync needed.
                // For high perf/responsiveness, setting .time is usually enough, but on some platforms explicit Seek is better.
                // Unity VideoPlayer handles .time set as a Seek.
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Seek error: {ex}");
            }
            await UniTask.Yield(); // suppress warning
        }

        private void CancelCurrentMasterPreparation(bool stopStandby, string reason)
        {
            _masterPrepareCts?.Cancel();
            _masterPrepareCts?.Dispose();
            _masterPrepareCts = null;

            if (stopStandby && _standbyVideoPlayer != null)
            {
                _standbyVideoPlayer.Stop();
            }
        }

        private bool IsStandbyActivelyPreparing()
        {
            return _activeAsyncOperationToken != CancellationToken.None && !_activeAsyncOperationToken.IsCancellationRequested;
        }

        private void AdjustDelayForPlatform()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            preparePreDelayMs = 0;
            internalStopToPrepareDelayMs = 0;
            preparePostDelayMs = 0;
#endif
#if UNITY_WEBGL
            // WebGL specific adjustments
            prepareTimeoutMs = 15000; // Network loading can be slower
#endif
        }

        private void ReleaseRenderTexture(ref RenderTexture rt, VideoPlayer player)
        {
            if (player != null && player.targetTexture == rt) player.targetTexture = null;
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
                rt = null;
            }
        }

        private void OnVideoLoopPointReachedHandler(VideoPlayer source)
        {
            // Handle looping logic if needed, or events
        }

        private void OnDestroy()
        {
            CancelCurrentMasterPreparation(false, "Destroy");
            _seekCts?.Cancel();
            _seekCts?.Dispose();

            ReleaseRenderTexture(ref _renderTextures[0], _videoPlayers != null && _videoPlayers.Length > 0 ? _videoPlayers[0] : null);
            ReleaseRenderTexture(ref _renderTextures[1], _videoPlayers != null && _videoPlayers.Length > 1 ? _videoPlayers[1] : null);
        }

#if UNITY_EDITOR
        public void EditorRecreateAllManagedTextures()
        {
            if (Application.isPlaying)
            {
                InitializePlayers();
            }
        }

        /// <summary>
        /// Gets whether the current primary video player is actively playing.
        /// </summary>
        public bool IsCurrentVideoPlaying => _currentVideoPlayer != null && _currentVideoPlayer.isPrepared && _currentVideoPlayer.isPlaying;
#endif
    }
}