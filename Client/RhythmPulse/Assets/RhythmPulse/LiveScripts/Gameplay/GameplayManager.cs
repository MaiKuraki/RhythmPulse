using System.Threading;
using CycloneGames.Service;
using CycloneGames.UIFramework;
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

        private async UniTask InitializeMedias()
        {
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/GodKnows_audio.ogg");
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/KDA-POP_STARS_audio.ogg");
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/MikaNakashima-GLAMOROUS_SKY_audio.ogg");

            gameplayMusicPlayer.InitializeMusicPlayer("D:/Downloads/MusicGameMedias/MusicGameMedias/GodKnows_audio.ogg");
            gameplayVideoPlayer.InitializeVideoPlayer("D:/Downloads/MusicGameMedias/MusicGameMedias/GodKnows_video.mp4");
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