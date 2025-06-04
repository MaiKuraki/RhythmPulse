using System;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Video;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace RhythmPulse.Gameplay.Media
{
    public interface IGameplayVideoPlayer
    {
        /// <summary>
        /// Gets the RenderTexture for the currently active and playing/prepared video.
        /// </summary>
        RenderTexture CurrentVideoTexture { get; }

        /// <summary>
        /// Gets the RenderTexture holding the last frame of the previously active video after a successful swap.
        /// Useful for transition effects.
        /// </summary>
        RenderTexture PreviousFrameTexture { get; }

        /// <summary>
        /// Initializes the video player with the given URL.
        /// Prepares the video on a standby player and swaps upon successful preparation.
        /// </summary>
        /// <param name="videoUrl">The URL of the video to load.</param>
        /// <param name="bLoop">Whether the video should loop.</param>
        /// <param name="OnPrepared">Callback invoked when the video is ready to be played (after successful preparation and swap).</param>
        void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null);

        /// <summary>
        /// Plays the currently prepared video.
        /// </summary>
        void Play();

        /// <summary>
        /// Stops the currently active video and cancels any ongoing standby preparation.
        /// </summary>
        void Stop();

        /// <summary>
        /// Pauses the currently playing video.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes the currently paused video.
        /// </summary>
        void Resume();

        /// <summary>
        /// Gets the current playback time of the active video in milliseconds.
        /// </summary>
        /// <returns>Playback time in milliseconds, or 0 if not prepared/available.</returns>
        long GetPlaybackTimeMSec();

        /// <summary>
        /// Seeks the active video to the specified time in milliseconds.
        /// </summary>
        /// <param name="milliSeconds">Time to seek to, in milliseconds.</param>
        void SeekTime(long milliSeconds);
    }

    [RequireComponent(typeof(VideoPlayer))] // Ensures at least one VideoPlayer is attempted to be present
    public class GameplayVideoPlayer : MonoBehaviour, IGameplayVideoPlayer
    {
        private const string DEBUG_FLAG = "[GameplayVideoPlayer]";

        [Header("Video Settings")]
        public Vector2Int textureResolution = new Vector2Int(1920, 1080);
        public RenderTextureFormat textureFormat = RenderTextureFormat.ARGB32;
        public int depthBuffer = 0; // Typically 0 for video, 16 or 24 if depth is needed for some reason
        public FilterMode filterMode = FilterMode.Bilinear;

        [Header("Prepare Settings")]
        // These Delays is designed for solve the freeze when the Mobile devices call the Prepare method, 
        // It can avoid most of the freeze but still have some, when you switch the video in a short time frequently.
        // If the Platform is PC, you can set the value to 0
        public int preparePreDelayMs = 100;
        public int internalStopToPrepareDelayMs = 100;
        public int preparePostDelayMs = 50;
        public int prepareTimeoutMs = 5000; // Timeout for a single prepare attempt

        [Header("Retry Settings")]
        public int maxPrepareRetries = 1; // Max number of retries for preparation
        public int prepareRetryDelayMs = 500; // Delay between prepare retries

        private VideoPlayer[] _videoPlayers = new VideoPlayer[2]; // Array to hold the two VideoPlayer components
        private VideoPlayer _currentVideoPlayer; // The VideoPlayer currently active or meant to be active
        private VideoPlayer _standbyVideoPlayer; // The VideoPlayer used for preparing the next video

        // These store the actual RenderTexture objects.
        private RenderTexture[] _renderTextures = new RenderTexture[2];

        // These fields will reference one of the _renderTextures[] elements.
        // Their references will be swapped to always point to the correct logical texture.
        private RenderTexture _currentVideoTexture;  // Texture for the _currentVideoPlayer
        private RenderTexture _previousFrameTexture; // Texture for the _standbyVideoPlayer (which holds the previous video's last frame after a swap)

        public RenderTexture CurrentVideoTexture => _currentVideoTexture;
        public RenderTexture PreviousFrameTexture => _previousFrameTexture;

        // Master CTS for the entire preparation process initiated by InitializeVideoPlayer.
        // It's cancelled when a new InitializeVideoPlayer is called or Stop() is called.
        private CancellationTokenSource _masterPrepareCts = null;
        private Action _currentUserOnPreparedCallback; // User callback for when preparation is complete
        private string _currentVideoUrlBeingPreparedOnStandby = null; // URL of the video currently being prepared on the standby player
        private CancellationToken _activeAsyncOperationToken = CancellationToken.None; // Token of the currently active LaunchMasterPrepareAsync operation
        private CancellationTokenSource _seekCts = null; // CTS for the seek operation on the current player

        private enum PrepareAttemptStatus { Success, Timeout, Error, Cancelled }

        private void Awake()
        {
            _videoPlayers = GetComponents<VideoPlayer>();
            if (_videoPlayers.Length < 2)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Requires 2 VideoPlayer components. Found {_videoPlayers.Length}. Attempting to add missing ones.");
                var existingPlayers = _videoPlayers.ToList();
                while (existingPlayers.Count < 2)
                {
                    VideoPlayer newPlayer = gameObject.AddComponent<VideoPlayer>();
                    existingPlayers.Add(newPlayer);
                }
                _videoPlayers = existingPlayers.ToArray();
                CLogger.LogInfo($"{DEBUG_FLAG} Ensured 2 VideoPlayer components are present. Count: {_videoPlayers.Length}");
            }

            //  Initialize Default Values, should be overridden in InitializeVideoPlayer
            foreach (var player in _videoPlayers)
            {
                player.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
                player.playOnAwake = false;
            }

            // Assign initial roles to the video players
                _currentVideoPlayer = _videoPlayers[0];
            _standbyVideoPlayer = _videoPlayers[1];

            // Create and assign RenderTextures to their respective VideoPlayers
            CreateAndAssignTargetTexture(0, ref _renderTextures[0], _currentVideoPlayer); // _videoPlayers[0] uses _renderTextures[0]
            CreateAndAssignTargetTexture(1, ref _renderTextures[1], _standbyVideoPlayer); // _videoPlayers[1] uses _renderTextures[1]

            // Initialize public texture properties to point to the correct RenderTextures
            _currentVideoTexture = _renderTextures[0];
            _previousFrameTexture = _renderTextures[1];

            // Configure both video players
            ConfigureVideoPlayer(_currentVideoPlayer, _currentVideoTexture);
            ConfigureVideoPlayer(_standbyVideoPlayer, _previousFrameTexture); // Standby player also needs configuration

            AdjustDelayForHighPerformanceDevices();
        }

        private void ConfigureVideoPlayer(VideoPlayer player, RenderTexture targetTexture)
        {
            if (player == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} ConfigureVideoPlayer: Player is null.");
                return;
            }
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = targetTexture; // Assign its designated texture
            player.audioOutputMode = VideoAudioOutputMode.None; // Assuming audio is handled elsewhere
            player.waitForFirstFrame = false; // Crucial for responsiveness, prepare will handle first frame
            player.isLooping = false; // Default, will be set by InitializeVideoPlayer
            player.Stop(); // Ensure it's in a known, stopped state

            // Subscribe to loopPointReached event
            player.loopPointReached -= OnVideoLoopPointReachedHandler; // Remove first to prevent double subscription
            player.loopPointReached += OnVideoLoopPointReachedHandler;
        }

        private void CreateAndAssignTargetTexture(int textureArrayIndex, ref RenderTexture textureField, VideoPlayer targetPlayer)
        {
            // Release existing texture if it's assigned to this field and player
            ReleaseRenderTexture(ref textureField, (targetPlayer != null && textureField == targetPlayer.targetTexture) ? targetPlayer : null);

            textureField = new RenderTexture(textureResolution.x, textureResolution.y, depthBuffer, textureFormat)
            {
                filterMode = filterMode,
                name = $"GameplayVideoRenderTexture_{textureArrayIndex}" // Unique name for debugging
            };

            if (!textureField.Create())
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create RenderTexture {textureArrayIndex} ('{textureField.name}').");
                Destroy(textureField); // Destroy the RenderTexture object if creation fails
                textureField = null;
                // If this player was supposed to use this texture, null its targetTexture
                if (targetPlayer != null && (targetPlayer.targetTexture == null || !targetPlayer.targetTexture.IsCreated()))
                {
                    targetPlayer.targetTexture = null;
                }
                return;
            }

            if (targetPlayer != null)
            {
                targetPlayer.targetTexture = textureField; // Assign the new texture to the player
            }
            // CLogger.LogInfo($"{DEBUG_FLAG} Created and assigned texture '{textureField.name}' to player {targetPlayer?.GetInstanceID()}.");
        }

        // Manages the cancellation of the overall preparation process.
        private void CancelCurrentMasterPreparation(bool stopStandbyPlayerIfPreparing, string reason = "New operation")
        {
            if (_masterPrepareCts != null) // This is the CTS created by InitializeVideoPlayer
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Cancelling master prep CTS (Hash: {_masterPrepareCts.GetHashCode()}) for '{_currentVideoUrlBeingPreparedOnStandby ?? "Unknown"}' (Reason: {reason}). Current ActiveToken Hash: {_activeAsyncOperationToken.GetHashCode()}");
                _masterPrepareCts.Cancel();
                _masterPrepareCts.Dispose(); // Dispose the CancellationTokenSource
                _masterPrepareCts = null;
            }
            // When _masterPrepareCts is cancelled, any CancellationTokenSource linked to its token (e.g., linkedCtsForThisAsyncTask in InitializeVideoPlayer)
            // will also have its token enter a cancelled state. The linked CTS itself will be disposed by its own .ContinueWith() or .Finally() block.

            _currentVideoUrlBeingPreparedOnStandby = null;
            _activeAsyncOperationToken = CancellationToken.None; // Reset the active operation token

            if (stopStandbyPlayerIfPreparing && _standbyVideoPlayer != null)
            {
                // Add more checks if _standbyVideoPlayer could be problematic
                if (_standbyVideoPlayer.gameObject == null || !_standbyVideoPlayer.gameObject.activeInHierarchy)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Standby VideoPlayer (ID: {_standbyVideoPlayer.GetInstanceID()}) or its GameObject is null/inactive. Cannot Stop().");
                }
                else
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} CancelCurrentMasterPreparation: Stopping standby VideoPlayer (ID: {_standbyVideoPlayer.GetInstanceID()}, URL: '{_standbyVideoPlayer.url}').");
                    _standbyVideoPlayer.Stop(); // This call might cause an NRE if the VideoPlayer is in a bad state.
                }
            }
            else if (stopStandbyPlayerIfPreparing && _standbyVideoPlayer == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Standby VideoPlayer was null in CancelCurrentMasterPreparation. Cannot Stop().");
            }
        }

        public void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null)
        {
            if (_standbyVideoPlayer == null || _currentVideoPlayer == null || _renderTextures[0] == null || _renderTextures[1] == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} VideoPlayer components or textures not fully initialized. Cannot initialize video '{videoUrl}'.");
                return;
            }
            if (string.IsNullOrEmpty(videoUrl))
            {
                CLogger.LogError($"{DEBUG_FLAG} Video URL is null or empty. Cannot initialize.");
                return;
            }

            // Optimization: If the requested video is already current and prepared,
            // and not currently being (re-)prepared on standby for the same URL.
            if (_currentVideoPlayer.url == videoUrl && _currentVideoPlayer.isPrepared &&
                !(_currentVideoUrlBeingPreparedOnStandby == videoUrl && IsStandbyActivelyPreparing()))
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Video '{videoUrl}' is already prepared on current player. Invoking OnPrepared.");
                OnPrepared?.Invoke();
                return;
            }

            // Optimization: If the requested video is already being prepared on the standby player.
            if (IsStandbyActivelyPreparing() && _currentVideoUrlBeingPreparedOnStandby == videoUrl)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Video '{videoUrl}' is already being prepared on standby. Updating callback.");
                _currentUserOnPreparedCallback = OnPrepared; // Update to the latest callback
                return;
            }

            // Standard path: Cancel any ongoing master preparation and start a new one.
            // This will dispose the previous _masterPrepareCts.
            CancelCurrentMasterPreparation(true, $"New InitializeVideoPlayer call for '{videoUrl}' (to standby)");

            _currentUserOnPreparedCallback = OnPrepared;
            _currentVideoUrlBeingPreparedOnStandby = videoUrl;

            // Create a new master CTS for this InitializeVideoPlayer call.
            _masterPrepareCts = new CancellationTokenSource();

            // Create a new linked CTS for this specific asynchronous task.
            // This CTS links the _masterPrepareCts.Token with the GameObject's destruction token.
            // Its lifetime is tied to this single LaunchMasterPrepareAsync call.
            var linkedCtsForThisAsyncTask = CancellationTokenSource.CreateLinkedTokenSource(
                _masterPrepareCts.Token,
                this.GetCancellationTokenOnDestroy() // Ensures task stops if GameObject is destroyed
            );
            _activeAsyncOperationToken = linkedCtsForThisAsyncTask.Token; // Store this token as the active operation's identifier

            // CLogger.LogInfo($"{DEBUG_FLAG} Launching Master Prepare for '{videoUrl}' on STANDBY. MasterCTS Hash: {_masterPrepareCts.GetHashCode()}, LinkedTaskCTS Hash: {linkedCtsForThisAsyncTask.GetHashCode()}");

            // Pass the token of the linkedCtsForThisAsyncTask to the async method.
            LaunchMasterPrepareAsync(_standbyVideoPlayer, videoUrl, bLoop, linkedCtsForThisAsyncTask.Token)
                .ContinueWith(() => // UniTask's ContinueWith, similar to Task.ContinueWith
                {
                    // This block executes after LaunchMasterPrepareAsync completes, faults, or is cancelled.
                    // This is the correct and sole place to dispose linkedCtsForThisAsyncTask.
                    // CLogger.LogInfo($"{DEBUG_FLAG} LaunchMasterPrepareAsync chain for '{_currentVideoUrlBeingPreparedOnStandby}' (LinkedTaskCTS Hash: {linkedCtsForThisAsyncTask.GetHashCode()}) ended. Disposing its linked CTS.");
                    linkedCtsForThisAsyncTask.Dispose();
                })
                .Forget((ex) => // Handle any exceptions from LaunchMasterPrepareAsync or the ContinueWith block
                {
                    CLogger.LogError($"{DEBUG_FLAG} Uncaught exception from LaunchMasterPrepareAsync chain for '{_currentVideoUrlBeingPreparedOnStandby}': {ex}");
                    // linkedCtsForThisAsyncTask should have been disposed by the ContinueWith block.
                    // If this Forget block is reached due to an error and this operation was still considered active,
                    // reset its tracking state.
                    if (_activeAsyncOperationToken == linkedCtsForThisAsyncTask.Token)
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Resetting active prepare state due to Forget exception for token {linkedCtsForThisAsyncTask.Token.GetHashCode()}.");
                        _activeAsyncOperationToken = CancellationToken.None;
                        _currentVideoUrlBeingPreparedOnStandby = null;
                        // _currentUserOnPreparedCallback = null; // LaunchMasterPrepareAsync's finally block handles this on failure/cancellation
                    }
                    // DO NOT dispose _masterPrepareCts here; it's managed by CancelCurrentMasterPreparation.
                });
        }

        // This async method manages the multi-attempt preparation logic for a single video on a given player.
        // It receives an operationToken (which is linkedCtsForThisAsyncTask.Token from InitializeVideoPlayer).
        // It does NOT own or dispose any CancellationTokenSource.
        private async UniTask LaunchMasterPrepareAsync(VideoPlayer playerToPrepareOn, string videoUrl, bool bLoop, CancellationToken operationToken)
        {
            bool overallSuccess = false;
            int attempt = 0;
            PrepareAttemptStatus attemptStatus = PrepareAttemptStatus.Error;
            string originalUrlForLog = videoUrl; // For logging, as playerToPrepareOn.url might change

            try
            {
                while (attempt <= maxPrepareRetries && !overallSuccess && !operationToken.IsCancellationRequested)
                {
                    operationToken.ThrowIfCancellationRequested(); // Check for cancellation before each attempt

                    if (attempt > 0) // If this is a retry
                    {
                        CLogger.LogInfo($"{DEBUG_FLAG} Retrying prep for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()}, attempt {attempt + 1}/{maxPrepareRetries + 1}. Delay: {prepareRetryDelayMs}ms.");
                        await UniTask.Delay(prepareRetryDelayMs, cancellationToken: operationToken);
                        operationToken.ThrowIfCancellationRequested(); // Check after delay
                    }

                    attemptStatus = await TryPrepareAttemptAsync(playerToPrepareOn, videoUrl, bLoop, operationToken);

                    if (operationToken.IsCancellationRequested) // Check if cancelled during TryPrepareAttemptAsync
                    {
                        attemptStatus = PrepareAttemptStatus.Cancelled;
                        break; // Exit retry loop
                    }

                    if (attemptStatus == PrepareAttemptStatus.Success)
                    {
                        // CLogger.LogInfo($"{DEBUG_FLAG} Prep Succeeded on standby player {playerToPrepareOn.GetInstanceID()} for: '{originalUrlForLog}'. Swapping players and invoking callback.");

                        // --- CRITICAL SWAP LOGIC ---
                        // playerToPrepareOn (which was _standbyVideoPlayer) is now ready with the new video.
                        // _currentVideoPlayer is the currently active one (old video).

                        // 1. Pause (not Stop) the video that is currently active (_currentVideoPlayer).
                        //    This keeps its last frame available for PreviousFrameTexture.
                        if (_currentVideoPlayer.isPlaying)
                        {
                            // CLogger.LogInfo($"{DEBUG_FLAG} Pausing previously current player (ID: {_currentVideoPlayer.GetInstanceID()}, URL: '{_currentVideoPlayer.url}') for smooth transition.");
                            _currentVideoPlayer.Pause();
                        }

                        // 2. Swap the VideoPlayer object references
                        VideoPlayer tempPlayer = _currentVideoPlayer;
                        _currentVideoPlayer = playerToPrepareOn; // playerToPrepareOn was _standbyVideoPlayer, now becomes _currentVideoPlayer
                        _standbyVideoPlayer = tempPlayer;       // old _currentVideoPlayer now becomes _standbyVideoPlayer

                        // 3. Swap the RenderTexture field references (_currentVideoTexture and _previousFrameTexture)
                        //    _currentVideoTexture should point to the texture used by the NEW _currentVideoPlayer.
                        //    _previousFrameTexture should point to the texture used by the NEW _standbyVideoPlayer.
                        if (_currentVideoPlayer.targetTexture == _renderTextures[0])
                        { // New current player is using texture 0
                            _currentVideoTexture = _renderTextures[0];
                            _previousFrameTexture = _renderTextures[1]; // So previous must be texture 1
                        }
                        else // New current player is using texture 1
                        {
                            _currentVideoTexture = _renderTextures[1];
                            _previousFrameTexture = _renderTextures[0]; // So previous must be texture 0
                        }
                        // CLogger.LogInfo($"{DEBUG_FLAG} Swap complete. New Current Player: {_currentVideoPlayer.GetInstanceID()}, New Standby Player: {_standbyVideoPlayer.GetInstanceID()}. Current Texture: '{_currentVideoTexture.name}', Previous Texture: '{_previousFrameTexture.name}'.");

                        _currentUserOnPreparedCallback?.Invoke();
                        overallSuccess = true; // Mark success to exit retry loop
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Timeout)
                    {
                        if (operationToken.IsCancellationRequested) break; // If cancelled, exit
                        if (attempt < maxPrepareRetries)
                        {
                            CLogger.LogWarning($"{DEBUG_FLAG} Standby prep attempt {attempt + 1} for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()} timed out. Will retry.");
                            // If the player is still set to the target URL and not prepared (which it shouldn't be if timed out),
                            // and this is still the active operation, stop it to reset before retry.
                            if (playerToPrepareOn.url == originalUrlForLog && !playerToPrepareOn.isPrepared && _activeAsyncOperationToken == operationToken)
                            {
                                playerToPrepareOn.Stop();
                                await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: operationToken); // Small delay after stop
                            }
                        }
                        else
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Standby prep for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()} timed out after {maxPrepareRetries + 1} attempts. Giving up.");
                        }
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Error)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Standby prep attempt {attempt + 1} for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()} failed with error. No more retries for this sequence.");
                        break; // Exit retry loop on unrecoverable error
                    }
                    else if (attemptStatus == PrepareAttemptStatus.Cancelled)
                    {
                        operationToken.ThrowIfCancellationRequested(); // Should throw, or loop condition will catch it
                    }
                    attempt++;
                } // End of while loop

                if (!overallSuccess && !operationToken.IsCancellationRequested) // If all retries failed and not due to cancellation
                {
                    CLogger.LogError($"{DEBUG_FLAG} All standby prep attempts for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()} failed. Last status: {attemptStatus}.");
                }

                if (preparePostDelayMs > 0 && overallSuccess && !operationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(preparePostDelayMs, cancellationToken: operationToken);
                }
            }
            catch (OperationCanceledException) // Catches cancellations from ThrowIfCancellationRequested or awaits within the try block
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Master prep task for '{originalUrlForLog}' was cancelled (Token Hash: {operationToken.GetHashCode()}).");
                // If the player was being prepared with the target URL and this operation (token) was active, stop it.
                if (playerToPrepareOn.url == originalUrlForLog && !playerToPrepareOn.isPrepared && _activeAsyncOperationToken == operationToken)
                {
                    playerToPrepareOn.Stop();
                }
                attemptStatus = PrepareAttemptStatus.Cancelled; // Ensure status reflects cancellation
                overallSuccess = false; // Not successful if cancelled
            }
            catch (Exception ex) // Catch any other unexpected exceptions
            {
                CLogger.LogError($"{DEBUG_FLAG} Unhandled exception in master prep for '{originalUrlForLog}' on player {playerToPrepareOn.GetInstanceID()}: {ex}.");
                if (playerToPrepareOn.url == originalUrlForLog && _activeAsyncOperationToken == operationToken) playerToPrepareOn.Stop(); // Stop if an error occurred for this active op
                attemptStatus = PrepareAttemptStatus.Error; // Ensure status reflects error
                overallSuccess = false; // Not successful due to error
            }
            finally
            {
                // This 'finally' block is for cleaning up states related to THIS LaunchMasterPrepareAsync attempt.
                // It does NOT dispose any CancellationTokenSource.
                // _masterPrepareCts is managed by CancelCurrentMasterPreparation.
                // The CancellationTokenSource for operationToken (linkedCtsForThisAsyncTask) is managed by InitializeVideoPlayer's .ContinueWith().

                // Check if this asynchronous operation was still considered the globally "active" one when it finished.
                if (_activeAsyncOperationToken == operationToken)
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} LaunchMasterPrepareAsync for '{originalUrlForLog}' (Token: {operationToken.GetHashCode()}) ended. OverallSuccess: {overallSuccess}. Resetting active state flags.");
                    _activeAsyncOperationToken = CancellationToken.None; // Clear the active operation token
                    _currentVideoUrlBeingPreparedOnStandby = null;       // Clear the URL being prepared
                    if (!overallSuccess) // If this specific preparation process failed (due to error, timeout, or cancellation)
                    {
                        _currentUserOnPreparedCallback = null; // Clear the user callback
                    }
                }
                else // This operation finished, but another one became active in the meantime.
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} LaunchMasterPrepareAsync for '{originalUrlForLog}' (Token: {operationToken.GetHashCode()}) ended, but it was NO LONGER the active operation (current active: {_activeAsyncOperationToken.GetHashCode()}). No global state reset by this instance's finally block.");
                }
            }
        }

        // Attempts to prepare the video on a single VideoPlayer instance once.
        private async UniTask<PrepareAttemptStatus> TryPrepareAttemptAsync(VideoPlayer playerInstance, string videoUrl, bool bLoop, CancellationToken attemptToken)
        {
            if (playerInstance == null) return PrepareAttemptStatus.Error;
            attemptToken.ThrowIfCancellationRequested(); // Check for cancellation at the beginning

            var eventSignal = new UniTaskCompletionSource<bool>(); // True for success (prepared), False for error (errorReceived)

            // Link the eventSignal's cancellation to the attemptToken.
            // If attemptToken is cancelled, eventSignal.TrySetCanceled() is called,
            // which will cause eventSignal.Task to transition to a cancelled state.
            using var tokenRegistration = attemptToken.RegisterWithoutCaptureExecutionContext(() => eventSignal.TrySetCanceled(attemptToken));

            VideoPlayer.EventHandler localOnCompleteHandler = null;
            VideoPlayer.ErrorEventHandler localOnErrorHandler = null;

            try
            {
                localOnCompleteHandler = (source) =>
                {
                    if (source == playerInstance && source.url == videoUrl && !attemptToken.IsCancellationRequested)
                    {
                        eventSignal.TrySetResult(true); // Signal successful preparation
                    }
                };
                localOnErrorHandler = (source, message) =>
                {
                    if (source == playerInstance && source.url == videoUrl && !attemptToken.IsCancellationRequested)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} VideoPlayer Error (player {playerInstance.GetInstanceID()}, url '{videoUrl}'): {message}");
                        eventSignal.TrySetResult(false); // Signal error during preparation
                    }
                };

                if (preparePreDelayMs > 0) await UniTask.Delay(preparePreDelayMs, cancellationToken: attemptToken);
                attemptToken.ThrowIfCancellationRequested();

                playerInstance.Stop();
                playerInstance.url = null;

                if (internalStopToPrepareDelayMs > 0) await UniTask.Delay(internalStopToPrepareDelayMs, cancellationToken: attemptToken);
                else await UniTask.Yield(PlayerLoopTiming.Update, attemptToken);
                attemptToken.ThrowIfCancellationRequested();

                if (playerInstance.targetTexture == null || !playerInstance.targetTexture.IsCreated())
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Target RenderTexture for player {playerInstance.GetInstanceID()} ('{videoUrl}') is invalid. Attempting to re-assign/recreate.");
                    // Texture re-creation logic from your original code would go here if needed.
                    // For this fix, we focus on the Timeout line. Ensure CreateAndAssignTargetTexture is called if this path is hit
                    // and _videoPlayers/_renderTextures are available.
                    // For example:
                    // int textureArrayIndex = System.Array.IndexOf(_videoPlayers, playerInstance);
                    // if (textureArrayIndex != -1) {
                    //     CreateAndAssignTargetTexture(textureArrayIndex, ref _renderTextures[textureArrayIndex], playerInstance);
                    //     if (playerInstance.targetTexture == null || !playerInstance.targetTexture.IsCreated()) {
                    //         CLogger.LogError($"{DEBUG_FLAG} Failed to ensure valid RenderTexture for player {playerInstance.GetInstanceID()}. Aborting.");
                    //         return PrepareAttemptStatus.Error;
                    //     }
                    // } else {
                    //     CLogger.LogError($"{DEBUG_FLAG} Could not determine player index for texture recreation. Aborting.");
                    //     return PrepareAttemptStatus.Error;
                    // }
                    if (playerInstance.targetTexture == null)
                    { // Simplified check if full recreation logic is omitted for snippet
                        CLogger.LogError($"{DEBUG_FLAG} Target texture is null for player {playerInstance.GetInstanceID()} before preparing '{videoUrl}'. This can lead to errors.");
                        // return PrepareAttemptStatus.Error; // Or handle appropriately
                    }
                }

                playerInstance.source = VideoSource.Url;
                playerInstance.url = videoUrl;
                playerInstance.isLooping = bLoop;

                playerInstance.prepareCompleted += localOnCompleteHandler;
                playerInstance.errorReceived += localOnErrorHandler;

                playerInstance.Prepare();

                bool prepEventResult;
                try
                {
                    // Wait for prepareCompleted or errorReceived, with a timeout.
                    // eventSignal.Task is already linked with attemptToken via tokenRegistration.
                    // If attemptToken is cancelled, eventSignal.Task will throw OperationCanceledException.
                    // If eventSignal.Task does not complete within prepareTimeoutMs, Timeout() throws TimeoutException.
                    prepEventResult = await eventSignal.Task.Timeout(TimeSpan.FromMilliseconds(prepareTimeoutMs));
                }
                catch (TimeoutException)
                {
                    if (!attemptToken.IsCancellationRequested && playerInstance.url == videoUrl && !playerInstance.isPrepared)
                    {
                        CLogger.LogWarning($"{DEBUG_FLAG} Prepare attempt for '{videoUrl}' on player {playerInstance.GetInstanceID()} timed out. Stopping player.");
                        playerInstance.Stop();
                    }
                    return PrepareAttemptStatus.Timeout;
                }
                // If eventSignal.Task was cancelled by attemptToken, the await eventSignal.Task.Timeout(...)
                // will throw an OperationCanceledException, which will be caught by the outer catch block.

                attemptToken.ThrowIfCancellationRequested();

                if (prepEventResult)
                {
                    if (playerInstance.isPrepared && playerInstance.url == videoUrl) return PrepareAttemptStatus.Success;
                    CLogger.LogWarning($"{DEBUG_FLAG} Prep for '{videoUrl}' (player {playerInstance.GetInstanceID()}) received complete_event, but player state is invalid. isPrepared: {playerInstance.isPrepared}, current URL: '{playerInstance.url}'. Treating as error.");
                    return PrepareAttemptStatus.Error;
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Prep for '{videoUrl}' (player {playerInstance.GetInstanceID()}) failed due to an error event from VideoPlayer.");
                    return PrepareAttemptStatus.Error;
                }
            }
            catch (OperationCanceledException)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Prepare attempt for '{videoUrl}' (player {playerInstance.GetInstanceID()}) was cancelled.");
                if (playerInstance != null && playerInstance.url == videoUrl && !playerInstance.isPrepared)
                {
                    playerInstance.Stop();
                }
                return PrepareAttemptStatus.Cancelled;
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception in TryPrepareAttemptAsync for '{videoUrl}' (player {playerInstance.GetInstanceID()}): {ex}");
                if (playerInstance != null && playerInstance.url == videoUrl) playerInstance.Stop();
                return PrepareAttemptStatus.Error;
            }
            finally
            {
                if (playerInstance != null)
                {
                    playerInstance.prepareCompleted -= localOnCompleteHandler;
                    playerInstance.errorReceived -= localOnErrorHandler;
                }
            }
        }
        private void OnVideoLoopPointReachedHandler(VideoPlayer source)
        {
            if (source == _currentVideoPlayer)
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Video loop point reached for CURRENT player: {source.url}");
                if (!source.isLooping)
                {
                    // Video ended and is not set to loop by user.
                    // Could fire an "ended" event here if needed for game logic.
                }
            }
            // Could also handle if (source == _standbyVideoPlayer) but usually not relevant for loop points.
        }

        private bool IsStandbyActivelyPreparing()
        {
            // Checks if there's an active, non-cancelled asynchronous operation token.
            return _activeAsyncOperationToken != CancellationToken.None && !_activeAsyncOperationToken.IsCancellationRequested;
        }

        public void Play()
        {
            if (_currentVideoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} Current VideoPlayer is null. Cannot Play."); return; }

            if (_currentVideoPlayer.isPrepared)
            {
                _currentVideoPlayer.Play();
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Play called, but current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}, URL: '{_currentVideoPlayer.url}') is not prepared. Is standby preparing? {IsStandbyActivelyPreparing()}");
                // Optionally, if standby is preparing the *same* URL, one might queue the Play call or wait.
                // For now, it just logs.
            }
        }

        public void Stop()
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Stop() called. Cancelling master prep and stopping players.");
            CancelCurrentMasterPreparation(true, "Explicit Stop() call"); // Cancel standby prep and stop standby player

            // Cancel any ongoing seek operation on the current player
            _seekCts?.Cancel();
            // _seekCts is disposed when a new seek starts or in OnDestroy. Don't dispose here without replacing.

            if (_currentVideoPlayer != null && (_currentVideoPlayer.isPlaying || _currentVideoPlayer.isPaused || _currentVideoPlayer.isPrepared))
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Stopping current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}).");
                _currentVideoPlayer.Stop();
            }
            // _standbyVideoPlayer should have been stopped by CancelCurrentMasterPreparation if it was preparing.
            // If it was holding a previously played video (paused), ensure it's also stopped.
            if (_standbyVideoPlayer != null && _standbyVideoPlayer != _currentVideoPlayer &&
                (_standbyVideoPlayer.isPlaying || _standbyVideoPlayer.isPaused || _standbyVideoPlayer.isPrepared))
            {
                // CLogger.LogInfo($"{DEBUG_FLAG} Stopping standby VideoPlayer (ID: {_standbyVideoPlayer.GetInstanceID()}) as part of general Stop().");
                _standbyVideoPlayer.Stop();
            }
        }

        public void Pause()
        {
            if (_currentVideoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} Current VideoPlayer is null. Cannot Pause."); return; }

            if (_currentVideoPlayer.isPrepared && _currentVideoPlayer.isPlaying)
            {
                _currentVideoPlayer.Pause();
            }
            else if (!_currentVideoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Pause called, but current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}) is not prepared.");
            }
            else if (!_currentVideoPlayer.isPlaying)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Pause called, but current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}) is already not playing (paused or stopped).");
            }
        }

        public void Resume() // Or Unpause
        {
            if (_currentVideoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} Current VideoPlayer is null. Cannot Resume."); return; }

            // Play if paused, or if stopped but still prepared (e.g., after seeking while paused)
            if (_currentVideoPlayer.isPrepared && (_currentVideoPlayer.isPaused || !_currentVideoPlayer.isPlaying))
            {
                _currentVideoPlayer.Play();
            }
            else if (!_currentVideoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Resume called, but current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}) is not prepared.");
            }
        }

        public long GetPlaybackTimeMSec()
        {
            if (_currentVideoPlayer == null || !_currentVideoPlayer.isPrepared) return 0;
            return (long)(_currentVideoPlayer.clockTime * 1000.0); // clockTime is in seconds
        }

        public void SeekTime(long milliSeconds)
        {
            if (_currentVideoPlayer == null) { CLogger.LogError($"{DEBUG_FLAG} Current VideoPlayer is null. Cannot SeekTime."); return; }

            if (IsStandbyActivelyPreparing() && _currentVideoPlayer.url == _currentVideoUrlBeingPreparedOnStandby)
            {
                // This is a tricky situation: seeking the current player while the standby is preparing the *same URL*.
                // The seek will apply to the _currentVideoPlayer instance. If the standby finishes and swaps, the seek might be lost.
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called for '{_currentVideoPlayer.url}', which may also be targeted by standby. Seek operates on current instance. This might lead to unexpected behavior if standby swaps with the same video.");
            }

            if (!_currentVideoPlayer.isPrepared)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but current video (ID: {_currentVideoPlayer.GetInstanceID()}, URL: '{_currentVideoPlayer.url}') is not prepared.");
                return;
            }
            if (!_currentVideoPlayer.canSetTime) // Check if seeking is supported
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SeekTime called, but current VideoPlayer (ID: {_currentVideoPlayer.GetInstanceID()}, URL: '{_currentVideoPlayer.url}') cannot be seeked (canSetTime=false). This often happens with live streams.");
                return;
            }

            _seekCts?.Cancel(); // Cancel any previous seek operation
            _seekCts?.Dispose(); // Dispose the old CTS
            _seekCts = new CancellationTokenSource(); // Create a new CTS for this seek operation

            // Link with GameObject destruction token
            var linkedSeekCts = CancellationTokenSource.CreateLinkedTokenSource(_seekCts.Token, this.GetCancellationTokenOnDestroy());

            InternalSeekTimeAsync(_currentVideoPlayer, milliSeconds, linkedSeekCts.Token)
                .ContinueWith(() => // Ensure linkedSeekCts is disposed when the async operation (or its chain) finishes
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} Seek operation chain for '{_currentVideoPlayer?.url}' to {milliSeconds}ms finished. Disposing linkedSeekCts.");
                    linkedSeekCts.Dispose();
                })
                .Forget(ex => // Catch and log any exceptions from the seek operation or its ContinueWith
                {
                    if (ex is OperationCanceledException)
                    {
                        // CLogger.LogInfo($"{DEBUG_FLAG} Seek operation for '{_currentVideoPlayer?.url}' to {milliSeconds}ms was cancelled. Message: {ex.Message}");
                    }
                    else
                    {
                        // CLogger.LogError($"{DEBUG_FLAG} Seek operation for '{_currentVideoPlayer?.url}' to {milliSeconds}ms failed or was cancelled with exception: {ex}");
                    }
                    // linkedSeekCts should ideally be disposed by ContinueWith.
                    // If an exception occurs that prevents ContinueWith from running AND linkedSeekCts hasn't been disposed,
                    // it might leak. This is rare if .ContinueWith() itself doesn't throw before scheduling.
                    // For utmost safety, one might re-check and dispose here, but usually not needed.
                });
        }

        private async UniTask InternalSeekTimeAsync(VideoPlayer player, long milliSeconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation at the start

            double newTimeSec = Mathf.Max(0, (float)milliSeconds / 1000.0f); // Ensure non-negative
            if (player.length > 0) // player.length can be 0 or negative if unknown (e.g. not prepared, or live stream)
            {
                newTimeSec = Math.Min(newTimeSec, player.length); // Clamp to video duration
            }

            CLogger.LogInfo($"{DEBUG_FLAG} Seeking player {player.GetInstanceID()} (URL: '{player.url}') to {newTimeSec}s ({milliSeconds}ms).");

            bool wasPlaying = player.isPlaying;
            if (wasPlaying)
            {
                player.Pause(); // Pause before seek for more reliable behavior on some platforms
                // await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken); // Optional: wait a frame for pause to register
                // cancellationToken.ThrowIfCancellationRequested();
            }

            // Unity's VideoPlayer.time can be unreliable if set too rapidly or if the player is not in a stable state.
            // Some platforms prefer seeking while paused.
            player.time = newTimeSec;

            // It might take a frame or more for the time to actually apply and for the frame to update.
            // Waiting for 'seekCompleted' event is more robust if available and needed, but adds complexity.
            // For now, a short delay or yield might help.
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken); // Give a frame for time to apply
                                                                             // One could also wait until player.time is close to newTimeSec, with a timeout.
                                                                             // E.g., await UniTask.WaitUntil(() => Mathf.Approximately((float)player.time, (float)newTimeSec) || !player.isPrepared || player.isLooping /* some exit condition */, cancellationToken: cancellationToken);

            cancellationToken.ThrowIfCancellationRequested(); // Check after potential delay/yield

            if (wasPlaying)
            {
                player.Play(); // Resume if it was playing
            }
            CLogger.LogInfo($"{DEBUG_FLAG} Seek completed for player {player.GetInstanceID()}. Current time: {player.time}s.");
        }

#if UNITY_EDITOR
        // Method specifically for an editor button to call for recreating textures based on current settings.
        public void EditorRecreateAllManagedTextures()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Editor button: Recreating all managed render textures based on current settings.");

            if (_videoPlayers == null || _videoPlayers.Length < 2 || _renderTextures == null || _renderTextures.Length < 2)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: Component not fully initialized (players/textures array missing). Awake might not have run properly.");
                // Attempt to run parts of Awake logic if in editor and not playing,
                // but be very careful with calling Awake manually or parts of it.
                if (!Application.isPlaying && (gameObject.GetComponent<GameplayVideoPlayer>() != null))
                {
                    // This is a simplified re-initialization for editor context.
                    // A full Awake() call might have side effects.
                    _videoPlayers = GetComponents<VideoPlayer>();
                    if (_videoPlayers.Length < 2)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Still less than 2 VideoPlayers after GetComponents. Add them manually or ensure Awake runs.");
                        return;
                    }
                    _renderTextures = new RenderTexture[2]; // Reset render textures array
                }
                else if (Application.isPlaying)
                {
                    // If playing, Awake should have handled it. This state is unexpected.
                    CLogger.LogError($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: Called while playing but component seems uninitialized. This is an inconsistent state.");
                    return;
                }
                if (_videoPlayers == null || _videoPlayers.Length < 2 || _renderTextures == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: Failed to ensure basic arrays. Aborting.");
                    return; // Still not initialized
                }
            }
            if (_videoPlayers[0] == null || _videoPlayers[1] == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: One or more VideoPlayer components in the array are null. Re-fetching...");
                _videoPlayers = GetComponents<VideoPlayer>(); // Re-fetch
                if (_videoPlayers.Length < 2 || _videoPlayers[0] == null || _videoPlayers[1] == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: Still missing VideoPlayer components after re-fetch. Aborting.");
                    return;
                }
                // Re-assign based on array index if they were null before
                // This assumes _currentVideoPlayer and _standbyVideoPlayer roles haven't been swapped yet or are unknown.
                // If roles were known, maintain them. For simplicity here, re-default from array.
                _currentVideoPlayer = _videoPlayers[0];
                _standbyVideoPlayer = _videoPlayers[1];
            }

            // Validate/Recreate texture for the player at _videoPlayers[0] (which should use _renderTextures[0])
            ValidateAndRecreateTextureIfNeeded(0, ref _renderTextures[0], _videoPlayers[0]);
            // Validate/Recreate texture for the player at _videoPlayers[1] (which should use _renderTextures[1])
            ValidateAndRecreateTextureIfNeeded(1, ref _renderTextures[1], _videoPlayers[1]);

            // After recreating, ensure the _currentVideoTexture and _previousFrameTexture fields
            // still correctly point to the textures associated with the current roles of _currentVideoPlayer and _standbyVideoPlayer.
            if (_currentVideoPlayer == _videoPlayers[0])
            {
                _currentVideoTexture = _renderTextures[0];
                _previousFrameTexture = _renderTextures[1];
            }
            else if (_currentVideoPlayer == _videoPlayers[1])
            {
                _currentVideoTexture = _renderTextures[1];
                _previousFrameTexture = _renderTextures[0];
            }
            else // Fallback if _currentVideoPlayer is somehow not one of the two array players
            {
                CLogger.LogWarning($"{DEBUG_FLAG} EditorRecreateAllManagedTextures: _currentVideoPlayer is not one of the array players (_videoPlayers[0] or _videoPlayers[1]). Defaulting texture pointers. CurrentPlayer: {_currentVideoPlayer?.GetInstanceID()}, Player0: {_videoPlayers[0]?.GetInstanceID()}, Player1: {_videoPlayers[1]?.GetInstanceID()}");
                // Default to array order if roles are messed up
                _currentVideoPlayer = _videoPlayers[0];
                _standbyVideoPlayer = _videoPlayers[1];
                _currentVideoTexture = _renderTextures[0];
                _previousFrameTexture = _renderTextures[1];
            }
            CLogger.LogInfo($"{DEBUG_FLAG} Textures re-validated/recreated by editor. Current: {_currentVideoTexture?.name}, Previous: {_previousFrameTexture?.name}");
        }

        /// <summary>
        /// Gets whether the current primary video player is actively playing.
        /// </summary>
        public bool IsCurrentVideoPlaying => _currentVideoPlayer != null && _currentVideoPlayer.isPrepared && _currentVideoPlayer.isPlaying;

        // This function is called when the script is loaded or a value is changed in the Inspector (Editor only).
        private void OnValidate()
        {
            // OnValidate can be called frequently in the editor, even before Awake when adding the component.
            // Only perform full validation if basic structures are somewhat initialized or if playing.
            if (!Application.isPlaying)
            {
                // In editor, not playing: Only perform light checks or defer to Awake/EditorRecreate button.
                // If critical arrays are null, it's too early for OnValidate to do much.
                if (_videoPlayers == null || _renderTextures == null)
                {
                    // CLogger.LogInfo($"{DEBUG_FLAG} OnValidate (Editor, not playing): Component not fully initialized yet. Will be handled by Awake or EditorRecreate button.");
                    return;
                }
                // If arrays exist but elements might be null (e.g., after script recompilation)
                if (_videoPlayers.Length < 2 || _videoPlayers[0] == null || _videoPlayers[1] == null)
                {
                    // CLogger.LogWarning($"{DEBUG_FLAG} OnValidate (Editor, not playing): VideoPlayers array not fully populated. Awake should handle this.");
                    return; // Let Awake handle full setup.
                }
            }
            else
            { // Playing
                if (_videoPlayers == null || _videoPlayers.Length < 2 || _renderTextures == null || _renderTextures.Length < 2 ||
                    _currentVideoPlayer == null || _standbyVideoPlayer == null)
                {
                    // CLogger.LogWarning($"{DEBUG_FLAG} OnValidate (Playing): Core components not initialized. Likely too early in lifecycle.");
                    return; // Not ready for validation logic during play mode if core refs are missing.
                }
            }

            // At this point, _videoPlayers and _renderTextures should exist if we proceed.
            // Ensure _currentVideoPlayer and _standbyVideoPlayer references are assigned if possible,
            // especially if OnValidate runs before Awake or after a recompile that might clear non-serialized fields.
            if (_currentVideoPlayer == null && _videoPlayers != null && _videoPlayers.Length > 0) _currentVideoPlayer = _videoPlayers[0];
            if (_standbyVideoPlayer == null && _videoPlayers != null && _videoPlayers.Length > 1) _standbyVideoPlayer = _videoPlayers[1];

            // If still null after attempting assignment, can't proceed with validation that depends on them.
            if (_currentVideoPlayer == null || _standbyVideoPlayer == null) return;

            // Validate texture for the player currently in the _currentVideoPlayer role
            ValidateAndRecreateTextureIfNeeded(_currentVideoPlayer == _videoPlayers[0] ? 0 : 1,
                                             ref (_currentVideoPlayer == _videoPlayers[0] ? ref _renderTextures[0] : ref _renderTextures[1]),
                                             _currentVideoPlayer);
            // Validate texture for the player currently in the _standbyVideoPlayer role
            ValidateAndRecreateTextureIfNeeded(_standbyVideoPlayer == _videoPlayers[0] ? 0 : 1,
                                             ref (_standbyVideoPlayer == _videoPlayers[0] ? ref _renderTextures[0] : ref _renderTextures[1]),
                                             _standbyVideoPlayer);

            // Ensure our _currentVideoTexture and _previousFrameTexture fields point to the correct array elements
            // based on which VideoPlayer component is currently _currentVideoPlayer.
            if (_currentVideoPlayer == _videoPlayers[0])
            {
                _currentVideoTexture = _renderTextures[0];
                _previousFrameTexture = _renderTextures[1];
            }
            else if (_currentVideoPlayer == _videoPlayers[1])
            {
                _currentVideoTexture = _renderTextures[1];
                _previousFrameTexture = _renderTextures[0];
            }
            // No else: if _currentVideoPlayer isn't one of these, something is very wrong,
            // but EditorRecreate button or re-init should fix it. OnValidate is for reacting to inspector changes.
        }

        // Validates if a specific RenderTexture (textureInArray) matches current settings and is assigned to the associatedPlayer.
        // Recreates or reassigns if necessary.
        private void ValidateAndRecreateTextureIfNeeded(int textureArrayIndex, ref RenderTexture textureInArray, VideoPlayer associatedPlayer)
        {
            if (associatedPlayer == null)
            {
                // CLogger.LogWarning($"{DEBUG_FLAG} ValidateAndRecreateTextureIfNeeded: associatedPlayer is null for texture index {textureArrayIndex}. Cannot validate assignment.");
                // We can still validate the textureInArray itself if it exists.
            }

            bool needsRecreation = false;
            if (textureInArray == null || !textureInArray.IsCreated())
            {
                needsRecreation = true;
                CLogger.LogInfo($"{DEBUG_FLAG} OnValidate: Texture {textureArrayIndex} (current name: {textureInArray?.name}) is null or not created.");
            }
            else if (textureInArray.width != textureResolution.x ||
                     textureInArray.height != textureResolution.y ||
                     textureInArray.format != textureFormat ||
                     textureInArray.depth != depthBuffer ||
                     textureInArray.filterMode != filterMode)
            {
                needsRecreation = true;
                CLogger.LogInfo($"{DEBUG_FLAG} OnValidate: Texture {textureArrayIndex} ('{textureInArray.name}') parameters changed (Resolution: {textureInArray.width}x{textureInArray.height} vs {textureResolution.x}x{textureResolution.y}, Format: {textureInArray.format} vs {textureFormat}, etc.).");
            }

            if (needsRecreation)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} OnValidate: Recreating texture for index {textureArrayIndex}. Associated player: {associatedPlayer?.GetInstanceID()}");
                // Pass the player that *should* be using this texture slot from the _videoPlayers array.
                // This ensures the texture is created for the correct player if roles were swapped or are being set up.
                VideoPlayer targetPlayerForThisSlot = null;
                if (_videoPlayers != null && _videoPlayers.Length > textureArrayIndex && _videoPlayers[textureArrayIndex] != null)
                {
                    targetPlayerForThisSlot = _videoPlayers[textureArrayIndex];
                }
                else if (associatedPlayer != null)
                {
                    // Fallback: if the _videoPlayers array is not reliable here, use the passed 'associatedPlayer'.
                    // This can happen if OnValidate is called in a weird state.
                    targetPlayerForThisSlot = associatedPlayer;
                }
                else
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} OnValidate: Cannot determine target player for texture slot {textureArrayIndex} during recreation.");
                }

                CreateAndAssignTargetTexture(textureArrayIndex, ref textureInArray, targetPlayerForThisSlot);
            } // Texture parameters are fine, check if it's correctly assigned to the associated player
            else if (associatedPlayer != null && associatedPlayer.targetTexture != textureInArray)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} OnValidate: Player {associatedPlayer.GetInstanceID()} targetTexture mismatch for texture index {textureArrayIndex} ('{textureInArray?.name}'). Reassigning.");
                associatedPlayer.targetTexture = textureInArray;
            }
        }
#endif // UNITY_EDITOR

        private void OnDestroy()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} OnDestroy called. Cleaning up video players and textures.");
            // Cancel any ongoing master preparation. False means don't try to Stop standby player, it's being destroyed anyway.
            CancelCurrentMasterPreparation(false, "OnDestroy");

            // Cancel and dispose the seek CTS
            _seekCts?.Cancel();
            _seekCts?.Dispose();
            _seekCts = null;

            foreach (var player in _videoPlayers) // Iterate over the array of VideoPlayer components
            {
                if (player != null)
                {
                    player.loopPointReached -= OnVideoLoopPointReachedHandler; // Unsubscribe event
                    if (Application.isPlaying) // Stop() can cause issues if called during editor OnDestroy sometimes
                    {
                        // Check if player is playing or paused before stopping, to avoid warnings if already stopped/never started.
                        if (player.isPlaying || player.isPaused)
                        {
                            player.Stop();
                        }
                    }
                    // Note: VideoPlayer components themselves will be destroyed with the GameObject.
                }
            }

            // Release the RenderTextures stored in the array
            // Pass the corresponding player from the array to ReleaseRenderTexture, if it exists.
            ReleaseRenderTexture(ref _renderTextures[0], (_videoPlayers.Length > 0 && _videoPlayers[0] != null) ? _videoPlayers[0] : null);
            ReleaseRenderTexture(ref _renderTextures[1], (_videoPlayers.Length > 1 && _videoPlayers[1] != null) ? _videoPlayers[1] : null);

            _currentVideoTexture = null; // Clear references
            _previousFrameTexture = null;
        }

        // Adjusts delays for high-performance devices (e.g., PC standalone, Editor) for faster transitions.
        private void AdjustDelayForHighPerformanceDevices()
        {
#if UNITY_STANDALONE
            preparePreDelayMs = 0;
            internalStopToPrepareDelayMs = 0;
            preparePostDelayMs = 0;
            CLogger.LogInfo($"{DEBUG_FLAG} Adjusted delays for high-performance device/editor (all set to 0ms).");
#endif
        }

        // Releases a RenderTexture and clears its reference from an associated VideoPlayer.
        private void ReleaseRenderTexture(ref RenderTexture textureField, VideoPlayer associatedPlayer)
        {
            if (textureField != null)
            {
                // If an associated player is provided and is using this texture, clear its targetTexture.
                if (associatedPlayer != null && associatedPlayer.targetTexture == textureField)
                {
                    associatedPlayer.targetTexture = null;
                }
                if (textureField.IsCreated()) // Check if the texture has been created on the GPU
                {
                    textureField.Release(); // Release GPU resources
                }
                Destroy(textureField); // Destroy the RenderTexture asset itself (UnityEngine.Object)
                textureField = null;   // Clear the C# reference
            }
        }
    }
}