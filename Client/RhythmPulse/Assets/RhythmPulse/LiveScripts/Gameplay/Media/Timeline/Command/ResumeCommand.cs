namespace RhythmPulse.Media
{
    public class ResumeCommand : ITimelineCommand
    {
        private Timeline _timeline;
        public ResumeCommand(Timeline timeline) { _timeline = timeline; }
        public void Execute()
        {
            if (_timeline.State == _timeline.PausedState)
            {
                _timeline?.UnityMusicPlayer?.Resume();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                _timeline?.UnityVideoPlayer?.Resume();
                _timeline.ChangeState(_timeline.PlayingState);
            }
        }
    }
}