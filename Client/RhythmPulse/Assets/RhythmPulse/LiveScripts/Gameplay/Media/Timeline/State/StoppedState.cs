namespace RhythmPulse.Media
{
    public sealed class StoppedState : TimelineState
    {
        public StoppedState(Timeline timeline) : base(timeline) { }

        public override void OnEnter()
        {
            Timeline.SetPlaybackTimeMSec(0);
            Timeline.RaiseStoppedPlay();
        }

        public override void OnExit() { }
    }
}