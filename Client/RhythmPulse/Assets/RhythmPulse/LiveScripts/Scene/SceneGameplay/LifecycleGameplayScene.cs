using System;
using System.Threading;
using CycloneGames.UIFramework;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Gameplay;
using RhythmPulse.UI;
using VContainer;

namespace RhythmPulse.Scene
{
    public class LifecycleGameplayScene : ISceneLifecycle
    {
        [Inject] private readonly IUIService uiService;
        [Inject] private readonly GameplayManager gameplayManager;
        private CancellationTokenSource cancelLoadGameplayMedias = new CancellationTokenSource();
        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            Gameplay.GameplayData musicGameplayData = reader.Read<Gameplay.GameplayData>();
            if (cancelLoadGameplayMedias != null)
            {
                cancelLoadGameplayMedias.Cancel();
                cancelLoadGameplayMedias.Dispose();
            }
            cancelLoadGameplayMedias = new CancellationTokenSource();
            gameplayManager.InitializeMedias(musicGameplayData, cancelLoadGameplayMedias).Forget();
            uiService.OpenUI(UIWindowName.GameplayHUDBeatsGame);
            return UniTask.CompletedTask;
        }
        public UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            uiService.CloseUI(UIWindowName.GameplayHUDBeatsGame);
            uiService.CloseUI(UIWindowName.UIWindowGameplayResult);
            cancelLoadGameplayMedias.Cancel();
            cancelLoadGameplayMedias.Dispose();
            return UniTask.CompletedTask;
        }

        public UniTask OnFinalize(ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnInitialize(ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}