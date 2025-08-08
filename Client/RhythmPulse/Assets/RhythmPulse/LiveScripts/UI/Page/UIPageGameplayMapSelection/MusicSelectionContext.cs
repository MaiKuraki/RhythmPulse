namespace RhythmPulse.UI
{
    /// <summary>
    /// Encapsulates the context for a music selection session, passed from the lobby.
    /// This defines the filtering criteria for which maps and difficulties are displayed.
    /// </summary>
    public class MusicSelectionContext
    {
        /// <summary>
        /// The type of beatmap to filter for.
        /// Can be a specific type (e.g., "JustDance") or empty/null for general modes.
        /// </summary>
        public string FilterBeatMapType { get; }

        public MusicSelectionContext(string filterBeatMapType)
        {
            FilterBeatMapType = filterBeatMapType;
        }
    }
}
