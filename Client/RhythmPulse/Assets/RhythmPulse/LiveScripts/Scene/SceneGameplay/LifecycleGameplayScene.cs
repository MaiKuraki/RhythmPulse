using System;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
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
        public async UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            // Close UI windows asynchronously and wait for them to complete
            // This prevents duplicate close operations during scene transitions
            try
            {
                await uiService.CloseUIAsync(UIWindowName.GameplayHUDBeatsGame, cancellationToken);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LifecycleGameplayScene] Error closing {UIWindowName.GameplayHUDBeatsGame}: {ex.Message}");
            }

            try
            {
                await uiService.CloseUIAsync(UIWindowName.UIWindowGameplayResult, cancellationToken);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LifecycleGameplayScene] Error closing {UIWindowName.UIWindowGameplayResult}: {ex.Message}");
            }

            cancelLoadGameplayMedias.Cancel();
            cancelLoadGameplayMedias.Dispose();
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