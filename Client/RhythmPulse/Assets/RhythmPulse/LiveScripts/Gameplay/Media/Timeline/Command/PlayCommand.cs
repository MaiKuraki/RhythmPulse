namespace RhythmPulse.Media
{
    public sealed class PlayCommand : ITimelineCommand
    {
        private readonly Timeline _timeline;

        public PlayCommand(Timeline timeline)
        {
            _timeline = timeline;
        }

        public void Execute()
        {
            if (_timeline.State == _timeline.PlayingState) return;

            _timeline.UnityMusicPlayer?.Play();
            _timeline.UnityVideoPlayer?.Play();
            _timeline.ChangeState(_timeline.PlayingState);
        }
    }
}