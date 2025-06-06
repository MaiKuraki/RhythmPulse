using System.Threading;
using CycloneGames.UIFramework;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Scene;
using UnityEngine;
using VContainer;

namespace RhythmPulse.UI
{
    public class UIWindowLobby : UIWindow
    {
        private IObjectResolver objectResolver;
        private ISceneManagementAPIGateway sceneManagementAPIGateway;

        [SerializeField] Transform GameModeSelectionTF;
        [SerializeField] Transform GameplayMapSelectionTF;

        private UIPageGameModeSelection uiPageGameModeSelection;
        private UIPageGameplayMapSelection uiPageGameplayMapSelection;
        private CancellationTokenSource cancelRebuildMapList;
        private bool IsDIInitialized = false;

        protected override void Awake()
        {
            base.Awake();

            uiPageGameModeSelection = GameModeSelectionTF.GetComponent<UIPageGameModeSelection>();
            uiPageGameplayMapSelection = GameplayMapSelectionTF.GetComponent<UIPageGameplayMapSelection>();
            RegisterElementsAfterDIInitialized(destroyCancellationToken).Forget();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            IsDIInitialized = false;
        }

        [Inject]
        void Construct(IObjectResolver objectResolver, ISceneManagementAPIGateway sceneManagementAPIGateway)
        {
            this.objectResolver = objectResolver;
            this.sceneManagementAPIGateway = sceneManagementAPIGateway;

            IsDIInitialized = true;
        }

        protected override void OnFinishedOpen()
        {
            base.OnFinishedOpen();

            EnterGameModeSelection();
        }

        private async UniTask RegisterElementsAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            objectResolver.Inject(uiPageGameModeSelection);
            objectResolver.Inject(uiPageGameplayMapSelection);
        }

        void EnterGameplay(Gameplay.GameplayData gameplayData)
        {
            sceneManagementAPIGateway.Push(SceneDefinitions.Gameplay, null, gameplayData, null);
        }

        public void EnterGameModeSelection()
        {
            GameplayMapSelectionTF.gameObject.SetActive(false);
            GameModeSelectionTF.gameObject.SetActive(true);

            uiPageGameModeSelection.EnterTraditionalBeatsGame -= EnterTraditionalBeatsGame;
            uiPageGameModeSelection.EnterTraditionalBeatsGame += EnterTraditionalBeatsGame;

            uiPageGameModeSelection.EnterDanceGame -= EnterJustDanceGame;
            uiPageGameModeSelection.EnterDanceGame += EnterJustDanceGame;
        }

        private void EnterMusicSelection(string gameModeType)
        {
            GameModeSelectionTF.gameObject.SetActive(false);
            GameplayMapSelectionTF.gameObject.SetActive(true);

            uiPageGameplayMapSelection.ClickBackEvent -= EnterGameModeSelection;
            uiPageGameplayMapSelection.ClickBackEvent += EnterGameModeSelection;
            uiPageGameplayMapSelection.EnterGameplayEvent -= EnterGameplay;
            uiPageGameplayMapSelection.EnterGameplayEvent += EnterGameplay;

            cancelRebuildMapList?.Cancel();
            cancelRebuildMapList?.Dispose();
            cancelRebuildMapList = null;
            cancelRebuildMapList = new CancellationTokenSource();
            uiPageGameplayMapSelection.RebuildMapListAfterDIInitialized(gameModeType, cancelRebuildMapList.Token).Forget();
        }

        private void EnterTraditionalBeatsGame()
        {
            EnterMusicSelection(string.Empty);
        }

        private void EnterJustDanceGame()
        {
            EnterMusicSelection(RhythmPulse.GameplayData.Runtime.BeatMapTypeConstant.JustDance);
        }
    }
}