using System.Threading;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Scene;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.UI
{
    public class UIWindowLobby : UIWindow
    {
        private const string DEBUG_FLAG = "[UIWindowLobby]";
        [Inject] private IObjectResolver objectResolver;
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

        protected override void OnFinishedOpen()
        {
            base.OnFinishedOpen();

            EnterGameModeSelection();
        }

        // Cached scene resolver to avoid repeated lookups (0GC after initial cache)
        private LifetimeScope cachedSceneScope;
        private IObjectResolver cachedSceneResolver;
        private int cachedSceneHandle = -1;

        private IObjectResolver GetSceneResolver()
        {
            if (cachedSceneResolver != null && cachedSceneScope != null)
            {
                if (cachedSceneScope.Container != null &&
                    cachedSceneScope.gameObject.scene.isLoaded &&
                    cachedSceneScope.gameObject.scene.handle == cachedSceneHandle)
                {
                    return cachedSceneResolver;
                }

                cachedSceneScope = null;
                cachedSceneResolver = null;
                cachedSceneHandle = -1;
            }

            var allLifetimeScopes = Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < allLifetimeScopes.Length; i++)
            {
                var scope = allLifetimeScopes[i];

                if (scope.Container == null || !scope.gameObject.scene.isLoaded)
                    continue;

                if (scope.IsRoot)
                    continue;

                if (scope is ProjectSharedLifetimeScope)
                    continue;

                if (scope.Parent != null)
                {
                    // Cache for future use
                    cachedSceneScope = scope;
                    cachedSceneResolver = scope.Container;
                    cachedSceneHandle = scope.gameObject.scene.handle;
                    return cachedSceneResolver;
                }
            }

            // Fallback to injected resolver
            return objectResolver;
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
                CLogger.LogWarning($"{DEBUG_FLAG} Invalid GamplayData");
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
