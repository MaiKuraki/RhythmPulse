using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RhythmPulse.AssetManagement
{
    /// <summary>
    /// Loads and persists GameObjects across scene transitions via <see cref="Object.DontDestroyOnLoad"/>.
    /// <para>
    /// Each entry carries a stable <see cref="AssetResolverEntry.EntryId"/> so runtime code can address a
    /// persistent object without searching the scene, plus its own instantiation policy and dependency
    /// edges. References are <see cref="AssetRef{T}"/> values, so the authoring data survives renames and
    /// moves and is validated at build time.
    /// </para>
    /// <para>
    /// Loads go through an <see cref="AssetBucketScope"/> so every handle this resolver owns is tagged with
    /// one bucket and can be released as a group.
    /// </para>
    /// </summary>
    public sealed class AssetResolver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Persistent objects owned by this resolver. Entry order is also the display order in the Inspector.")]
        private List<AssetResolverEntry> entries = new List<AssetResolverEntry>();

        [SerializeField]
        [Tooltip("When enabled, InitializeAsync also waits for every delayed entry to finish instantiating.")]
        private bool waitForDelayedEntries;

        private IAssetHandle<GameObject>[] _loadedHandles;
        private GameObject[] _instances;
        private bool[] _isInstantiating;
        private readonly Dictionary<string, int> _entryIdToIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int[] _dependencyStarts;
        private int[] _dependencyCounts;
        private int[] _dependencyIndices;
        private int[] _missingDependencyCounts;
        private bool[] _entryHasCycle;
        private AssetBucketScope _assetScope;
        private bool _initialized;

        /// <summary>Number of authored entries, including entries whose reference is invalid.</summary>
        public int EntryCount => entries?.Count ?? 0;

        /// <summary>
        /// Loads (in parallel) and instantiates every non-manual entry according to its policy.
        /// </summary>
        public async UniTask InitializeAsync(IAssetPackage assetPackage)
        {
            if (assetPackage == null)
            {
                RPLog.Channel.Error("Cannot initialize without an asset package.");
                return;
            }

            // Re-entry would dispose the live handles and instantiate a second copy of every persistent
            // prefab, so the first successful call wins. Handles are released when this component dies.
            if (_initialized)
            {
                RPLog.Channel.Warning("InitializeAsync called more than once; keeping the existing instances.");
                return;
            }

            CancellationToken ct = this.GetCancellationTokenOnDestroy();
            int count = entries?.Count ?? 0;

            DisposeLoadedHandles();
            _assetScope = RhythmPulseAssetRuntimeLabels.CreateAssetResolverScope(assetPackage);
            _initialized = true;
            BuildRuntimeCache();

            // Phase 1: fire every preload in parallel; entries without preload are loaded on demand.
            var loadTasks = new UniTask[count];
            for (int i = 0; i < count; i++)
            {
                loadTasks[i] = UniTask.CompletedTask;
                AssetResolverEntry entry = entries[i];
                if (!entry.PreloadOnInitialize)
                {
                    continue;
                }

                if (!entry.Prefab.IsValid)
                {
                    RPLog.Channel.Warning($"Skipping entry with an invalid prefab reference: {GetEntryDisplay(i)}");
                    continue;
                }

                IAssetHandle<GameObject> handle = _assetScope.LoadAsync(
                    entry.Prefab,
                    owner: GetEntryOwner(entry),
                    cancellationToken: ct);
                _loadedHandles[i] = handle;
                // One unreachable prefab must not abort the whole boot: each load reports and continues.
                loadTasks[i] = ObserveLoadAsync(handle, GetEntryDisplay(i), ct);
            }

            await UniTask.WhenAll(loadTasks).AttachExternalCancellation(ct);

            // Phase 2: instantiate according to each entry's policy.
            var immediateTasks = new UniTask[count];
            var delayedTasks = new UniTask[count];
            var manualNames = new List<string>();
            var immediateIndices = new List<int>();
            var delayedIndices = new List<int>();

            for (int i = 0; i < count; i++)
            {
                immediateTasks[i] = UniTask.CompletedTask;
                delayedTasks[i] = UniTask.CompletedTask;

                AssetResolverEntry entry = entries[i];
                if (entry.InstantiationMode == AssetResolverInstantiationMode.Manual)
                {
                    manualNames.Add(GetEntryDisplay(i));
                    continue;
                }

                if (entry.InstantiationMode == AssetResolverInstantiationMode.Delayed)
                {
                    delayedIndices.Add(i);
                    UniTask delayedTask = InstantiateEntryWithPolicyAsync(i, ct);
                    if (waitForDelayedEntries)
                    {
                        delayedTasks[i] = delayedTask;
                    }
                    else
                    {
                        ForgetWithLogging(delayedTask);
                    }

                    continue;
                }

                immediateIndices.Add(i);
                immediateTasks[i] = InstantiateEntryWithPolicyAsync(i, ct);
            }

            await UniTask.WhenAll(immediateTasks).AttachExternalCancellation(ct);
            LogInstantiationSummary(immediateIndices);

            if (waitForDelayedEntries)
            {
                await UniTask.WhenAll(delayedTasks).AttachExternalCancellation(ct);
                LogInstantiationSummary(delayedIndices);
            }

            if (manualNames.Count > 0)
            {
                RPLog.Channel.Info($"Loaded but not instantiated (manual): {string.Join(", ", manualNames)}");
            }
        }

        /// <summary>
        /// Initializes and then awaits every entry (including manual ones by default), so the caller knows
        /// the full set is ready. Throws when an entry cannot be satisfied if <paramref name="failOnAnyFailure"/>
        /// is true.
        /// </summary>
        public async UniTask InitializeAndWaitAllAsync(
            IAssetPackage assetPackage,
            bool includeManual = true,
            bool failOnAnyFailure = true,
            CancellationToken cancellationToken = default)
        {
            await InitializeAsync(assetPackage);

            if (entries == null)
            {
                return;
            }

            CancellationToken token = GetEffectiveToken(cancellationToken);
            for (int i = 0; i < entries.Count; i++)
            {
                AssetResolverEntry entry = entries[i];
                if (!includeManual && entry.InstantiationMode == AssetResolverInstantiationMode.Manual)
                {
                    continue;
                }

                bool success = await InstantiateEntryInternalAsync(i, token, true, true);
                if (success)
                {
                    continue;
                }

                string message = $"Entry could not be instantiated: {GetEntryDisplay(i)}";
                if (failOnAnyFailure)
                {
                    throw new InvalidOperationException(message);
                }

                RPLog.Channel.Warning(message);
            }
        }

        public UniTask<bool> InstantiateEntryAsync(int index, CancellationToken cancellationToken = default)
        {
            return InstantiateEntryInternalAsync(index, GetEffectiveToken(cancellationToken), true, true);
        }

        public async UniTask<bool> InstantiateEntryAsync(
            string entryIdOrDisplayName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(entryIdOrDisplayName))
            {
                return false;
            }

            if (TryResolveEntryIndex(entryIdOrDisplayName, out int index))
            {
                return await InstantiateEntryAsync(index, cancellationToken);
            }

            RPLog.Channel.Warning($"Cannot find entry: {entryIdOrDisplayName}");
            return false;
        }

        public bool TryGetInstance(int index, out GameObject instance)
        {
            instance = null;
            if (_instances == null || index < 0 || index >= _instances.Length)
            {
                return false;
            }

            instance = _instances[index];
            return instance != null;
        }

        public bool TryGetInstance(string entryId, out GameObject instance)
        {
            instance = null;
            if (!TryResolveEntryIndex(entryId, out int index))
            {
                return false;
            }

            return TryGetInstance(index, out instance);
        }

        /// <summary>
        /// Resolves a component on an instantiated entry, e.g. the UI root inside the UI framework prefab.
        /// </summary>
        public bool TryGetEntryComponent<TComponent>(string entryId, out TComponent component)
            where TComponent : Component
        {
            component = null;
            if (!TryGetInstance(entryId, out GameObject instance) || instance == null)
            {
                return false;
            }

            component = instance.GetComponentInChildren<TComponent>(true);
            return component != null;
        }

        private bool TryResolveEntryIndex(string entryIdOrDisplayName, out int index)
        {
            index = -1;
            if (entries == null || string.IsNullOrWhiteSpace(entryIdOrDisplayName))
            {
                return false;
            }

            if (_entryIdToIndex.TryGetValue(entryIdOrDisplayName.Trim(), out index))
            {
                return true;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].DisplayName, entryIdOrDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        private CancellationToken GetEffectiveToken(CancellationToken callerToken)
        {
            return callerToken.CanBeCanceled ? callerToken : this.GetCancellationTokenOnDestroy();
        }

        private async UniTask<bool> InstantiateEntryInternalAsync(
            int index,
            CancellationToken cancellationToken,
            bool respectDelay,
            bool resolveDependencies)
        {
            if (_loadedHandles == null || index < 0 || index >= _loadedHandles.Length)
            {
                return false;
            }

            if (_entryHasCycle != null && _entryHasCycle[index])
            {
                RPLog.Channel.Error($"Cyclic dependency prevents instantiation: {GetEntryDisplay(index)}");
                return false;
            }

            if (HasLiveInstance(index))
            {
                return true;
            }

            AssetResolverEntry entry = entries[index];
            bool delayBeforeDependencies = respectDelay
                                           && entry.InstantiationMode == AssetResolverInstantiationMode.Delayed
                                           && entry.DelayStartMode == AssetDelayStartMode.BeforeDependencies;
            bool delayAfterDependencies = respectDelay
                                          && entry.InstantiationMode == AssetResolverInstantiationMode.Delayed
                                          && entry.DelayStartMode == AssetDelayStartMode.AfterDependencies;

            if (delayBeforeDependencies)
            {
                await DelayByEntryAsync(entry, cancellationToken);
            }

            if (resolveDependencies)
            {
                bool dependenciesReady = await EnsureDependenciesAsync(index, entry, cancellationToken);
                if (!dependenciesReady)
                {
                    return false;
                }
            }

            if (delayAfterDependencies)
            {
                await DelayByEntryAsync(entry, cancellationToken);
            }

            if (_isInstantiating[index])
            {
                await WaitForInstantiationAsync(index, cancellationToken);
                return HasLiveInstance(index);
            }

            _isInstantiating[index] = true;
            try
            {
                IAssetHandle<GameObject> handle = _loadedHandles[index];
                if (handle == null)
                {
                    if (_assetScope == null || !entry.Prefab.IsValid)
                    {
                        return false;
                    }

                    handle = _assetScope.LoadAsync(
                        entry.Prefab,
                        owner: GetEntryOwner(entry),
                        cancellationToken: cancellationToken);
                    _loadedHandles[index] = handle;
                }

                await handle.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread(cancellationToken);

                return InstantiateLoadedHandle(index, handle);
            }
            finally
            {
                _isInstantiating[index] = false;
            }
        }

        private static UniTask DelayByEntryAsync(AssetResolverEntry entry, CancellationToken cancellationToken)
        {
            float delay = entry.GetDelaySeconds();
            if (delay <= 0f)
            {
                return UniTask.CompletedTask;
            }

            // Asset loads must not scale with gameplay time, otherwise a paused game would never resolve.
            return UniTask.Delay(
                TimeSpan.FromSeconds(delay),
                DelayType.Realtime,
                PlayerLoopTiming.Update,
                cancellationToken);
        }

        private async UniTask<bool> EnsureDependenciesAsync(
            int index,
            AssetResolverEntry entry,
            CancellationToken cancellationToken)
        {
            int dependencyCount = _dependencyCounts[index];
            if (dependencyCount == 0)
            {
                return true;
            }

            int dependencyStart = _dependencyStarts[index];
            int satisfied = 0;
            int valid = 0;

            for (int i = 0; i < dependencyCount; i++)
            {
                int dependencyIndex = _dependencyIndices[dependencyStart + i];
                valid++;

                bool isReady = HasLiveInstance(dependencyIndex);
                if (!isReady && entry.AutoInstantiateDependencies)
                {
                    isReady = await InstantiateEntryInternalAsync(dependencyIndex, cancellationToken, true, true);
                }

                if (!isReady && entry.DependencyWaitTimeoutSeconds > 0f)
                {
                    isReady = await WaitUntilDependencyReadyAsync(
                        dependencyIndex,
                        entry.DependencyWaitTimeoutSeconds,
                        cancellationToken);
                }

                if (isReady)
                {
                    satisfied++;
                }

                if (entry.DependencyMode == AssetDependencyMode.Any && satisfied > 0)
                {
                    return true;
                }
            }

            if (entry.DependencyMode == AssetDependencyMode.Any)
            {
                bool anySatisfied = satisfied > 0;
                if (!anySatisfied)
                {
                    LogDependencyUnsatisfied(index, entry, valid, satisfied);
                }

                return anySatisfied;
            }

            bool allSatisfied = valid > 0 && satisfied == valid;
            if (!allSatisfied)
            {
                LogDependencyUnsatisfied(index, entry, valid, satisfied);
            }

            return allSatisfied;
        }

        private void LogDependencyUnsatisfied(int index, AssetResolverEntry entry, int valid, int satisfied)
        {
            int missing = _missingDependencyCounts[index];
            string message =
                $"Dependency check failed for '{GetEntryDisplay(index)}' ({satisfied}/{valid}, missing:{missing}).";

            if (entry.DependencyMissingPolicy == AssetDependencyMissingPolicy.Error)
            {
                RPLog.Channel.Error(message);
            }
            else
            {
                RPLog.Channel.Warning(message);
            }
        }

        private async UniTask WaitForInstantiationAsync(int index, CancellationToken cancellationToken)
        {
            while (_isInstantiating[index])
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTask<bool> WaitUntilDependencyReadyAsync(
            int index,
            float timeoutSeconds,
            CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (HasLiveInstance(index))
                {
                    return true;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                // Unscaled so a paused game (timeScale 0) cannot make the timeout never elapse.
                elapsed += Time.unscaledDeltaTime;
            }

            return HasLiveInstance(index);
        }

        private void LogInstantiationSummary(List<int> indices)
        {
            if (indices.Count == 0)
            {
                return;
            }

            var parts = new List<string>(indices.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (!HasLiveInstance(index))
                {
                    continue;
                }

                AssetResolverEntry entry = entries[index];
                string display = string.IsNullOrWhiteSpace(entry.DisplayName) ? $"#{index}" : entry.DisplayName;
                string id = entry.EntryId?.Trim();
                string tag = !string.IsNullOrEmpty(id) &&
                             !string.Equals(id, display, StringComparison.OrdinalIgnoreCase)
                    ? $" <{id}>"
                    : string.Empty;
                parts.Add($"- #{index} {display}{tag}");
            }

            if (parts.Count == 0)
            {
                return;
            }

            RPLog.Channel.Info($"Instantiated {parts.Count}/{indices.Count}:\n{string.Join("\n", parts)}");
        }

        private void BuildRuntimeCache()
        {
            int count = entries?.Count ?? 0;

            _loadedHandles = new IAssetHandle<GameObject>[count];
            _instances = new GameObject[count];
            _isInstantiating = new bool[count];
            _dependencyStarts = new int[count];
            _dependencyCounts = new int[count];
            _missingDependencyCounts = new int[count];
            _entryHasCycle = new bool[count];

            _entryIdToIndex.Clear();

            for (int i = 0; i < count; i++)
            {
                string id = entries[i].EntryId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                id = id.Trim();
                if (_entryIdToIndex.ContainsKey(id))
                {
                    RPLog.Channel.Warning($"Duplicate EntryId '{id}' ignored; the first occurrence wins.");
                    continue;
                }

                _entryIdToIndex.Add(id, i);
            }

            var flatDependencies = new List<int>(count * 2);
            for (int i = 0; i < count; i++)
            {
                _dependencyStarts[i] = flatDependencies.Count;

                string raw = entries[i].DependsOnIds;
                if (!string.IsNullOrEmpty(raw))
                {
                    ParseAndAppendDependencies(i, raw, flatDependencies);
                }

                _dependencyCounts[i] = flatDependencies.Count - _dependencyStarts[i];
            }

            _dependencyIndices = flatDependencies.Count == 0 ? Array.Empty<int>() : flatDependencies.ToArray();
            MarkCycleEntries();
        }

        private void ParseAndAppendDependencies(int entryIndex, string raw, List<int> flatDependencies)
        {
            int tokenStart = 0;
            int length = raw.Length;

            for (int i = 0; i <= length; i++)
            {
                bool isEnd = i == length;
                if (!isEnd)
                {
                    char character = raw[i];
                    isEnd = character == ',' || character == ';';
                }

                if (!isEnd)
                {
                    continue;
                }

                int tokenLength = i - tokenStart;
                if (tokenLength > 0)
                {
                    string token = raw.Substring(tokenStart, tokenLength).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        if (_entryIdToIndex.TryGetValue(token, out int dependencyIndex))
                        {
                            if (dependencyIndex == entryIndex)
                            {
                                _missingDependencyCounts[entryIndex]++;
                                RPLog.Channel.Warning(
                                    $"Self dependency ignored for '{GetEntryDisplay(entryIndex)}'.");
                            }
                            else if (!ContainsDependency(flatDependencies, _dependencyStarts[entryIndex], dependencyIndex))
                            {
                                flatDependencies.Add(dependencyIndex);
                            }
                        }
                        else
                        {
                            _missingDependencyCounts[entryIndex]++;
                            string message =
                                $"Missing dependency '{token}' for '{GetEntryDisplay(entryIndex)}'.";
                            if (entries[entryIndex].DependencyMissingPolicy == AssetDependencyMissingPolicy.Error)
                            {
                                RPLog.Channel.Error(message);
                            }
                            else
                            {
                                RPLog.Channel.Warning(message);
                            }
                        }
                    }
                }

                tokenStart = i + 1;
            }
        }

        private static bool ContainsDependency(List<int> flatDependencies, int start, int candidate)
        {
            for (int i = start; i < flatDependencies.Count; i++)
            {
                if (flatDependencies[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkCycleEntries()
        {
            int count = entries?.Count ?? 0;
            if (count == 0)
            {
                return;
            }

            var state = new byte[count];
            var stack = new int[count];
            int depth = 0;

            for (int i = 0; i < count; i++)
            {
                if (state[i] == 0)
                {
                    DfsMarkCycles(i, state, stack, ref depth);
                }
            }
        }

        private void DfsMarkCycles(int node, byte[] state, int[] stack, ref int depth)
        {
            state[node] = 1;
            stack[depth] = node;
            depth++;

            int start = _dependencyStarts[node];
            int count = _dependencyCounts[node];
            for (int i = 0; i < count; i++)
            {
                int next = _dependencyIndices[start + i];
                byte nextState = state[next];

                if (nextState == 0)
                {
                    DfsMarkCycles(next, state, stack, ref depth);
                    continue;
                }

                if (nextState != 1)
                {
                    continue;
                }

                int cycleStart = -1;
                for (int s = 0; s < depth; s++)
                {
                    if (stack[s] == next)
                    {
                        cycleStart = s;
                        break;
                    }
                }

                if (cycleStart < 0)
                {
                    continue;
                }

                for (int s = cycleStart; s < depth; s++)
                {
                    _entryHasCycle[stack[s]] = true;
                }
            }

            depth--;
            state[node] = 2;
        }

        /// <summary>
        /// Awaits one preload while containing its failure, so a single broken entry cannot abort the
        /// remaining entries. Cancellation still propagates because it means the owner is going away.
        /// </summary>
        private static async UniTask ObserveLoadAsync(
            IAssetHandle<GameObject> handle,
            string display,
            CancellationToken cancellationToken)
        {
            try
            {
                await handle.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RPLog.Channel.Error(exception, $"Failed to preload '{display}'.");
            }
        }

        private async UniTask InstantiateEntryWithPolicyAsync(int index, CancellationToken cancellationToken)
        {
            try
            {
                await InstantiateEntryInternalAsync(index, cancellationToken, true, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Matches the preload policy: report the entry and keep the rest of the boot intact.
                RPLog.Channel.Error(exception, $"Failed to instantiate '{GetEntryDisplay(index)}'.");
            }
        }

        private bool InstantiateLoadedHandle(int index, IAssetHandle<GameObject> handle)
        {
            if (HasLiveInstance(index))
            {
                return false;
            }

            if (string.IsNullOrEmpty(handle.Error) && handle.AssetObject is GameObject prefab)
            {
                GameObject instance = Instantiate(prefab);
                DontDestroyOnLoad(instance);
                _instances[index] = instance;
                return true;
            }

            RPLog.Channel.Error($"Failed to load prefab: {entries[index].Prefab.Location}");
            return false;
        }

        private bool HasLiveInstance(int index)
        {
            return _instances != null
                   && index >= 0
                   && index < _instances.Length
                   && _instances[index] != null;
        }

        private string GetEntryDisplay(int index)
        {
            if (entries == null || index < 0 || index >= entries.Count)
            {
                return $"#{index}";
            }

            AssetResolverEntry entry = entries[index];
            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return entry.DisplayName;
            }

            return string.IsNullOrWhiteSpace(entry.EntryId) ? $"#{index}" : entry.EntryId;
        }

        private static string GetEntryOwner(AssetResolverEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.EntryId))
            {
                return entry.EntryId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return entry.DisplayName.Trim();
            }

            return RhythmPulseAssetRuntimeLabels.Owners.AssetResolver;
        }

        private static async void ForgetWithLogging(UniTask task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Owner is going away; nothing to report.
            }
            catch (Exception exception)
            {
                RPLog.Channel.Error(exception, "Delayed instantiation failed.");
            }
        }

        private void DisposeLoadedHandles()
        {
            if (_loadedHandles == null)
            {
                return;
            }

            for (int i = 0; i < _loadedHandles.Length; i++)
            {
                _loadedHandles[i]?.Dispose();
            }

            _loadedHandles = null;
            _instances = null;
            _isInstantiating = null;
            _dependencyStarts = null;
            _dependencyCounts = null;
            _dependencyIndices = null;
            _missingDependencyCounts = null;
            _entryHasCycle = null;
            _assetScope = null;
            _initialized = false;
            _entryIdToIndex.Clear();
        }

        private void OnDestroy()
        {
            DisposeLoadedHandles();
        }
    }

    public enum AssetResolverInstantiationMode
    {
        Immediate = 0,
        Delayed = 1,
        Manual = 2,
    }

    public enum AssetDelayStartMode
    {
        BeforeDependencies = 0,
        AfterDependencies = 1,
    }

    public enum AssetDependencyMode
    {
        All = 0,
        Any = 1,
    }

    public enum AssetDependencyMissingPolicy
    {
        Skip = 0,
        Error = 1,
    }

    [Serializable]
    public struct AssetResolverEntry
    {
        [Tooltip("Stable id used by runtime lookups (TryGetInstance / InstantiateEntryAsync). Leave empty for authoring-only entries.")]
        public string EntryId;

        [Tooltip("Readable name used by logs and the Inspector. Defaults to the prefab name when empty.")]
        public string DisplayName;

        [Tooltip("Prefab loaded and instantiated by this entry.")]
        public AssetRef<GameObject> Prefab;

        [Tooltip("Load the prefab during InitializeAsync even if the entry is not instantiated yet.")]
        public bool PreloadOnInitialize;

        [Tooltip("Immediate instantiates during InitializeAsync, Delayed waits for the authored delay, Manual never auto-instantiates.")]
        public AssetResolverInstantiationMode InstantiationMode;

        [Min(0f)]
        [Tooltip("Delay in seconds, only applied when InstantiationMode is Delayed.")]
        public float DelaySeconds;

        [Tooltip("Whether the delay elapses before or after dependencies are resolved.")]
        public AssetDelayStartMode DelayStartMode;

        [Tooltip("Comma or semicolon separated EntryIds this entry depends on.")]
        public string DependsOnIds;

        [Tooltip("Require every dependency (All) or at least one (Any).")]
        public AssetDependencyMode DependencyMode;

        [Tooltip("Instantiate missing dependencies automatically instead of waiting for them.")]
        public bool AutoInstantiateDependencies;

        [Min(0f)]
        [Tooltip("How long to wait for a dependency instance before failing. Zero disables waiting.")]
        public float DependencyWaitTimeoutSeconds;

        [Tooltip("Log level used when dependencies cannot be satisfied.")]
        public AssetDependencyMissingPolicy DependencyMissingPolicy;

        public float GetDelaySeconds()
        {
            if (InstantiationMode != AssetResolverInstantiationMode.Delayed)
            {
                return 0f;
            }

            return Mathf.Max(0f, DelaySeconds);
        }
    }
}
