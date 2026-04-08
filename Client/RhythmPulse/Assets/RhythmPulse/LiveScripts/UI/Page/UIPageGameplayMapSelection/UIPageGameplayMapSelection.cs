using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using R3;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay;
using RhythmPulse.Media;
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
        [SerializeField] private UnityVideoRender videoRender;

        [Header("BeatMap Info")]
        [SerializeField] private TMP_Text Text_BeatMapType;
        [SerializeField] private TMP_Text Text_BeatMapVersion;
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
        private IUnityMusicPlayer musicPlayer;
        private IUnityVideoPlayer _unityVideoPlayer;

        private bool IsDIInitialized = false;
        private List<ItemData> items = new List<ItemData>();
        private CancellationTokenSource cancelForSelection;
        private CancellationTokenSource cancelForMediaInfoUpdate;
        private Gameplay.GameplayData gameplayData;
        private MapInfo? currentMapInfo; // To store the currently selected map's info.
        private MusicSelectionContext currentContext;
        private StringBuilder previewAudioName = new StringBuilder();
        private StringBuilder previewVideoName = new StringBuilder();

        // State for the currently selected map's difficulties
        private List<BeatMapInfo> difficultyFilesForCurrentMode = new List<BeatMapInfo>();
        private int currentDifficultyIndex = -1;

        private const string INVLID_DIFFICULTY_FILE = "INVALID_CONFIG.yaml";

        void Awake()
        {
            enterMusicGameplayButton.OnClickAsObservable().Subscribe(_ => EnterGameplay()).AddTo(this);
            backButton.OnClickAsObservable().Subscribe(_ => ClickBack()).AddTo(this);

            // PERFORMANCE: Subscribe to difficulty change buttons only once.
            // The handlers will use class state to determine behavior.
            Btn_SelectLast.OnClickAsObservable().Subscribe(_ => ChangeDifficulty(-1)).AddTo(this);
            Btn_SelectNext.OnClickAsObservable().Subscribe(_ => ChangeDifficulty(1)).AddTo(this);

            AdjustConfirmDelayForHighPerformanceDevices();
        }

        void OnEnable()
        {
            if (scrollView != null)
            {
                // Re-subscribe when page is shown again (e.g., back from mode page to map selection).
                // Defensive de-dup: remove first in case OnEnable is triggered repeatedly.
                scrollView.OnSelectedEvent -= OnSelectItem;
                scrollView.OnSelectedEvent += OnSelectItem;
            }
        }

        [Inject]
        void Construct(
            IUIService uiService,
            IAudioLoadService audioLoadService,
            IGameplayMapStorage mapStorage,
            IGameplayMapListManager gameplayMapListManager,
            ITimeline timeline,
            IUnityMusicPlayer musicPlayer,
            IUnityVideoPlayer unityVideoPlayer)
        {
            this.uiService = uiService;
            this.audioLoadService = audioLoadService;
            this.mapStorage = mapStorage;
            this.gameplayMapListManager = gameplayMapListManager;
            this.timeline = timeline;
            this.musicPlayer = musicPlayer;
            this._unityVideoPlayer = unityVideoPlayer;

            scrollView.SetCellInterval(1 / (uiService.GetRootCanvasSize().Item2 / 140.0f));

            IsDIInitialized = true;
            UnityEngine.Debug.Log($"Construct MapSelection");
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
            if (scrollView != null)
            {
                scrollView.OnSelectedEvent -= OnSelectItem;
            }

            // Cancel any pending operations when the page is disabled
            // (but don't dispose - that happens in OnDestroy)
            cancelForSelection?.Cancel();
            cancelForMediaInfoUpdate?.Cancel();
            timeline?.Stop();
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

        public async UniTask RebuildMapListAfterDIInitialized(MusicSelectionContext context, CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            currentContext = context;
            items.Clear();

            bool isJustDanceMode = context.FilterBeatMapType == BeatMapTypeConstant.JustDance;
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
            if (!isActiveAndEnabled || this == null) return; // Guard for rapid page disable/destroy
            // Cancel any pending media update from a previous selection.
            cancelForMediaInfoUpdate?.Cancel();
            cancelForMediaInfoUpdate?.Dispose();
            cancelForMediaInfoUpdate = new CancellationTokenSource();

            if (itemData == null) // Handle case where list is empty or selection is cleared
            {
                currentMapInfo = null;
                Text_MapDisplayName?.SetText("No Maps Available");
                // RefreshDifficulty will handle clearing the old state and invalidating gameplayData
                RefreshDifficulty(null);
                return;
            }

            currentMapInfo = itemData.MapInfo;
            // Use .Value as currentMapInfo is now nullable
            Text_MapDisplayName?.SetText(currentMapInfo.Value.DisplayName);

            // The BeatMapType here is the *filter* type from the lobby, used to find correct media overrides.
            bool bHasOverrideMedia = CheckForOverrideMedia(currentMapInfo.Value, currentContext.FilterBeatMapType);

            // Refresh the list of available difficulties for the selected map and current mode.
            // This will trigger an update to the gameplayData via UpdateDifficultyDisplay.
            RefreshDifficulty(currentMapInfo);

            // Asynchronously load and prepare media preview.
            UpdateMediaDataAsync(itemData, bHasOverrideMedia, cancelForMediaInfoUpdate).Forget();
        }

        /// <summary>
        /// A GC-friendly check for media overrides, avoiding LINQ.
        /// </summary>
        private bool CheckForOverrideMedia(in MapInfo mapInfo, string filterBeatMapType)
        {
            if (mapInfo.MediaOverrides == null || string.IsNullOrEmpty(filterBeatMapType))
            {
                return false;
            }
            foreach (var overrideInfo in mapInfo.MediaOverrides)
            {
                if (overrideInfo.BeatMapType == filterBeatMapType)
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
                ? mapStorage.GetPreviewAudioPath(itemData.MapInfo, currentContext.FilterBeatMapType)
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
                ? mapStorage.GetPreviewVideoPath(itemData.MapInfo, currentContext.FilterBeatMapType)
                : mapStorage.GetPreviewVideoPath(itemData.MapInfo);

            // Initialize video player
            if (!string.IsNullOrEmpty(videoPath))
            {
                previewVideoName.Append(FilePathUtility.GetUnityWebRequestUri(videoPath, UnityPathSource.AbsoluteOrFullUri));
                _unityVideoPlayer?.InitializeVideoPlayer(
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

            if (videoRender && isActiveAndEnabled && this != null)
            {
                var gp = _unityVideoPlayer as UnityVideoProvider;
                if (gp != null)
                {
                    videoRender.SetTargetTexture(gp.CurrentVideoTexture);
                }
            }

            timeline?.Play();
        }

        /// <summary>
        /// Refreshes the difficulty selector based on the selected map and current game mode filter.
        /// This method is responsible for populating the list of available difficulties.
        /// </summary>
        private void RefreshDifficulty(MapInfo? mapInfo)
        {
            difficultyFilesForCurrentMode.Clear();

            if (mapInfo?.BeatmapDifficultyFiles != null)
            {
                foreach (var beatMapInfo in mapInfo.Value.BeatmapDifficultyFiles)
                {
                    // For the generic "Beats" mode (where BeatMapType is empty), we include anything 
                    // that is NOT an exclusive mode like JustDance. For a specific mode like JustDance, 
                    // we only include beatmaps of that type.
                    bool isCompatible = string.IsNullOrEmpty(currentContext.FilterBeatMapType)
                        ? beatMapInfo.BeatMapType != BeatMapTypeConstant.JustDance // Example of exclusion for "traditional" modes
                        : beatMapInfo.BeatMapType == currentContext.FilterBeatMapType;

                    if (isCompatible)
                    {
                        difficultyFilesForCurrentMode.Add(beatMapInfo);
                    }
                }
            }

            // Sort difficulties for a consistent and predictable order.
            // This assumes 'Difficulty' is a comparable type (e.g., int or enum).
            difficultyFilesForCurrentMode.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));

            // After refreshing the list, reset the selection to the first item.
            currentDifficultyIndex = difficultyFilesForCurrentMode.Count > 0 ? 0 : -1;
            UpdateDifficultyDisplay();
        }

        /// <summary>
        /// Updates the UI for the difficulty display and, crucially, constructs the final
        /// GameplayData based on the currently selected difficulty. This is the single
        /// point of truth for the selected gameplay configuration.
        /// </summary>
        private void UpdateDifficultyDisplay()
        {
            if (!IsDIInitialized || !isActiveAndEnabled || this == null)
            {
                return; // Page is not in a valid state to update UI
            }
            bool hasDifficulties = currentDifficultyIndex != -1;

            // Enable/disable navigation buttons based on whether there's more than one difficulty.
            bool canChange = difficultyFilesForCurrentMode.Count > 1;
            Btn_SelectLast.interactable = canChange;
            Btn_SelectNext.interactable = canChange;

            if (!hasDifficulties || !currentMapInfo.HasValue)
            {
                if (Text_DifficultyName)
                {
                    Text_DifficultyName.SetText("N/A");
                }
                // Invalidate gameplay data if no valid difficulty is available for the current map/mode.
                gameplayData = default;
                return;
            }

            // A valid difficulty is selected, so update the display and construct the GameplayData.
            var currentBeatMap = difficultyFilesForCurrentMode[currentDifficultyIndex];
            if (Text_DifficultyName)
            {
                Text_DifficultyName.SetText(currentBeatMap.Difficulty.ToString());
            }
            if (Text_BeatMapType)
            {
                Text_BeatMapType.SetText(currentBeatMap.BeatMapType);
            }
            if (Text_BeatMapVersion)
            {
                Text_BeatMapVersion.SetText(currentBeatMap.Version);
            }

            // We construct the final, valid GameplayData using the specific BeatMapType
            // from the selected difficulty, not the filter type from the lobby. This ensures
            // the correct game mode, difficulty, and version are used to start the gameplay scene.
            var beatMapFile = BeatMapUtility.GetBeatMapFile(currentBeatMap.BeatMapType, currentBeatMap.Difficulty, currentBeatMap.Version);
            gameplayData = new Gameplay.GameplayData(currentMapInfo.Value, currentBeatMap.BeatMapType, beatMapFile);
        }

        /// <summary>
        /// Changes the selected difficulty index and updates the UI.
        /// </summary>
        /// <param name="direction">-1 for previous, 1 for next.</param>
        private void ChangeDifficulty(int direction)
        {
            if (difficultyFilesForCurrentMode.Count <= 1) return;

            // Cycle through the available difficulties.
            currentDifficultyIndex = (currentDifficultyIndex + direction + difficultyFilesForCurrentMode.Count) % difficultyFilesForCurrentMode.Count;

            UpdateDifficultyDisplay();
        }
    }
}
