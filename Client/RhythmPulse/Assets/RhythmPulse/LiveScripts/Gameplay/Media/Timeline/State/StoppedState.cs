using CycloneGames.Logger;

namespace RhythmPulse.Gameplay.Media
{
    public class StoppedState : TimelineState
    {
        public StoppedState(Timeline timeline) : base(timeline) { }

        public override void OnEnter()
        {
            CLogger.LogInfo($"[Timeline] Enter Stopped State");

            _timeline.SetPlaybackTimeMSec(long.MinValue);     
            _timeline.OnStoppedPlayAction?.Invoke();
        }

        public override void OnExit()
        {
            CLogger.LogInfo("[Timeline] Exit Stopped State");
        }
    }
}