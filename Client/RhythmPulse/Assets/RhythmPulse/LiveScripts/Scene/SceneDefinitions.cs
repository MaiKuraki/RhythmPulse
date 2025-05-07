using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.AddressableAssets;

namespace RhythmPulse.Scene
{
    public class SceneDefinitions
    {
        public static ISceneIdentifier Transition { get; }
        public static ISceneIdentifier Splash { get; } = new AddressableSceneIdentifier("Assets/RhythmPulse/LiveContent/Scenes/Scene_Splash.unity");
    }
}