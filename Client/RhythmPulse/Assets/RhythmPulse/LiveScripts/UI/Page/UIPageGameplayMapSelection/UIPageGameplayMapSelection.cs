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
    /// <summary>
    /// Manages the UI page for selecting a gameplay map, handling map lists,
    /// difficulty selection, and media previews.
    /// </summary>
    public class UIPageGameplayMapSelection : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIPageGameplayMapSelection]";
        
        [Header("Component References")]
        [SerializeField] private RhythmPulse.UI.MapListScrollView scrollView = default;
        [SerializeField] private Button enterMusicGameplayButton = default;
        [SerializeField] private Button backButton = default;
        [SerializeField] private TMP_Text Text_MapDisplayName;
        [SerializeField] private RawImage rawImg_PreviewVideoScreen;
        
        [Header("Difficulty Selection")]
        [SerializeField] private TMP_Text Text_DifficultyName;
        [SerializeField] private Button Btn_SelectLast;
        [SerializeField] private Button Btn_SelectNext;

        [Header("Configuration")]
        [SerializeField] private int confirmDelayMs = 200;

        public Action<Gameplay.GameplayData> EnterGameplayEvent;
        public Action ClickBackEvent;
        public Timeline PreviewMediaTimeline => timeline as Timeline;

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
        private Gameplay.GameplayData gameplayData;
        private string BeatMapType = string.Empty;
        private StringBuilder previewAudioName = new StringBuilder();
        private StringBuilder previewVideoName = new StringBuilder();
        
        // State for the currently selected map's difficulties
        private List<BeatMapInfo> difficultyFilesForCurrentMode = new List<BeatMapInfo>();
        private int currentDifficultyIndex = -1;
        
        private const string INVLID_DIFFICULTY_FILE = "INVALID_CONFIG.yaml";
        
        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay());
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack());
            
            scrollView.OnSelectedEvent -= OnSelectItem;
            scrollView.OnSelectedEvent += OnSelectItem;

            // PERFORMANCE: Subscribe to difficulty change buttons only once.
            // The handlers will use class state to determine behavior.
            Btn_SelectLast.OnClickAsObservable().Subscribe(_ => ChangeDifficulty(-1)).AddTo(this);
            Btn_SelectNext.OnClickAsObservable().Subscribe(_ => ChangeDifficulty(1)).AddTo(this);
            
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
            
            // AV-Sync tick for media playback.
            PreviewMediaTimeline?.Tick();
        }

        private void AdjustConfirmDelayForHighPerformanceDevices()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            confirmDelayMs = 0;
#endif
        }

        public async UniTask RebuildMapListAfterDIInitialized(string beatMapType, CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            this.BeatMapType = beatMapType;
            items.Clear();

            bool isJustDanceMode = beatMapType == BeatMapTypeConstant.JustDance;
            var mapsToList = isJustDanceMode
                ? gameplayMapListManager.GetAvailableMapsByBeatMapType(BeatMapTypeConstant.JustDance)
                : gameplayMapListManager.GetAvailableMapsExcludingType(BeatMapTypeConstant.JustDance);

            foreach (var mapInfo in mapsToList)
            {
                items.Add(new ItemData(mapInfo));
            }
            
            scrollView.UpdateData(items);
            
            if (items.Count > 0)
            {
                scrollView.SelectCell(0);

                cancelForSelection?.Cancel();
                cancelForSelection?.Dispose();
                cancelForSelection = new CancellationTokenSource();
                scrollView.ForceUpdateSelectionAsync(0, cancelForSelection).Forget();
            }
            else
            {
                OnSelectItem(null); // Handle empty list case
            }
        }

        private void EnterGameplay()
        {
            if (gameplayData.IsVliad)
            {
                EnterGameplayEvent?.Invoke(gameplayData);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot enter gameplay, GameplayData is invalid. Likely no valid difficulty selected.");
            }
        }

        private void ClickBack()
        {
            cancelForSelection?.Cancel();
            cancelForSelection?.Dispose();
            cancelForSelection = null;
            
            timeline?.Stop();
            ClickBackEvent?.Invoke();
        }

        private void OnSelectItem(ItemData itemData)
        {
            // Cancel any pending media update from a previous selection.
            cancelForMediaInfoUpdate?.Cancel();
            cancelForMediaInfoUpdate?.Dispose();
            cancelForMediaInfoUpdate = new CancellationTokenSource();

            if (itemData == null) // Handle case where list is empty or selection is cleared
            {
                Text_MapDisplayName?.SetText("No Maps Available");
                gameplayData = default;
                RefreshDifficulty(default);
                return;
            }

            Text_MapDisplayName?.SetText(itemData.MapInfo.DisplayName);

            bool bHasOverrideMedia = CheckForOverrideMedia(itemData.MapInfo, this.BeatMapType);

            gameplayData = new Gameplay.GameplayData(itemData.MapInfo, this.BeatMapType, INVLID_DIFFICULTY_FILE);
            RefreshDifficulty(itemData.MapInfo);

            UpdateMediaDataAsync(itemData, bHasOverrideMedia, cancelForMediaInfoUpdate).Forget();
        }

        /// <summary>
        /// A GC-friendly check for media overrides, avoiding LINQ.
        /// </summary>
        private bool CheckForOverrideMedia(MapInfo mapInfo, string beatMapType)
        {
            if (mapInfo.MediaOverrides == null || string.IsNullOrEmpty(beatMapType))
            {
                return false;
            }
            foreach (var overrideInfo in mapInfo.MediaOverrides)
            {
                if (overrideInfo.BeatMapType == beatMapType)
                {
                    return true;
                }
            }
            return false;
        }

        private async UniTask UpdateMediaDataAsync(ItemData itemData, bool bHasOverrideMedia, CancellationTokenSource cancellationTokenSource)
        {
            await UniTask.Delay(confirmDelayMs, false, PlayerLoopTiming.Update, cancellationTokenSource.Token);
            if (cancellationTokenSource.IsCancellationRequested) return;

            // Prepare audio path
            previewAudioName.Clear();
            string audioPath = bHasOverrideMedia 
                ? mapStorage.GetPreviewAudioPath(itemData.MapInfo, BeatMapType) 
                : mapStorage.GetPreviewAudioPath(itemData.MapInfo);
            previewAudioName.Append(FilePathUtility.GetUnityWebRequestUri(audioPath, UnityPathSource.AbsoluteOrFullUri));
            
            // Load audio
            await audioLoadService.LoadAudioAsync(previewAudioName.ToString());
            if (cancellationTokenSource.IsCancellationRequested) return;

            // Stop previous media and initialize new audio
            timeline?.Stop();
            musicPlayer?.InitializeMusicPlayer(previewAudioName.ToString(), true);

            // Prepare video path
            bool isVideoPrepared = false;
            previewVideoName.Clear();
            string videoPath = bHasOverrideMedia
                ? mapStorage.GetPreviewVideoPath(itemData.MapInfo, BeatMapType)
                : mapStorage.GetPreviewVideoPath(itemData.MapInfo);
            
            // Initialize video player
            if (!string.IsNullOrEmpty(videoPath))
            {
                previewVideoName.Append(FilePathUtility.GetUnityWebRequestUri(videoPath, UnityPathSource.AbsoluteOrFullUri));
                videoPlayer?.InitializeVideoPlayer(
                    videoUrl: previewVideoName.ToString(),
                    bLoop: true,
                    OnPrepared: () => { isVideoPrepared = true; });

                await UniTask.WaitUntil(() => isVideoPrepared, PlayerLoopTiming.Update, cancellationTokenSource.Token);
                if (cancellationTokenSource.IsCancellationRequested) return;
            }
            else
            {
                isVideoPrepared = true; // No video to prepare
            }
            
            if (rawImg_PreviewVideoScreen)
            {
                rawImg_PreviewVideoScreen.texture = ((GameplayVideoPlayer)videoPlayer)?.CurrentVideoTexture;
            }

            timeline?.Play();
        }
        
        /// <summary>
        /// Refreshes the difficulty selector based on the selected map and current game mode.
        /// </summary>
        private void RefreshDifficulty(MapInfo mapInfo)
        {
            difficultyFilesForCurrentMode.Clear();
            currentDifficultyIndex = -1;

            if (mapInfo.BeatmapDifficultyFiles != null)
            {
                foreach (var beatMapInfo in mapInfo.BeatmapDifficultyFiles)
                {
                    // For the generic "Beats" mode, we include anything that is NOT an exclusive mode like JustDance.
                    bool isCompatible = string.IsNullOrEmpty(this.BeatMapType)
                        ? beatMapInfo.BeatMapType != BeatMapTypeConstant.JustDance // Example of exclusion
                        : beatMapInfo.BeatMapType == this.BeatMapType;

                    if (isCompatible)
                    {
                        difficultyFilesForCurrentMode.Add(beatMapInfo);
                    }
                }
            }

            // Sort difficulties numerically for consistent order.
            difficultyFilesForCurrentMode.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));

            UpdateDifficultyDisplay();
        }

        /// <summary>
        /// Updates the UI elements for difficulty display and interaction.
        /// </summary>
        private void UpdateDifficultyDisplay()
        {
            if (difficultyFilesForCurrentMode.Count == 0)
            {
                Text_DifficultyName?.SetText("N/A");
                gameplayData.UpdateBeatMapFile(INVLID_DIFFICULTY_FILE);
                Btn_SelectLast.interactable = false;
                Btn_SelectNext.interactable = false;
                return;
            }

            if (currentDifficultyIndex < 0 || currentDifficultyIndex >= difficultyFilesForCurrentMode.Count)
            {
                currentDifficultyIndex = 0;
            }
            
            var currentBeatMap = difficultyFilesForCurrentMode[currentDifficultyIndex];
            
            Text_DifficultyName?.SetText(currentBeatMap.Difficulty.ToString());

            var beatMapFile = BeatMapUtility.GetBeatMapFile(currentBeatMap.BeatMapType, currentBeatMap.Difficulty, currentBeatMap.Version);
            gameplayData.UpdateBeatMapFile(beatMapFile);

            bool canChange = difficultyFilesForCurrentMode.Count > 1;
            Btn_SelectLast.interactable = canChange;
            Btn_SelectNext.interactable = canChange;
        }

        /// <summary>
        /// Changes the selected difficulty index.
        /// </summary>
        /// <param name="direction">-1 for previous, 1 for next.</param>
        private void ChangeDifficulty(int direction)
        {
            if (difficultyFilesForCurrentMode.Count <= 1) return;

            currentDifficultyIndex += direction;
            if (currentDifficultyIndex < 0)
            {
                currentDifficultyIndex = difficultyFilesForCurrentMode.Count - 1;
            }
            else if (currentDifficultyIndex >= difficultyFilesForCurrentMode.Count)
            {
                currentDifficultyIndex = 0;
            }
            
            UpdateDifficultyDisplay();
        }
    }
}