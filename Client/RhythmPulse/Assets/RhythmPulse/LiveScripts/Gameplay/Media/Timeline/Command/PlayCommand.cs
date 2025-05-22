namespace RhythmPulse.Gameplay.Media
{
    public class PlayCommand : ITimelineCommand
    {
        private Timeline _timeline;
        public PlayCommand(Timeline timeline)
        {
            _timeline = timeline;
        }
        public void Execute()
        {
            if (_timeline.State != _timeline.PlayingState) 
            {
                _timeline?.GameplayMusicPlayer?.Play();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                _timeline?.GameplayVideoPlayer?.Play();
                _timeline.ChangeState(_timeline.PlayingState);
            }
        }
    }
}