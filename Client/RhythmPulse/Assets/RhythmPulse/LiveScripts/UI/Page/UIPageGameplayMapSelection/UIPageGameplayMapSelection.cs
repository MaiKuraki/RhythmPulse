using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using CycloneGames.Service;
using CycloneGames.UIFramework;
using Cysharp.Threading.Tasks;
using R3;
using RhythmPulse.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    class UIPageGameplayMapSelection : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIPageGameplayMapSelection]";
        [SerializeField] RhythmPulse.UI.MapListScrollView scrollView = default;
        [SerializeField] Button enterMusicGameplayButton = default;
        [SerializeField] Button backButton = default;
        [SerializeField] TMP_Text Text_MapDisplayName;

        public Action EnterGameplayEvent;
        public Action ClickBackEvent;
        private IGameplayMapListManager gameplayMapListManager;
        private IUIService uiService;
        private bool IsDIInitialized = false;
        private List<ItemData> items = new List<ItemData>();
        private CancellationTokenSource cancelForSelection;

        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay());
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack());
            scrollView.OnUpdateMapDisplayName -= UpdateMapDisplayName;
            scrollView.OnUpdateMapDisplayName += UpdateMapDisplayName;
        }

        void Start()
        {
            InitializeMapListAfterDIInitialized(destroyCancellationToken).Forget();
        }

        [Inject]
        void Construct(IUIService uiService, IGameplayMapListManager gameplayMapListManager)
        {
            this.uiService = uiService;
            this.gameplayMapListManager = gameplayMapListManager;

            scrollView.SetCellInterval(1 / (uiService.GetRootCanvasSize().Item2 / 140.0f));

            IsDIInitialized = true;
        }

        void OnDestroy()
        {
            IsDIInitialized = false;
        }

        void OnEnable()
        {
            if (IsDIInitialized)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} OnEnable");

                if (cancelForSelection != null)
                {
                    cancelForSelection.Cancel();
                    cancelForSelection.Dispose();
                }
                cancelForSelection = new CancellationTokenSource();
                scrollView.ForceUpdateSelectionAsync(0, cancelForSelection).Forget();
            }
        }

        async UniTask InitializeMapListAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized /* && gameplayMapListManager.Initialized */, PlayerLoopTiming.Update, cancellationToken);

            for (int i = 0; i < gameplayMapListManager.AvailableMaps.Count; i++)
            {
                items.Add(new ItemData(gameplayMapListManager.AvailableMaps[i]));
            }

            scrollView.UpdateData(items);
            scrollView.SelectCell(0);

            if (cancelForSelection != null)
            {
                cancelForSelection.Cancel();
                cancelForSelection.Dispose();
            }
            cancelForSelection = new CancellationTokenSource();
            scrollView.ForceUpdateSelectionAsync(0, cancelForSelection).Forget();
        }

        void EnterGameplay()
        {
            EnterGameplayEvent?.Invoke();
        }

        void ClickBack()
        {
            ClickBackEvent?.Invoke();
        }

        private void UpdateMapDisplayName(string displayName)
        {
            Text_MapDisplayName.SetText(displayName);
        }
    }
}
