namespace RhythmPulse.Gameplay.Media
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
                // _timeline.AudioPlayer.MusicPause();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                // _timeline.VideoPlayer?.Pause();
                _timeline.ChangeState(_timeline.PausedState);
            }
        }
    }
}