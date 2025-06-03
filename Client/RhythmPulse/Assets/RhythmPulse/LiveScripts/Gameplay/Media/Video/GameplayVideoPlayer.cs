using System;
using CycloneGames.Logger; // Assuming this is your logging library
using UnityEngine;
using UnityEngine.Video;
using Cysharp.Threading.Tasks;
using System.Threading;

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
        void CreateTargetTexture();
    }

    [RequireComponent(typeof(VideoPlayer))]
    public class GameplayVideoPlayer : MonoBehaviour, IGameplayVideoPlayer
    {
        private const string DEBUG_FLAG = "[GameplayVideoPlayer]";

        [Header("Video Settings")]
        public Vector2Int textureResolution = new Vector2Int(1920, 1080);
        public RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        public int depthBuffer = 0;
        public FilterMode filterMode = FilterMode.Bilinear;

        [Header("Prepare Settings")]
        public int preparePreDelayMs = 200;
        public int internalStopToPrepareDelayMs = 90;
        public int preparePostDelayMs = 30;
        public int prepareTimeoutMs = 3000;

        [Header("Retry Settings")]
        public int maxPrepareRetries = 2;
        public int prepareRetryDelayMs = 1000;

        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        public RenderTexture VideoTexture => videoTexture;

        private CancellationTokenSource _masterPrepareCts = null;
        private Action _currentUserOnPreparedCallback;
        private string _currentVideoUrlBeingPrepared = null;
        private CancellationToken _activeAsyncOperationToken = CancellationToken.None;
        private CancellationTokenSource _seekCts = null;

        private enum PrepareAttemptStatus { Success, Timeout, Error, Cancelled }

        private void Awake()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer component not found. Disabling component.");
                enabled = false;
                return;
            }
            CreateTargetTexture();
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = videoTexture;
            videoPlayer.loopPointReached += OnVideoLoopPointReachedHandler;
            videoPlayer.waitForFirstFrame = false;
        }

        private void OnDestroy()
        {
            CancelCurrentMasterOperation(false, "OnDestroy");
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= OnVideoLoopPointReachedHandler;
            }
            ReleaseRenderTexture();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (videoPlayer == null) videoPlayer = GetComponent<VideoPlayer>();
            if (Application.isPlaying && videoPlayer != null && videoTexture != null &&
                (videoTexture.width != textureResolution.x ||
                 videoTexture.height != textureResolution.y ||
                 videoTexture.format != textureFormat ||
                 videoTexture.depth != depthBuffer ||
                 videoTexture.filterMode != filterMode))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Video texture parameters changed in OnValidate. Recreating texture.");
                CreateTargetTexture();
            }
        }
#endif

        private void ReleaseRenderTexture()
        {
            if (videoTexture != null)
            {
                if (videoPlayer != null && videoPlayer.targetTexture == videoTexture)
                {
                    videoPlayer.targetTexture = null;
                }
                if (videoTexture.IsCreated())
                {
                    videoTexture.Release();
                }
                Destroy(videoTexture);
                videoTexture = null;
            }
        }

        public void CreateTargetTexture()
        {
            ReleaseRenderTexture();

            videoTexture = new RenderTexture(textureResolution.x, textureResolution.y, depthBuffer, textureFormat)
            {
                filterMode = filterMode,
                name = "GameplayVideoRenderTexture"
            };

            if (!videoTexture.Create())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create RenderTexture.");
                Destroy(videoTexture);
                videoTexture = null;
                if (videoPlayer != null && (videoPlayer.targetTexture == videoTexture || (videoPlayer.targetTexture != null && !videoPlayer.targetTexture.IsCreated())))
                {
                    videoPlayer.targetTexture = null;
                }
                return;
            }

            if (videoPlayer != null)
            {
                videoPlayer.targetTexture = videoTexture;
            }
            else if (Application.isPlaying)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} VideoPlayer not available for targetTexture assignment during CreateTargetTexture.");
            }
        }

        private void CancelCurrentMasterOperation(bool stopPlayer = true, string reason = "New operation")
        {
            if (_masterPrepareCts != null)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Cancelling master op for '{_currentVideoUrlBeingPrepared ?? "Unknown"}' (Reason: {reason}). Token: {_activeAsyncOperationToken.GetHashCode()}");
                _masterPrepareCts.Cancel();
                _masterPrepareCts.Dispose();
                _masterPrepareCts = null;
            }

            _currentVideoUrlBeingPrepared = null;
            _currentUserOnPreparedCallback = null;
            _activeAsyncOperationToken = CancellationToken.None;

            if (videoPlayer != null && stopPlayer)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} CancelCurrentMasterOperation: Stopping VideoPlayer.");
                videoPlayer.Stop(); // VideoPlayer.Stop() will cancel preparing, stop playback, and set isPrepared to false.
            }
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            if (videoPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer component is null. Cannot initialize.");
                return;
            }
            if (string.IsNullOrEmpty(videoUrl))
            {
                CLogger.LogError($"{DEBUG_FLAG} Video URL is null or empty. Cannot initialize.");
                return;
            }

            CancelCurrentMasterOperation(true, $"New InitializeVideoPlayer call for '{videoUrl}'");

            _currentUserOnPreparedCallback = OnPrepared;
            _currentVideoUrlBeingPrepared = videoUrl;

            _masterPrepareCts = new CancellationTokenSource();
            var linkedCtsForAsyncTask = CancellationTokenSource.CreateLinkedTokenSource(
                _masterPrepareCts.Token,
                this.GetCancellationTokenOnDestroy()
            );
            _activeAsyncOperationToken = linkedCtsForAsyncTask.Token;

            // CLogger.LogInfo($"{DEBUG_FLAG} Launching Master Prepare for '{videoUrl}'. New active token: {_activeAsyncOperationToken.GetHashCode()}");
            LaunchMasterPrepareAsync(videoUrl, bLoop, _currentUserOnPreparedCallback, _activeAsyncOperationToken, linkedCtsForAsyncTask);
        }

        private async void LaunchMasterPrepareAsync(string videoUrl, bool bLoop, Action userOnPreparedCallback, CancellationToken masterOperationToken, CancellationTokenSource linkedCtsToDispose)
        {
            bool overallSuccess = false;
            int attempt = 0;
            PrepareAttemptStatus attemptStatus = PrepareAttemptStatus.Error;

            try
            {
                while (attempt <= maxPrepareRetries && !overallSuccess)
                {
                    masterOperationToken.ThrowIfCancellationRequested();

                    if (attempt > 0)
                    {
                        CLogger.LogInfo($"{DEBUG_FLAG} Retrying prep for '{videoUrl}', attempt {attempt + 1}/{maxPrepareRetries + 1}. Delay: {prepareRetryDelayMs}ms.");
                        await UniTask.Delay(prepareRetryDelayMs, cancellationToken: masterOperationToken);
                        masterOperationToken.ThrowIfCancellationRequested();
                    }

                    attemptStatus = await TryPrepareAttemptAsync(videoUrl, bLoop, masterOperationToken);

                    if (attemptStatus == PrepareAttemptStatus.Success)
                    {
                        // CLogger.LogInfo($"{DEBUG_FLAG} Prep Succeeded: '{videoUrl}', attempt {attempt + 1}. Invoking callback. Token: {masterOperationToken.GetHashCode()}");
                        userOnPreparedCallback?.Invoke();
                        overallSuccess = true;
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Timeout)
                    {
                        if (attempt < maxPrepareRetries)
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Prep attempt {attempt + 1} for '{videoUrl}' timed out. Will retry.");
                            if (videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared && _activeAsyncOperationToken == masterOperationToken)
                            {
                                // CLogger.LogInfo($"{DEBUG_FLAG} Retry logic: Stopping player for '{videoUrl}' as it's not prepared after timeout.");
                                videoPlayer.Stop();
                                await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: masterOperationToken);
                            }
                        }
                        else
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Prep for '{videoUrl}' timed out after {maxPrepareRetries + 1} attempts. Giving up.");
                        }
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Error)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Prep attempt {attempt + 1} for '{videoUrl}' failed with error. No more retries.");
                        break;
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Cancelled)
                    {
                        masterOperationToken.ThrowIfCancellationRequested();
                    }
                    attempt++;
                }

                if (!overallSuccess && !masterOperationToken.IsCancellationRequested)
                {
                    CLogger.LogError($"{DEBUG_FLAG} All prep attempts for '{videoUrl}' failed. Last status: {attemptStatus}. Token: {masterOperationToken.GetHashCode()}");
                }

                if (preparePostDelayMs > 0 && !masterOperationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(preparePostDelayMs, cancellationToken: masterOperationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Master prep task for '{videoUrl}' (Token: {masterOperationToken.GetHashCode()}) cancelled in LaunchMasterPrepareAsync.");
                if (videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared && _activeAsyncOperationToken == masterOperationToken)
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} OCE in LaunchMaster: Stopping player for '{videoUrl}' as it's not prepared.");
                    videoPlayer.Stop();
                }
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Unhandled ex in master prep task for '{videoUrl}': {ex}. Token: {masterOperationToken.GetHashCode()}");
                if (videoPlayer != null && videoPlayer.url == videoUrl && _activeAsyncOperationToken == masterOperationToken) videoPlayer.Stop();
            }
            finally
            {
                linkedCtsToDispose?.Dispose();

                if (_activeAsyncOperationToken == masterOperationToken)
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} Master op for '{videoUrl}' (Token: {masterOperationToken.GetHashCode()}) concluded. Cleaning up master CTS & active token.");
                    if (_masterPrepareCts != null && !_masterPrepareCts.IsCancellationRequested)
                    {
                        _masterPrepareCts.Cancel();
                        _masterPrepareCts.Dispose();
                        _masterPrepareCts = null;
                    }
                    _currentVideoUrlBeingPrepared = null;
                    _activeAsyncOperationToken = CancellationToken.None;
                }
            }
        }

        private async UniTask<PrepareAttemptStatus> TryPrepareAttemptAsync(string videoUrl, bool bLoop, CancellationToken attemptToken)
        {
            if (videoPlayer == null) return PrepareAttemptStatus.Error;
            attemptToken.ThrowIfCancellationRequested();

            VideoPlayer.EventHandler localOnCompleteHandler = null;
            VideoPlayer.ErrorEventHandler localOnErrorHandler = null;
            CancellationTokenRegistration tokenRegistration = default;
            var eventSignal = new UniTaskCompletionSource<bool>();

            if (attemptToken.CanBeCanceled)
            {
                tokenRegistration = attemptToken.Register(() => eventSignal.TrySetCanceled(attemptToken));
            }

            try
            {
                localOnCompleteHandler = (source) =>
                {
                    if (source == videoPlayer && source.url == videoUrl && !attemptToken.IsCancellationRequested)
                    {
                        eventSignal.TrySetResult(true);
                    }
                };
                localOnErrorHandler = (source, message) =>
                {
                    if (source == videoPlayer && source.url == videoUrl && !attemptToken.IsCancellationRequested)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} RAW errorReceived for '{videoUrl}': {message}. Token: {attemptToken.GetHashCode()}");
                        eventSignal.TrySetResult(false);
                    }
                };

                if (preparePreDelayMs > 0) await UniTask.Delay(preparePreDelayMs, cancellationToken: attemptToken);
                attemptToken.ThrowIfCancellationRequested();

                videoPlayer.Stop();
                videoPlayer.url = null;

                if (internalStopToPrepareDelayMs > 0)
                {
                    await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: attemptToken);
                }
                else
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, attemptToken);
                }
                attemptToken.ThrowIfCancellationRequested();

                if (videoTexture == null || !videoTexture.IsCreated())
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Target RT invalid for '{videoUrl}'. Recreating.");
                    CreateTargetTexture();
                    if (videoTexture == null || !videoTexture.IsCreated())
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Failed to create valid RT for '{videoUrl}'. Aborting attempt.");
                        return PrepareAttemptStatus.Error;
                    }
                }
                // Ensure targetTexture is assigned if it was just (re)created.
                // CreateTargetTexture itself handles assignment if videoPlayer is not null.
                // If videoPlayer.targetTexture is somehow not our videoTexture, re-assign.
                if (videoPlayer.targetTexture != videoTexture) videoPlayer.targetTexture = videoTexture;


                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = videoUrl;
                videoPlayer.isLooping = bLoop;

                videoPlayer.prepareCompleted += localOnCompleteHandler;
                videoPlayer.errorReceived += localOnErrorHandler;

                videoPlayer.Prepare();

                bool attemptSucceededEvent = false;
                try
                {
                    attemptSucceededEvent = await eventSignal.Task.Timeout(TimeSpan.FromMilliseconds(prepareTimeoutMs));
                }
                catch (TimeoutException)
                {
                    if (!attemptToken.IsCancellationRequested && videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared)
                    {
                        // CLogger.LogInfo($"{DEBUG_FLAG} TryPrepare Timeout: Stopping player for '{videoUrl}' (not prepared).");
                        videoPlayer.Stop();
                    }
                    return PrepareAttemptStatus.Timeout;
                }
                catch (OperationCanceledException)
                {
                    if (videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared)
                    {
                        // CLogger.LogInfo($"{DEBUG_FLAG} TryPrepare OCE (eventSignal): Stopping player for '{videoUrl}' (not prepared).");
                        videoPlayer.Stop();
                    }
                    throw;
                }

                attemptToken.ThrowIfCancellationRequested();

                if (attemptSucceededEvent)
                {
                    if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.url == videoUrl)
                    {
                        return PrepareAttemptStatus.Success;
                    }
                    else
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Prep attempt for '{videoUrl}' (Token: {attemptToken.GetHashCode()}) got complete_event, but player state invalid. isPrepared: {videoPlayer?.isPrepared}, url: '{videoPlayer?.url}'.");
                        return PrepareAttemptStatus.Error;
                    }
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Prep attempt Failed (error event) for '{videoUrl}'. Token: {attemptToken.GetHashCode()}");
                    return PrepareAttemptStatus.Error;
                }
            }
            catch (OperationCanceledException)
            {
                if (videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared)
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} TryPrepare OCE (main): Stopping player for '{videoUrl}' (not prepared).");
                    videoPlayer.Stop();
                }
                return PrepareAttemptStatus.Cancelled;
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Ex in TryPrepareAttemptAsync for '{videoUrl}' (Token: {attemptToken.GetHashCode()}): {ex}");
                if (videoPlayer != null && videoPlayer.url == videoUrl) videoPlayer.Stop();
                return PrepareAttemptStatus.Error;
            }
            finally
            {
                if (videoPlayer != null)
                {
                    videoPlayer.prepareCompleted -= localOnCompleteHandler;
                    videoPlayer.errorReceived -= localOnErrorHandler;
                }
                if (attemptToken.CanBeCanceled) tokenRegistration.Dispose();
            }
        }

        private void OnVideoLoopPointReachedHandler(VideoPlayer source)
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Video loop point reached: {source.url}");
        }

        private bool IsActivelyPreparing()
        {
            return _activeAsyncOperationToken != CancellationToken.None && !_activeAsyncOperationToken.IsCancellationRequested;
        }

        public void Play()
        {
            if (videoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} VP null. Cannot Play."); return; }

            if (videoPlayer.isPrepared)
            {
                videoPlayer.Play();
            }
            else if (IsActivelyPreparing())
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but video ('{_currentVideoUrlBeingPrepared}') is preparing AND NOT READY. Ignored.");
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but VP not prepared. URL: '{videoPlayer.url}'. State: stopped/error.");
            }
        }

        public void Stop()
        {
            CancelCurrentMasterOperation(true, "Explicit Stop() call");

            if (videoPlayer != null && (videoPlayer.isPlaying || videoPlayer.isPaused) && !IsActivelyPreparing())
            {
                videoPlayer.Stop();
            }
        }

        public void Pause()
        {
            if (videoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} VP null. Cannot Pause."); return; }

            if (IsActivelyPreparing())
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Pause called, but video ('{_currentVideoUrlBeingPrepared}') is preparing. Ignored.");
                return;
            }
            if (videoPlayer.isPrepared && videoPlayer.isPlaying)
            {
                videoPlayer.Pause();
            }
            else if (!videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Pause called, but video not prepared.");
            }
        }

        public void Resume()
        {
            if (videoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} VP null. Cannot Resume."); return; }

            if (IsActivelyPreparing())
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but video ('{_currentVideoUrlBeingPrepared}') is preparing. Ignored.");
                return;
            }
            if (videoPlayer.isPrepared && videoPlayer.isPaused)
            {
                videoPlayer.Play();
            }
            else if (!videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but video not prepared.");
            }
            else if (!videoPlayer.isPaused && videoPlayer.isPrepared)
            {
                videoPlayer.Play(); // If prepared but not paused (e.g. stopped), Play will start it.
            }
        }

        public long GetPlaybackTimeMSec()
        {
            if (videoPlayer == null) return 0;
            if (IsActivelyPreparing())
            {
                return 0;
            }
            return (videoPlayer.isPrepared) ? (long)(videoPlayer.clockTime * 1000.0) : 0;
        }

        public void SeekTime(long milliSeconds)
        {
            if (videoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} VP null. Cannot SeekTime."); return; }

            if (IsActivelyPreparing())
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but video ('{_currentVideoUrlBeingPrepared}') is preparing. Ignored.");
                return;
            }
            if (!videoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but video not prepared.");
                return;
            }
            if (!videoPlayer.canSetTime)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but VP cannot be seeked (canSetTime=false).");
                return;
            }

            double newTime = milliSeconds / 1000.0;
            newTime = Math.Max(0, newTime);
            if (videoPlayer.length > 0)
            {
                newTime = Math.Min(newTime, videoPlayer.length);
            }

            _seekCts?.Cancel();
            _seekCts?.Dispose();
            _seekCts = null;
            _seekCts = new CancellationTokenSource();
            InternalSeekTime(milliSeconds, _seekCts).Forget();
        }

        private async UniTask InternalSeekTime(long milliSeconds, CancellationTokenSource cancellationToken)
        {
            await UniTask.WaitUntil(() => videoPlayer != null && videoPlayer.canSetTime, PlayerLoopTiming.Update, cancellationToken.Token);
            if (cancellationToken != null && cancellationToken.IsCancellationRequested)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime cancelled.");
                return;
            }
            double newTime = milliSeconds / 1000.0;
            newTime = Math.Max(0, newTime);
            if (videoPlayer.length > 0)
            {
                newTime = Math.Min(newTime, videoPlayer.length);
            }
            videoPlayer.time = newTime;
        }
    }
}