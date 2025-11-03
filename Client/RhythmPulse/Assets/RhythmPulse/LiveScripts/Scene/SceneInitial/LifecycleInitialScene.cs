using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Misc;
using VContainer;

namespace RhythmPulse.Scene
{
    public class LifecycleInitialScene : ISceneLifecycle
    {
        [Inject] IUIService uiService;
        [Inject] IAssetPathBuilderFactory assetPathBuilderFactory;
        [Inject] IUnityObjectSpawner unityObjectSpawner;
        [Inject] IMainCameraService mainCameraService;
        [Inject][Key("Addressables")] IAssetModule assetModule;
        [Inject] AddressableResolverForDontDestroy assetResolver;
        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public async UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            var pkg = assetModule.GetPackage("DefaultPackage");
            await assetResolver.InitializeAsync(assetModule);
            uiService.Initialize(assetPathBuilderFactory, unityObjectSpawner, mainCameraService, pkg);
            await GlobalSceneNavigator.Instance.Push(SceneDefinitions.Splash);
        }

        public UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnFinalize(ISceneDataWriter writer, System.IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public async UniTask OnInitialize(ISceneDataReader reader, System.IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {

            await UniTask.CompletedTask;
        }
    }
}