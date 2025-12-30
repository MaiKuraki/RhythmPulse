namespace RhythmPulse.Media
{
    public sealed class ResumeCommand : ITimelineCommand
    {
        private readonly Timeline _timeline;

        public ResumeCommand(Timeline timeline)
        {
            _timeline = timeline;
        }

        public void Execute()
        {
            if (_timeline.State != _timeline.PausedState) return;

            _timeline.UnityMusicPlayer?.Resume();
            _timeline.UnityVideoPlayer?.Resume();
            _timeline.ChangeState(_timeline.PlayingState);
        }
    }
}