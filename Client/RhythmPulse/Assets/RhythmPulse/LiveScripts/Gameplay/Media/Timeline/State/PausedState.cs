using CycloneGames.Logger;

namespace RhythmPulse.Gameplay.Media
{    
    public class PausedState : TimelineState
    {
        public PausedState(Timeline timeline) : base(timeline) { }

        public override void OnEnter()
        {
            CLogger.LogInfo("[Timeline] Enter Paused State");
            
            _timeline.OnPausedPlayAction?.Invoke();
        }

        public override void OnExit()
        {
            CLogger.LogInfo("[Timeline] Exit Paused State");
        }
    }
}