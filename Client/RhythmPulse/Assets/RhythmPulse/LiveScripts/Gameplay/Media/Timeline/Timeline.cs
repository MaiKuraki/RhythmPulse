using System;
using VContainer;

namespace RhythmPulse.Media
{
    public sealed class Timeline : ITimeline, IDisposable
    {
        private readonly PlayingState _playingState;
        private readonly PausedState _pausedState;
        private readonly StoppedState _stoppedState;

        private readonly PlayCommand _playCommand;
        private readonly PauseCommand _pauseCommand;
        private readonly StopCommand _stopCommand;
        private readonly ResumeCommand _resumeCommand;

        private bool _isDisposed;

        public IUnityMusicPlayer UnityMusicPlayer { get; private set; }
        public IUnityVideoPlayer UnityVideoPlayer { get; private set; }

        public long PlaybackTimeMSec { get; private set; }
        public ITimelineState State { get; private set; }

        public ITimelineState PlayingState => _playingState;
        public ITimelineState PausedState => _pausedState;
        public ITimelineState StoppedState => _stoppedState;

        // Events for external listeners
        public event Action OnStartedPlay;
        public event Action OnStoppedPlay;
        public event Action OnPausedPlay;
        public event Action OnResumedPlay;

        public Timeline()
        {
            _playingState = new PlayingState(this);
            _pausedState = new PausedState(this);
            _stoppedState = new StoppedState(this);

            _playCommand = new PlayCommand(this);
            _pauseCommand = new PauseCommand(this);
            _stopCommand = new StopCommand(this);
            _resumeCommand = new ResumeCommand(this);
        }

        [Inject]
        public void Construct(IUnityMusicPlayer unityMusicPlayer, IUnityVideoPlayer unityVideoPlayer)
        {
            UnityMusicPlayer = unityMusicPlayer;
            UnityVideoPlayer = unityVideoPlayer;
        }

        public void SetPlaybackTimeMSec(long milliSeconds) => PlaybackTimeMSec = milliSeconds;

        public void ChangeState(ITimelineState newState)
        {
            if (State == newState) return;

            State?.OnExit();
            State = newState;
            State?.OnEnter();
        }

        public void Tick()
        {
            State?.OnUpdate();
        }

        public void Play() => _playCommand.Execute();
        public void Pause() => _pauseCommand.Execute();
        public void Stop() => _stopCommand.Execute();
        public void Resume() => _resumeCommand.Execute();

        // Internal event invocation
        internal void RaiseStartedPlay() => OnStartedPlay?.Invoke();
        internal void RaiseStoppedPlay() => OnStoppedPlay?.Invoke();
        internal void RaisePausedPlay() => OnPausedPlay?.Invoke();
        internal void RaiseResumedPlay() => OnResumedPlay?.Invoke();

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            OnStartedPlay = null;
            OnStoppedPlay = null;
            OnPausedPlay = null;
            OnResumedPlay = null;
        }
    }
}