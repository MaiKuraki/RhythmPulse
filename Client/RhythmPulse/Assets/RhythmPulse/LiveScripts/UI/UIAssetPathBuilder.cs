using System;
using CycloneGames.Service;

namespace RhythmPulse.UI
{
    /// <summary> 
    /// A struct implementation of <see cref="IAssetPathBuilder"/> for UI assets. 
    /// </summary> 
    public struct UIAssetPathBuilder : IAssetPathBuilder
    {
        // The prefix for UI asset paths 
        private const string Prefix = "Assets/RhythmPulse/LiveContent/ScriptableObjects/UI/Window/";
        // The suffix for UI asset paths 
        private const string Suffix = ".asset";

        /// <summary> 
        /// Builds the full asset path for a UI window based on its name. 
        /// </summary> 
        /// <param name="UIWindowName">The name of the UI window.</param> 
        /// <returns>The full asset path for the UI window.</returns> 
        public string GetAssetPath(string UIWindowName)
        {
            // Calculate the total length of the resulting string 
            int length = Prefix.Length + UIWindowName.Length + Suffix.Length;
            // Allocate a character buffer on the stack 
            Span<char> buffer = stackalloc char[length];

            // Copy the prefix into the buffer 
            Prefix.AsSpan().CopyTo(buffer);
            // Copy the window name into the buffer after the prefix 
            UIWindowName.AsSpan().CopyTo(buffer.Slice(Prefix.Length));
            // Copy the suffix into the buffer after the window name 
            Suffix.AsSpan().CopyTo(buffer.Slice(Prefix.Length + UIWindowName.Length));

            // Convert the character buffer to a string 
            return new string(buffer);
        }
    }
}