using System.Threading;
using CycloneGames.Logger;
using CycloneGames.Service;
using CycloneGames.UIFramework;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay.Media;
using RhythmPulse.Scene;
using UnityEngine;
using VContainer;

namespace RhythmPulse.Gameplay
{
    public class GameplayManager : MonoBehaviour
    {
        private bool IsDIInitialized = false;
        private IMainCameraService mainCameraService;
        private AudioManager audioManager;
        private IGameplayMusicPlayer gameplayMusicPlayer;
        private IGameplayVideoPlayer gameplayVideoPlayer;
        private ITimeline gameplayTimeline;
        private IUIService uiService;
        private ISceneManagementAPIGateway sceneManagementAPIGateway;
        [SerializeField] Camera gameplayCamera;
        [SerializeField] GameplayVideoRender gameplayVideoRender;

        public Timeline GameplayTimeline => (Timeline)gameplayTimeline;

        [Inject]
        public void Construct(IMainCameraService mainCameraService,
                        AudioManager audioManager,
                        IGameplayMusicPlayer gameplayMusicPlayer,
                        IGameplayVideoPlayer gameplayVideoPlayer,
                        ITimeline gameplayTimeline,
                        IUIService uiService,
                        ISceneManagementAPIGateway sceneManagementAPIGateway)
        {
            this.mainCameraService = mainCameraService;
            this.audioManager = audioManager;
            this.gameplayMusicPlayer = gameplayMusicPlayer;
            this.gameplayVideoPlayer = gameplayVideoPlayer;
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

            //  Test code
            await InitializeMedias();
            gameplayTimeline.Play();
        }

        void Awake()
        {
            RunAfterDIInitialized(destroyCancellationToken).Forget();
        }

        private void Update()
        {
            GameplayTimeline?.Tick();
        }

        private async UniTask InitializeMedias()
        {
            await audioManager.LoadAudioAsync(FilePathUtility.GetUnityWebRequestUri(System.IO.Path.GetFullPath("./MusicGameMedias/AyaHirano-GodKnows/AyaHirano-GodKnows_audio.ogg"), UnityPathSource.AbsoluteOrFullUri));
            await audioManager.LoadAudioAsync(FilePathUtility.GetUnityWebRequestUri(System.IO.Path.GetFullPath("./MusicGameMedias/Doa-Hero/Doa-Hero_audio.ogg"), UnityPathSource.AbsoluteOrFullUri));
            await audioManager.LoadAudioAsync(FilePathUtility.GetUnityWebRequestUri(System.IO.Path.GetFullPath("./MusicGameMedias/MikaNakashima-GLAMOROUS_SKY/MikaNakashima-GLAMOROUS_SKY_audio.ogg"), UnityPathSource.AbsoluteOrFullUri));

            gameplayMusicPlayer.InitializeMusicPlayer(FilePathUtility.GetUnityWebRequestUri(System.IO.Path.GetFullPath("./MusicGameMedias/MikaNakashima-GLAMOROUS_SKY/MikaNakashima-GLAMOROUS_SKY_audio.ogg"), UnityPathSource.AbsoluteOrFullUri));
            gameplayVideoPlayer.InitializeVideoPlayer(FilePathUtility.GetUnityWebRequestUri(System.IO.Path.GetFullPath("./MusicGameMedias/MikaNakashima-GLAMOROUS_SKY/MikaNakashima-GLAMOROUS_SKY_video.mp4"), UnityPathSource.AbsoluteOrFullUri));
            gameplayVideoRender.SetTargetTexture(((GameplayVideoPlayer)gameplayVideoPlayer).VideoTexture);
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

        public void Exit()
        {
            gameplayTimeline.Stop();
            audioManager.UnloadAllAudio().Forget();
            sceneManagementAPIGateway.Push(SceneDefinitions.Lobby);
        }

        void OnDestroy()
        {
            mainCameraService?.RemoveCameraFromStack(gameplayCamera);
        }
    }
}