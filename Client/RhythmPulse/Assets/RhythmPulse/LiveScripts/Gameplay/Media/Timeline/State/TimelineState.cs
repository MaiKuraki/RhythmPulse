namespace RhythmPulse.Gameplay.Media
{
    public interface ITimelineState
    {
        void OnEnter();
        void OnUpdate();
        void OnExit();
    }

    public abstract class TimelineState : ITimelineState
    {
        protected Timeline _timeline { get; private set; }
        public TimelineState(Timeline timeline) { _timeline = timeline; }
        public abstract void OnEnter();
        public abstract void OnExit();
        public virtual void OnUpdate() { }
    }
}