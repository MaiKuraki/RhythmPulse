using VContainer;
using VContainer.Unity;
using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RhythmPulse
{
    public class RhythmPulseObjectSpawner : IUnityObjectSpawner
    {
        [Inject] private IObjectResolver objectResolver;

        private LifetimeScope cachedSceneScope;
        private IObjectResolver cachedSceneResolver;
        private int cachedSceneHandle = -1; // Scene handle for validation
        private bool sceneEventRegistered = false;

        /// <summary>
        /// Initializes the spawner. Called by VContainer after injection.
        /// </summary>
        [Inject]
        private void Construct()
        {
            // Register for scene unload events to clear cache (only once)
            if (!sceneEventRegistered)
            {
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                sceneEventRegistered = true;
            }
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (cachedSceneHandle == scene.handle)
            {
                ClearCache();
            }
        }

        private void ClearCache()
        {
            cachedSceneScope = null;
            cachedSceneResolver = null;
            cachedSceneHandle = -1;
        }

        /// <summary>
        /// Gets the most appropriate resolver for dependency injection.
        /// </summary>
        private IObjectResolver GetResolverForInjection()
        {
            if (cachedSceneResolver != null && cachedSceneScope != null)
            {
                if (cachedSceneScope.Container != null &&
                    cachedSceneScope.gameObject.scene.isLoaded &&
                    cachedSceneScope.gameObject.scene.handle == cachedSceneHandle)
                {
                    return cachedSceneResolver;
                }

                ClearCache();
            }

            var allLifetimeScopes = Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < allLifetimeScopes.Length; i++)
            {
                var scope = allLifetimeScopes[i];

                // Skip if container is not ready
                if (scope.Container == null)
                    continue;

                // Skip if not in a loaded scene
                if (!scope.gameObject.scene.isLoaded)
                    continue;

                // Skip root scope (RootLifetimeScope)
                if (scope.IsRoot)
                    continue;

                // Skip global scope (ProjectSharedLifetimeScope)
                if (scope is ProjectSharedLifetimeScope)
                    continue;

                // Scene scopes have a parent (ProjectSharedLifetimeScope) and are not root
                if (scope.Parent != null)
                {
                    // Cache the resolver for future use (0GC after this)
                    cachedSceneScope = scope;
                    cachedSceneResolver = scope.Container;
                    cachedSceneHandle = scope.gameObject.scene.handle;
                    return cachedSceneResolver;
                }
            }

            // No scene scope found, use global resolver
            // Don't cache global resolver to allow scene scope to be found later
            return objectResolver;
        }

        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                CLogger.LogError($"[RhythmPulseObjectSpawner] Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin);
            var resolver = GetResolverForInjection();
            resolver.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                CLogger.LogError($"[RhythmPulseObjectSpawner] Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin, parent);
            var resolver = GetResolverForInjection();
            resolver.Inject(obj);
            return obj;
        }
    }
}