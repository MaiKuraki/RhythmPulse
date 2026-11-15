using System.Threading;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Misc;
using RhythmPulse.Scene;
using UnityEngine;
using VContainer;
using CycloneGames.Logging;

namespace RhythmPulse.UI
{
    public class UIWindowLobby : UIWindow
    {
        [Inject] private ISceneManagementAPIGateway sceneManagementAPIGateway;

        [SerializeField] Transform GameModeSelectionTF;
        [SerializeField] Transform GameplayMapSelectionTF;

        private UIPageGameModeSelection uiPageGameModeSelection;
        private UIPageGameplayMapSelection uiPageGameplayMapSelection;
        private CancellationTokenSource cancelRebuildMapList;
        private bool isGameModeSelectionSubscribed = false;
        private bool isMusicSelectionSubscribed = false;

        protected override void Awake()
        {
            base.Awake();

            uiPageGameModeSelection = GameModeSelectionTF.GetComponent<UIPageGameModeSelection>();
            uiPageGameplayMapSelection = GameplayMapSelectionTF.GetComponent<UIPageGameplayMapSelection>();
        }

        protected override void OnDestroy()
        {
            // Clean up event subscriptions
            if (uiPageGameModeSelection != null)
            {
                uiPageGameModeSelection.EnterTraditionalBeatsGame -= EnterTraditionalBeatsGame;
                uiPageGameModeSelection.EnterDanceGame -= EnterJustDanceGame;
            }

            if (uiPageGameplayMapSelection != null)
            {
                uiPageGameplayMapSelection.ClickBackEvent -= EnterGameModeSelection;
                uiPageGameplayMapSelection.EnterGameplayEvent -= EnterGameplay;
            }

            cancelRebuildMapList?.Cancel();
            cancelRebuildMapList?.Dispose();

            base.OnDestroy();
        }

        void Start()
        {
            RegisterElementsAfterDIInitialized(destroyCancellationToken);
        }

        protected override void OnOpened()
        {
            base.OnOpened();

            EnterGameModeSelection();
        }

        // Resolves the active scene scope; shared with the spawner and the window binder so the
        // "which container injects this" rule lives in exactly one place.
        [Inject] private readonly SceneScopeResolver sceneScopeResolver;

        private IObjectResolver GetSceneResolver()
        {
            return sceneScopeResolver.Resolve();
        }

        private void RegisterElementsAfterDIInitialized(CancellationToken cancellationToken)
        {
            // Child components are part of the prefab and may need manual injection
            // Use scene resolver to ensure they can access scene-scoped dependencies (e.g., ITimeline)
            var sceneResolver = GetSceneResolver();
            sceneResolver.Inject(uiPageGameModeSelection);
            sceneResolver.Inject(uiPageGameplayMapSelection);
        }

        void EnterGameplay(Gameplay.GameplayData gameplayData)
        {
            if (gameplayData.IsVliad)
            {
                sceneManagementAPIGateway.Push(SceneDefinitions.Gameplay, null, gameplayData, null);
            }
            else
            {
                RPLog.Channel.Warning("Invalid GamplayData");
            }
        }

        public void EnterGameModeSelection()
        {
            GameplayMapSelectionTF.gameObject.SetActive(false);
            GameModeSelectionTF.gameObject.SetActive(true);

            // Subscribe only once to avoid duplicate event handlers
            if (!isGameModeSelectionSubscribed)
            {
                uiPageGameModeSelection.EnterTraditionalBeatsGame += EnterTraditionalBeatsGame;
                uiPageGameModeSelection.EnterDanceGame += EnterJustDanceGame;
                isGameModeSelectionSubscribed = true;
            }
        }

        private void EnterMusicSelection(MusicSelectionContext context)
        {
            GameModeSelectionTF.gameObject.SetActive(false);
            GameplayMapSelectionTF.gameObject.SetActive(true);

            // Subscribe only once to avoid duplicate event handlers
            if (!isMusicSelectionSubscribed)
            {
                uiPageGameplayMapSelection.ClickBackEvent += EnterGameModeSelection;
                uiPageGameplayMapSelection.EnterGameplayEvent += EnterGameplay;
                isMusicSelectionSubscribed = true;
            }

            cancelRebuildMapList?.Cancel();
            cancelRebuildMapList?.Dispose();
            cancelRebuildMapList = new CancellationTokenSource();
            uiPageGameplayMapSelection.RebuildMapListAfterDIInitialized(context, cancelRebuildMapList.Token).Forget();
        }

        private void EnterTraditionalBeatsGame()
        {
            EnterMusicSelection(new MusicSelectionContext(string.Empty));
        }

        private void EnterJustDanceGame()
        {
            EnterMusicSelection(new MusicSelectionContext(RhythmPulse.GameplayData.Runtime.BeatMapTypeConstant.JustDance));
        }
    }
}
