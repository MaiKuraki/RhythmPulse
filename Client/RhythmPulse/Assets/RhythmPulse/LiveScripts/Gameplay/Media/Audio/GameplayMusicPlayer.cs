using CycloneGames.Factory;
using RhythmPulse.Audio;

namespace RhythmPulse.Gameplay.Media
{
    public interface IGameplayMusicPlayer
    {
        void InitializeMusicPlayer(in string InAudioKey);
        void Play();
        void Stop();
        void Pause();
        void Resume();
        long GetPlaybackTimeMSec();
        void SeekTime(long milliSeconds);
    }
    public class GameplayMusicPlayer : IGameplayMusicPlayer
    {
        private const string DEBUG_FLAG = "[GameplayMusicPlayer] ";
        private IUnityObjectSpawner spawner;
        private AudioManager audioManager;
        private MonoObjectPool<GameAudioData, GameAudioSource> GameplayMusicPlayerSpawner;

        private GameAudioSource MusicPlayer;

        public GameplayMusicPlayer(IUnityObjectSpawner spawner, AudioManager audioManager)
        {
            this.spawner = spawner;
            this.audioManager = audioManager;

            GameplayMusicPlayerSpawner = new MonoObjectPool<GameAudioData, GameAudioSource>(
                spawner,
                audioManager.AudioSourcePrefab,
                initialSize: 5,
                autoExpand: true);
        }

        public void InitializeMusicPlayer(in string InAudioKey)
        {
            MusicPlayer = GameplayMusicPlayerSpawner.Spawn(new GameAudioData() { Key = InAudioKey });
        }

        public void Play()
        {
            MusicPlayer.Play();
        }

        public void Stop()
        {
            MusicPlayer.Stop();
        }

        public void Pause()
        {
            MusicPlayer.Pause();
        }

        public void Resume()
        {
            MusicPlayer.Resume();
        }

        public long GetPlaybackTimeMSec()
        {
            return MusicPlayer ? MusicPlayer.GetPlaybackTimeMSec() : 0;
        }

        public void SeekTime(long milliSeconds)
        {
            MusicPlayer.SeekTime(milliSeconds);
        }
    }
}