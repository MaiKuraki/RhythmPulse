using System;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logging;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations;
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
        private const string DEBUG_FLAG = "[UIServiceHost]";

        private static readonly LogChannel Log = RpLog.Channel;

        private readonly IMainCameraService _mainCameraService;
        private readonly SceneScopeResolver _sceneScopes;
        private IUIService _service;

        public UIServiceHost(IMainCameraService mainCameraService, SceneScopeResolver sceneScopes)
        {
            _mainCameraService = mainCameraService ??
                throw new ArgumentNullException(nameof(mainCameraService));
            _sceneScopes = sceneScopes ?? throw new ArgumentNullException(nameof(sceneScopes));
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
                Log.Error($"{DEBUG_FLAG} Cannot create the UI service without an initialized asset package.");
                return null;
            }

            UIRoot root = UnityEngine.Object.FindFirstObjectByType<UIRoot>(FindObjectsInactive.Include);
            if (root == null)
            {
                Log.Error($"{DEBUG_FLAG} UIRoot not found. Make sure the UIFramework prefab is instantiated before the initial scene enters.");
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
                Log.Warning($"{DEBUG_FLAG} UIRoot has no UI camera assigned; UI will not render.");
            }

            Log.Info($"{DEBUG_FLAG} UI service created for {root.LayerCount} layers.");
            return _service;
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
                Log.Error(exception, $"{DEBUG_FLAG} UI service disposal failed.");
            }
        }
    }
}
