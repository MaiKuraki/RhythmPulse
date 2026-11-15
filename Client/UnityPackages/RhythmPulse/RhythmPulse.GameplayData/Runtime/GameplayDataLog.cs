using CycloneGames.Logging;

namespace RhythmPulse.GameplayData.Runtime
{
    /// <summary>
    /// Assembly-local logging facade for <c>RhythmPulse.GameplayData.Runtime</c>.
    /// </summary>
    internal static class GameplayDataLog
    {
        internal const string Category = "RhythmPulse.GameplayData";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
