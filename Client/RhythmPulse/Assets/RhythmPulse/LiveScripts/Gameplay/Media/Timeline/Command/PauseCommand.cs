namespace RhythmPulse.Media
{
    public sealed class PauseCommand : ITimelineCommand
    {
        private readonly Timeline _timeline;

        public PauseCommand(Timeline timeline)
        {
            _timeline = timeline;
        }

        public void Execute()
        {
            if (_timeline.State != _timeline.PlayingState) return;

            _timeline.UnityMusicPlayer?.Pause();
            _timeline.UnityVideoPlayer?.Pause();
            _timeline.ChangeState(_timeline.PausedState);
        }
    }
}