using System.Collections.Generic;
using VYaml.Annotations;

namespace RhythmPulse.GameplayData.Runtime
{
    [System.Serializable]
    [YamlObject]
    public partial struct MapInfo
    {
        [YamlMember(Order = 0)]
        public string UniqueID { get; set; }
        [YamlMember(Order = 1)]
        public string LastModifiedTime { get; set; }

        [YamlMember(Order = 2)]
        public string DisplayName { get; set; }

        [YamlMember(Order = 3)]
        public string AudioFile { get; set; }

        [YamlMember(Order = 4)]
        public string VideoFile { get; set; }

        [YamlMember(Order = 5)]
        public string PreviewAudioFile { get; set; }

        [YamlMember(Order = 6)]
        public string PreviewVideoFile { get; set; }

        [YamlMember(Order = 7)]
        public string[] InGameBackgroundPictures { get; set; }

        [YamlMember(Order = 8)]
        public string Vocalist { get; set; }

        [YamlMember(Order = 9)]
        public string Composer { get; set; }

        [YamlMember(Order = 10)]
        public string Arranger { get; set; }

        [YamlMember(Order = 11)]
        public string Lyricist { get; set; }

        [YamlMember(Order = 12)]
        public string BeatmapAuthor { get; set; }

        [YamlMember(Order = 13)]
        public List<BeatMapInfo> BeatmapDifficultyFiles { get; set; }
        
        [YamlMember(Order = 14)]
        public List<BeatMapTypeMediaOverride> MediaOverrides { get; set; }
    }

    [System.Serializable]
    [YamlObject]
    public partial struct BeatMapInfo
    {
        [YamlMember(Order = 0)]
        public string MD5 { get; set; }

        [YamlMember(Order = 1)]
        public string[] BeatMapType { get; set; }

        [YamlMember(Order = 2)]
        public int Difficulty { get; set; }

        [YamlMember(Order = 3)]
        public string Version { get; set; }
    }

    [System.Serializable]
    [YamlObject]
    public partial struct BeatMapTypeMediaOverride
    {
        [YamlMember(Order = 0)]
        public string BeatMapType { get; set; } // The mode to override, e.g., "JustDance"

        // These are optional. If a value is empty, the system will use the default from MapInfo.
        [YamlMember(Order = 1)]
        public string AudioFile { get; set; }

        [YamlMember(Order = 2)]
        public string VideoFile { get; set; }

        [YamlMember(Order = 3)]
        public string PreviewAudioFile { get; set; }

        [YamlMember(Order = 4)]
        public string PreviewVideoFile { get; set; }
    }

    public static class BeatMapTypeConstant
    {
        public const string Mania = "Mania";
        public const string OSU = "OSU";
        public const string JustDance = "JustDance";
        public const string BeatSaber = "BeatSaber";
        public const string Taiko = "Taiko";
        public const string Combined = "Combined";
    }
}