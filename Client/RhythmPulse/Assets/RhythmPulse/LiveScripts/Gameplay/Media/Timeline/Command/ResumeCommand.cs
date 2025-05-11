namespace RhythmPulse.Gameplay.Media
{
    public class ResumeCommand : ITimelineCommand
    {
        private Timeline _timeline;
        public ResumeCommand(Timeline timeline) { _timeline = timeline; }
        public void Execute()
        {
            if (_timeline.State == _timeline.PausedState)
            {
                _timeline.ChangeState(_timeline.PlayingState);
            }
        }
    }
}