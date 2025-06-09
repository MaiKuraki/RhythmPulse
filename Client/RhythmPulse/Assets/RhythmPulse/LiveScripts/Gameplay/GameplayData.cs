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
        public MapInfo MapInfo;
        public string BeatMapType;
        public string BeatMapFileName;
    }
}