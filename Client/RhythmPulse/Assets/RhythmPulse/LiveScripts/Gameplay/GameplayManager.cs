using System.Threading;
using CycloneGames.Service;
using Cysharp.Threading.Tasks;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay.Media;
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
        private GameplayVideoPlayer gameplayVideoPlayer;
        private ITimeline gameplayTimeline;
        [SerializeField] Camera gameplayCamera;
        [SerializeField] GameplayVideoRender gameplayVideoRender;

        [Inject]
        public void Construct(IMainCameraService mainCameraService,
                        AudioManager audioManager,
                        IGameplayMusicPlayer gameplayMusicPlayer,
                        GameplayVideoPlayer gameplayVideoPlayer,
                        ITimeline gameplayTimeline)
        {
            this.mainCameraService = mainCameraService;
            this.audioManager = audioManager;
            this.gameplayMusicPlayer = gameplayMusicPlayer;
            this.gameplayVideoPlayer = gameplayVideoPlayer;
            this.gameplayTimeline = gameplayTimeline;

            IsDIInitialized = true;
        }

        private async UniTask RunAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            //  Test code
            await InitializeMedias();
            gameplayTimeline.Play();

            if (cancellationToken.IsCancellationRequested) return;
            mainCameraService.AddCameraToStack(gameplayCamera);
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
            gameplayVideoRender.SetTargetTexture(gameplayVideoPlayer.VideoTexture);
        }

        void OnDestroy()
        {
            mainCameraService?.RemoveCameraFromStack(gameplayCamera);
        }
    }
}