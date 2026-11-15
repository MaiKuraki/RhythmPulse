using CycloneGames.Logging;

namespace RhythmPulse.Editor
{
    /// <summary>
    /// Assembly-local logging facade for <c>RhythmPulse.EditorTools</c>.
    /// <para>
    /// This assembly exists because <c>CycloneGames.Logging.Core</c> is not auto-referenced: scripts that
    /// live in the predefined <c>Assembly-CSharp-Editor</c> cannot see <see cref="LogChannel"/>, so any
    /// editor tool that emits records needs its own asmdef that references the contract explicitly.
    /// </para>
    /// </summary>
    internal static class EditorToolsLog
    {
        internal const string Category = "RhythmPulse.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
