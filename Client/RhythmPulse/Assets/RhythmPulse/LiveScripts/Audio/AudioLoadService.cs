using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RhythmPulse.Audio
{
    public interface IAudioLoadService
    {
        UniTask<AudioClip> LoadAudioAsync(string path);
        UniTask UnloadAudio(string path);
        UniTask UnloadAllAudio();
        void ForceUnloadAll();
        GameAudioSource AudioSourcePrefab { get; }
        bool TryGetLoadedClip(string key, out AudioClip clip);
    }

    public sealed class AudioLoadService : IAudioLoadService, IDisposable
    {
        private AudioManager _audioManager;
        private bool _isInitialized;
        private bool _isDisposed;

        public GameAudioSource AudioSourcePrefab => _audioManager?.AudioSourcePrefab;

        public AudioLoadService()
        {
            Initialize();
        }

        private void Initialize()
        {
            _audioManager = UnityEngine.Object.FindFirstObjectByType<AudioManager>();
            if (_audioManager == null)
            {
                Debug.LogError("[AudioLoadService] No AudioManager instance found in the scene");
                return;
            }
            _isInitialized = true;
        }

        public async UniTask<AudioClip> LoadAudioAsync(string path)
        {
            if (!_isInitialized || _audioManager == null) return null;
            return await _audioManager.LoadAudioAsync(path);
        }

        public async UniTask UnloadAudio(string path)
        {
            if (_audioManager != null)
                await _audioManager.UnloadAudio(path);
        }

        public async UniTask UnloadAllAudio()
        {
            if (_audioManager != null)
                await _audioManager.UnloadAllAudio();
        }

        public void ForceUnloadAll()
        {
            _audioManager?.ForceUnloadAll();
        }

        public bool TryGetLoadedClip(string key, out AudioClip clip)
        {
            clip = null;
            if (_audioManager == null) return false;
            return _audioManager.TryGetLoadedClip(key, out clip);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isInitialized = false;
            _audioManager = null;
        }
    }
}