using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
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
        [SerializeField] int confirmDelayMs = 200;
        [Space(20)]
        [Header("------ Difficulty Selection ------")]
        [SerializeField] TMP_Text Text_DifficultyName;
        [SerializeField] Button Btn_SelectLast;
        [SerializeField] Button Btn_SelectNext;

        public Action<Gameplay.GameplayData> EnterGameplayEvent;
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
        public Timeline PreviewVideoTimeline => (Timeline)timeline;
        private Gameplay.GameplayData gameplayData;
        private string BeatMapType = string.Empty;
        private StringBuilder previewAudioName = new StringBuilder();
        private StringBuilder previewVideoName = new StringBuilder();
        private List<int> difficultyList = new List<int>();
        private string INVLID_DIFFICULTY_FILE = "INVALID_CONFIG.yaml";

        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay(gameplayData));
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack());
            scrollView.OnSelectedEvent -= OnSelectItem;
            scrollView.OnSelectedEvent += OnSelectItem;
            AdjustConfirmDelayForHighPerformanceDevices();
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

            cancelForSelection?.Cancel();
            cancelForSelection?.Dispose();
            cancelForSelection = null;

            cancelForMediaInfoUpdate?.Cancel();
            cancelForMediaInfoUpdate?.Dispose();
            cancelForMediaInfoUpdate = null;
        }

        void OnDisable()
        {
            cancelForSelection?.Cancel();
            cancelForSelection?.Dispose();
            cancelForSelection = null;

            cancelForMediaInfoUpdate?.Cancel();
            cancelForMediaInfoUpdate?.Dispose();
            cancelForMediaInfoUpdate = null;
        }

        void Update()
        {
            if (!IsDIInitialized) return;

            if (PreviewVideoTimeline != null)
            {
                PreviewVideoTimeline?.Tick();   //  For AV-Sync
            }
        }

        private void AdjustConfirmDelayForHighPerformanceDevices()
        {
#if UNITY_STANDALONE
            confirmDelayMs = 0;
#endif
        }

        public async UniTask RebuildMapListAfterDIInitialized(string beatMapType, CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized /* && gameplayMapListManager.Initialized */, PlayerLoopTiming.Update, cancellationToken);

            this.BeatMapType = beatMapType;

            //  TODO: to be implemented, generate the map list.
            items.Clear();
            if (string.IsNullOrEmpty(beatMapType))
            {
                var allMaps = gameplayMapListManager.AvailableMaps;
                foreach (var mapInfo in allMaps)
                {
                    items.Add(new ItemData(mapInfo));
                }
            }
            else
            {
                //  TODO: now only JustDance mode
                var justDanceMaps = gameplayMapListManager.GetAvailableMapsByBeatMapType(BeatMapTypeConstant.JustDance);
                foreach (var mapInfo in justDanceMaps)
                {
                    items.Add(new ItemData(mapInfo));
                }
            }

            scrollView.UpdateData(items);
            scrollView.SelectCell(0);

            cancelForSelection?.Cancel();
            cancelForSelection?.Dispose();
            cancelForSelection = null;
            cancelForSelection = new CancellationTokenSource();
            scrollView.ForceUpdateSelectionAsync(0, cancelForSelection).Forget();
        }

        void EnterGameplay(Gameplay.GameplayData gameplayData)
        {
            EnterGameplayEvent?.Invoke(gameplayData);
        }

        void ClickBack()
        {
            cancelForSelection?.Cancel();
            cancelForSelection?.Dispose();
            cancelForSelection = null;
            timeline?.Stop();
            ClickBackEvent?.Invoke();
        }

        private void OnSelectItem(ItemData itemData)
        {
            Text_MapDisplayName?.SetText(itemData?.MapInfo.DisplayName);

            cancelForMediaInfoUpdate?.Cancel();
            cancelForMediaInfoUpdate?.Dispose();
            cancelForMediaInfoUpdate = null;
            cancelForMediaInfoUpdate = new CancellationTokenSource();
            bool bHasOverrideMedia = itemData.MapInfo.MediaOverrides != null
                                    && itemData.MapInfo.MediaOverrides.Count > 0
                                    && itemData.MapInfo.MediaOverrides.Exists(m => m.BeatMapType == this.BeatMapType);

            gameplayData = new Gameplay.GameplayData(itemData.MapInfo, this.BeatMapType, INVLID_DIFFICULTY_FILE);
            RefreshDifficulty(itemData.MapInfo, this.BeatMapType);

            UpdateMediaDataAsync(itemData, bHasOverrideMedia, cancelForMediaInfoUpdate).Forget();
        }

        private async UniTask UpdateMediaDataAsync(ItemData itemData, bool bHasOverrideMedia, CancellationTokenSource cancellationTokenSource)
        {
            await UniTask.Delay(confirmDelayMs, false, PlayerLoopTiming.Update, cancellationTokenSource.Token);
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) return;
            previewAudioName.Clear();
            if (bHasOverrideMedia) previewAudioName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewAudioPath(itemData.MapInfo, BeatMapType), UnityPathSource.AbsoluteOrFullUri));
            else previewAudioName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewAudioPath(itemData.MapInfo), UnityPathSource.AbsoluteOrFullUri));
            await audioLoadService.LoadAudioAsync(previewAudioName.ToString());
            CLogger.LogInfo($"{DEBUG_FLAG} UpdateMediaDataAsync {itemData.MapInfo.DisplayName}, Time: {Time.time}");
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) return;
            timeline?.Stop();
            musicPlayer?.InitializeMusicPlayer(previewAudioName.ToString(), true);
            bool isVideoPrepared = false;
            previewVideoName.Clear();
            if (bHasOverrideMedia) previewVideoName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewVideoPath(itemData.MapInfo, BeatMapType), UnityPathSource.AbsoluteOrFullUri));
            else previewVideoName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetPreviewVideoPath(itemData.MapInfo), UnityPathSource.AbsoluteOrFullUri));
            videoPlayer?.InitializeVideoPlayer(
                videoUrl: previewVideoName.ToString(),
                bLoop: true,
                OnPrepared: () => { isVideoPrepared = true; });
            await UniTask.WaitUntil(() => isVideoPrepared, PlayerLoopTiming.Update, cancellationTokenSource.Token);
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) return;

            if (rawImg_PreviewVideoScreen) rawImg_PreviewVideoScreen.texture = ((GameplayVideoPlayer)videoPlayer)?.CurrentVideoTexture;

            timeline?.Play();
        }

        private void RefreshDifficulty(MapInfo mapInfo, string beatMapType)
        {
            difficultyList.Clear();
            foreach (var beatMapInfo in mapInfo.BeatmapDifficultyFiles)
            {
                if (beatMapType == beatMapInfo.BeatMapType[0])
                {
                    difficultyList.Add(beatMapInfo.Difficulty);
                }
            }

            int defaultDifficulty = -1;
            if (difficultyList.Count >= 1)
            {
                defaultDifficulty = difficultyList[0];
                Text_DifficultyName?.SetText(difficultyList[0].ToString());
            }
            else
            {
                Text_DifficultyName?.SetText("N/A");
            }

            if (defaultDifficulty != -1)
            {
                var defaultBeatMapFile = BeatMapUtility.GetBeatMapFile(beatMapType, defaultDifficulty, version: "Default");
                gameplayData.UpdateBeatMapFile(defaultBeatMapFile);
            }
            else
            {
                gameplayData.UpdateBeatMapFile(INVLID_DIFFICULTY_FILE);
            }

            Btn_SelectLast.OnClickAsObservable().Subscribe(_ =>
            {
                if (difficultyList.Count <= 1) return;
                int currentIndex = difficultyList.IndexOf(int.Parse(Text_DifficultyName.text));
                int nextIndex = (currentIndex - 1 + difficultyList.Count) % difficultyList.Count;
                Text_DifficultyName.SetText(difficultyList[nextIndex].ToString());

                // TODO: implement BeatMapFileName 
                var beatMapFile = BeatMapUtility.GetBeatMapFile(beatMapType, difficultyList[nextIndex], version: "Default");
                gameplayData.UpdateBeatMapFile(beatMapFile);
            });
            Btn_SelectNext.OnClickAsObservable().Subscribe(_ =>
            {
                if (difficultyList.Count <= 1) return;
                int currentIndex = difficultyList.IndexOf(int.Parse(Text_DifficultyName.text));
                int nextIndex = (currentIndex + 1) % difficultyList.Count;
                Text_DifficultyName.SetText(difficultyList[nextIndex].ToString());

                // TODO: implement BeatMapFileName
                var beatMapFile = BeatMapUtility.GetBeatMapFile(beatMapType, difficultyList[nextIndex], version: "Default");
                gameplayData.UpdateBeatMapFile(beatMapFile);
            });
        }
    }
}
