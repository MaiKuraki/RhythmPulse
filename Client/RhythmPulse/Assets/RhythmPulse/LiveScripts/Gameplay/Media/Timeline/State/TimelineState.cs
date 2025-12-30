namespace RhythmPulse.Media
{
    public interface ITimelineState
    {
        void OnEnter();
        void OnUpdate();
        void OnExit();
    }

    public abstract class TimelineState : ITimelineState
    {
        protected readonly Timeline Timeline;

        protected TimelineState(Timeline timeline)
        {
            Timeline = timeline;
        }

        public abstract void OnEnter();
        public abstract void OnExit();
        public virtual void OnUpdate() { }
    }
}