using System;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

namespace RhythmPulse.Media
{
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class UnityVideoProvider : MonoBehaviour, IUnityVideoPlayer
    {
        [Header("Video Settings")]
        [SerializeField] private Vector2Int textureResolution = new Vector2Int(1920, 1080);
        [SerializeField] private RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        [SerializeField] private int depthBuffer = 0;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
        [SerializeField] private VideoTimeUpdateMode timeUpdateMode = VideoTimeUpdateMode.DSPTime;
        [SerializeField] private bool skipOnDrop = true;
        [SerializeField] private bool autoDownscaleOnLowEnd = true;

        [Header("Audio Settings")]
        [SerializeField] private bool enableAudio = false;
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 1.0f;

        [Header("Seek Settings")]
        [SerializeField] private bool cancelStandbyPrepareOnSeekSameUrl = true;
        [SerializeField] private int seekTimeoutMs = 2000;

        [Header("Prepare Settings")]
        [SerializeField] private int preparePreDelayMs = 100;
        [SerializeField] private int internalStopToPrepareDelayMs = 100;
        [SerializeField] private int preparePostDelayMs = 50;
        [SerializeField] private int prepareTimeoutMs = 8000;

        [Header("Retry Settings")]
        [SerializeField] private int maxPrepareRetries = 2;
        [SerializeField] private int prepareRetryDelayMs = 500;

        // Double-buffer video players
        private VideoPlayer[] _videoPlayers;
        private VideoPlayer _currentVideoPlayer;
        private VideoPlayer _standbyVideoPlayer;
        private RenderTexture[] _renderTextures;

        private RenderTexture _currentVideoTexture;
        private RenderTexture _previousFrameTexture;

        public RenderTexture CurrentVideoTexture => _currentVideoTexture;
        public RenderTexture PreviousFrameTexture => _previousFrameTexture;

        // Cancellation handling
        private CancellationTokenSource _masterPrepareCts;
        private CancellationTokenSource _seekCts;
        private Action _currentUserOnPreparedCallback;
        private string _currentVideoUrlBeingPreparedOnStandby;
        private CancellationToken _activeAsyncOperationToken;

        // Cached event handlers to avoid lambda allocation
        private VideoPlayer.EventHandler _onPrepareCompleted;
        private VideoPlayer.ErrorEventHandler _onErrorReceived;
        private UniTaskCompletionSource<bool> _prepareCompletionSource;
        private string _preparingUrl;

        // State
        private Vector2Int _baseTextureResolution;
        private long _cachedDurationMs;
        private bool _isLowEndDevice;

        private void Awake()
        {
            _baseTextureResolution = textureResolution;
            
            // Cache event handlers once (zero-GC)
            _onPrepareCompleted = OnPrepareCompletedHandler;
            _onErrorReceived = OnErrorReceivedHandler;
            
            CheckLowEndDevice();
            InitializePlayers();
            AdjustDelayForPlatform();
        }

        private void CheckLowEndDevice()
        {
            if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 4096)
            {
                _isLowEndDevice = true;
            }

            if (_isLowEndDevice && autoDownscaleOnLowEnd)
            {
                textureResolution = new Vector2Int(_baseTextureResolution.x / 2, _baseTextureResolution.y / 2);
            }
            else
            {
                textureResolution = _baseTextureResolution;
            }
        }

        private void InitializePlayers()
        {
            var existingPlayers = GetComponents<VideoPlayer>();
            int existingCount = existingPlayers.Length;

            // Pre-allocate fixed array (zero GC after init)
            _videoPlayers = new VideoPlayer[2];
            _renderTextures = new RenderTexture[2];

            // Use existing or add new players
            _videoPlayers[0] = existingCount > 0 ? existingPlayers[0] : gameObject.AddComponent<VideoPlayer>();
            _videoPlayers[1] = existingCount > 1 ? existingPlayers[1] : gameObject.AddComponent<VideoPlayer>();

            _currentVideoPlayer = _videoPlayers[0];
            _standbyVideoPlayer = _videoPlayers[1];

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
            player.waitForFirstFrame = false;
            player.isLooping = false;

            if (enableAudio)
            {
                player.audioOutputMode = VideoAudioOutputMode.Direct;
                player.SetDirectAudioVolume(0, defaultVolume);
            }
            else
            {
                player.audioOutputMode = VideoAudioOutputMode.None;
            }

            SafeStopPlayer(player);
            
            player.loopPointReached -= OnVideoLoopPointReachedHandler;
            player.loopPointReached += OnVideoLoopPointReachedHandler;
        }

        private void CreateAndAssignTargetTexture(int index, ref RenderTexture textureField, VideoPlayer targetPlayer)
        {
            ReleaseRenderTexture(ref textureField, targetPlayer);

            textureField = new RenderTexture(textureResolution.x, textureResolution.y, depthBuffer, textureFormat)
            {
                filterMode = filterMode,
                name = index == 0 ? "UnityVideoRT_0" : "UnityVideoRT_1",
                autoGenerateMips = false,
                useMipMap = false
            };

            if (!textureField.Create())
            {
                CLogger.LogError("[UnityVideoProvider] Failed to create RenderTexture");
                return;
            }

            if (targetPlayer != null) targetPlayer.targetTexture = textureField;
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            _currentUserOnPreparedCallback = OnPrepared;
            InitializeVideoPlayerAsync(videoUrl, bLoop).Forget(e =>
            {
                if (e is not OperationCanceledException)
                    CLogger.LogError("[UnityVideoProvider] InitializeVideoPlayer failed: " + e.Message);
            });
        }

        public async UniTask InitializeVideoPlayerAsync(string videoUrl, bool bLoop = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(videoUrl))
            {
                CLogger.LogError("[UnityVideoProvider] Video URL is empty");
                return;
            }

            // Already prepared on current player
            if (_currentVideoPlayer != null && 
                _currentVideoPlayer.url == videoUrl && 
                _currentVideoPlayer.isPrepared &&
                !(_currentVideoUrlBeingPreparedOnStandby == videoUrl && IsStandbyActivelyPreparing()))
            {
                _currentUserOnPreparedCallback?.Invoke();
                return;
            }

            CancelCurrentMasterPreparation(true);

            _masterPrepareCts = new CancellationTokenSource();
            _currentVideoUrlBeingPreparedOnStandby = videoUrl;

            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                _masterPrepareCts.Token,
                this.GetCancellationTokenOnDestroy(),
                cancellationToken
            );

            var capturedToken = linkedSource.Token;
            _activeAsyncOperationToken = capturedToken;

            try
            {
                if (_standbyVideoPlayer == null)
                {
                    CLogger.LogError("[UnityVideoProvider] Standby VideoPlayer is null");
                    return;
                }

                await LaunchMasterPrepareAsync(_standbyVideoPlayer, videoUrl, bLoop, capturedToken);
            }
            finally
            {
                linkedSource.Dispose();
                if (_activeAsyncOperationToken.Equals(capturedToken))
                {
                    _activeAsyncOperationToken = default;
                    _currentVideoUrlBeingPreparedOnStandby = null;
                }
            }
        }

        private async UniTask LaunchMasterPrepareAsync(VideoPlayer player, string url, bool loop, CancellationToken token)
        {
            if (player == null) return;

            bool success = false;
            int attempt = 0;

            while (attempt <= maxPrepareRetries && !success)
            {
                token.ThrowIfCancellationRequested();
                if (player == null) break;

                if (attempt > 0)
                    await UniTask.Delay(prepareRetryDelayMs, cancellationToken: token);

                var status = await TryPrepareAttemptAsync(player, url, loop, token);

                if (status == PrepareAttemptStatus.Success)
                {
                    if (player != null)
                    {
                        PerformSwap(player);
                        success = true;
                        _currentUserOnPreparedCallback?.Invoke();
                    }
                    break;
                }
                else if (status == PrepareAttemptStatus.Error || status == PrepareAttemptStatus.Cancelled)
                {
                    break;
                }
                attempt++;
            }

            if (!success && !token.IsCancellationRequested)
            {
                CLogger.LogError("[UnityVideoProvider] Failed to prepare video after retries");
            }
        }

        private enum PrepareAttemptStatus { Success, Timeout, Error, Cancelled }

        private async UniTask<PrepareAttemptStatus> TryPrepareAttemptAsync(VideoPlayer player, string url, bool loop, CancellationToken token)
        {
            _prepareCompletionSource = new UniTaskCompletionSource<bool>();
            _preparingUrl = url;

            try
            {
                if (player == null) return PrepareAttemptStatus.Cancelled;

                if (preparePreDelayMs > 0)
                    await UniTask.Delay(preparePreDelayMs, cancellationToken: token);

                if (player == null) return PrepareAttemptStatus.Cancelled;

                SafeStopPlayer(player);
                player.url = null;

                if (player.targetTexture == null || !player.targetTexture.IsCreated())
                {
                    CLogger.LogError("[UnityVideoProvider] TargetTexture missing");
                    return PrepareAttemptStatus.Error;
                }

                if (internalStopToPrepareDelayMs > 0)
                    await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: token);
                else
                    await UniTask.Yield(PlayerLoopTiming.Update, token);

                if (player == null) return PrepareAttemptStatus.Cancelled;

                player.source = VideoSource.Url;
                player.url = url;
                player.isLooping = loop;
                
                // Use cached handlers (zero allocation)
                player.prepareCompleted += _onPrepareCompleted;
                player.errorReceived += _onErrorReceived;

                player.Prepare();

                bool result = await _prepareCompletionSource.Task.Timeout(TimeSpan.FromMilliseconds(prepareTimeoutMs));
                return result ? PrepareAttemptStatus.Success : PrepareAttemptStatus.Error;
            }
            catch (TimeoutException)
            {
                SafeStopPlayer(player);
                return PrepareAttemptStatus.Timeout;
            }
            catch (OperationCanceledException)
            {
                SafeStopPlayer(player);
                return PrepareAttemptStatus.Cancelled;
            }
            catch (Exception ex)
            {
                CLogger.LogError("[UnityVideoProvider] Prepare exception: " + ex.Message);
                SafeStopPlayer(player);
                return PrepareAttemptStatus.Error;
            }
            finally
            {
                if (player != null)
                {
                    player.prepareCompleted -= _onPrepareCompleted;
                    player.errorReceived -= _onErrorReceived;
                }
                _preparingUrl = null;
            }
        }

        // Cached event handlers (zero allocation)
        private void OnPrepareCompletedHandler(VideoPlayer source)
        {
            if (source != null && source.url == _preparingUrl)
                _prepareCompletionSource?.TrySetResult(true);
        }

        private void OnErrorReceivedHandler(VideoPlayer source, string msg)
        {
            CLogger.LogError("[UnityVideoProvider] Video Error: " + msg);
            _prepareCompletionSource?.TrySetResult(false);
        }

        private void PerformSwap(VideoPlayer newPlayer)
        {
            if (_currentVideoPlayer != null && _currentVideoPlayer.isPlaying)
                SafePausePlayer(_currentVideoPlayer);

            var oldPlayer = _currentVideoPlayer;
            _currentVideoPlayer = newPlayer;
            _standbyVideoPlayer = oldPlayer;

            if (_currentVideoPlayer != null && _currentVideoPlayer.isPrepared)
                _cachedDurationMs = (long)(_currentVideoPlayer.length * 1000);
            else
                _cachedDurationMs = 0;

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
            CancelCurrentMasterPreparation(true);
            CancelSeek();
            SafeStopPlayer(_currentVideoPlayer);
            SafeStopPlayer(_standbyVideoPlayer);
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
                _currentVideoPlayer.SetDirectAudioVolume(0, defaultVolume);
        }

        public long GetPlaybackTimeMSec()
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared) return 0;
            return (long)(_currentVideoPlayer.time * 1000.0);
        }

        public long GetMediaDurationMSec()
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared) return 0;
            return _cachedDurationMs;
        }

        public void SeekTime(long milliSeconds)
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared || !_currentVideoPlayer.canSetTime)
                return;

            if (cancelStandbyPrepareOnSeekSameUrl && 
                IsStandbyActivelyPreparing() && 
                _currentVideoUrlBeingPreparedOnStandby == _currentVideoPlayer.url)
            {
                CancelCurrentMasterPreparation(true);
            }

            CancelSeek();
            _seekCts = new CancellationTokenSource();
            
            var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                _seekCts.Token, 
                this.GetCancellationTokenOnDestroy()
            );

            SeekInternalAsync(milliSeconds, linkedSource).Forget();
        }

        private async UniTaskVoid SeekInternalAsync(long ms, CancellationTokenSource linkedSource)
        {
            try
            {
                _currentVideoPlayer.time = ms / 1000.0;
                await UniTask.Yield();
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    CLogger.LogError("[UnityVideoProvider] Seek error: " + ex.Message);
            }
            finally
            {
                linkedSource.Dispose();
            }
        }

        private void CancelCurrentMasterPreparation(bool stopStandby)
        {
            _masterPrepareCts?.Cancel();
            _masterPrepareCts?.Dispose();
            _masterPrepareCts = null;

            if (stopStandby)
                SafeStopPlayer(_standbyVideoPlayer);
        }

        private void CancelSeek()
        {
            _seekCts?.Cancel();
            _seekCts?.Dispose();
            _seekCts = null;
        }

        private bool IsStandbyActivelyPreparing()
        {
            return _activeAsyncOperationToken != default && !_activeAsyncOperationToken.IsCancellationRequested;
        }

        private void AdjustDelayForPlatform()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            preparePreDelayMs = 0;
            internalStopToPrepareDelayMs = 0;
            preparePostDelayMs = 0;
#endif
#if UNITY_WEBGL
            prepareTimeoutMs = 15000;
#endif
        }

        private void SafeStopPlayer(VideoPlayer player)
        {
            if (player == null) return;
            try { player.Stop(); } catch { }
        }

        private void SafePausePlayer(VideoPlayer player)
        {
            if (player == null) return;
            try { player.Pause(); } catch { }
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

        private void OnVideoLoopPointReachedHandler(VideoPlayer source) { }

        private void OnDestroy()
        {
            CancelCurrentMasterPreparation(false);
            CancelSeek();

            if (_videoPlayers != null)
            {
                ReleaseRenderTexture(ref _renderTextures[0], _videoPlayers.Length > 0 ? _videoPlayers[0] : null);
                ReleaseRenderTexture(ref _renderTextures[1], _videoPlayers.Length > 1 ? _videoPlayers[1] : null);
            }
        }

#if UNITY_EDITOR
        public void EditorRecreateAllManagedTextures()
        {
            if (Application.isPlaying) InitializePlayers();
        }

        public bool IsCurrentVideoPlaying => _currentVideoPlayer != null && _currentVideoPlayer.isPrepared && _currentVideoPlayer.isPlaying;
#endif
    }
}