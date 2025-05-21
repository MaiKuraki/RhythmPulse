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
        private GameplayMusicPlayer gameplayMusicPlayer;
        [SerializeField] Camera gameplayCamera;

        [Inject]
        public void Construct(IMainCameraService mainCameraService, AudioManager audioManager, GameplayMusicPlayer gameplayMusicPlayer)
        {
            this.mainCameraService = mainCameraService;
            this.audioManager = audioManager;
            this.gameplayMusicPlayer = gameplayMusicPlayer;
            
            IsDIInitialized = true;
        }

        private async UniTask RunAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);

            //  Test code
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/GodKnows_audio.ogg");
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/KDA-POP_STARS_audio.ogg");
            await audioManager.LoadAudioAsync("D:/Downloads/MusicGameMedias/MusicGameMedias/MikaNakashima-GLAMOROUS_SKY_audio.ogg");
            gameplayMusicPlayer.InitializeMusicPlayer("D:/Downloads/MusicGameMedias/MusicGameMedias/MikaNakashima-GLAMOROUS_SKY_audio.ogg");
            gameplayMusicPlayer.Play();
            
            if (cancellationToken.IsCancellationRequested) return;
            mainCameraService.AddCameraToStack(gameplayCamera);
        }

        void Awake()
        {
            RunAfterDIInitialized(destroyCancellationToken).Forget();
        }

        void OnDestroy()
        {
            mainCameraService?.RemoveCameraFromStack(gameplayCamera);
        }
    }
}