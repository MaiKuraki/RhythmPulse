using System;
using CycloneGames.Factory.Runtime;
using UnityEngine;
using VContainer;

namespace RhythmPulse.Audio
{
    public struct GameAudioData
    {
        public string Key;
    }

    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudioSource : MonoBehaviour, IPoolable<GameAudioData, GameAudioSource>, IDisposable
    {
        private IDespawnableMemoryPool<GameAudioSource> _pool;
        private GameAudioData _data;
        private IAudioLoadService _audioLoadService;
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private long _audioDurationMs;
        private bool _isBeingDestroyed;

        [Inject]
        public void Construct(IAudioLoadService audioLoadService)
        {
            _audioLoadService = audioLoadService;
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private void OnDestroy()
        {
            _isBeingDestroyed = true;
            Dispose();
        }

        public void SetLoop(bool loop)
        {
            if (_audioSource != null)
                _audioSource.loop = loop;
        }

        public void Play()
        {
            if (string.IsNullOrEmpty(_data.Key)) return;
            if (_audioLoadService == null) return;

            if (!_audioLoadService.TryGetLoadedClip(_data.Key, out _audioClip) || _audioClip == null)
                return;

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null) return;
            }

            _audioSource.clip = _audioClip;
            _audioDurationMs = (long)(_audioClip.length * 1000f);
            _audioSource.Play();
        }

        public void Stop()
        {
            if (_audioSource != null)
                _audioSource.Stop();
            _audioDurationMs = 0;
        }

        public void Pause()
        {
            if (_audioSource != null)
                _audioSource.Pause();
        }

        public void Resume()
        {
            if (_audioSource != null)
                _audioSource.UnPause();
        }

        public long GetPlaybackTimeMSec()
        {
            if (_audioSource == null) return 0;
            return (long)(_audioSource.time * 1000f);
        }

        public long GetAudioClipLengthMSec()
        {
            return _audioDurationMs;
        }

        public void SeekTime(long milliSeconds)
        {
            if (_audioSource != null)
                _audioSource.time = milliSeconds / 1000f;
        }

        public void OnDespawned()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }

            _data = default;
            _pool = null;
            gameObject.SetActive(false);
        }

        public void OnSpawned(GameAudioData data, IDespawnableMemoryPool<GameAudioSource> pool)
        {
            _data = data;
            _pool = pool;

            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            gameObject.SetActive(true);
        }

        public void Dispose()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }

            _audioClip = null;
            _audioDurationMs = 0;

            // Avoid pool operations during destruction to prevent teardown races
            if (!_isBeingDestroyed)
                _pool?.Despawn(this);
        }
    }
}