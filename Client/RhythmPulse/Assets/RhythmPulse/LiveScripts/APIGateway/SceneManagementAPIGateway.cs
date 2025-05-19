using MackySoft.Navigathena.SceneManagement;

namespace RhythmPulse.APIGateway
{
    public interface ISceneManagementAPIGateway
    {
        void Push(ISceneIdentifier sceneIdentifier);
    }
    public class SceneManagementAPIGateway : ISceneManagementAPIGateway
    {
        public void Push(ISceneIdentifier sceneIdentifier) => GlobalSceneNavigator.Instance.Push(sceneIdentifier);
    }
}

