using System;
using R3;
using UnityEngine;

namespace RhythmPulse
{
    public static class UnityProviderInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void SetDefaultObservableSystem()
        {
            SetDefaultObservableSystem(static ex => UnityEngine.Debug.LogException(ex));
        }

        public static void SetDefaultObservableSystem(Action<Exception> unhandledExceptionHandler)
        {
            ObservableSystem.RegisterUnhandledExceptionHandler(unhandledExceptionHandler);
            ObservableSystem.DefaultTimeProvider = UnityTimeProvider.Update;
            ObservableSystem.DefaultFrameProvider = UnityFrameProvider.Update;
        }
    }
}