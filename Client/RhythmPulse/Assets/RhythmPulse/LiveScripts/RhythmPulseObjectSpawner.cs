using VContainer;
using CycloneGames.Logger;
using CycloneGames.Factory.Runtime;
using UnityEngine;

namespace RhythmPulse
{
    public class RhythmPulseObjectSpawner : IUnityObjectSpawner
    {
        [Inject] private IObjectResolver objectResolver;

        public T Create<T>(T origin) where T : UnityEngine.Object
        {
            if (origin == null)
            {
                CLogger.LogError($"Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin);
            objectResolver.Inject(obj);
            return obj;
        }

        public T Create<T>(T origin, Transform parent) where T : Object
        {
            if (origin == null)
            {
                CLogger.LogError($"Invalid prefab to spawn");
                return null;
            }

            var obj = UnityEngine.Object.Instantiate(origin, parent);
            objectResolver.Inject(obj);
            return obj;
        }
    }
}