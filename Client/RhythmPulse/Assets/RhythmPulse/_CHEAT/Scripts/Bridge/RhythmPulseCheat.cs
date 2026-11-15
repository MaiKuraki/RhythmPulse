using System;
using System.Reflection;
using UnityEngine;

namespace RhythmPulse.Cheat
{
    /// <summary>
    /// Optional debug-command bridge to <c>CycloneGames.Cheat</c>.
    /// </summary>
    /// <remarks>
    /// Resolved entirely through reflection so this module compiles and runs when the cheat framework is
    /// not installed: only command dispatch becomes inert. The framework itself gates dispatch behind
    /// <c>ENABLE_CHEAT</c> in release builds, so this bridge adds no build-time coupling either.
    /// </remarks>
    public static class RhythmPulseCheat
    {
        public const string Ping = "Debug_Ping";

        private const string RuntimeTypeName = "CycloneGames.Cheat.Runtime.CheatCommandRuntime";
        private const string RuntimeAssemblyHint = "CycloneGames.Cheat.Runtime";

        private static object _runtime;
        private static MethodInfo _publish;

        /// <summary>True when the optional cheat package was found and exposes a publish entry point.</summary>
        public static bool IsAvailable => _runtime != null && _publish != null;

        public static void Initialize()
        {
            Shutdown();

            Type runtimeType = OptionalModuleReflection.ResolveType(RuntimeTypeName, RuntimeAssemblyHint);
            if (runtimeType == null)
            {
                // Package absent: dispatch stays inert, no error.
                return;
            }

            try
            {
                // PublishAsync(string commandId, Router router = null)
                MethodInfo[] methods = runtimeType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo candidate = methods[i];
                    if (candidate.Name != "PublishAsync")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                    {
                        _publish = candidate;
                        _runtime = Activator.CreateInstance(runtimeType);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                _runtime = null;
                _publish = null;
                Debug.LogWarning($"[Cheat] Cheat runtime could not be created: {exception.Message}");
            }
        }

        /// <summary>
        /// Always leaves a visible trace in the Console, then forwards to the framework when present.
        /// </summary>
        public static void Publish(string commandId)
        {
            Debug.Log($"[Cheat] {commandId}");

            if (!IsAvailable)
            {
                return;
            }

            try
            {
                _publish.Invoke(_runtime, new object[] { commandId, null });
            }
            catch (Exception exception)
            {
                // Optional package failed at runtime: ignore, the game keeps running.
                Debug.LogWarning($"[Cheat] Dispatch failed for '{commandId}': {exception.Message}");
            }
        }

        public static void Shutdown()
        {
            if (_runtime is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort teardown of an optional package.
                }
            }

            _runtime = null;
            _publish = null;
        }
    }
}
