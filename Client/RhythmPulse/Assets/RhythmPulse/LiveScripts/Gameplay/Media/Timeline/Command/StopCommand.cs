namespace RhythmPulse.Gameplay.Media
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
                _timeline.ChangeState(_timeline.StoppedState);
            }
        }
    }
}