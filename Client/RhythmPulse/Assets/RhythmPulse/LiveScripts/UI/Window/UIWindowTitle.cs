using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using R3;
using RhythmPulse.APIGateway;
using RhythmPulse.Scene;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using CycloneGames.Logging;

namespace RhythmPulse.UI
{
    public class UIWindowTitle : UIWindow
    {
        [Inject][Key("Addressables")] private IAssetModule assetModule;
        [Inject] private readonly ISceneManagementAPIGateway sceneManagementAPIGateway;
        [SerializeField] private Button buttonStart;
        [SerializeField] private AppVersionInfo appVersionInfo;

        protected override void Awake()
        {
            base.Awake();

            buttonStart.OnClickAsObservable().Subscribe(_ => ClickStart());
        }

        void Start()
        {
            appVersionInfo?.UpdateVersionDisplayEvent?.Invoke(assetModule)
                .AttachExternalCancellation(this.GetCancellationTokenOnDestroy())
                .Forget();
        }

        void ClickStart()
        {
            // RPLog.Channel.Info("ClickStart");
            sceneManagementAPIGateway.Push(SceneDefinitions.Lobby);
        }
    }
}