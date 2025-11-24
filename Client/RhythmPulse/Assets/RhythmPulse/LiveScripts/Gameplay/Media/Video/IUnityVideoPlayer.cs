using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RhythmPulse.Media
{
    /// <summary>
    /// Interface for controlling gameplay video playback.
    /// Supports both callback-based and async/await workflows.
    /// </summary>
    public interface IUnityVideoPlayer : IMediaPlayer
    {
        RenderTexture CurrentVideoTexture { get; }
        RenderTexture PreviousFrameTexture { get; }

        /// <summary>
        /// Initializes the video player with the given URL.
        /// Prepares the video on a standby player and swaps upon successful preparation.
        /// </summary>
        void InitializeVideoPlayer(in string videoUrl, bool bLoop = false, Action OnPrepared = null);

        /// <summary>
        /// Asynchronous version of InitializeVideoPlayer.
        /// </summary>
        UniTask InitializeVideoPlayerAsync(string videoUrl, bool bLoop = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the audio volume for the current video player.
        /// </summary>
        void SetVolume(float volume);
    }
}