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
                _timeline?.GameplayMusicPlayer?.Pause();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                _timeline?.GameplayVideoPlayer?.Pause();
                _timeline.ChangeState(_timeline.PausedState);
            }
        }
    }
}