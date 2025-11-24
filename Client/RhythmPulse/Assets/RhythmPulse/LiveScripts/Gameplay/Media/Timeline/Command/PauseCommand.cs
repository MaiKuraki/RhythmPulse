namespace RhythmPulse.Media
{
    public class PauseCommand : ITimelineCommand
    {
        private Timeline _timeline;
        public PauseCommand(Timeline timeline)
        {
            _timeline = timeline;
        }
        public void Execute()
        {
            if (_timeline.State == _timeline.PlayingState)
            {
                _timeline?.UnityMusicPlayer?.Pause();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                _timeline?.UnityVideoPlayer?.Pause();
                _timeline.ChangeState(_timeline.PausedState);
            }
        }
    }
}