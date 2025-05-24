using CycloneGames.Logger;
using UnityEngine;
using VContainer;
using CycloneGames.Factory;
using System;

namespace RhythmPulse.Audio
{
    public struct GameAudioData
    {
        public string Key { get; set; }
    }

    [RequireComponent(typeof(AudioSource))]
    public class GameAudioSource : MonoBehaviour, IPoolable<GameAudioData, IMemoryPool>, IDisposable, ITickable
    {
        private const string DEBUG_FLAG = "[GameAudio]";
        private IMemoryPool _pool;
        private GameAudioData _data = default;
        private IAudioLoadService audioLoadService;
        private AudioSource audioSource;
        private AudioClip audioClip = null;
        private static long currentAudioClipLength = 0;

        [Inject]
        public void Construct(IAudioLoadService audioLoadService)
        {
            this.audioLoadService = audioLoadService;
        }

        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }

        void OnDestroy()
        {
            Dispose();
        }

        public GameAudioSource(IAudioLoadService audioLoadService)
        {
            this.audioLoadService = audioLoadService;
        }

        public void Play()
        {
            if (_data.Equals(default)) return;

            if (!audioLoadService.GetLoadedClips().TryGetValue(_data.Key, out audioClip))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Audio clip not found for key: {_data.Key}");
                return;
            }

            audioSource.clip = audioClip;
            currentAudioClipLength = (long)(audioClip.length * 1000f);
            audioSource.Play();
        }

        public void Stop()
        {
            audioSource.Stop();
            currentAudioClipLength = 0;
        }

        public void Pause()
        {
            audioSource.Pause();
        }

        public void Resume()
        {
            audioSource.UnPause();
        }

        public long GetPlaybackTimeMSec()
        {
            return (long)(audioSource.time * 1000f);
        }

        public long GetAudioClipLengthMSec()
        {
            return currentAudioClipLength;
        }

        public void SeekTime(long milliSeconds)
        {
            audioSource.time = milliSeconds / 1000f;
        }

        public void OnDespawned()
        {
            _data = default;
            _pool = null;
        }

        public void OnSpawned(GameAudioData data, IMemoryPool pool)
        {
            _data = data;
            _pool = pool;
        }

        public void Dispose()
        {
            audioSource?.Stop();
            audioClip = null;

            _pool?.Despawn(this);
        }

        public void Tick()
        {

        }
    }
}