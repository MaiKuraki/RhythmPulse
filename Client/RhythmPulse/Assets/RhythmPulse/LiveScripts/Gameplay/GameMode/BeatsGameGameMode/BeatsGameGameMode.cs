namespace RhythmPulse.Gameplay
{
    public abstract class BeatsGameGameMode : IBeatsGameGameMode
    {
        public abstract void OnEnter();

        public virtual void OnExit() { }
    }
}
