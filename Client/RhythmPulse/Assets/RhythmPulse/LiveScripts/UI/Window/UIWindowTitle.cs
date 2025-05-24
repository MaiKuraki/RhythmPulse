using CycloneGames.Logger;
using CycloneGames.UIFramework;
using R3;
using RhythmPulse.APIGateway;
using RhythmPulse.Scene;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    public class UIWindowTitle : UIWindow
    {
        [Inject] private readonly ISceneManagementAPIGateway sceneManagementAPIGateway;
        [SerializeField] private Button buttonStart;

        protected override void Awake()
        {
            base.Awake();

            buttonStart.OnClickAsObservable().Subscribe(_ => ClickStart());
        }

        void ClickStart()
        {
            // CLogger.LogInfo("[UIWindowTitle] ClickStart");
            sceneManagementAPIGateway.Push(SceneDefinitions.Lobby);
        }
    }
}