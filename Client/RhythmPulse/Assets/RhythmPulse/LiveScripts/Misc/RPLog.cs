using CycloneGames.Logging;

namespace RhythmPulse
{
    /// <summary>
    /// Process-wide logging facade for the <c>RhythmPulse.HotUpdate</c> assembly.
    /// <para>
    /// Every producer in the assembly emits through this single category so the host can filter,
    /// redirect, or silence game code without touching call sites. The channel resolves the ambient
    /// writer on each call, so the backend installed by <c>CycloneGames.Logging.Unity</c> can be created,
    /// replaced, or torn down at any time — including after this type has been initialized.
    /// </para>
    /// <para>
    /// Naming follows the .NET convention for two-letter acronyms (all caps: <c>UI</c>, <c>IO</c>, <c>RP</c>)
    /// and the framework's <c>{Module}Log</c> facade shape.
    /// </para>
    /// </summary>
    internal static class RPLog
    {
        /// <summary>
        /// Filter key used by logging settings and by the Unity console bridge. Categories are matched
        /// exactly (case-insensitive), so this is an independent key from <c>RhythmPulse.Editor</c> and
        /// <c>RhythmPulse.GameplayData</c>. Keep it stable: renaming it silently drops any category
        /// filter the host configured.
        /// </summary>
        internal const string Category = "RhythmPulse";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
