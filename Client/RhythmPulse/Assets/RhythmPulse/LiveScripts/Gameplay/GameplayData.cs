using MackySoft.Navigathena.SceneManagement;
using RhythmPulse.GameplayData.Runtime;

namespace RhythmPulse.Gameplay
{
    public struct GameplayData : ISceneData
    {
        public MapInfo MapInfo;
        public string BeatMapType;
        public string BeatMapFileName;
    }
}