using System;
using CycloneGames.Factory.Runtime;
using RhythmPulse.Audio;

namespace RhythmPulse.Media
{
    public sealed class UnityMusicPlayer : IUnityMusicPlayer, IDisposable
    {
        private readonly IUnityObjectSpawner _spawner;
        private readonly IAudioLoadService _audioLoadService;
        private readonly IFactory<GameAudioSource> _audioSourceFactory;
        private readonly IMemoryPool<GameAudioData, GameAudioSource> _musicPlayerPool;

        private GameAudioSource _musicPlayer;
        private bool _isDisposed;

        public bool IsAnyAudioInitialized { get; private set; }

        public UnityMusicPlayer(
            IUnityObjectSpawner spawner, 
            IAudioLoadService audioLoadService, 
            IFactory<GameAudioSource> audioSourceFactory)
        {
            _spawner = spawner;
            _audioLoadService = audioLoadService;
            _audioSourceFactory = audioSourceFactory;
            _musicPlayerPool = new ObjectPool<GameAudioData, GameAudioSource>(audioSourceFactory, 5);
        }

        public void InitializeMusicPlayer(in string InAudioKey, bool bLoop = false)
        {
            if (_musicPlayer != null)
            {
                _musicPlayer.Dispose();
                _musicPlayer = null;
            }

            _musicPlayer = _musicPlayerPool.Spawn(new GameAudioData { Key = InAudioKey });
            _musicPlayer.SetLoop(bLoop);
            IsAnyAudioInitialized = true;
        }

        public void Play()
        {
            if (!IsAnyAudioInitialized || _musicPlayer == null) return;
            _musicPlayer.Play();
        }

        public void Stop()
        {
            if (!IsAnyAudioInitialized || _musicPlayer == null) return;
            
            _musicPlayer.Stop();
            _musicPlayerPool.Despawn(_musicPlayer);
            _musicPlayer = null;
            IsAnyAudioInitialized = false;
        }

        public void Pause()
        {
            if (!IsAnyAudioInitialized || _musicPlayer == null) return;
            _musicPlayer.Pause();
        }

        public void Resume()
        {
            if (!IsAnyAudioInitialized || _musicPlayer == null) return;
            _musicPlayer.Resume();
        }

        public long GetPlaybackTimeMSec()
        {
            if (_musicPlayer == null) return 0;
            return _musicPlayer.GetPlaybackTimeMSec();
        }

        public void SeekTime(long milliSeconds)
        {
            if (!IsAnyAudioInitialized || _musicPlayer == null) return;
            _musicPlayer.SeekTime(milliSeconds);
        }

        public long GetMediaDurationMSec()
        {
            if (_musicPlayer == null) return 0;
            return _musicPlayer.GetAudioClipLengthMSec();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_musicPlayer != null)
            {
                _musicPlayer.Dispose();
                _musicPlayer = null;
            }

            IsAnyAudioInitialized = false;
        }
    }
}