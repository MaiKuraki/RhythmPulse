using CycloneGames.Factory;
using RhythmPulse.Audio;

namespace RhythmPulse.Gameplay.Media
{
    public interface IGameplayMusicPlayer
    {
        void InitializeMusicPlayer(in string InAudioKey, bool bLoop = false);
        void Play();
        void Stop();
        void Pause();
        void Resume();
        long GetPlaybackTimeMSec();
        long GetCurrentMusicLengthMSec();
        void SeekTime(long milliSeconds);
        bool IsAnyAudioInitialized { get; }
    }
    public class GameplayMusicPlayer : IGameplayMusicPlayer
    {
        private const string DEBUG_FLAG = "[GameplayMusicPlayer] ";
        private IUnityObjectSpawner spawner;
        private IAudioLoadService audioLoadService;
        private MonoObjectPool<GameAudioData, GameAudioSource> GameplayMusicPlayerSpawner;

        private GameAudioSource MusicPlayer;
        public bool IsAnyAudioInitialized { get; private set; } = false;

        public GameplayMusicPlayer(IUnityObjectSpawner spawner, IAudioLoadService audioLoadService)
        {
            this.spawner = spawner;
            this.audioLoadService = audioLoadService;

            GameplayMusicPlayerSpawner = new MonoObjectPool<GameAudioData, GameAudioSource>(
                spawner,
                audioLoadService.AudioSourcePrefab,
                initialSize: 5,
                autoExpand: true);
            IsAnyAudioInitialized = false;
        }

        public void InitializeMusicPlayer(in string InAudioKey, bool bLoop = false)
        {
            MusicPlayer = GameplayMusicPlayerSpawner.Spawn(new GameAudioData() { Key = InAudioKey });
            MusicPlayer.SetLoop(bLoop);
            IsAnyAudioInitialized = true;
        }

        public void Play()
        {
            if (!IsAnyAudioInitialized) return;
            MusicPlayer.Play();
        }

        public void Stop()
        {
            if (!IsAnyAudioInitialized) return;
            MusicPlayer.Stop();
            MusicPlayer.Dispose();
        }

        public void Pause()
        {
            if (!IsAnyAudioInitialized) return;
            MusicPlayer.Pause();
        }

        public void Resume()
        {
            if (!IsAnyAudioInitialized) return;
            MusicPlayer.Resume();
        }

        public long GetPlaybackTimeMSec()
        {
            return MusicPlayer ? MusicPlayer.GetPlaybackTimeMSec() : 0;
        }

        public long GetCurrentMusicLengthMSec()
        {
            return MusicPlayer ? MusicPlayer.GetAudioClipLengthMSec() : 0;
        }

        public void SeekTime(long milliSeconds)
        {
            if (!IsAnyAudioInitialized) return;
            MusicPlayer.SeekTime(milliSeconds);
        }
    }
}