using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;
using RhythmPulse.Misc;
using UnityEngine;

namespace RhythmPulse
{
    /// <summary>
    /// Spawns objects for the engine-side factory pipeline and injects them through
    /// <see cref="SceneScopeResolver"/> so scene-local registrations reach the instance.
    /// </summary>
    public class RhythmPulseObjectSpawner : IUnityObjectSpawner
    {
        private readonly SceneScopeResolver sceneScopes;

        public RhythmPulseObjectSpawner(SceneScopeResolver sceneScopes)
        {
            this.sceneScopes = sceneScopes;
        }

        public T Create<T>(T origin) where T : Object
        {
            if (origin == null)
            {
                RPLog.Channel.Error("Invalid prefab to spawn");
                return null;
            }

            T obj = Object.Instantiate(origin);
            sceneScopes.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                RPLog.Channel.Error("Invalid prefab to spawn");
                return null;
            }

            T obj = Object.Instantiate(origin, parent);
            sceneScopes.Inject(obj);
            return obj;
        }
    }
}
