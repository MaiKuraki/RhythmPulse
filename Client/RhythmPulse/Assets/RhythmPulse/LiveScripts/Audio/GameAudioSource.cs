using CycloneGames.Logger;
using UnityEngine;
using VContainer;
using CycloneGames.Factory.Runtime;
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
        private long instanceAudioClipLength = 0;

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

        public void SetLoop(bool loop)
        {
            audioSource.loop = loop;
        }

        public void Play()
        {
            if (_data.Equals(default(GameAudioData))) return;

            if (!audioLoadService.GetLoadedClips().TryGetValue(_data.Key, out audioClip))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Audio clip not found for key: {_data.Key}");
                return;
            }
            if (audioLoadService == null || !audioLoadService.GetLoadedClips().TryGetValue(_data.Key, out audioClip))
            {
                // CycloneGames.Logger.CLogger.LogError($"{DEBUG_FLAG} Audio clip not found for key: {_data.Key} or audioLoadService is null.");
                return;
            }
            if (audioClip == null)
            {
                // CycloneGames.Logger.CLogger.LogError($"{DEBUG_FLAG} Loaded audio clip is null for key: {_data.Key}.");
                return;
            }
            if (audioSource == null)
            {
                // CycloneGames.Logger.CLogger.LogError($"{DEBUG_FLAG} AudioSource component is null.");
                return;
            }

            audioSource.clip = audioClip;
            instanceAudioClipLength = (long)(audioClip.length * 1000f);
            audioSource.Play();
        }

        public void Stop()
        {
            audioSource?.Stop();
            instanceAudioClipLength = 0;
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
            return instanceAudioClipLength;
        }

        public void SeekTime(long milliSeconds)
        {
            audioSource.time = milliSeconds / 1000f;
        }

        public void OnDespawned()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }

            _data = default;
            _pool = null;
            this.gameObject.SetActive(false);
        }

        public void OnSpawned(GameAudioData data, IMemoryPool pool)
        {
            this._data = data;
            this._pool = pool;
            this.gameObject.SetActive(true);
        }

        public void Dispose()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
            audioClip = null;
            instanceAudioClipLength = 0;

            _pool?.Despawn(this);
        }

        public void Tick()
        {

        }
    }
}