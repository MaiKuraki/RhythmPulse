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

        // Loaded AudioClips mapped by path
        private Dictionary<string, AudioClip> loadedClips = new Dictionary<string, AudioClip>();

        // Audio loading states
        private Dictionary<string, AudioState> audioStates = new Dictionary<string, AudioState>();

        // Object pool storing unloaded but cached AudioClips with unload timestamp
        private Dictionary<string, (AudioClip clip, float unloadTime)> audioClipPool = new Dictionary<string, (AudioClip, float)>();

        // Pool size limit
        public int poolSize = 5;

        // Playback state tracking: true if the clip is playing
        private Dictionary<string, bool> isPlayingMap = new Dictionary<string, bool>();

        public Dictionary<string, AudioClip> GetLoadedClips()
        {
            return loadedClips;
        }

        public Dictionary<string, AudioState> GetAudioStates()
        {
            return audioStates;
        }

        public Dictionary<string, (AudioClip clip, float unloadTime)> GetAudioClipPool()
        {
            return audioClipPool;
        }

        public Dictionary<string, bool> GetIsPlayingMap()
        {
            return isPlayingMap;
        }

        // Loading tasks to prevent duplicate concurrent loads
        private Dictionary<string, UniTaskCompletionSource<AudioClip>> loadingTasks = new Dictionary<string, UniTaskCompletionSource<AudioClip>>();

        async void OnDestroy()
        {
            await UnloadAllAudio();
        }

        /// <summary>
        /// Asynchronously loads an AudioClip from the specified path.
        /// Uses caching and pooling to optimize performance.
        /// </summary>
        public async UniTask<AudioClip> LoadAudioAsync(string path)
        {
            // If already loaded, return immediately
            if (audioStates.TryGetValue(path, out var state))
            {
                if (state == AudioState.Loaded && loadedClips.TryGetValue(path, out var clip))
                {
                    return clip;
                }
                else if (state == AudioState.Loading)
                {
                    // Wait for ongoing loading task to complete
                    if (loadingTasks.TryGetValue(path, out var tcs))
                    {
                        return await tcs.Task;
                    }
                    else
                    {
                        // Defensive: no loading task found, wait until state changes
                        await WaitForStateChange(path, AudioState.Loading);
                        return loadedClips.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                    }
                }
                else if (state == AudioState.Unloading)
                {
                    // Wait until unloading completes, then reload
                    await WaitForStateChange(path, AudioState.Unloading);
                    return await LoadAudioAsync(path);
                }
            }

            // Check if clip is in pool
            if (audioClipPool.TryGetValue(path, out var poolEntry))
            {
                // Move from pool to loaded
                loadedClips[path] = poolEntry.clip;
                audioStates[path] = AudioState.Loaded;
                audioClipPool.Remove(path);
                isPlayingMap[path] = false;
                return poolEntry.clip;
            }

            // Pool full? Unload oldest unused clip
            if (audioClipPool.Count >= poolSize)
            {
                string oldestUnusedPath = null;
                float oldestTime = float.MaxValue;

                foreach (var kvp in audioClipPool)
                {
                    bool isPlaying = isPlayingMap.ContainsKey(kvp.Key) && isPlayingMap[kvp.Key];
                    if (!isPlaying && kvp.Value.unloadTime < oldestTime)
                    {
                        oldestTime = kvp.Value.unloadTime;
                        oldestUnusedPath = kvp.Key;
                    }
                }

                if (!string.IsNullOrEmpty(oldestUnusedPath))
                {
                    Debug.Log($"Pool is full, unloading oldest unused audio: {oldestUnusedPath}");
                    await UnloadAudio(oldestUnusedPath);
                }
            }

            // Mark as loading and create a loading task
            audioStates[path] = AudioState.Loading;
            var completionSource = new UniTaskCompletionSource<AudioClip>();
            loadingTasks[path] = completionSource;

            try
            {
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, GetAudioType(path)))
                {
                    await www.SendWebRequest().ToUniTask();

                    if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Debug.LogError($"Error loading audio '{path}': {www.error}");
                        audioStates[path] = AudioState.NotLoaded;
                        completionSource.TrySetResult(null);
                        loadingTasks.Remove(path);
                        return null;
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(www);
                    loadedClips[path] = clip;
                    audioStates[path] = AudioState.Loaded;
                    isPlayingMap[path] = false;

                    completionSource.TrySetResult(clip);
                    loadingTasks.Remove(path);

                    return clip;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception loading audio '{path}': {ex}");
                audioStates[path] = AudioState.NotLoaded;
                completionSource.TrySetResult(null);
                loadingTasks.Remove(path);
                return null;
            }
        }

        /// <summary>
        /// Asynchronously unloads an AudioClip from memory.
        /// If the clip is not playing, it will be cached in the pool.
        /// </summary>
        public async UniTask UnloadAudio(string path)
        {
            if (!audioStates.TryGetValue(path, out var state))
                return;

            if (state == AudioState.Loading)
            {
                // Wait for loading to finish before unloading
                await WaitForStateChange(path, AudioState.Loading);
                // Re-check state after waiting
                state = audioStates.TryGetValue(path, out var newState) ? newState : AudioState.NotLoaded;
                if (state != AudioState.Loaded)
                    return;
            }

            if (state == AudioState.Loaded)
            {
                audioStates[path] = AudioState.Unloading;

                if (loadedClips.TryGetValue(path, out var clip))
                {
                    bool isPlaying = isPlayingMap.ContainsKey(path) && isPlayingMap[path];

                    if (!isPlaying)
                    {
                        if (audioClipPool.Count < poolSize)
                        {
                            // Cache clip in pool with current time
                            audioClipPool[path] = (clip, Time.time);
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
                        audioStates[path] = AudioState.Loaded;
                        return;
                    }

                    loadedClips.Remove(path);
                    isPlayingMap.Remove(path);
                }

                audioStates[path] = AudioState.NotLoaded;
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
        /// Gets the current loading state of the audio clip.
        /// </summary>
        public AudioState GetAudioState(string path)
        {
            return audioStates.TryGetValue(path, out var state) ? state : AudioState.NotLoaded;
        }

        /// <summary>
        /// Sets the playing state of the audio clip.
        /// Adds the entry if it does not exist.
        /// </summary>
        public void SetPlayingState(string path, bool isPlaying)
        {
            isPlayingMap[path] = isPlaying;
        }

        /// <summary>
        /// Helper method to wait until the audio state changes from a specific state.
        /// </summary>
        private async UniTask WaitForStateChange(string path, AudioState waitingState)
        {
            while (audioStates.TryGetValue(path, out var state) && state == waitingState)
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