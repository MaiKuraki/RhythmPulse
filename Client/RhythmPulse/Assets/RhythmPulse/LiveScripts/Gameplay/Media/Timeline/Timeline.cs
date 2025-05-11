using System;

namespace RhythmPulse.Gameplay.Media
{
    public interface ITimeline
    {
        void Play();
        void Pause();
        void Stop();
        void Resume();
    }

    public partial class Timeline : ITimeline, IDisposable
    {
        public Timeline()
        {
            playingState = new PlayingState(this);
            pausedState = new PausedState(this);
            stoppedState = new StoppedState(this);

            playCommand = new PlayCommand(this);
            pauseCommand = new PauseCommand(this);
            stopCommand = new StopCommand(this);
            resumeCommand = new ResumeCommand(this);
        }

        public long PlaybackTimeMSec { get; private set; }
        public void SetPlaybackTimeMSec(long milliSeconds) => PlaybackTimeMSec = milliSeconds;
        public ITimelineState State { get; private set; } = null;
        public void ChangeState(ITimelineState newState)
        {
            if (State == newState) return;

            State?.OnExit();
            State = newState;
            State?.OnEnter();
        }
        public void Tick()
        {
            if (State != null) State.OnUpdate();
        }
        private PlayingState playingState;
        private PausedState pausedState;
        private StoppedState stoppedState;
        public ITimelineState PlayingState => playingState;
        public ITimelineState PausedState => pausedState;
        public ITimelineState StoppedState => stoppedState;
        private PlayCommand playCommand;
        private PauseCommand pauseCommand;
        private StopCommand stopCommand;
        private ResumeCommand resumeCommand;
        public void Play() => playCommand.Execute();
        public void Pause() => pauseCommand.Execute();
        public void Stop() => stopCommand.Execute();
        public void Resume() => resumeCommand.Execute();
        public Action OnStartedPlayAction { get; }
        public Action OnStoppedPlayAction { get; }
        public Action OnPausedPlayAction { get; }
        public Action OnResumedPlayAction { get; }
        public void Dispose()
        {
            //  VitalRouter UnRegister.
            // UnmapRoutes();
        }
    }
}