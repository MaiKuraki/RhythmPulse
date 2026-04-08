using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmPulse.Audio
{
    public interface IAudioLoader
    {
        UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType, CancellationToken cancellationToken = default);
    }

    public interface IAudioContentProvider
    {
        bool CanHandle(string path);
        UniTask<AudioClip> LoadAudioAsync(string path, CancellationToken cancellationToken = default);
    }

    public sealed class UnityAudioLoader : IAudioLoader
    {
        public async UniTask<AudioClip> LoadAudioAsync(string path, AudioType audioType, CancellationToken cancellationToken = default)
        {
            using var www = UnityWebRequestMultimedia.GetAudioClip(path, audioType);
            await www.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            if (www.result == UnityWebRequest.Result.ConnectionError || 
                www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[UnityAudioLoader] Error loading audio: " + www.error);
                return null;
            }

            return DownloadHandlerAudioClip.GetContent(www);
        }
    }

    public sealed class ExternalAudioContentProvider : IAudioContentProvider
    {
        private readonly IAudioLoader _loader;

        public ExternalAudioContentProvider(IAudioLoader loader)
        {
            _loader = loader;
        }

        public bool CanHandle(string path)
        {
            // Keep behavior permissive for now: external provider is the default fallback.
            return !string.IsNullOrEmpty(path);
        }

        public UniTask<AudioClip> LoadAudioAsync(string path, CancellationToken cancellationToken = default)
        {
            var normalizedPath = NormalizePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                CLogger.LogError("[ExternalAudioContentProvider] Invalid path: " + path);
                return UniTask.FromResult<AudioClip>(null);
            }

            var audioType = GetAudioType(path);
            return _loader.LoadAudioAsync(normalizedPath, audioType, cancellationToken);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // If already URL/file URI, keep it as AbsoluteOrFullUri.
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("jar:file://", StringComparison.OrdinalIgnoreCase))
            {
                return FilePathUtility.GetUnityWebRequestUri(path, UnityPathSource.AbsoluteOrFullUri);
            }

            // Heuristic: rooted disk paths are absolute, otherwise treat as persistentData relative path.
            if (System.IO.Path.IsPathRooted(path))
            {
                return FilePathUtility.GetUnityWebRequestUri(path, UnityPathSource.AbsoluteOrFullUri);
            }

            return FilePathUtility.GetUnityWebRequestUri(path, UnityPathSource.PersistentData);
        }

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
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                if (c != t) return false;
            }
            return true;
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
        [SerializeField] private AssetRef<GameObject> _audioSourcePrefabRef;

        // Pre-allocated collections
        private readonly Dictionary<string, AudioClip> _loadedClips = new(32);
        private readonly Dictionary<string, AudioLoadState> _audioLoadStates = new(32);
        private readonly Dictionary<string, AudioLoadRequest> _loadingTasks = new(8);
        private readonly Dictionary<string, long> _audioMemoryUsage = new(32);
        private readonly List<string> _tempPathList = new(32); // Reusable list for iterations

        private IAssetModule _assetModule;
        private IAssetHandle<GameObject> _audioSourceHandle;

        private IAudioLoader _audioLoader;
        private List<IAudioContentProvider> _audioProviders;
        private GameAudioSource _audioSourcePrefab;
        private bool _audioSourceReady;

        private sealed class AudioLoadRequest
        {
            public readonly UniTaskCompletionSource<AudioClip> Completion = new();
            public readonly CancellationTokenSource Cancellation = new();
        }

        public static AudioManager Instance { get; private set; }
        public GameAudioSource AudioSourcePrefab => _audioSourcePrefab;
        public bool IsInitialized => _audioSourceReady;
        public long TotalMemoryUsage { get; private set; }

        public void SetAssetModule(IAssetModule assetModule)
        {
            _assetModule = assetModule;
        }

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
            _audioProviders = new List<IAudioContentProvider>(2)
            {
                new ExternalAudioContentProvider(_audioLoader)
            };
            _audioSourceReady = false;
            LoadAudioSourceAsync().Forget();
        }

        private void OnDestroy()
        {
            // Release the AudioSourcePrefab handle
            _audioSourceHandle?.Dispose();
            ForceUnloadAll();
        }

        private async UniTaskVoid LoadAudioSourceAsync()
        {
            try
            {
                // Wait for IAssetModule to be provided via SetAssetModule()
                await UniTask.WaitUntil(() => _assetModule != null, cancellationToken: destroyCancellationToken);

                var pkg = _assetModule.GetPackage("DefaultPackage");
                if (pkg == null)
                {
                    CLogger.LogError("[AudioManager] Failed to get DefaultPackage from IAssetModule");
                    return;
                }

                if (!_audioSourcePrefabRef.IsValid)
                {
                    CLogger.LogError("[AudioManager] AudioSource prefab AssetRef is invalid.");
                    return;
                }

                _audioSourceHandle = pkg.LoadAsync(_audioSourcePrefabRef);
                await _audioSourceHandle.Task;

                if (string.IsNullOrEmpty(_audioSourceHandle.Error) && _audioSourceHandle.AssetObject != null)
                {
                    var prefabObj = _audioSourceHandle.AssetObject as GameObject;
                    _audioSourcePrefab = prefabObj?.GetComponent<GameAudioSource>();
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
                    CLogger.LogError("[AudioManager] Failed to load AudioSourcePrefab: " + _audioSourcePrefabRef.Location + ", Error: " + (_audioSourceHandle?.Error ?? "Unknown error"));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CLogger.LogError("[AudioManager] Unexpected error loading AudioSourcePrefab: " + ex.Message);
            }
        }

        public async UniTask<AudioClip> LoadAudioAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_audioLoadStates.TryGetValue(path, out var state))
            {
                if (state == AudioLoadState.Loaded && _loadedClips.TryGetValue(path, out var existingClip))
                    return existingClip;

                if (state == AudioLoadState.Loading)
                {
                    if (_loadingTasks.TryGetValue(path, out var request))
                        return await request.Completion.Task.AttachExternalCancellation(cancellationToken);
                    
                    await WaitForStateChange(path, AudioLoadState.Loading, cancellationToken);
                    return _loadedClips.TryGetValue(path, out var loadedClip) ? loadedClip : null;
                }

                if (state == AudioLoadState.Unloading)
                {
                    await WaitForStateChange(path, AudioLoadState.Unloading, cancellationToken);
                    return await LoadAudioAsync(path, cancellationToken);
                }
            }

            _audioLoadStates[path] = AudioLoadState.Loading;
            var loadRequest = new AudioLoadRequest();
            _loadingTasks[path] = loadRequest;

            try
            {
                var provider = ResolveProvider(path);
                if (provider == null)
                {
                    _audioLoadStates[path] = AudioLoadState.NotLoaded;
                    loadRequest.Completion.TrySetResult(null);
                    _loadingTasks.Remove(path);
                    CLogger.LogError("[AudioManager] No audio provider can handle path: " + path);
                    return null;
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destroyCancellationToken, loadRequest.Cancellation.Token);
                var clip = await provider.LoadAudioAsync(path, linkedCts.Token);

                if (clip != null)
                {
                    _loadedClips[path] = clip;
                    _audioLoadStates[path] = AudioLoadState.Loaded;
                    UpdateMemoryUsage(path, clip);
                    loadRequest.Completion.TrySetResult(clip);
                    _loadingTasks.Remove(path);
                    loadRequest.Cancellation.Dispose();
                    return clip;
                }
                else
                {
                    _audioLoadStates[path] = AudioLoadState.NotLoaded;
                    loadRequest.Completion.TrySetResult(null);
                    _loadingTasks.Remove(path);
                    loadRequest.Cancellation.Dispose();
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                _audioLoadStates[path] = AudioLoadState.NotLoaded;
                loadRequest.Completion.TrySetCanceled();
                _loadingTasks.Remove(path);
                loadRequest.Cancellation.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError("[AudioManager] Exception loading audio: " + ex.Message);
                _audioLoadStates[path] = AudioLoadState.NotLoaded;
                loadRequest.Completion.TrySetResult(null);
                _loadingTasks.Remove(path);
                loadRequest.Cancellation.Dispose();
                return null;
            }
        }

        public async UniTask UnloadAudio(string path)
        {
            if (!_audioLoadStates.TryGetValue(path, out var currentState))
                return;

            if (currentState == AudioLoadState.Loading)
            {
                if (_loadingTasks.TryGetValue(path, out var request))
                    await request.Completion.Task;
                else
                    await WaitForStateChange(path, AudioLoadState.Loading, destroyCancellationToken);

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
            // Cancel in-flight loads first so awaiting callers don't hang.
            _tempPathList.Clear();
            foreach (var kvp in _loadingTasks)
                _tempPathList.Add(kvp.Key);

            for (int i = 0; i < _tempPathList.Count; i++)
            {
                var key = _tempPathList[i];
                if (!_loadingTasks.TryGetValue(key, out var request)) continue;

                request.Cancellation.Cancel();
                request.Completion.TrySetCanceled();
                request.Cancellation.Dispose();
            }
            _tempPathList.Clear();

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

        private async UniTask WaitForStateChange(string path, AudioLoadState waitingState, CancellationToken cancellationToken)
        {
            while (_audioLoadStates.TryGetValue(path, out var state) && state == waitingState)
                await UniTask.Yield(cancellationToken);
        }

        private IAudioContentProvider ResolveProvider(string path)
        {
            for (int i = 0; i < _audioProviders.Count; i++)
            {
                var provider = _audioProviders[i];
                if (provider.CanHandle(path))
                    return provider;
            }
            return null;
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