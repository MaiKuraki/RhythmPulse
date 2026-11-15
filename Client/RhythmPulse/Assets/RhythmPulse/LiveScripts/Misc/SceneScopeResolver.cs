using System;
using CycloneGames.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.Misc
{
    /// <summary>
    /// Single owner of the "which container should inject this runtime-created object" decision.
    /// <para>
    /// Scene scopes are children of <see cref="ProjectSharedLifetimeScope"/> and hold the scene-local
    /// registrations (timeline, gameplay manager, ...). Objects created at runtime must be injected from
    /// the scene scope when one is loaded, and from the project scope otherwise. The lookup is cached per
    /// scene handle because <c>FindObjectsByType</c> is far too expensive to run per spawned object.
    /// </para>
    /// <para>
    /// Kept separate from the spawner so that everything that creates objects outside the spawner path
    /// (the UI framework instantiates windows itself) uses the exact same rule.
    /// </para>
    /// </summary>
    public sealed class SceneScopeResolver : IDisposable
    {

        private static readonly LogChannel Log = RPLog.Channel;

        private readonly IObjectResolver _projectResolver;
        private LifetimeScope _cachedScope;
        private IObjectResolver _cachedResolver;
        private int _cachedSceneHandle = -1;
        private bool _subscribed;

        public SceneScopeResolver(IObjectResolver projectResolver)
        {
            _projectResolver = projectResolver ??
                throw new ArgumentNullException(nameof(projectResolver));

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _subscribed = true;
        }

        /// <summary>
        /// The resolver to inject with: the loaded scene scope when available, otherwise the project scope.
        /// </summary>
        public IObjectResolver Resolve()
        {
            if (IsCachedScopeAlive())
            {
                return _cachedResolver;
            }

            ClearCache();

            LifetimeScope[] allScopes = UnityEngine.Object.FindObjectsByType<LifetimeScope>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < allScopes.Length; i++)
            {
                LifetimeScope scope = allScopes[i];

                // Container may not be built yet, or the scope may belong to a scene that is unloading.
                if (scope.Container == null || !scope.gameObject.scene.isLoaded)
                {
                    continue;
                }

                // Root and project scopes are the fallback, never the target.
                if (scope.IsRoot || scope is ProjectSharedLifetimeScope)
                {
                    continue;
                }

                // A scene scope is a child of the project scope.
                if (scope.Parent != null)
                {
                    _cachedScope = scope;
                    _cachedResolver = scope.Container;
                    _cachedSceneHandle = scope.gameObject.scene.handle;
                    return _cachedResolver;
                }
            }

            // Not cached: a scene scope may appear later, and the project resolver stays valid either way.
            return _projectResolver;
        }

        /// <summary>
        /// Injects dependencies into an already created object using <see cref="Resolve"/>.
        /// </summary>
        public void Inject(object instance)
        {
            if (instance == null)
            {
                return;
            }

            IObjectResolver resolver = Resolve();
            if (resolver == null)
            {
                Log.Warning($"No resolver available; skipped injection for {instance.GetType().Name}.");
                return;
            }

            resolver.Inject(instance);
        }

        private bool IsCachedScopeAlive()
        {
            return _cachedResolver != null &&
                   _cachedScope != null &&
                   _cachedScope.Container != null &&
                   _cachedScope.gameObject.scene.isLoaded &&
                   _cachedScope.gameObject.scene.handle == _cachedSceneHandle;
        }

        private void ClearCache()
        {
            _cachedScope = null;
            _cachedResolver = null;
            _cachedSceneHandle = -1;
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (_cachedSceneHandle == scene.handle)
            {
                ClearCache();
            }
        }

        public void Dispose()
        {
            if (!_subscribed)
            {
                return;
            }

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _subscribed = false;
            ClearCache();
        }
    }
}
