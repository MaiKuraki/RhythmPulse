namespace RhythmPulse.Media
{
    public interface ITimeline
    {
        void Play();
        void Pause();
        void Stop();
        void Resume();
        ITimelineState State { get; }
    }
}