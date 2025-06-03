using System;
using CycloneGames.Logger;
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
        public int preparePreDelayMs = 30;
        public int internalStopToPrepareDelayMs = 50;
        public int preparePostDelayMs = 30;
        public int prepareTimeoutMs = 3000;

        private VideoPlayer videoPlayer;
        private RenderTexture videoTexture;
        public RenderTexture VideoTexture => videoTexture;

        private CancellationTokenSource _masterPrepareCts = null;
        private Action _currentUserOnPreparedCallback;
        private string _currentVideoUrlBeingPrepared = null;
        private CancellationToken _tokenForActiveAsyncTask = CancellationToken.None;
        private CancellationToken _activeAsyncOperationToken;


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
        }

        private void OnDestroy()
        {
            CancelCurrentMasterOperation(false);
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= OnVideoLoopPointReachedHandler;
            }
            if (videoTexture != null)
            {
                if (videoTexture.IsCreated()) videoTexture.Release();
                Destroy(videoTexture);
                videoTexture = null;
            }
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
                // ... (existing OnValidate logic) ...
                CLogger.LogInfo($"{DEBUG_FLAG} Video texture parameters changed. Recreating texture.");
                CreateTargetTexture();
                if (videoPlayer.targetTexture != videoTexture && videoTexture != null && videoTexture.IsCreated())
                {
                    videoPlayer.targetTexture = videoTexture;
                }
            }
        }
#endif

        public void CreateTargetTexture()
        {
            // ... (existing CreateTargetTexture logic) ...
            if (videoTexture != null)
            {
                if (videoPlayer != null && videoPlayer.targetTexture == videoTexture) videoPlayer.targetTexture = null;
                if (videoTexture.IsCreated()) videoTexture.Release();
                Destroy(videoTexture); videoTexture = null;
            }
            videoTexture = new RenderTexture(textureResolution.x, textureResolution.y, depthBuffer, textureFormat)
            {
                filterMode = filterMode, name = "GameplayVideoRenderTexture"
            };
            if (!videoTexture.Create()) {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create RenderTexture.");
                Destroy(videoTexture); videoTexture = null;
                if (videoPlayer != null && videoPlayer.targetTexture != null && !videoPlayer.targetTexture.IsCreated()) videoPlayer.targetTexture = null;
                return;
            }
            if (videoPlayer != null) videoPlayer.targetTexture = videoTexture;
            else if (Application.isPlaying) CLogger.LogWarning($"{DEBUG_FLAG} VideoPlayer not available for targetTexture assignment.");
        }
        
        private void CancelCurrentMasterOperation(bool stopPlayer = true)
        {
            if (_masterPrepareCts != null)
            {
                //CLogger.LogInfo($"{DEBUG_FLAG} Cancelling master operation for URL: '{_currentVideoUrlBeingPrepared ?? "Unknown"}'");
                _masterPrepareCts.Cancel();
                _masterPrepareCts.Dispose(); 
                _masterPrepareCts = null;
            }
            // These are associated with the operation started by InitializeVideoPlayer
            _currentVideoUrlBeingPrepared = null;
            _currentUserOnPreparedCallback = null; 
            _tokenForActiveAsyncTask = CancellationToken.None; // Mark no task as active

            if (videoPlayer != null && stopPlayer)
            {
                videoPlayer.Stop(); // This sets isPrepared to false
            }
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            if (videoPlayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer component is null. Cannot initialize.");
                return;
            }

            CancelCurrentMasterOperation(true); 

            _currentUserOnPreparedCallback = OnPrepared; 
            _currentVideoUrlBeingPrepared = videoUrl; 

            _masterPrepareCts = new CancellationTokenSource();
            // This linkedCts is what the actual async method will use.
            // It gets cancelled if _masterPrepareCts is cancelled OR if the GameObject is destroyed.
            var linkedCtsForAsyncTask = CancellationTokenSource.CreateLinkedTokenSource(
                _masterPrepareCts.Token,
                this.GetCancellationTokenOnDestroy() 
            );
            _tokenForActiveAsyncTask = linkedCtsForAsyncTask.Token;

            LaunchPrepareAsync(videoUrl, bLoop, _currentUserOnPreparedCallback, _tokenForActiveAsyncTask, linkedCtsForAsyncTask);
        }

        private async void LaunchPrepareAsync(string videoUrl, bool bLoop, Action userOnPreparedCallback, CancellationToken asyncTaskToken, CancellationTokenSource linkedCtsToDispose)
        {
            try
            {
                await PrepareAsyncInternal(videoUrl, bLoop, userOnPreparedCallback, asyncTaskToken);
            }
            catch (OperationCanceledException)
            {
                 //CLogger.LogInfo($"{DEBUG_FLAG} Preparation task for '{videoUrl}' (Token Hash: {asyncTaskToken.GetHashCode()}) was cancelled in LaunchPrepareAsync.");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Unhandled exception in preparation task for '{videoUrl}': {ex}");
            }
            finally
            {
                // Dispose the CancellationTokenSource that was created specifically for this async launch.
                // _masterPrepareCts is managed by CancelCurrentMasterOperation or when this task naturally concludes.
                linkedCtsToDispose?.Dispose();
            }
        }

        private async UniTask PrepareAsyncInternal(string videoUrl, bool bLoop, Action userOnPreparedCallback, CancellationToken token)
        {
            bool localIsSuccessful = false; // Tracks success of this specific prepare attempt
            bool localTimedOut = false;
            
            CancellationTokenRegistration tokenRegistration = default;
            var eventSignal = new UniTaskCompletionSource<bool>(); 

            if (token.CanBeCanceled)
            {
                tokenRegistration = token.Register(() => eventSignal.TrySetCanceled(token));
            }

            VideoPlayer.EventHandler localOnCompleteHandler = null;
            VideoPlayer.ErrorEventHandler localOnErrorHandler = null;

            localOnCompleteHandler = (source) => {
                //CLogger.LogWarning($"{DEBUG_FLAG} RAW prepareCompleted EVENT FIRED for source.url: {source?.url}, expected: {videoUrl}, token cancelled: {token.IsCancellationRequested}");
                if (source != null && source.url == videoUrl && !token.IsCancellationRequested) {
                    eventSignal.TrySetResult(true);
                }
            };
            localOnErrorHandler = (source, message) => {
                //CLogger.LogWarning($"{DEBUG_FLAG} RAW errorReceived EVENT FIRED for source.url: {source?.url}, message: {message}, expected: {videoUrl}, token cancelled: {token.IsCancellationRequested}");
                if (source != null && source.url == videoUrl && !token.IsCancellationRequested) {
                    CLogger.LogError($"{DEBUG_FLAG} Error event during prepare for '{videoUrl}': {message}");
                    eventSignal.TrySetResult(false); 
                }
            };

            try
            {
                if (preparePreDelayMs > 0) await UniTask.Delay(preparePreDelayMs, cancellationToken: token);
                token.ThrowIfCancellationRequested();

                videoPlayer.Stop(); // Ensure player is stopped and reset
                videoPlayer.url = string.Empty;
                if (internalStopToPrepareDelayMs > 0) {
                    await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: token);
                } else {
                    await UniTask.Yield(PlayerLoopTiming.Update, token); 
                }
                token.ThrowIfCancellationRequested();

                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = videoUrl; 
                videoPlayer.isLooping = bLoop;

                videoPlayer.prepareCompleted += localOnCompleteHandler;
                videoPlayer.errorReceived += localOnErrorHandler;

                //CLogger.LogInfo($"{DEBUG_FLAG} Starting Prepare for: {videoUrl} (Token Hash: {token.GetHashCode()})");
                videoPlayer.Prepare();

                try
                {
                    localIsSuccessful = await eventSignal.Task.Timeout(TimeSpan.FromMilliseconds(prepareTimeoutMs));
                }
                catch (TimeoutException)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Prepare timed out for '{videoUrl}' (Token Hash: {token.GetHashCode()}).");
                    localIsSuccessful = false;
                    localTimedOut = true; 
                    // If timeout occurs, and this operation hasn't been cancelled externally, stop the player.
                    if (!token.IsCancellationRequested && videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared) {
                        videoPlayer.Stop();
                    }
                }
                
                // CRITICAL: If the token for this operation was cancelled (e.g., by a new Initialize call),
                // we must not proceed to invoke the callback for this (now stale) operation.
                token.ThrowIfCancellationRequested(); 

                if (localIsSuccessful)
                {
                    // The event indicated success for this URL, and this operation wasn't cancelled yet.
                    // Now, make a final check on the player's state.
                    if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.url == videoUrl)
                    {
                        //CLogger.LogInfo($"{DEBUG_FLAG} Prepare Succeeded for: {videoUrl}. Invoking user callback. (Token Hash: {token.GetHashCode()})");
                        userOnPreparedCallback?.Invoke(); // Invoke the specific callback passed to this async method
                    }
                    else
                    {
                        // This means prepareCompleted fired, but by now, the player state is no longer valid
                        // (likely due to a Stop() from a rapid superseding InitializeVideoPlayer call that happened
                        // *after* prepareCompleted but *before* this block was reached).
                        CLogger.LogWarning($"{DEBUG_FLAG} Prepare for '{videoUrl}' (Token Hash: {token.GetHashCode()}) received prepareCompleted, but player state invalid for callback. isPrepared: {videoPlayer?.isPrepared}, url: '{videoPlayer?.url}'. Callback SKIPPED.");
                        localIsSuccessful = false; // Correct the status as we couldn't confirm and call back
                    }
                }
                else if (!token.IsCancellationRequested) // Failed (error event for this op or timeout for this op)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Prepare ultimately Failed (error/timeout) for: {videoUrl} (Token Hash: {token.GetHashCode()}). isPrepared: {videoPlayer?.isPrepared}, TimedOut: {localTimedOut}");
                }

                // Post-delay happens after callback/failure determination for this specific operation's logic
                if (preparePostDelayMs > 0) {
                     try { await UniTask.Delay(preparePostDelayMs, cancellationToken: token); }
                     catch (OperationCanceledException) { /* Post-delay cancelled, fine */ }
                }
            }
            catch (OperationCanceledException) 
            {
                //CLogger.LogInfo($"{DEBUG_FLAG} PrepareAsyncInternal for '{videoUrl}' (Token Hash: {token.GetHashCode()}) was explicitly cancelled by its token.");
                if (videoPlayer != null && videoPlayer.url == videoUrl && !videoPlayer.isPrepared)
                {
                    videoPlayer.Stop(); // Ensure player is stopped if cancelled mid-prepare for this URL
                }
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception in PrepareAsyncInternal for '{videoUrl}' (Token Hash: {token.GetHashCode()}): {ex}");
                if (videoPlayer != null && videoPlayer.url == videoUrl)
                {
                    videoPlayer.Stop();
                }
            }
            finally
            {
                if (videoPlayer != null) {
                    videoPlayer.prepareCompleted -= localOnCompleteHandler;
                    videoPlayer.errorReceived -= localOnErrorHandler;
                }
                if (token.CanBeCanceled) tokenRegistration.Dispose();

                // If the async task that just finished was using the currently _activeAsyncOperationToken,
                // then this operation is concluding, so we can clear the global "active" state.
                if (_activeAsyncOperationToken == token) 
                {
                    //CLogger.LogInfo($"{DEBUG_FLAG} Active async operation for '{videoUrl}' (Token Hash: {token.GetHashCode()}) concluded. Clearing global active state.");
                    // The _masterPrepareCts (parent of token) is disposed by CancelCurrentMasterOperation
                    // when a new Init call happens, or when Stop is called, or OnDestroy.
                    // If this was the active task and it finished naturally (not superseded),
                    // we should also ensure its _masterPrepareCts is cleaned up.
                    if (_masterPrepareCts != null && !_masterPrepareCts.IsCancellationRequested) { // Check if it's still our original CTS
                         // This check might be tricky if _masterPrepareCts was already replaced.
                         // The key is _activeAsyncOperationToken == token.
                         _masterPrepareCts.Cancel(); // Cancel it to signal no further linked operations
                         _masterPrepareCts.Dispose();
                         _masterPrepareCts = null;
                    }
                    _currentVideoUrlBeingPrepared = null;
                    // _currentUserOnPreparedCallback was for this call, and it's either been used or not.
                    // It will be overwritten by the next InitializeVideoPlayer.
                    _activeAsyncOperationToken = CancellationToken.None;
                }
            }
        }
        
        private void OnVideoLoopPointReachedHandler(VideoPlayer source) { /* CLogger.LogInfo($"{DEBUG_FLAG} Video loop point reached: {source.url}"); */ }

        private bool IsActivelyPreparing()
        {
            // "Actively preparing" means an InitializeVideoPlayer call was made,
            // its async task was launched, and that task hasn't fully completed its cleanup yet
            // (specifically, _activeAsyncOperationToken hasn't been reset to None).
            return _activeAsyncOperationToken != CancellationToken.None && !_activeAsyncOperationToken.IsCancellationRequested;
        }

        public void Play()
        {
            //CLogger.LogInfo($"{DEBUG_FLAG} Play attempt. isPrepared: {videoPlayer?.isPrepared}, IsActivelyPreparing: {IsActivelyPreparing()}, currentUrl: '{_currentVideoUrlBeingPrepared ?? videoPlayer?.url}'");
            if (videoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} VideoPlayer is null."); return; }

            if (videoPlayer.isPrepared)
            {
                // if (IsActivelyPreparing()) {
                //     CLogger.LogInfo($"{DEBUG_FLAG} Play: Player is prepared. Async task for '{_currentVideoUrlBeingPrepared ?? videoPlayer.url}' might be in final cleanup. Proceeding.");
                // }
                videoPlayer.Play();
            }
            else if (IsActivelyPreparing()) 
            { 
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but video ('{_currentVideoUrlBeingPrepared}') is currently preparing AND IS NOT YET READY (isPrepared is false)."); 
            }
            else 
            { 
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but VideoPlayer is not prepared. Current URL may be '{videoPlayer.url}'"); 
            }
        }

        public void Stop()
        {
            //CLogger.LogInfo($"{DEBUG_FLAG} Stop() called. Will cancel any current master operation.");
            CancelCurrentMasterOperation(true); 
            // The above should have stopped the player if it was part of the cancelled operation.
            // This is a fallback if it was playing independently.
            if(videoPlayer != null && (videoPlayer.isPlaying || videoPlayer.isPaused)) {
                videoPlayer.Stop();
            }
        }
        // Pause, Resume, GetPlaybackTimeMSec, SeekTime use IsActivelyPreparing() similarly to Play()
        public void Pause()
        {
            if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPlaying && !IsActivelyPreparing()) videoPlayer.Pause();
            else if (IsActivelyPreparing()) CLogger.LogWarning($"{DEBUG_FLAG} Pause called, but video is preparing.");
        }

        public void Resume()
        {
            if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.isPaused && !IsActivelyPreparing()) videoPlayer.Play();
            else if (IsActivelyPreparing()) CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but video is preparing.");
            else if (videoPlayer != null && !videoPlayer.isPrepared) CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but video not prepared.");
        }

        public long GetPlaybackTimeMSec()
        {
            return (videoPlayer != null && videoPlayer.isPrepared && !IsActivelyPreparing()) ? (long)(videoPlayer.time * 1000.0) : 0;
        }

        public void SeekTime(long milliSeconds)
        {
            if (videoPlayer != null && videoPlayer.isPrepared && videoPlayer.canSetTime && !IsActivelyPreparing())
            {
                double newTime = milliSeconds / 1000.0;
                if (newTime < 0) newTime = 0;
                if (videoPlayer.length > 0 && newTime > videoPlayer.length) newTime = videoPlayer.length;
                videoPlayer.time = newTime;
            }
             else if (IsActivelyPreparing()) { CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but video is currently preparing ({_currentVideoUrlBeingPrepared})."); }
             else if (videoPlayer != null && !videoPlayer.isPrepared) { CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but video is not prepared."); }
             else if (videoPlayer != null && !videoPlayer.canSetTime) { CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but VideoPlayer cannot be seeked (canSetTime is false)."); }
        }
    }
}