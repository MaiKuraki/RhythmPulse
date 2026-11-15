using System;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logging;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations;
using RhythmPulse.AssetManagement;
using RhythmPulse.Misc;
using RhythmPulse.Services;
using UnityEngine;

namespace RhythmPulse.UI
{
    /// <summary>
    /// Composition root for the UI layer.
    /// The UIFramework service needs the instantiated <see cref="UIRoot"/> and the initialized
    /// asset package, neither of which exist while the root container is being built, so the
    /// service is created lazily once the boot sequence has finished loading both.
    /// </summary>
    public sealed class UIServiceHost : IDisposable
    {

        private static readonly LogChannel Log = RPLog.Channel;

        private readonly IMainCameraService _mainCameraService;
        private readonly SceneScopeResolver _sceneScopes;
        private readonly AssetResolver _assetResolver;
        private IUIService _service;

        public UIServiceHost(
            IMainCameraService mainCameraService,
            SceneScopeResolver sceneScopes,
            AssetResolver assetResolver)
        {
            _mainCameraService = mainCameraService ??
                throw new ArgumentNullException(nameof(mainCameraService));
            _sceneScopes = sceneScopes ?? throw new ArgumentNullException(nameof(sceneScopes));
            _assetResolver = assetResolver;
        }

        /// <summary>Null until <see cref="EnsureInitialized"/> has run.</summary>
        public IUIService Service => _service;

        /// <summary>
        /// Creates the UI service on first call. Later calls return the existing instance.
        /// </summary>
        /// <param name="package">Initialized package that owns all UI configurations and prefabs.</param>
        public IUIService EnsureInitialized(IAssetPackage package)
        {
            if (_service != null)
            {
                return _service;
            }

            if (package == null)
            {
                Log.Error("Cannot create the UI service without an initialized asset package.");
                return null;
            }

            UIRoot root = ResolveUiRoot();
            if (root == null)
            {
                Log.Error(
                    "UIRoot not found. Author the UI framework prefab as an AssetResolver entry with id '" +
                    RhythmPulseAssetRuntimeLabels.Entries.UiFramework + "'.");
                return null;
            }

            var assetProvider = new AssetManagementUIWindowAssetProvider(package, new UIAssetPathBuilder());
            var options = new UIServiceOptions
            {
                InitialWindowCapacity = 8,
                MaxActiveWindows = 16,
                // One window per frame keeps the first frame of a scene transition predictable.
                MaxInstantiatesPerFrame = 1,
            };

            IUIWindowBinder[] binders = new IUIWindowBinder[]
            {
                new RhythmPulseWindowBinder(_sceneScopes),
            };

            _service = new UIService(root, assetProvider, options, binders);

            // Screen-space-camera UI only renders when its camera is an overlay in the main stack,
            // which the removed UIManager used to do for us.
            if (root.UICamera != null)
            {
                _mainCameraService.AddCameraToStack(root.UICamera, 0);
            }
            else
            {
                Log.Warning("UIRoot has no UI camera assigned; UI will not render.");
            }

            Log.Info($"UI service created for {root.LayerCount} layers.");
            return _service;
        }

        /// <summary>
        /// Resolves the UI root through the AssetResolver entry that owns the UI framework prefab, so no
        /// scene-wide lookup is needed. Falls back to a scene search for setups without that entry.
        /// </summary>
        private UIRoot ResolveUiRoot()
        {
            if (_assetResolver != null
                && _assetResolver.TryGetEntryComponent(
                    RhythmPulseAssetRuntimeLabels.Entries.UiFramework,
                    out UIRoot resolved))
            {
                return resolved;
            }

            return UnityEngine.Object.FindFirstObjectByType<UIRoot>(FindObjectsInactive.Include);
        }

        public void Dispose()
        {
            IUIService service = _service;
            _service = null;
            if (service == null)
            {
                return;
            }

            try
            {
                service.Dispose();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "UI service disposal failed.");
            }
        }
    }
}
