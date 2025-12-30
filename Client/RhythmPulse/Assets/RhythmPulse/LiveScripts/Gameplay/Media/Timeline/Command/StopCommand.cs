namespace RhythmPulse.Media
{
    public sealed class StopCommand : ITimelineCommand
    {
        private readonly Timeline _timeline;

        public StopCommand(Timeline timeline)
        {
            _timeline = timeline;
        }

        public void Execute()
        {
            if (_timeline.State == _timeline.StoppedState) return;

            _timeline.UnityMusicPlayer?.Stop();
            _timeline.UnityVideoPlayer?.Stop();
            _timeline.ChangeState(_timeline.StoppedState);
        }
    }
}