using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RhythmPulse.Misc
{
    /// <summary>
    /// Loads and persists GameObjects across scene transitions via <see cref="Object.DontDestroyOnLoad"/>.
    /// Each entry uses <see cref="AssetRef{T}"/> for type-safe, GUID-tracked asset references that
    /// auto-heal on rename/move and are validated by the AssetRef build-time validator.
    /// Loads all entries in parallel before instantiating on the main thread.
    /// </summary>
    public sealed class AddressableResolverForDontDestroy : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[AssetResolver]";

        [SerializeField]
        private List<AssetResolverEntry> entries = new List<AssetResolverEntry>();

        private List<IAssetHandle<GameObject>> _loadedHandles;

        public bool Initialized { get; private set; } = false;

        public async UniTask InitializeAsync(IAssetPackage assetPackage)
        {
            // Idempotency guard: prevent double-init (re-entry during async load leaks handles)
            if (Initialized || _loadedHandles != null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Already initialized or initializing. Skipping.");
                return;
            }

            if (assetPackage == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid asset package.");
                return;
            }

            var ct = this.GetCancellationTokenOnDestroy();
            int count = entries.Count;
            _loadedHandles = new List<IAssetHandle<GameObject>>(count);

            // ── Phase 1: Fire all loads in parallel ──
            var tasks = new UniTask[count];
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (!entry.Prefab.IsValid)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Skipping invalid entry: {entry.DisplayName}");
                    tasks[i] = UniTask.CompletedTask;
                    _loadedHandles.Add(null);
                    continue;
                }

                var handle = assetPackage.LoadAsync(entry.Prefab);
                _loadedHandles.Add(handle);
                tasks[i] = handle.Task;
            }

            await UniTask.WhenAll(tasks).AttachExternalCancellation(ct);

            // ── Phase 2: Instantiate (must be on main thread) ──
            for (int i = 0; i < count; i++)
            {
                var handle = _loadedHandles[i];
                if (handle == null) continue;

                if (string.IsNullOrEmpty(handle.Error) && handle.AssetObject is GameObject prefab)
                {
                    var instance = Instantiate(prefab);
                    DontDestroyOnLoad(instance);
                    CLogger.LogInfo($"{DEBUG_FLAG} Instantiate: {prefab.name}");
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load: {entries[i].Prefab.Location}, Error: {handle.Error}");
                }
            }

            Initialized = true;
            CLogger.LogInfo($"{DEBUG_FLAG} Initialization complete. {count} entries processed.");
        }

        private void OnDestroy()
        {
            if (_loadedHandles == null) return;

            for (int i = 0; i < _loadedHandles.Count; i++)
                _loadedHandles[i]?.Dispose();

            // Null out the list reference; the List object itself is GC'd with this MonoBehaviour.
            _loadedHandles = null;
            CLogger.LogInfo($"{DEBUG_FLAG} All handles disposed.");
        }
    }

    [System.Serializable]
    public struct AssetResolverEntry
    {
        public string DisplayName;
        public AssetRef<GameObject> Prefab;
    }
}