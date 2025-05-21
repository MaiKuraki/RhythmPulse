using CycloneGames.Factory;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using RhythmPulse.Audio;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.Gameplay.Media
{
    // public interface IGameplayMusicPlayer { }
    public class GameplayMusicPlayer
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

        public void GetPlaybackTimeMSec()
        {
            MusicPlayer.GetPlaybackTimeMSec();
        }

        public void SeekTime(long milliSeconds)
        {
            MusicPlayer.SeekTime(milliSeconds);
        }
    }
}