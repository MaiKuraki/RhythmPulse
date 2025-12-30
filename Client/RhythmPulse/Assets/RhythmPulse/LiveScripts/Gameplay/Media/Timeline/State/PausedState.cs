namespace RhythmPulse.Media
{
    public sealed class PausedState : TimelineState
    {
        public PausedState(Timeline timeline) : base(timeline) { }

        public override void OnEnter()
        {
            Timeline.RaisePausedPlay();
        }

        public override void OnExit() { }
    }
}