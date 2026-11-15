using System;
using Cysharp.Threading.Tasks;
using IngameDebugConsole;
using UnityDebugSheet;
using UnityDebugSheet.Runtime.Extensions.IngameDebugConsole;
using UnityDebugSheet.Runtime.Extensions.Unity;
using UnityEngine;

namespace RhythmPulse.Cheat
{
    /// <summary>
    /// Owns the debug sheet entries for this project and the optional cheat bridge lifecycle.
    /// </summary>
    /// <remarks>
    /// This assembly deliberately keeps zero compile-time dependency on gameplay, framework, or optional
    /// packages: an asmdef reference to a package that is not installed is a hard compile error, and as an
    /// AOT assembly it must not reference hot-updatable code either. Everything outside the debug UI
    /// packages is reached through <see cref="OptionalModuleReflection"/> and stays inert when absent.
    /// <para>
    /// The canvas reveal also lives here: the sheet must stay hidden until every button is registered,
    /// otherwise the first frame shows an empty page. That replaced the standalone initializer component.
    /// </para>
    /// </remarks>
    public class DebugSheetManager : MonoBehaviour
    {
        private const int DebugSheetWaitFrameCount = 30;
        private const string FpsCounterTypeName = "CycloneGames.Utility.Runtime.FPSCounter";
        private const string FpsCounterAssemblyHint = "CycloneGames.Utility.Runtime";
        private const string VisibilityMemberName = "Visible";

        private DebugPage _debugPageRoot;
        private Canvas _debugCanvas;
        private CanvasGroup _debugCanvasGroup;
        private bool _isFpsVisible = true;

        public static DebugSheetManager Instance { get; private set; }

        [SerializeField] private bool _singleton = true;

        // Reflection cache shared by every FPS toggle click.
        private static bool _fpsResolved;
        private static bool _fpsWarned;
        private static Type _fpsCounterType;
        private static Action<object, bool> _setFpsVisible;
        private static Func<object, bool> _getFpsVisible;

        private void Awake()
        {
            if (!_singleton)
            {
                RhythmPulseCheat.Initialize();
                return;
            }

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            RhythmPulseCheat.Initialize();
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        private void OnDestroy()
        {
            RhythmPulseCheat.Shutdown();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private async UniTask InitializeAsync()
        {
            try
            {
                if (!await WaitForDebugSheetAsync())
                {
                    return;
                }

                if (!PrepareDebugCanvasForInitialization())
                {
                    return;
                }

                _debugPageRoot = DebugSheet.Instance.GetOrCreateInitialPage();
                if (_debugPageRoot == null)
                {
                    Debug.LogError("[DebugSheet] GetOrCreateInitialPage returned null; buttons will not be registered.");
                    return;
                }

                SyncFpsVisibility();
                TryAddButton("Toggle FPS", () =>
                {
                    _isFpsVisible = !_isFpsVisible;
                    ApplyFpsVisibility(_isFpsVisible);
                });

                DebugLogManager debugLogManager = FindDebugLogManager();
                if (debugLogManager != null)
                {
                    TryAddPageLinkButton<IngameDebugConsoleDebugPage>(
                        "In-Game Console Debug",
                        onLoad: x => x.page.Setup(debugLogManager));
                }
                else
                {
                    TryAddButton(
                        "In-Game Console Debug",
                        () => Debug.LogError("[DebugSheet] DebugLogManager is missing; cannot open the console page."));
                }

                TryAddPageLinkButton<RhythmPulseDebugPage>("RhythmPulse");
                TryAddPageLinkButton<SystemInfoDebugPage>("System Info");

                // Proves the optional cheat bridge end-to-end: the Console trace always appears, the
                // framework dispatch only when the package is installed.
                TryAddButton("Cheat Ping", () => RhythmPulseCheat.Publish(RhythmPulseCheat.Ping));

                await FinalizeDebugCanvasInitializationAsync();
            }
            catch (OperationCanceledException)
            {
                // Owner is going away.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DebugSheet] Initialization failed: {exception}");
            }
        }

        private async UniTask<bool> WaitForDebugSheetAsync()
        {
            for (int i = 0; i < DebugSheetWaitFrameCount && DebugSheet.Instance == null; i++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            if (DebugSheet.Instance != null)
            {
                return true;
            }

            Debug.LogError("[DebugSheet] DebugSheet.Instance is null; buttons will not be registered.");
            return false;
        }

        /// <summary>
        /// Hides the sheet before its content is authored so the first visible frame is complete.
        /// </summary>
        private bool PrepareDebugCanvasForInitialization()
        {
            DebugSheet debugSheet = DebugSheet.Instance;
            if (debugSheet == null)
            {
                Debug.LogError("[DebugSheet] DebugSheet.Instance is null; canvas cannot be prepared.");
                return false;
            }

            Transform debugTransform = debugSheet.transform;
            _debugCanvas = debugTransform != null ? debugTransform.GetComponent<Canvas>() : null;
            if (_debugCanvas == null)
            {
                Debug.LogError("[DebugSheet] DebugSheet canvas is missing.");
                return false;
            }

            _debugCanvasGroup = _debugCanvas.GetComponent<CanvasGroup>();
            if (_debugCanvasGroup == null)
            {
                _debugCanvasGroup = _debugCanvas.gameObject.AddComponent<CanvasGroup>();
            }

            _debugCanvasGroup.alpha = 0f;
            _debugCanvasGroup.interactable = false;
            _debugCanvasGroup.blocksRaycasts = false;
            _debugCanvas.enabled = true;
            Canvas.ForceUpdateCanvases();
            return true;
        }

        private async UniTask FinalizeDebugCanvasInitializationAsync()
        {
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            Canvas.ForceUpdateCanvases();
            _debugPageRoot?.Reload();

            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);
            Canvas.ForceUpdateCanvases();
            _debugPageRoot?.Reload();

            if (_debugCanvas != null)
            {
                _debugCanvas.enabled = true;
            }

            if (_debugCanvasGroup != null)
            {
                _debugCanvasGroup.alpha = 1f;
                _debugCanvasGroup.interactable = true;
                _debugCanvasGroup.blocksRaycasts = true;
            }

            Debug.Log($"[DebugSheet] Ready. Cells: {_debugPageRoot?.ItemInfos.Count ?? 0}");
        }

        private void TryAddButton(string text, Action clicked)
        {
            try
            {
                _debugPageRoot.AddButton(text, clicked: clicked);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DebugSheet] Failed to add button '{text}': {exception.Message}");
            }
        }

        private void TryAddPageLinkButton<TPage>(
            string text,
            Action<(string pageId, TPage page)> onLoad = null) where TPage : DebugPageBase
        {
            try
            {
                _debugPageRoot.AddPageLinkButton(text, onLoad: onLoad);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DebugSheet] Failed to add page link '{text}': {exception.Message}");
            }
        }

        private static DebugLogManager FindDebugLogManager()
        {
            DebugLogManager manager = DebugLogManager.Instance;
            return manager != null ? manager : FindAnyObjectByType<DebugLogManager>();
        }

        private void SyncFpsVisibility()
        {
            if (!EnsureFpsCounterResolved() || _getFpsVisible == null)
            {
                return;
            }

            UnityEngine.Object[] counters = OptionalModuleReflection.FindAll(_fpsCounterType);
            if (counters.Length > 0)
            {
                _isFpsVisible = _getFpsVisible(counters[0]);
            }
        }

        private static void ApplyFpsVisibility(bool isVisible)
        {
            if (!EnsureFpsCounterResolved())
            {
                return;
            }

            UnityEngine.Object[] counters = OptionalModuleReflection.FindAll(_fpsCounterType);
            if (counters.Length == 0)
            {
                WarnOnce("[DebugSheet] No FPSCounter instance found. Author the 'fps-counter' AssetResolver entry.");
                return;
            }

            for (int i = 0; i < counters.Length; i++)
            {
                _setFpsVisible(counters[i], isVisible);
            }
        }

        /// <summary>
        /// Resolves the optional counter once. Returns false when the package is absent or exposes no
        /// boolean visibility member, in which case the toggle silently does nothing.
        /// </summary>
        private static bool EnsureFpsCounterResolved()
        {
            if (_fpsResolved)
            {
                return true;
            }

            // Only cache success: the counter assembly may not be loaded on the first call.
            _fpsCounterType ??= OptionalModuleReflection.ResolveType(
                FpsCounterTypeName,
                FpsCounterAssemblyHint);

            if (_fpsCounterType == null)
            {
                WarnOnce($"[DebugSheet] Optional module '{FpsCounterAssemblyHint}' is not loaded; the FPS toggle stays inactive.");
                return false;
            }

            _fpsResolved = OptionalModuleReflection.TryCreateBoolMemberAccessor(
                _fpsCounterType,
                VisibilityMemberName,
                out _setFpsVisible,
                out _getFpsVisible);

            if (!_fpsResolved)
            {
                WarnOnce($"[DebugSheet] '{FpsCounterTypeName}' exposes no '{VisibilityMemberName}' member; the FPS toggle stays inactive.");
            }

            return _fpsResolved;
        }

        private static void WarnOnce(string message)
        {
            if (_fpsWarned)
            {
                return;
            }

            _fpsWarned = true;
            Debug.LogWarning(message);
        }
    }
}
