using CycloneGames.Logging;

namespace RhythmPulse
{
    /// <summary>
    /// Assembly-local logging facade for <c>RhythmPulse.HotUpdate</c>.
    /// Every producer in this assembly emits through this single category so the host can
    /// filter, redirect, or silence game code without touching call sites.
    /// </summary>
    internal static class RpLog
    {
        internal const string Category = "RhythmPulse.HotUpdate";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
