namespace RhythmPulse.Media
{
    public class StopCommand : ITimelineCommand
    {
        private Timeline _timeline;
        public StopCommand(Timeline timeline)
        {
            _timeline = timeline;
        }
        public void Execute()
        {
            if (_timeline.State != _timeline.StoppedState)
            {
                _timeline?.UnityMusicPlayer?.Stop();
                // _timeline.AudioPlayer.SFXPauseEvent?.Invoke();
                _timeline?.UnityVideoPlayer?.Stop();
                _timeline.ChangeState(_timeline.StoppedState);
            }
        }
    }
}