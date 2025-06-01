using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
using CycloneGames.Service;
using CycloneGames.UIFramework;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using R3;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay;
using RhythmPulse.Gameplay.Media;
using RhythmPulse.GameplayData.Runtime;
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
        [SerializeField] RawImage rawImg_PreviewVideoScreen;

        public Action EnterGameplayEvent;
        public Action ClickBackEvent;
        private IGameplayMapListManager gameplayMapListManager;
        private IUIService uiService;
        private IAudioLoadService audioLoadService;
        private IGameplayMapStorage mapStorage;
        private ITimeline timeline;
        private IGameplayMusicPlayer musicPlayer;
        private IGameplayVideoPlayer videoPlayer;
        private bool IsDIInitialized = false;
        private List<ItemData> items = new List<ItemData>();
        private CancellationTokenSource cancelForSelection;
        private CancellationTokenSource cancelForMediaInfoUpdate;
        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay());
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack());
            scrollView.OnSelectedEvent -= OnSelectItem;
            scrollView.OnSelectedEvent += OnSelectItem;
        }

        void Start()
        {
            InitializeMapListAfterDIInitialized(destroyCancellationToken).Forget();
        }

        [Inject]
        void Construct(
            IUIService uiService,
            IAudioLoadService audioLoadService,
            IGameplayMapStorage mapStorage,
            IGameplayMapListManager gameplayMapListManager,
            ITimeline timeline,
            IGameplayMusicPlayer musicPlayer,
            IGameplayVideoPlayer videoPlayer)
        {
            this.uiService = uiService;
            this.audioLoadService = audioLoadService;
            this.mapStorage = mapStorage;
            this.gameplayMapListManager = gameplayMapListManager;
            this.timeline = timeline;
            this.musicPlayer = musicPlayer;
            this.videoPlayer = videoPlayer;

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

        private void OnSelectItem(ItemData itemData)
        {
            Text_MapDisplayName.SetText(itemData.MapInfo.DisplayName);

            if (cancelForMediaInfoUpdate != null && cancelForMediaInfoUpdate.IsCancellationRequested)
            {
                cancelForMediaInfoUpdate.Cancel();
                cancelForMediaInfoUpdate.Dispose();
            }
            cancelForMediaInfoUpdate = new CancellationTokenSource();
            UpdateMediaDataAsync(itemData, cancelForMediaInfoUpdate).Forget();
        }

        private async UniTask UpdateMediaDataAsync(ItemData itemData, CancellationTokenSource cancellationTokenSource)
        {
            await audioLoadService.LoadAudioAsync(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewAudioPath(itemData.MapInfo), UnityPathSource.AbsoluteOrFullUri));

            timeline.Stop();
            musicPlayer.InitializeMusicPlayer(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewAudioPath(itemData.MapInfo), UnityPathSource.AbsoluteOrFullUri));
            videoPlayer.InitializeVideoPlayer(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewVideoPath(itemData.MapInfo), UnityPathSource.AbsoluteOrFullUri));

            rawImg_PreviewVideoScreen.texture = ((GameplayVideoPlayer)videoPlayer).VideoTexture;

            timeline.Play();
        }
    }
}
