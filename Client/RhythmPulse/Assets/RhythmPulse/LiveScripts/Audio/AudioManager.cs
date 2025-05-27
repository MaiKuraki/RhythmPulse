using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Addler.Runtime.Core.LifetimeBinding;
using CycloneGames.Logger;

namespace RhythmPulse.Audio
{
    /// <summary>
    /// Interface for loading audio clips asynchronously.
    /// </summary>
    public interface IAudioLoader
    {
        UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType);
    }

    /// <summary>
    /// Implementation of IAudioLoader using Unity's UnityWebRequest to load audio clips.
    /// </summary>
    public class UnityAudioLoader : IAudioLoader
    {
        /// <summary>
        /// Asynchronously loads an audio clip from the specified path.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="audioType">The type of audio file to load.</param>
        /// <returns>The loaded AudioClip, or null if loading fails.</returns>
        public async UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType)
        {
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, audioType))
            {
                await www.SendWebRequest().ToUniTask();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"Error loading audio '{path}': {www.error}");
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }
    }

    /// <summary>
    /// Manages the loading, unloading, and playback of audio clips.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        /// <summary>
        /// Represents the current state of an audio clip.
        /// </summary>
        public enum AudioLoadState
        {
            NotLoaded,  // The audio clip has not been loaded yet.
            Loading,    // The audio clip is currently being loaded.
            Loaded,     // The audio clip has been successfully loaded.
            Unloading   // The audio clip is currently being unloaded.
        }

        // Dictionary to store loaded AudioClips 
        private Dictionary<string, AudioClip> loadedClips = new Dictionary<string, AudioClip>();

        // Dictionary to track the loading state of audio clips 
        private Dictionary<string, AudioLoadState> audioLoadStates = new Dictionary<string, AudioLoadState>();

        // Dictionary to manage loading tasks and prevent duplicate concurrent loads 
        private Dictionary<string, UniTaskCompletionSource<AudioClip>> loadingTasks = new Dictionary<string, UniTaskCompletionSource<AudioClip>>();

        // Dictionary to track memory usage of loaded audio clips (in bytes)
        private Dictionary<string, long> audioMemoryUsage = new Dictionary<string, long>();

        private IAudioLoader audioLoader;

        // Public getters for internal dictionaries (for debugging or external access)
        public Dictionary<string, AudioClip> GetLoadedClips() => loadedClips;
        public Dictionary<string, AudioLoadState> GetAudioStates() => audioLoadStates;
        public Dictionary<string, long> GetAudioMemoryUsage() => audioMemoryUsage;

        /// <summary>
        /// Gets the total memory used by all loaded audio clips in bytes.
        /// </summary>
        public long TotalMemoryUsage { get; private set; } = 0;

        public static AudioManager Instance { get; private set; }

        [SerializeField] private bool _singleton = true;
        [SerializeField] private string AudioSourcePrefabPath = "Assets/RhythmPulse/LiveContent/Prefabs/Audio/AudioSource.prefab";
        GameAudioSource audioSourcePrefab;
        public GameAudioSource AudioSourcePrefab => audioSourcePrefab;
        public bool IsInitialized => audioSourceReady;
        bool audioSourceReady = false;

        private void Awake()
        {
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }

                Instance = this;
                DontDestroyOnLoad(gameObject);
            }

            audioLoader = new UnityAudioLoader();
            audioSourceReady = false;
            LoadAudioSouceAsync().Forget();
        }

        private void OnDestroy()
        {
            // Forcefully unload all audio clips when the AudioManager is destroyed 
            ForceUnloadAll();
        }

        private async UniTask LoadAudioSouceAsync()
        {
            try
            {
                AsyncOperationHandle<GameObject> loadHandle =
                    Addressables.LoadAssetAsync<GameObject>(AudioSourcePrefabPath);

                await loadHandle.BindTo(this.gameObject);
                await loadHandle.ToUniTask(PlayerLoopTiming.Update, destroyCancellationToken);

                if (loadHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    audioSourcePrefab = loadHandle.Result.GetComponent<GameAudioSource>();
                    if (audioSourcePrefab != null)
                    {
                        audioSourceReady = true;
                    }
                    else
                    {
                        CLogger.LogError($"Loaded prefab from '{AudioSourcePrefabPath}' is missing the GameAudioSource component.");
                    }
                }
                else
                {
                    CLogger.LogError($"Failed to load AudioSourcePrefab from '{AudioSourcePrefabPath}'. Status: {loadHandle.Status}, Error: {loadHandle.OperationException?.Message}");
                    // audioSourcePrefab will remain null
                }
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning($"AudioSourcePrefab loading was cancelled, likely because the AudioManager GameObject was destroyed.");
            }
            catch (Exception ex)
            {
                CLogger.LogError($"An unexpected error occurred while loading AudioSourcePrefab: {ex}");
            }
        }

        /// <summary>
        /// Calculates the approximate memory usage of an AudioClip in bytes.
        /// </summary>
        private long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;

            // Memory calculation based on: 
            // samples * channels * (bits per sample / 8)
            // Unity typically uses 16-bit samples (2 bytes per sample)
            return clip.samples * clip.channels * 2;
        }

        /// <summary>
        /// Updates the memory usage statistics for a specific audio clip.
        /// </summary>
        private void UpdateMemoryUsage(string path, AudioClip clip)
        {
            if (clip == null)
            {
                if (audioMemoryUsage.ContainsKey(path))
                {
                    TotalMemoryUsage -= audioMemoryUsage[path];
                    audioMemoryUsage.Remove(path);
                }
                return;
            }

            long memory = CalculateAudioClipMemoryUsage(clip);
            if (audioMemoryUsage.TryGetValue(path, out long existingMemory))
            {
                TotalMemoryUsage -= existingMemory;
            }

            audioMemoryUsage[path] = memory;
            TotalMemoryUsage += memory;
        }

        /// <summary>
        /// Asynchronously loads an AudioClip from the specified path.
        /// </summary>
        public async UniTask<AudioClip> LoadAudioAsync(string path)
        {
            // If the audio clip is already loaded, return it immediately 
            if (audioLoadStates.TryGetValue(path, out var state))
            {
                if (state == AudioLoadState.Loaded && loadedClips.TryGetValue(path, out var clip))
                {
                    return clip;
                }
                else if (state == AudioLoadState.Loading)
                {
                    // Wait for the ongoing loading task to complete 
                    if (loadingTasks.TryGetValue(path, out var tcs))
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        // Defensive: if no loading task is found, wait until the state changes 
                        await WaitForStateChange(path, AudioLoadState.Loading);
                        return loadedClips.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                    }
                }
                else if (state == AudioLoadState.Unloading)
                {
                    // Wait until unloading completes, then reload the audio clip 
                    await WaitForStateChange(path, AudioLoadState.Unloading);
                    return await LoadAudioAsync(path);
                }
            }

            // Mark the audio clip as loading and create a loading task 
            audioLoadStates[path] = AudioLoadState.Loading;
            var completionSource = new UniTaskCompletionSource<AudioClip>();
            loadingTasks[path] = completionSource;

            try
            {
                var audioType = GetAudioType(path);
                var clip = await audioLoader.LoadAudioAsync(path, audioType);

                if (clip != null)
                {
                    loadedClips[path] = clip;
                    audioLoadStates[path] = AudioLoadState.Loaded;
                    UpdateMemoryUsage(path, clip); // Update memory usage

                    completionSource.TrySetResult(clip);
                    loadingTasks.Remove(path);

                    return clip;
                }
                else
                {
                    audioLoadStates[path] = AudioLoadState.NotLoaded;
                    completionSource.TrySetResult(null);
                    loadingTasks.Remove(path);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception loading audio '{path}': {ex}");
                audioLoadStates[path] = AudioLoadState.NotLoaded;
                completionSource.TrySetResult(null);
                loadingTasks.Remove(path);
                return null;
            }
        }

        /// <summary>
        /// Asynchronously unloads an AudioClip from memory.
        /// If the clip is successfully unloaded, its entry will be removed from tracking.
        /// If called on a path already marked as 'NotLoaded', its entry will also be removed.
        /// </summary>
        public async UniTask UnloadAudio(string path)
        {
            if (!audioLoadStates.TryGetValue(path, out var currentState))
            {
                // Path is not tracked or already fully unloaded and removed.
                return;
            }

            var state = currentState; // Use a local variable for state that can be updated

            if (state == AudioLoadState.Loading)
            {
                // Wait for the ongoing loading task to complete.
                if (loadingTasks.TryGetValue(path, out var tcs))
                {
                    await tcs.Task; // Wait for the load attempt to finish
                }
                else
                {
                    // Fallback: if no specific task, wait for state change.
                    // This might happen if the task was removed prematurely.
                    await WaitForStateChange(path, AudioLoadState.Loading);
                }

                // After waiting, re-fetch the state as it might have changed by the loading process or another call.
                if (!audioLoadStates.TryGetValue(path, out state))
                {
                    // Entry was removed during our wait (e.g., by ForceUnloadAll or another UnloadAudio call)
                    return;
                }
                // Note: LoadAudioAsync should have already removed the entry from loadingTasks upon its completion or failure.
            }

            if (state == AudioLoadState.Loaded)
            {
                audioLoadStates[path] = AudioLoadState.Unloading;
                // Consider UniTask.Yield() here if other systems need to react to the 'Unloading' state
                await UniTask.Yield();

                if (loadedClips.TryGetValue(path, out var clip))
                {
                    UpdateMemoryUsage(path, null); // Clears memory usage and removes from audioMemoryUsage
                    Destroy(clip);                 // Destroy the AudioClip object
                    loadedClips.Remove(path);      // Remove from loadedClips
                }

                // immediately remove data in dictionary
                audioLoadStates.Remove(path);
                // clear tasks
                loadingTasks.Remove(path);
            }
            else if (state == AudioLoadState.NotLoaded)
            {
                audioLoadStates.Remove(path);
                loadingTasks.Remove(path);
            }
        }

        /// <summary>
        /// Unloads all loaded audio clips asynchronously.
        /// </summary>
        public async UniTask UnloadAllAudio()
        {
            var paths = new List<string>(loadedClips.Keys);
            foreach (var path in paths)
            {
                await UnloadAudio(path);
            }
        }

        /// <summary>
        /// Forcefully unloads all loaded audio clips, regardless of their state.
        /// </summary>
        public void ForceUnloadAll()
        {
            // Unload all loaded AudioClips 
            foreach (var kvp in loadedClips)
            {
                var clip = kvp.Value;
                if (clip != null)
                {
                    Destroy(clip);
                }
            }
            loadedClips.Clear();

            // Clear all states and tasks 
            audioLoadStates.Clear();
            loadingTasks.Clear();
            audioMemoryUsage.Clear();
            TotalMemoryUsage = 0;
        }

        /// <summary>
        /// Retrieves the current loading state of an audio clip.
        /// </summary>
        public AudioLoadState GetAudioState(string path)
        {
            return audioLoadStates.TryGetValue(path, out var state) ? state : AudioLoadState.NotLoaded;
        }

        /// <summary>
        /// Waits until the audio state changes from a specific state.
        /// </summary>
        private async UniTask WaitForStateChange(string path, AudioLoadState waitingState)
        {
            while (audioLoadStates.TryGetValue(path, out var state) && state == waitingState)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// Determines the AudioType based on the file extension.
        /// </summary>
        private AudioType GetAudioType(string path)
        {
            path = path.ToLowerInvariant();
            if (path.EndsWith(".mp3")) return AudioType.MPEG;
            if (path.EndsWith(".wav")) return AudioType.WAV;
            if (path.EndsWith(".ogg")) return AudioType.OGGVORBIS;
            if (path.EndsWith(".aiff") || path.EndsWith(".aif")) return AudioType.AIFF;
            if (path.EndsWith(".xm") || path.EndsWith(".mod") || path.EndsWith(".it") || path.EndsWith(".s3m")) return AudioType.MOD;

            Debug.LogWarning($"Unsupported audio format: {path}");
            return AudioType.UNKNOWN;
        }
    }
}