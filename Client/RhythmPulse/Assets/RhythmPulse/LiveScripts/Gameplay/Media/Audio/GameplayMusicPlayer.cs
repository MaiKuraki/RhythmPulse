using CycloneGames.Factory.Runtime;
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
        private IFactory<GameAudioSource> audioSourceFactory;
        private IMemoryPool<GameAudioData, GameAudioSource> GameplayMusicPlayerSpawner;

        private GameAudioSource MusicPlayer;
        public bool IsAnyAudioInitialized { get; private set; } = false;

        public GameplayMusicPlayer(IUnityObjectSpawner spawner, IAudioLoadService audioLoadService, IFactory<GameAudioSource> audioSourceFactory)
        {
            this.spawner = spawner;
            this.audioLoadService = audioLoadService;
            this.audioSourceFactory = audioSourceFactory;

            GameplayMusicPlayerSpawner = new ObjectPool<GameAudioData, GameAudioSource>(audioSourceFactory, 5);
            IsAnyAudioInitialized = false;
        }

        public void InitializeMusicPlayer(in string InAudioKey, bool bLoop = false)
        {
            if (MusicPlayer != null)
            {
                MusicPlayer.Dispose();
            }
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
            GameplayMusicPlayerSpawner.Despawn(MusicPlayer);
            MusicPlayer = null;
            IsAnyAudioInitialized = false;
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