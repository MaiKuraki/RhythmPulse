using CycloneGames.IO.Unity;
using CycloneGames.Logging;

namespace RhythmPulse
{
    /// <summary>
    /// UnityWebRequest URI helper built on <see cref="UnityFileUri"/> (CycloneGames.IO).
    /// The IO package throws on invalid input; gameplay call sites pass best-effort paths, so this
    /// wrapper keeps the previous contract: return <c>null</c> and log a warning instead of throwing.
    /// </summary>
    public static class UnityPathUtility
    {
        private const string DEBUG_FLAG = "[UnityPathUtility]";

        /// <summary>
        /// Builds a platform-correct URI for use with UnityWebRequest.
        /// </summary>
        /// <param name="path">
        /// Relative path when <paramref name="location"/> is StreamingAssets or PersistentData,
        /// otherwise an absolute file path or a fully formatted URI.
        /// </param>
        /// <param name="location">Where the path is anchored.</param>
        /// <returns>The URI, or <c>null</c> when the input cannot be resolved.</returns>
        public static string GetUnityWebRequestUri(string path, UnityFileLocation location)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (UnityFileUri.TryCreate(path, location, out string uri, out UnityFileUriError error))
            {
                return uri;
            }

            RpLog.Channel.Warning($"{DEBUG_FLAG} Cannot build URI for '{path}' ({location}): {error}");
            return null;
        }
    }
}
