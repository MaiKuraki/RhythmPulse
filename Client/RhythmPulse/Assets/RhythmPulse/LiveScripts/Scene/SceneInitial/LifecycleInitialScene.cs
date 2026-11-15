using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Audio;
using RhythmPulse.Misc;
using RhythmPulse.UI;
using VContainer;

namespace RhythmPulse.Scene
{
    public class LifecycleInitialScene : ISceneLifecycle
    {
        [Inject] UIServiceHost uiServiceHost;
        [Inject][Key("Addressables")] IAssetModule assetModule;
        [Inject] AddressableResolverForDontDestroy assetResolver;
        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public async UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            var pkg = assetModule.GetPackage("DefaultPackage");
            await assetResolver.InitializeAsync(pkg);

            // AudioManager is instantiated by AddressableResolverForDontDestroy above,
            // so Instance is now available. Provide IAssetModule so it can finish initializing.
            AudioManager.Instance?.SetAssetModule(assetModule);

            uiServiceHost.EnsureInitialized(pkg);

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