using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;

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
        public enum AudioState
        {
            NotLoaded,  // The audio clip has not been loaded yet.
            Loading,    // The audio clip is currently being loaded.
            Loaded,     // The audio clip has been successfully loaded.
            Unloading   // The audio clip is currently being unloaded.
        }

        /// <summary>
        /// Represents the category of an audio clip.
        /// </summary>
        public enum AudioCategory
        {
            Music,
            SFX
        }

        // Dictionary to store loaded AudioClips, categorized by path and category.
        private Dictionary<AudioCategory, Dictionary<string, AudioClip>> loadedClips = new Dictionary<AudioCategory, Dictionary<string, AudioClip>>();

        // Dictionary to track the loading state of audio clips.
        private Dictionary<AudioCategory, Dictionary<string, AudioState>> audioStates = new Dictionary<AudioCategory, Dictionary<string, AudioState>>();

        // Object pool to cache unloaded AudioClips with their unload timestamps.
        private Dictionary<AudioCategory, Dictionary<string, (AudioClip clip, float unloadTime)>> audioClipPool = new Dictionary<AudioCategory, Dictionary<string, (AudioClip, float)>>();

        // Dictionary to define the maximum pool size for each audio category.
        public Dictionary<AudioCategory, int> poolSizes = new Dictionary<AudioCategory, int>
        {
            { AudioCategory.Music, 4 },  // Default pool size for Music.
            { AudioCategory.SFX, 32 }    // Default pool size for SFX.
        };

        // Dictionary to track the playback state of audio clips.
        private Dictionary<AudioCategory, Dictionary<string, bool>> isPlayingMap = new Dictionary<AudioCategory, Dictionary<string, bool>>();

        // Dictionary to manage loading tasks and prevent duplicate concurrent loads.
        private Dictionary<AudioCategory, Dictionary<string, UniTaskCompletionSource<AudioClip>>> loadingTasks = new Dictionary<AudioCategory, Dictionary<string, UniTaskCompletionSource<AudioClip>>>();

        private IAudioLoader audioLoader;

        // Public getters for internal dictionaries (for debugging or external access).
        public Dictionary<AudioCategory, Dictionary<string, AudioClip>> GetLoadedClips() => loadedClips;
        public Dictionary<AudioCategory, Dictionary<string, AudioState>> GetAudioStates() => audioStates;
        public Dictionary<AudioCategory, Dictionary<string, (AudioClip clip, float unloadTime)>> GetAudioClipPool() => audioClipPool;
        public Dictionary<AudioCategory, Dictionary<string, bool>> GetIsPlayingMap() => isPlayingMap;

        // Event to notify when an audio clip needs to be stopped before unloading.
        public event Action<AudioClip> OnStopAudioRequested;

        public static AudioManager Instance { get; private set; }

        [SerializeField] bool _singleton = true;

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

            // Initialize dictionaries for each audio category.
            foreach (AudioCategory category in Enum.GetValues(typeof(AudioCategory)))
            {
                loadedClips[category] = new Dictionary<string, AudioClip>();
                audioStates[category] = new Dictionary<string, AudioState>();
                audioClipPool[category] = new Dictionary<string, (AudioClip, float)>();
                isPlayingMap[category] = new Dictionary<string, bool>();
                loadingTasks[category] = new Dictionary<string, UniTaskCompletionSource<AudioClip>>();
            }
        }

        private void OnDestroy()
        {
            // Forcefully unload all audio clips when the AudioManager is destroyed.
            ForceUnloadAll();
        }

        /// <summary>
        /// Asynchronously loads an AudioClip from the specified path.
        /// Utilizes caching and pooling to optimize performance.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="category">The category of the audio clip.</param>
        /// <returns>The loaded AudioClip, or null if loading fails.</returns>
        public async UniTask<AudioClip> LoadAudioAsync(string path, AudioCategory category)
        {
            var loadedClipsCategory = loadedClips[category];
            var audioStatesCategory = audioStates[category];
            var audioClipPoolCategory = audioClipPool[category];
            var isPlayingMapCategory = isPlayingMap[category];
            var loadingTasksCategory = loadingTasks[category];

            // If the audio clip is already loaded, return it immediately.
            if (audioStatesCategory.TryGetValue(path, out var state))
            {
                if (state == AudioState.Loaded && loadedClipsCategory.TryGetValue(path, out var clip))
                {
                    return clip;
                }
                else if (state == AudioState.Loading)
                {
                    // Wait for the ongoing loading task to complete.
                    if (loadingTasksCategory.TryGetValue(path, out var tcs))
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        // Defensive: if no loading task is found, wait until the state changes.
                        await WaitForStateChange(path, AudioState.Loading, category);
                        return loadedClipsCategory.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                    }
                }
                else if (state == AudioState.Unloading)
                {
                    // Wait until unloading completes, then reload the audio clip.
                    await WaitForStateChange(path, AudioState.Unloading, category);
                    return await LoadAudioAsync(path, category);
                }
            }

            // Check if the audio clip is in the pool.
            if (audioClipPoolCategory.TryGetValue(path, out var poolEntry))
            {
                // Move the clip from the pool to the loaded dictionary.
                loadedClipsCategory[path] = poolEntry.clip;
                audioStatesCategory[path] = AudioState.Loaded;
                audioClipPoolCategory.Remove(path);
                isPlayingMapCategory[path] = false;
                return poolEntry.clip;
            }

            // If the pool is full, unload the oldest unused clip.
            if (audioClipPoolCategory.Count >= poolSizes[category])
            {
                string oldestUnusedPath = null;
                float oldestTime = float.MaxValue;

                foreach (var kvp in audioClipPoolCategory)
                {
                    bool isPlaying = isPlayingMapCategory.ContainsKey(kvp.Key) && isPlayingMapCategory[kvp.Key];
                    if (!isPlaying && kvp.Value.unloadTime < oldestTime)
                    {
                        oldestTime = kvp.Value.unloadTime;
                        oldestUnusedPath = kvp.Key;
                    }
                }

                if (!string.IsNullOrEmpty(oldestUnusedPath))
                {
                    Debug.Log($"Pool is full, unloading oldest unused audio: {oldestUnusedPath}");
                    await UnloadAudio(oldestUnusedPath, category);
                }
            }

            // Mark the audio clip as loading and create a loading task.
            audioStatesCategory[path] = AudioState.Loading;
            var completionSource = new UniTaskCompletionSource<AudioClip>();
            loadingTasksCategory[path] = completionSource;

            try
            {
                var audioType = GetAudioType(path);
                var clip = await audioLoader.LoadAudioAsync(path, audioType);

                if (clip != null)
                {
                    loadedClipsCategory[path] = clip;
                    audioStatesCategory[path] = AudioState.Loaded;
                    isPlayingMapCategory[path] = false;

                    completionSource.TrySetResult(clip);
                    loadingTasksCategory.Remove(path);

                    return clip;
                }
                else
                {
                    audioStatesCategory[path] = AudioState.NotLoaded;
                    completionSource.TrySetResult(null);
                    loadingTasksCategory.Remove(path);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception loading audio '{path}': {ex}");
                audioStatesCategory[path] = AudioState.NotLoaded;
                completionSource.TrySetResult(null);
                loadingTasksCategory.Remove(path);
                return null;
            }
        }

        /// <summary>
        /// Asynchronously unloads an AudioClip from memory.
        /// If the clip is not playing, it will be cached in the pool.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="category">The category of the audio clip.</param>
        public async UniTask UnloadAudio(string path, AudioCategory category)
        {
            var audioStatesCategory = audioStates[category];
            var loadedClipsCategory = loadedClips[category];
            var isPlayingMapCategory = isPlayingMap[category];
            var audioClipPoolCategory = audioClipPool[category];

            if (!audioStatesCategory.TryGetValue(path, out var state))
                return;

            if (state == AudioState.Loading)
            {
                // Wait for loading to finish before unloading.
                await WaitForStateChange(path, AudioState.Loading, category);
                // Re-check state after waiting.
                state = audioStatesCategory.TryGetValue(path, out var newState) ? newState : AudioState.NotLoaded;
                if (state != AudioState.Loaded)
                    return;
            }

            if (state == AudioState.Loaded)
            {
                audioStatesCategory[path] = AudioState.Unloading;

                if (loadedClipsCategory.TryGetValue(path, out var clip))
                {
                    bool isPlaying = isPlayingMapCategory.ContainsKey(path) && isPlayingMapCategory[path];

                    if (!isPlaying)
                    {
                        if (audioClipPoolCategory.Count < poolSizes[category])
                        {
                            // Cache the clip in the pool with the current time.
                            audioClipPoolCategory[path] = (clip, Time.time);
                        }
                        else
                        {
                            // If the pool is full, destroy the clip to free memory.
                            Destroy(clip);
                        }
                    }
                    else
                    {
                        // If the clip is playing, skip unloading and keep it loaded.
                        Debug.LogWarning($"Attempted to unload audio '{path}' which is currently playing. Skipping unload.");
                        audioStatesCategory[path] = AudioState.Loaded;
                        return;
                    }

                    loadedClipsCategory.Remove(path);
                    isPlayingMapCategory.Remove(path);
                }

                audioStatesCategory[path] = AudioState.NotLoaded;
            }
        }

        /// <summary>
        /// Unloads all loaded audio clips asynchronously.
        /// </summary>
        public async UniTask UnloadAllAudio()
        {
            foreach (var category in loadedClips.Keys)
            {
                var paths = new List<string>(loadedClips[category].Keys);
                foreach (var path in paths)
                {
                    await UnloadAudio(path, category);
                }
            }
        }

        /// <summary>
        /// Forcefully unloads all loaded audio clips, regardless of their state.
        /// </summary>
        public void ForceUnloadAll()
        {
            foreach (var category in loadedClips.Keys)
            {
                // Unload all loaded AudioClips.
                foreach (var kvp in loadedClips[category])
                {
                    var path = kvp.Key;
                    var clip = kvp.Value;

                    if (clip != null)
                    {
                        // Check if the clip is playing.
                        if (isPlayingMap[category].TryGetValue(path, out var isPlaying) && isPlaying)
                        {
                            Debug.LogWarning($"AudioClip '{path}' is currently playing. Stopping before unloading.");

                            // Trigger the event to notify external classes to stop the clip.
                            OnStopAudioRequested?.Invoke(clip);
                        }

                        Destroy(clip);
                    }
                }
                loadedClips[category].Clear();

                // Unload all AudioClips in the pool.
                foreach (var kvp in audioClipPool[category])
                {
                    var clip = kvp.Value.clip;
                    if (clip != null)
                    {
                        Destroy(clip);
                    }
                }
                audioClipPool[category].Clear();

                // Clear all states and tasks.
                audioStates[category].Clear();
                isPlayingMap[category].Clear();
                loadingTasks[category].Clear();
            }
        }

        /// <summary>
        /// Retrieves the current loading state of an audio clip.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="category">The category of the audio clip.</param>
        /// <returns>The current AudioState of the clip.</returns>
        public AudioState GetAudioState(string path, AudioCategory category)
        {
            return audioStates[category].TryGetValue(path, out var state) ? state : AudioState.NotLoaded;
        }

        /// <summary>
        /// Sets the playing state of an audio clip.
        /// Adds the entry if it does not exist.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="isPlaying">Whether the clip is currently playing.</param>
        /// <param name="category">The category of the audio clip.</param>
        public void SetPlayingState(string path, bool isPlaying, AudioCategory category)
        {
            isPlayingMap[category][path] = isPlaying;
        }

        /// <summary>
        /// Waits until the audio state changes from a specific state.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <param name="waitingState">The state to wait for a change from.</param>
        /// <param name="category">The category of the audio clip.</param>
        private async UniTask WaitForStateChange(string path, AudioState waitingState, AudioCategory category)
        {
            while (audioStates[category].TryGetValue(path, out var state) && state == waitingState)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// Determines the AudioType based on the file extension.
        /// </summary>
        /// <param name="path">The path to the audio file.</param>
        /// <returns>The corresponding AudioType.</returns>
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