using System;
using System.Collections.Generic;
using Addler.Runtime.Core.LifetimeBinding;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RhythmPulse.Audio
{
    public interface IAudioLoader
    {
        UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType);
    }

    public sealed class UnityAudioLoader : IAudioLoader
    {
        public async UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType)
        {
            using var www = UnityWebRequestMultimedia.GetAudioClip(path, audioType);
            await www.SendWebRequest().ToUniTask();

            if (www.result == UnityWebRequest.Result.ConnectionError || 
                www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[UnityAudioLoader] Error loading audio: " + www.error);
                return null;
            }

            return DownloadHandlerAudioClip.GetContent(www);
        }
    }

    public sealed partial class AudioManager : MonoBehaviour
    {
        public enum AudioLoadState
        {
            NotLoaded,
            Loading,
            Loaded,
            Unloading
        }

        [SerializeField] private bool _singleton = true;
        [SerializeField] private string _audioSourcePrefabPath = "Assets/RhythmPulse/LiveContent/Prefabs/Audio/AudioSource.prefab";

        // Pre-allocated collections
        private readonly Dictionary<string, AudioClip> _loadedClips = new(32);
        private readonly Dictionary<string, AudioLoadState> _audioLoadStates = new(32);
        private readonly Dictionary<string, UniTaskCompletionSource<AudioClip>> _loadingTasks = new(8);
        private readonly Dictionary<string, long> _audioMemoryUsage = new(32);
        private readonly List<string> _tempPathList = new(32); // Reusable list for iterations

        private IAudioLoader _audioLoader;
        private GameAudioSource _audioSourcePrefab;
        private bool _audioSourceReady;

        public static AudioManager Instance { get; private set; }
        public GameAudioSource AudioSourcePrefab => _audioSourcePrefab;
        public bool IsInitialized => _audioSourceReady;
        public long TotalMemoryUsage { get; private set; }

        // Zero-GC accessors
        public bool TryGetLoadedClip(string key, out AudioClip clip) => _loadedClips.TryGetValue(key, out clip);
        public AudioLoadState GetAudioState(string path) => _audioLoadStates.TryGetValue(path, out var state) ? state : AudioLoadState.NotLoaded;

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

            _audioLoader = new UnityAudioLoader();
            _audioSourceReady = false;
            LoadAudioSourceAsync().Forget();
        }

        private void OnDestroy()
        {
            ForceUnloadAll();
        }

        private async UniTaskVoid LoadAudioSourceAsync()
        {
            try
            {
                var loadHandle = Addressables.LoadAssetAsync<GameObject>(_audioSourcePrefabPath);
                await loadHandle.BindTo(gameObject);
                await loadHandle.ToUniTask(PlayerLoopTiming.Update, destroyCancellationToken);

                if (loadHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    _audioSourcePrefab = loadHandle.Result.GetComponent<GameAudioSource>();
                    if (_audioSourcePrefab != null)
                    {
                        _audioSourceReady = true;
                    }
                    else
                    {
                        CLogger.LogError("[AudioManager] Loaded prefab missing GameAudioSource component");
                    }
                }
                else
                {
                    CLogger.LogError("[AudioManager] Failed to load AudioSourcePrefab");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CLogger.LogError("[AudioManager] Unexpected error loading AudioSourcePrefab: " + ex.Message);
            }
        }

        public async UniTask<AudioClip> LoadAudioAsync(string path)
        {
            if (_audioLoadStates.TryGetValue(path, out var state))
            {
                if (state == AudioLoadState.Loaded && _loadedClips.TryGetValue(path, out var existingClip))
                    return existingClip;

                if (state == AudioLoadState.Loading)
                {
                    if (_loadingTasks.TryGetValue(path, out var tcs))
                        return await tcs.Task;
                    
                    await WaitForStateChange(path, AudioLoadState.Loading);
                    return _loadedClips.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                }

                if (state == AudioLoadState.Unloading)
                {
                    await WaitForStateChange(path, AudioLoadState.Unloading);
                    return await LoadAudioAsync(path);
                }
            }

            _audioLoadStates[path] = AudioLoadState.Loading;
            var completionSource = new UniTaskCompletionSource<AudioClip>();
            _loadingTasks[path] = completionSource;

            try
            {
                var audioType = GetAudioType(path);
                var clip = await _audioLoader.LoadAudioAsync(path, audioType);

                if (clip != null)
                {
                    _loadedClips[path] = clip;
                    _audioLoadStates[path] = AudioLoadState.Loaded;
                    UpdateMemoryUsage(path, clip);
                    completionSource.TrySetResult(clip);
                    _loadingTasks.Remove(path);
                    return clip;
                }
                else
                {
                    _audioLoadStates[path] = AudioLoadState.NotLoaded;
                    completionSource.TrySetResult(null);
                    _loadingTasks.Remove(path);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AudioManager] Exception loading audio: " + ex.Message);
                _audioLoadStates[path] = AudioLoadState.NotLoaded;
                completionSource.TrySetResult(null);
                _loadingTasks.Remove(path);
                return null;
            }
        }

        public async UniTask UnloadAudio(string path)
        {
            if (!_audioLoadStates.TryGetValue(path, out var currentState))
                return;

            if (currentState == AudioLoadState.Loading)
            {
                if (_loadingTasks.TryGetValue(path, out var tcs))
                    await tcs.Task;
                else
                    await WaitForStateChange(path, AudioLoadState.Loading);

                if (!_audioLoadStates.TryGetValue(path, out currentState))
                    return;
            }

            if (currentState == AudioLoadState.Loaded)
            {
                _audioLoadStates[path] = AudioLoadState.Unloading;
                await UniTask.Yield();

                if (_loadedClips.TryGetValue(path, out var clip))
                {
                    UpdateMemoryUsage(path, null);
                    Destroy(clip);
                    _loadedClips.Remove(path);
                }

                _audioLoadStates.Remove(path);
                _loadingTasks.Remove(path);
            }
            else if (currentState == AudioLoadState.NotLoaded)
            {
                _audioLoadStates.Remove(path);
                _loadingTasks.Remove(path);
            }
        }

        public async UniTask UnloadAllAudio()
        {
            // Use pre-allocated list to avoid GC
            _tempPathList.Clear();
            foreach (var kvp in _loadedClips)
                _tempPathList.Add(kvp.Key);

            for (int i = 0; i < _tempPathList.Count; i++)
                await UnloadAudio(_tempPathList[i]);

            _tempPathList.Clear();
        }

        public void ForceUnloadAll()
        {
            foreach (var kvp in _loadedClips)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            }

            _loadedClips.Clear();
            _audioLoadStates.Clear();
            _loadingTasks.Clear();
            _audioMemoryUsage.Clear();
            TotalMemoryUsage = 0;
        }

        private async UniTask WaitForStateChange(string path, AudioLoadState waitingState)
        {
            while (_audioLoadStates.TryGetValue(path, out var state) && state == waitingState)
                await UniTask.Yield();
        }

        private long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;
            return (long)clip.samples * clip.channels * 2;
        }

        private void UpdateMemoryUsage(string path, AudioClip clip)
        {
            if (clip == null)
            {
                if (_audioMemoryUsage.TryGetValue(path, out long existingMem))
                {
                    TotalMemoryUsage -= existingMem;
                    _audioMemoryUsage.Remove(path);
                }
                return;
            }

            long memory = CalculateAudioClipMemoryUsage(clip);
            if (_audioMemoryUsage.TryGetValue(path, out long existingMemory))
                TotalMemoryUsage -= existingMemory;

            _audioMemoryUsage[path] = memory;
            TotalMemoryUsage += memory;
        }

        // Zero-GC audio type detection using ReadOnlySpan
        private static AudioType GetAudioType(string path)
        {
            if (string.IsNullOrEmpty(path)) return AudioType.UNKNOWN;

            int dotIndex = path.LastIndexOf('.');
            if (dotIndex < 0 || dotIndex >= path.Length - 1) return AudioType.UNKNOWN;

            ReadOnlySpan<char> ext = path.AsSpan(dotIndex);

            if (ExtEquals(ext, ".mp3")) return AudioType.MPEG;
            if (ExtEquals(ext, ".wav")) return AudioType.WAV;
            if (ExtEquals(ext, ".ogg")) return AudioType.OGGVORBIS;
            if (ExtEquals(ext, ".aiff") || ExtEquals(ext, ".aif")) return AudioType.AIFF;
            if (ExtEquals(ext, ".xm") || ExtEquals(ext, ".mod") || ExtEquals(ext, ".it") || ExtEquals(ext, ".s3m")) return AudioType.MOD;

            return AudioType.UNKNOWN;
        }

        private static bool ExtEquals(ReadOnlySpan<char> ext, string target)
        {
            if (ext.Length != target.Length) return false;
            for (int i = 0; i < ext.Length; i++)
            {
                char c = ext[i];
                char t = target[i];
                // Case-insensitive compare for ASCII
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                if (c != t) return false;
            }
            return true;
        }
    }

#if UNITY_EDITOR
    // Editor-only accessors for Inspector debugging
    public sealed partial class AudioManager
    {
        public IReadOnlyDictionary<string, AudioClip> EditorGetLoadedClips() => _loadedClips;
        public IReadOnlyDictionary<string, AudioLoadState> EditorGetAudioStates() => _audioLoadStates;
        public IReadOnlyDictionary<string, long> EditorGetAudioMemoryUsage() => _audioMemoryUsage;
    }
#endif
}