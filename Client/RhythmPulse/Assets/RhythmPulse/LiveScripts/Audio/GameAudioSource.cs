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
		private bool isBeingDestroyed = false;

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
			isBeingDestroyed = true;
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

            if (audioLoadService == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} audioLoadService is null in Play() for key: {_data.Key}");
                return;
            }

            if (!audioLoadService.GetLoadedClips().TryGetValue(_data.Key, out audioClip))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Audio clip not found for key: {_data.Key}");
                return;
            }
            if (audioClip == null)
            {
                // CycloneGames.Logger.CLogger.LogError($"{DEBUG_FLAG} Loaded audio clip is null for key: {_data.Key}.");
                return;
            }
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    // CycloneGames.Logger.CLogger.LogError($"{DEBUG_FLAG} AudioSource component is null.");
                    return;
                }
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
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} OnSpawned: missing AudioSource component on {name}. Activating anyway to keep pool stable.");
                }
            }
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
			// Despawn only when not in OnDestroy to avoid teardown races on mobile
			if (!isBeingDestroyed)
			{
				_pool?.Despawn(this);
			}
        }

        public void Tick()
        {

        }
    }
}