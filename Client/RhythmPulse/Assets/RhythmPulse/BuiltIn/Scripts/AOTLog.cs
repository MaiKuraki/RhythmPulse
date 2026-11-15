using CycloneGames.Logging;

namespace RhythmPulse.AOT
{
    /// <summary>
    /// Logging facade for the <c>RhythmPulse.AOT</c> assembly.
    /// <para>
    /// Shares the game-wide <c>RhythmPulse</c> category with <c>RhythmPulse.RPLog</c>: the boot chain and
    /// the hot-updatable code are one product surface, and the source location attached to every record
    /// already separates them.
    /// </para>
    /// </summary>
    internal static class AOTLog
    {
        internal const string Category = "RhythmPulse";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
