using Cysharp.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace RhythmPulse.Audio
{
    public interface IAudioLoadService
    {
        UniTask<UnityEngine.AudioClip> LoadAudioAsync(string path);
        UniTask UnloadAudio(string path);
        UniTask UnloadAllAudio();
        void ForceUnloadAll();
        GameAudioSource AudioSourcePrefab { get; }
        Dictionary<string, AudioClip> GetLoadedClips();
    }


    public class AudioLoadService : IAudioLoadService, IDisposable
    {
        private const string DEBUG_FLAG = "[AudioLoadService]";
        private AudioManager audioManagerInstance;
        bool isInitialized = false;

        public GameAudioSource AudioSourcePrefab
        {
            get
            {
                return audioManagerInstance?.AudioSourcePrefab;
            }
        }

        public AudioLoadService()
        {
            Initialize();
        }

        /// <summary>
        /// To initialize this service, we need to find the AudioManager instance in the scene.
        /// </summary>
        private void Initialize()
        {
            audioManagerInstance = UnityEngine.GameObject.FindFirstObjectByType<AudioManager>();
            if (audioManagerInstance == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} No AudioManager instance found in the scene, please put an AudioManager prefab in the scene.");
                return;
            }

            isInitialized = true;
        }

        public async UniTask<AudioClip> LoadAudioAsync(string path)
        {
            return await audioManagerInstance.LoadAudioAsync(path);
        }

        public async UniTask UnloadAudio(string path)
        {
            await audioManagerInstance.UnloadAudio(path);
        }

        public async UniTask UnloadAllAudio()
        {
            await audioManagerInstance.UnloadAllAudio();
        }

        public void ForceUnloadAll()
        {
            audioManagerInstance.ForceUnloadAll();
        }

        public void Dispose()
        {
            isInitialized = false;
        }

        public Dictionary<string, AudioClip> GetLoadedClips()
        {
            return audioManagerInstance?.GetLoadedClips();
        }
    }
}