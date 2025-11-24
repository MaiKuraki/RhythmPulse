using System;
using System.Text;
using System.Threading;
using CycloneGames.Logger;
using CycloneGames.Service.Runtime;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Media;
using RhythmPulse.Scene;
using RhythmPulse.UI;
using UnityEngine;
using VContainer;

namespace RhythmPulse.Gameplay
{
    public class GameplayManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[GameplayManager]";
        private bool IsDIInitialized = false;
        private IMainCameraService mainCameraService;
        private IAudioLoadService audioLoadService;
        private IGameplayMapStorage mapStorage;
        private IUnityMusicPlayer _unityMusicPlayer;
        private IUnityVideoPlayer _unityVideoPlayer;
        private ITimeline gameplayTimeline;
        private IUIService uiService;
        private ISceneManagementAPIGateway sceneManagementAPIGateway;
        [SerializeField] Camera gameplayCamera;
        [SerializeField] UnityVideoRender unityVideoRender;

        public Timeline GameplayTimeline => (Timeline)gameplayTimeline;
        public Action<float> OnUpdatePlaybackProgress { get; set; }
        private bool IsGameplayMediaReady = false;
        StringBuilder audioFileName = new StringBuilder();
        StringBuilder videoFileName = new StringBuilder();

        [Inject]
        public void Construct(IMainCameraService mainCameraService,
                        IAudioLoadService audioLoadService,
                        IGameplayMapStorage mapStorage,
                        IUnityMusicPlayer unityMusicPlayer,
                        IUnityVideoPlayer unityVideoPlayer,
                        ITimeline gameplayTimeline,
                        IUIService uiService,
                        ISceneManagementAPIGateway sceneManagementAPIGateway)
        {
            this.mainCameraService = mainCameraService;
            this.audioLoadService = audioLoadService;
            this.mapStorage = mapStorage;
            this._unityMusicPlayer = unityMusicPlayer;
            this._unityVideoPlayer = unityVideoPlayer;
            this.gameplayTimeline = gameplayTimeline;
            this.uiService = uiService;
            this.sceneManagementAPIGateway = sceneManagementAPIGateway;

            IsDIInitialized = true;
        }

        private async UniTask RunAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;
            mainCameraService.AddCameraToStack(gameplayCamera, 0);

            await UniTask.WaitUntil(() => IsGameplayMediaReady, PlayerLoopTiming.Update, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            gameplayTimeline.Play();    //  TODO: may be move to sceneLifecycle?
        }

        void Awake()
        {
            IsGameplayMediaReady = false;
            RunAfterDIInitialized(destroyCancellationToken).Forget();
        }

        private void Update()
        {
            if (!IsDIInitialized) return;

            if (GameplayTimeline != null)
            {
                GameplayTimeline?.Tick();

                if (GameplayTimeline.State == GameplayTimeline.PlayingState)
                {
                    // CLogger.LogInfo($"CurrentAudioLength: {GameplayTimeline.GameplayMusicPlayer?.GetCurrentMusicLengthMSec()}");
                    OnUpdatePlaybackProgress?.Invoke(GetCurrentMusicPlaybackProgress());

                    if (GetPlaybackTimeRemainingMSec() <= 50)
                    {
                        StopGameplay();
                        uiService.OpenUI(UIWindowName.UIWindowGameplayResult);
                    }
                }
            }
        }

        public async UniTask InitializeMedias(Gameplay.GameplayData gameplayData, CancellationTokenSource cancel)
        {
            audioFileName.Clear();
            if (gameplayData.HasOverrideMedia) audioFileName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetAudioPath(gameplayData.MapInfo, gameplayData.BeatMapType), UnityPathSource.AbsoluteOrFullUri));
            else audioFileName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetAudioPath(gameplayData.MapInfo), UnityPathSource.AbsoluteOrFullUri));
            await audioLoadService.LoadAudioAsync(audioFileName.ToString());
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            _unityMusicPlayer.InitializeMusicPlayer(audioFileName.ToString());
            bool isVideoPrepared = false;
            videoFileName.Clear();
            if (gameplayData.HasOverrideMedia) videoFileName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetVideoPath(gameplayData.MapInfo, gameplayData.BeatMapType), UnityPathSource.AbsoluteOrFullUri));
            else videoFileName.Append(FilePathUtility.GetUnityWebRequestUri(mapStorage.GetVideoPath(gameplayData.MapInfo), UnityPathSource.AbsoluteOrFullUri));
            _unityVideoPlayer.InitializeVideoPlayer(
                                    videoUrl: videoFileName.ToString(),
                                    bLoop: false,
                                    OnPrepared: () => { isVideoPrepared = true; });
            await UniTask.WaitUntil(() => isVideoPrepared, PlayerLoopTiming.Update, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            unityVideoRender.SetTargetTexture(((UnityVideoProvider)_unityVideoPlayer).CurrentVideoTexture);

            IsGameplayMediaReady = true;
        }

        public float GetCurrentMusicPlaybackProgress()
        {
            return GameplayTimeline != null && GameplayTimeline.UnityMusicPlayer != null && GameplayTimeline.UnityMusicPlayer.GetMediaDurationMSec() > 0
                ? GameplayTimeline.PlaybackTimeMSec / (float)GameplayTimeline.UnityMusicPlayer.GetMediaDurationMSec()
                : 0;
        }

        public long GetPlaybackTimeRemainingMSec()
        {
            return GameplayTimeline != null && GameplayTimeline.UnityMusicPlayer != null && GameplayTimeline.UnityMusicPlayer.GetMediaDurationMSec() > 0
                ? GameplayTimeline.UnityMusicPlayer.GetMediaDurationMSec() - GameplayTimeline.PlaybackTimeMSec
                : -1;   // -1 means invalid
        }

        public void Pause()
        {
            if (gameplayTimeline.State == ((Timeline)gameplayTimeline).PlayingState)
            {
                gameplayTimeline.Pause();
            }
            else if (gameplayTimeline.State == ((Timeline)gameplayTimeline).PausedState)
            {
                gameplayTimeline.Resume();
            }
        }

        public void StopGameplay()
        {
            gameplayTimeline.Stop();
        }

        public void Exit()
        {
            StopGameplay();
            audioLoadService.UnloadAllAudio().Forget();
            sceneManagementAPIGateway.Push(SceneDefinitions.Lobby);
        }

        void OnDestroy()
        {
            mainCameraService?.RemoveCameraFromStack(gameplayCamera);
        }
    }
}