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
            // Deliberately bypasses RPLog: this runs before the logging pipeline exists
            // (AfterAssembliesLoaded vs. BeforeSceneLoad), and a swallowed exception here would hide
            // exactly the failures this handler exists to surface. Debug.LogException always prints.
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