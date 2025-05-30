using System;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using CycloneGames.Logger;

namespace RhythmPulse.GameplayData.Runtime
{
    /// <summary>
    /// Utility class for BeatMap operations, optimized for performance and reduced GC,
    /// suitable for Unity and older .NET profiles.
    /// ValidBeatMapTypes are automatically populated from BeatMapTypeConstant.
    /// StringBuilder capacity is dynamically calculated based on actual BeatMapType lengths.
    /// </summary>
    public static class BeatMapUtility
    {
        public static readonly string[] ValidBeatMapTypes;
        private static readonly StringBuilder FileNameBuilder; // Initialized in the static constructor

        // Constants for filename components (excluding beatMapType and version which are dynamic)
        private const int DifficultyLength = 2;
        private const int VersionMaxLength = 24; // As per current validation
        private const int SeparatorsLength = 2; // For the two '_' characters
        private const string FileExtension = ".yaml";
        private const int FileExtensionLength = 5; // Length of ".yaml"

        /// <summary>
        /// Static constructor to automatically populate ValidBeatMapTypes and initialize FileNameBuilder
        /// with a dynamically calculated capacity.
        /// This runs once when the BeatMapUtility class is first accessed.
        /// </summary>
        static BeatMapUtility()
        {
            // 1. Populate ValidBeatMapTypes from BeatMapTypeConstant
            var fields = typeof(BeatMapTypeConstant).GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.FlattenHierarchy);

            var typeList = new List<string>();
            foreach (var field in fields)
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                {
                    string value = field.GetRawConstantValue() as string;
                    if (value != null)
                    {
                        typeList.Add(value);
                    }
                }
            }
            ValidBeatMapTypes = typeList.ToArray();

            // 2. Determine the maximum length of a beatMapType string
            int maxBeatMapTypeActualLength = 0;
            if (ValidBeatMapTypes.Length > 0)
            {
                foreach (string typeName in ValidBeatMapTypes)
                {
                    if (typeName.Length > maxBeatMapTypeActualLength)
                    {
                        maxBeatMapTypeActualLength = typeName.Length;
                    }
                }
            }
            else
            {
                // Fallback if BeatMapTypeConstant is empty or has no valid string constants.
                // Use a reasonable default, e.g., if you expect types to generally be around this length.
                // If beatMapType can be 24-32, then a default of 32 might be safer here.
                maxBeatMapTypeActualLength = 32; // Default assumption if no types found
            }

            // 3. Calculate the total required capacity for FileNameBuilder
            int calculatedCapacity = maxBeatMapTypeActualLength +
                                     SeparatorsLength +
                                     DifficultyLength +
                                     VersionMaxLength + // Use max possible version length
                                     FileExtensionLength;

            // Add a small safety buffer and ensure a minimum practical size.
            // For example, add 10 characters as buffer, or ensure it's at least 64 or 80.
            calculatedCapacity = Math.Max(calculatedCapacity + 10, 80);

            // 4. Initialize FileNameBuilder with the calculated capacity
            FileNameBuilder = new StringBuilder(calculatedCapacity);
        }

        /// <summary>
        /// Generates a beatmap filename based on BeatMapType, Difficulty, and Version.
        /// Performs validation on input parameters. Optimized for performance and reduced GC.
        /// </summary>
        /// <param name="beatMapType">The type of beatmap. Should match a constant in BeatMapTypeConstant.</param>
        /// <param name="difficulty">The difficulty level (0-99).</param>
        /// <param name="version">A descriptive version string (alphanumeric, hyphens, max 24 chars).</param>
        /// <returns>The generated filename (e.g., "Mania_05_Default.yaml") or an empty string if validation fails.</returns>
        public static string GetBeatMapFile(string beatMapType, int difficulty, string version)
        {
            // Validate BeatMapType
            if (string.IsNullOrWhiteSpace(beatMapType))
            {
                CLogger.LogError($"[MapInfo.GetBeatMapFile] BeatMapType cannot be null or whitespace.");
                return string.Empty;
            }

            bool isValidBeatMapType = false;
            for (int i = 0; i < ValidBeatMapTypes.Length; i++)
            {
                // StringComparison.Ordinal is generally best for performance with non-linguistic strings.
                if (string.Equals(beatMapType, ValidBeatMapTypes[i], StringComparison.Ordinal))
                {
                    isValidBeatMapType = true;
                    break;
                }
            }
            if (!isValidBeatMapType)
            {
                CLogger.LogError($"[MapInfo.GetBeatMapFile] Invalid or unknown BeatMapType: '{beatMapType}'.");
                return string.Empty;
            }

            // Validate Difficulty
            if (difficulty < 0 || difficulty > 99)
            {
                CLogger.LogError($"[MapInfo.GetBeatMapFile] Invalid Difficulty value: {difficulty}. Must be between 0 and 99.");
                return string.Empty;
            }

            // Validate Version string
            if (string.IsNullOrWhiteSpace(version))
            {
                CLogger.LogError($"[MapInfo.GetBeatMapFile] Version string cannot be empty or whitespace.");
                return string.Empty;
            }
            // Ensure version string length does not exceed its defined max (VersionMaxLength)
            if (version.Length > VersionMaxLength)
            {
                CLogger.LogError($"[MapInfo.GetBeatMapFile] Version string '{version}' exceeds maximum length of {VersionMaxLength} characters.");
                return string.Empty;
            }

            for (int i = 0; i < version.Length; i++)
            {
                char c = version[i];
                bool isValidChar = (c >= 'a' && c <= 'z') ||
                                   (c >= 'A' && c <= 'Z') ||
                                   (c >= '0' && c <= '9') ||
                                   c == '-';
                if (!isValidChar)
                {
                    CLogger.LogError($"[MapInfo.GetBeatMapFile] Version string '{version}' contains invalid characters. Only alphanumeric characters and hyphens are allowed.");
                    return string.Empty;
                }
            }

            // WARNING: Access to FileNameBuilder is not thread-safe.
            // If called from multiple threads, ensure synchronization (e.g., lock).
            FileNameBuilder.Length = 0; // Clear the StringBuilder

            FileNameBuilder.Append(beatMapType);
            FileNameBuilder.Append('_');

            if (difficulty < 10)
            {
                FileNameBuilder.Append('0');
                FileNameBuilder.Append((char)('0' + difficulty));
            }
            else // difficulty is 10-99
            {
                FileNameBuilder.Append((char)('0' + (difficulty / 10)));
                FileNameBuilder.Append((char)('0' + (difficulty % 10)));
            }

            FileNameBuilder.Append('_');
            FileNameBuilder.Append(version);
            FileNameBuilder.Append(FileExtension);

            return FileNameBuilder.ToString();
        }
    }
}