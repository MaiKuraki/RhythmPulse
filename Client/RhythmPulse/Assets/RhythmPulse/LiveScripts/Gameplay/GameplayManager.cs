using System;
using System.Text;
using System.Threading;
using RhythmPulse.Services;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.IO.Unity;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Media;
using RhythmPulse.Scene;
using RhythmPulse.UI;
using UnityEngine;
using VContainer;
using CycloneGames.Logging;

namespace RhythmPulse.Gameplay
{
    public class GameplayManager : MonoBehaviour
    {
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
                    // RPLog.Channel.Info($"CurrentAudioLength: {GameplayTimeline.GameplayMusicPlayer?.GetCurrentMusicLengthMSec()}");
                    OnUpdatePlaybackProgress?.Invoke(GetCurrentMusicPlaybackProgress());

                    if (GetPlaybackTimeRemainingMSec() <= 50)
                    {
                        StopGameplay();
                        OpenResultWindowAsync(destroyCancellationToken).Forget();
                    }
                }
            }
        }

        private async UniTaskVoid OpenResultWindowAsync(CancellationToken cancellationToken)
        {
            try
            {
                await uiService.OpenAsync(UIWindowName.UIWindowGameplayResult, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Owner is being destroyed; nothing to report.
            }
            catch (Exception exception)
            {
                RPLog.Channel.Error(exception, "Failed to open the gameplay result window.");
            }
        }

        public async UniTask InitializeMedias(Gameplay.GameplayData gameplayData, CancellationTokenSource cancel)
        {
            audioFileName.Clear();
            if (gameplayData.HasOverrideMedia) audioFileName.Append(UnityPathUtility.GetUnityWebRequestUri(mapStorage.GetAudioPath(gameplayData.MapInfo, gameplayData.BeatMapType), UnityFileLocation.AbsolutePathOrUri));
            else audioFileName.Append(UnityPathUtility.GetUnityWebRequestUri(mapStorage.GetAudioPath(gameplayData.MapInfo), UnityFileLocation.AbsolutePathOrUri));
            await audioLoadService.LoadAudioAsync(audioFileName.ToString());
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            _unityMusicPlayer.InitializeMusicPlayer(audioFileName.ToString());
            bool isVideoPrepared = false;
            videoFileName.Clear();
            if (gameplayData.HasOverrideMedia) videoFileName.Append(UnityPathUtility.GetUnityWebRequestUri(mapStorage.GetVideoPath(gameplayData.MapInfo, gameplayData.BeatMapType), UnityFileLocation.AbsolutePathOrUri));
            else videoFileName.Append(UnityPathUtility.GetUnityWebRequestUri(mapStorage.GetVideoPath(gameplayData.MapInfo), UnityFileLocation.AbsolutePathOrUri));
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