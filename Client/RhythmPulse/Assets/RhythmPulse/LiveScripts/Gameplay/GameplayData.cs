using MackySoft.Navigathena.SceneManagement;
using RhythmPulse.GameplayData.Runtime;

namespace RhythmPulse.Gameplay
{
    public struct GameplayData : ISceneData
    {
        public bool IsVliad =>
                MapInfo.IsNotDefault
            && !string.IsNullOrEmpty(BeatMapType)
            && !string.IsNullOrEmpty(BeatMapFileName);
        public MapInfo MapInfo { get; private set; }
        public bool HasOverrideMedia { get; private set; }
        public string BeatMapType { get; private set; }
        public string BeatMapFileName { get; private set; }

        public GameplayData(MapInfo mapInfo, string beatMapType, string beatMapFileName)
        {
            MapInfo = mapInfo;
            BeatMapType = beatMapType;
            BeatMapFileName = beatMapFileName;
            HasOverrideMedia = mapInfo.MediaOverrides != null && mapInfo.MediaOverrides.Count > 0 && mapInfo.MediaOverrides.Exists(m => m.BeatMapType == beatMapType);
        }

        public void UpdateBeatMapFile(string beatMapFileName)
        {
            BeatMapFileName = beatMapFileName;
        }
    }
}