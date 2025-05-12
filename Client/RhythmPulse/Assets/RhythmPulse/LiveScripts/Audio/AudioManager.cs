using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;

namespace RhythmPulse.Audio
{
    public class AudioManager : MonoBehaviour
    {
        public enum AudioState
        {
            NotLoaded,  // Not loaded yet
            Loading,    // Currently loading
            Loaded,     // Loaded successfully
            Unloading   // Currently unloading
        }

        public enum AudioCategory
        {
            Music,
            SFX
        }

        // Loaded AudioClips mapped by path and category
        private Dictionary<AudioCategory, Dictionary<string, AudioClip>> loadedClips = new Dictionary<AudioCategory, Dictionary<string, AudioClip>>();

        // Audio loading states
        private Dictionary<AudioCategory, Dictionary<string, AudioState>> audioStates = new Dictionary<AudioCategory, Dictionary<string, AudioState>>();

        // Object pool storing unloaded but cached AudioClips with unload timestamp
        private Dictionary<AudioCategory, Dictionary<string, (AudioClip clip, float unloadTime)>> audioClipPool = new Dictionary<AudioCategory, Dictionary<string, (AudioClip, float)>>();

        // Pool size limit for each category
        public Dictionary<AudioCategory, int> poolSizes = new Dictionary<AudioCategory, int>
        {
            { AudioCategory.Music, 5 },  // Default pool size for Music
            { AudioCategory.SFX, 10 }    // Default pool size for SFX
        };

        // Playback state tracking: true if the clip is playing
        private Dictionary<AudioCategory, Dictionary<string, bool>> isPlayingMap = new Dictionary<AudioCategory, Dictionary<string, bool>>();

        // Loading tasks to prevent duplicate concurrent loads
        private Dictionary<AudioCategory, Dictionary<string, UniTaskCompletionSource<AudioClip>>> loadingTasks = new Dictionary<AudioCategory, Dictionary<string, UniTaskCompletionSource<AudioClip>>>();

        public Dictionary<AudioCategory, Dictionary<string, AudioClip>> GetLoadedClips() => loadedClips;
        public Dictionary<AudioCategory, Dictionary<string, AudioState>> GetAudioStates() => audioStates;
        public Dictionary<AudioCategory, Dictionary<string, (AudioClip clip, float unloadTime)>> GetAudioClipPool() => audioClipPool;
        public Dictionary<AudioCategory, Dictionary<string, bool>> GetIsPlayingMap() => isPlayingMap;

        private void Awake()
        {
            // Initialize dictionaries for each category
            foreach (AudioCategory category in Enum.GetValues(typeof(AudioCategory)))
            {
                loadedClips[category] = new Dictionary<string, AudioClip>();
                audioStates[category] = new Dictionary<string, AudioState>();
                audioClipPool[category] = new Dictionary<string, (AudioClip, float)>();
                isPlayingMap[category] = new Dictionary<string, bool>();
                loadingTasks[category] = new Dictionary<string, UniTaskCompletionSource<AudioClip>>();
            }
        }

        async void OnDestroy()
        {
            await UnloadAllAudio();
        }

        /// <summary>
        /// Asynchronously loads an AudioClip from the specified path.
        /// Uses caching and pooling to optimize performance.
        /// </summary>
        public async UniTask<AudioClip> LoadAudioAsync(string path, AudioCategory category)
        {
            var loadedClipsCategory = loadedClips[category];
            var audioStatesCategory = audioStates[category];
            var audioClipPoolCategory = audioClipPool[category];
            var isPlayingMapCategory = isPlayingMap[category];
            var loadingTasksCategory = loadingTasks[category];

            // If already loaded, return immediately
            if (audioStatesCategory.TryGetValue(path, out var state))
            {
                if (state == AudioState.Loaded && loadedClipsCategory.TryGetValue(path, out var clip))
                {
                    return clip;
                }
                else if (state == AudioState.Loading)
                {
                    // Wait for ongoing loading task to complete
                    if (loadingTasksCategory.TryGetValue(path, out var tcs))
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        // Defensive: no loading task found, wait until state changes
                        await WaitForStateChange(path, AudioState.Loading, category);
                        return loadedClipsCategory.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                    }
                }
                else if (state == AudioState.Unloading)
                {
                    // Wait until unloading completes, then reload
                    await WaitForStateChange(path, AudioState.Unloading, category);
                    return await LoadAudioAsync(path, category);
                }
            }

            // Check if clip is in pool
            if (audioClipPoolCategory.TryGetValue(path, out var poolEntry))
            {
                // Move from pool to loaded
                loadedClipsCategory[path] = poolEntry.clip;
                audioStatesCategory[path] = AudioState.Loaded;
                audioClipPoolCategory.Remove(path);
                isPlayingMapCategory[path] = false;
                return poolEntry.clip;
            }

            // Pool full? Unload oldest unused clip
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

            // Mark as loading and create a loading task
            audioStatesCategory[path] = AudioState.Loading;
            var completionSource = new UniTaskCompletionSource<AudioClip>();
            loadingTasksCategory[path] = completionSource;

            try
            {
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, GetAudioType(path)))
                {
                    await www.SendWebRequest().ToUniTask();

                    if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Debug.LogError($"Error loading audio '{path}': {www.error}");
                        audioStatesCategory[path] = AudioState.NotLoaded;
                        completionSource.TrySetResult(null);
                        loadingTasksCategory.Remove(path);
                        return null;
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    loadedClipsCategory[path] = clip;
                    audioStatesCategory[path] = AudioState.Loaded;
                    isPlayingMapCategory[path] = false;

                    completionSource.TrySetResult(clip);
                    loadingTasksCategory.Remove(path);

                    return clip;
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
                // Wait for loading to finish before unloading
                await WaitForStateChange(path, AudioState.Loading, category);
                // Re-check state after waiting
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
                            // Cache clip in pool with current time
                            audioClipPoolCategory[path] = (clip, Time.time);
                        }
                        else
                        {
                            // Pool full, destroy clip to free memory
                            Destroy(clip);
                        }
                    }
                    else
                    {
                        // If playing, do not cache, just keep loaded
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
        /// Gets the current loading state of the audio clip.
        /// </summary>
        public AudioState GetAudioState(string path, AudioCategory category)
        {
            return audioStates[category].TryGetValue(path, out var state) ? state : AudioState.NotLoaded;
        }

        /// <summary>
        /// Sets the playing state of the audio clip.
        /// Adds the entry if it does not exist.
        /// </summary>
        public void SetPlayingState(string path, bool isPlaying, AudioCategory category)
        {
            isPlayingMap[category][path] = isPlaying;
        }

        /// <summary>
        /// Helper method to wait until the audio state changes from a specific state.
        /// </summary>
        private async UniTask WaitForStateChange(string path, AudioState waitingState, AudioCategory category)
        {
            while (audioStates[category].TryGetValue(path, out var state) && state == waitingState)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// Determines the AudioType based on file extension.
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