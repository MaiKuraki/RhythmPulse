using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using R3;
using RhythmPulse.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    class UIPageGameplayMapSelection : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIPageGameplayMapSelection]";
        [SerializeField] RhythmPulse.UI.ScrollView scrollView = default;
        [SerializeField] Button enterMusicGameplayButton = default;
        [SerializeField] Button backButton = default;
        public Action EnterGameplayEvent;
        public Action ClickBackEvent;
        private IGameplayMapListManager gameplayMapListManager;
        private bool IsDIInitialized = false;
        private List<ItemData> items = new List<ItemData>();


        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay());
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack());
        }

        void Start()
        {
            InitializeMapListAfterDIInitialized(destroyCancellationToken).Forget();
        }

        [Inject]
        void Construct(IGameplayMapListManager gameplayMapListManager)
        {
            this.gameplayMapListManager = gameplayMapListManager;

            IsDIInitialized = true;
        }

        void OnDestroy()
        {
            IsDIInitialized = false;
        }

        async UniTask InitializeMapListAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            for (int i = 0; i < gameplayMapListManager.AvailableMaps.Count; i++)
            {
                items.Add(new ItemData(gameplayMapListManager.AvailableMaps[i].DisplayName));
            }

            scrollView.UpdateData(items);
            scrollView.SelectCell(0);
        }

        void EnterGameplay()
        {
            EnterGameplayEvent?.Invoke();
        }

        void ClickBack()
        {
            ClickBackEvent?.Invoke();
        }
    }
}
