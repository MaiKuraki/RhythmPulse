using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityDebugSheet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RhythmPulse.Cheat
{
    /// <summary>
    /// Live snapshot of the runtime state a developer usually reaches the Console for.
    /// </summary>
    /// <remarks>
    /// The page itself is instantiated onto the shared debug page prefab (the sheet adds the component by
    /// type), so no page prefab is required. Game-side data is read through
    /// <see cref="OptionalModuleReflection"/> to keep this assembly free of gameplay references — every
    /// section degrades to "n/a" when the corresponding module is absent.
    /// </remarks>
    public sealed class RhythmPulseDebugPage : DefaultDebugPageBase
    {
        private const string ResolverTypeName = "RhythmPulse.AssetManagement.AssetResolver";
        private const string ResolverAssemblyHint = "RhythmPulse.HotUpdate";
        private const string Unavailable = "n/a";

        protected override string Title => "RhythmPulse";

        public override IEnumerator Initialize()
        {
            RebuildItems();
            yield break;
        }

        private void RebuildItems()
        {
            ClearItems();

            AddButton("Refresh", subText: "rebuild this page", clicked: RebuildItems);

            AddLabel("Build");
            AddValue("Version", Application.version);
            AddValue("Unity", Application.unityVersion);
            AddValue("Build Type", Debug.isDebugBuild ? "Development" : "Release");
            AddValue("Platform", Application.platform.ToString());
            AddValue("Target FPS", Application.targetFrameRate.ToString());
            AddValue("Time Scale", Time.timeScale.ToString("0.##"));
            AddValue("System Memory", $"{SystemInfo.systemMemorySize} MB");

            AddLabel("Scene");
            Scene activeScene = SceneManager.GetActiveScene();
            AddValue("Active", activeScene.IsValid() ? $"{activeScene.name} [{activeScene.buildIndex}]" : "invalid");
            AddValue("Loaded Scenes", SceneManager.sceneCount.ToString());

            AddLabel("Asset Resolver");
            if (TryReadResolverStatus(out int alive, out int total, out List<string> rows))
            {
                AddValue("Entries", $"{alive}/{total} alive");
                for (int i = 0; i < rows.Count; i++)
                {
                    AddLabel(rows[i]);
                }
            }
            else
            {
                AddValue("Entries", "resolver not found");
            }

            Reload();
        }

        private void AddValue(string text, string value)
        {
            // Tapping a row copies "label: value" so it can be pasted into a bug report.
            AddButton(text, subText: value, clicked: () => GUIUtility.systemCopyBuffer = $"{text}: {value}");
        }

        /// <summary>
        /// Reads the resolver's authored entries and live instances. Best effort by design: the resolver
        /// lives in a hot-updatable assembly, so its state is inspected reflectively instead of through a
        /// compile-time reference.
        /// </summary>
        private static bool TryReadResolverStatus(out int alive, out int total, out List<string> rows)
        {
            alive = 0;
            total = 0;
            rows = null;

            Type resolverType = OptionalModuleReflection.ResolveType(ResolverTypeName, ResolverAssemblyHint);
            if (resolverType == null)
            {
                return false;
            }

            UnityEngine.Object[] resolvers = OptionalModuleReflection.FindAll(resolverType);
            if (resolvers.Length == 0)
            {
                return false;
            }

            const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo entriesField = resolverType.GetField("entries", AnyInstance);
            FieldInfo instancesField = resolverType.GetField("_instances", AnyInstance);
            if (entriesField == null || instancesField == null)
            {
                return false;
            }

            if (!(entriesField.GetValue(resolvers[0]) is IList entries))
            {
                return false;
            }

            if (!(instancesField.GetValue(resolvers[0]) is Array instances))
            {
                return false;
            }

            Type entryType = entriesField.FieldType.IsGenericType
                ? entriesField.FieldType.GetGenericArguments()[0]
                : null;
            FieldInfo idField = entryType?.GetField("EntryId");
            FieldInfo displayField = entryType?.GetField("DisplayName");

            total = entries.Count;
            rows = new List<string>(total);
            for (int i = 0; i < entries.Count; i++)
            {
                object entry = entries[i];
                string id = idField?.GetValue(entry) as string;
                string display = displayField?.GetValue(entry) as string;
                if (string.IsNullOrEmpty(display))
                {
                    display = $"#{i}";
                }

                bool isAlive = i < instances.Length && instances.GetValue(i) as GameObject != null;
                if (isAlive)
                {
                    alive++;
                }

                string tag = string.IsNullOrEmpty(id) ? string.Empty : $" <{id}>";
                rows.Add($"{display}{tag}: {(isAlive ? "alive" : "missing")}");
            }

            return true;
        }
    }
}
