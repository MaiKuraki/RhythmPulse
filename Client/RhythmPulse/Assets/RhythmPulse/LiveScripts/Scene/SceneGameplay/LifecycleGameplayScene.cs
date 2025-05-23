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
        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            uiService.OpenUI(UIWindowName.GameplayHUDBeatsGame);
            return UniTask.CompletedTask;
        }
        public UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnFinalize(ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            uiService.CloseUI(UIWindowName.GameplayHUDBeatsGame);
            return UniTask.CompletedTask;
        }

        public UniTask OnInitialize(ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {   
            return UniTask.CompletedTask;
        }
    }
}