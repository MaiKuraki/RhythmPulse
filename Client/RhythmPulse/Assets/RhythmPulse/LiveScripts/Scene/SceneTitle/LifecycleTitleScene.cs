using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.UI;
using VContainer;

namespace RhythmPulse.Scene
{
    public class LifecycleTitleScene : ISceneLifecycle
    {
        [Inject] private readonly IUIService uiService;
        [Inject] [Key("Addressables")] private readonly IAssetModule assetModule;
        [Inject] private readonly IUnityObjectSpawner objectSpawner;
        [Inject] private readonly IMainCameraService cameraService;
        [Inject] private readonly IAssetPathBuilderFactory  assetPathBuilderFactory;
        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnFinalize(ISceneDataWriter writer, IProgress<IProgressDataStore> progress,
            CancellationToken cancellationToken)
        {
            uiService.CloseUI(UIWindowName.Title);
            return UniTask.CompletedTask;
        }

        public async UniTask OnInitialize(ISceneDataReader reader, IProgress<IProgressDataStore> progress,
            CancellationToken cancellationToken)
        {
            var pkg = assetModule.GetPackage("DefaultPackage");
            uiService.OpenUI(UIWindowName.Title);
            await UpdateProgress(progress, cancellationToken);
        }

        private async UniTask UpdateProgress(IProgress<IProgressDataStore> progress,
            CancellationToken cancellationToken)
        {
            ProgressDataStore<LoadingProgressData> store = new();
            int fakeProgress = 0;
            int targetProgress = 100;
            int step = 4;
            while (fakeProgress < targetProgress)
            {
                fakeProgress += step;
                progress.Report(store.SetData(new LoadingProgressData(ELoadingState.Loading,
                    fakeProgress / (float)targetProgress, "Loading...")));
                await UniTask.Delay(30);
            }

            progress.Report(store.SetData(new LoadingProgressData(ELoadingState.Loaded, 1f, "Complete")));
            await UniTask.Delay(50);
            await UniTask.CompletedTask;
        }
    }
}