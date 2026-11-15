using CycloneGames.Logging;

namespace RhythmPulse.GameplayData.Editor
{
    /// <summary>
    /// Assembly-local logging facade for <c>RhythmPulse.GameplayData.Editor</c>.
    /// </summary>
    internal static class GameplayDataEditorLog
    {
        internal const string Category = "RhythmPulse.GameplayData.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
    }
}
