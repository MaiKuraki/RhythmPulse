using System;
using System.Reflection;
using UnityEngine;

namespace RhythmPulse.Cheat
{
    /// <summary>
    /// Adapter for touching optional modules the cheat UI cares about without taking a compile-time
    /// dependency on them.
    /// </summary>
    /// <remarks>
    /// The cheat module must keep compiling and running when a package is not installed: an asmdef
    /// reference to a missing package is a hard compile error, and as an AOT assembly this module must
    /// not reference hot-updatable code either. Everything optional is therefore resolved by name and
    /// degrades to a single warning when absent.
    /// <para>
    /// Usage contract: never throw, never spam — callers get <c>false</c> when the type or the member is
    /// missing and the feature simply stays inert.
    /// </para>
    /// </remarks>
    internal static class OptionalModuleReflection
    {
        /// <summary>
        /// Resolves a type by full name. <paramref name="assemblyHint"/> makes the lookup a single
        /// assembly probe; without it every loaded assembly is scanned.
        /// </summary>
        public static Type ResolveType(string typeName, string assemblyHint = null)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(assemblyHint))
            {
                Type hinted = Type.GetType($"{typeName}, {assemblyHint}", throwOnError: false);
                if (hinted != null)
                {
                    return hinted;
                }
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type found = assemblies[i].GetType(typeName, throwOnError: false);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch (Exception)
                {
                    // Dynamic assemblies can refuse name lookups; skip them.
                }
            }

            return null;
        }

        /// <summary>
        /// Builds set/get accessors for a boolean member addressed by its logical name (e.g. "Visible").
        /// </summary>
        /// <remarks>
        /// Probe order follows "public API first, serialized state last":
        /// <c>Set{Name}(bool)</c> → <c>{Name}</c> property → <c>_{name}</c> field → <c>{Name}</c> field.
        /// The field fallbacks matter because IL2CPP stripping can drop public members that nothing calls
        /// statically, while <c>[SerializeField]</c> fields are preserved.
        /// </remarks>
        public static bool TryCreateBoolMemberAccessor(
            Type type,
            string memberName,
            out Action<object, bool> setter,
            out Func<object, bool> getter)
        {
            setter = null;
            getter = null;
            if (type == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
            const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            object[] invokeArguments = null;

            MethodInfo method = type.GetMethod(
                "Set" + memberName,
                PublicInstance,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            if (method != null)
            {
                invokeArguments = new object[1];
                object[] arguments = invokeArguments;
                setter = (target, value) =>
                {
                    arguments[0] = value;
                    method.Invoke(target, arguments);
                };
            }

            PropertyInfo property = type.GetProperty(memberName, PublicInstance);
            MethodInfo propertySetter = property?.GetSetMethod(nonPublic: true);
            if (setter == null && propertySetter != null)
            {
                invokeArguments ??= new object[1];
                object[] arguments = invokeArguments;
                setter = (target, value) =>
                {
                    arguments[0] = value;
                    propertySetter.Invoke(target, arguments);
                };
            }

            MethodInfo propertyGetter = property?.GetGetMethod(nonPublic: true);
            if (propertyGetter != null && propertyGetter.ReturnType == typeof(bool))
            {
                getter = target => (bool)propertyGetter.Invoke(target, null);
            }

            if (setter != null && getter != null)
            {
                return true;
            }

            string backingFieldName = "_" + char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
            FieldInfo field = type.GetField(backingFieldName, AnyInstance) ?? type.GetField(memberName, AnyInstance);
            if (field == null || field.FieldType != typeof(bool))
            {
                return setter != null || getter != null;
            }

            setter ??= (target, value) => field.SetValue(target, value);
            getter ??= target => (bool)field.GetValue(target);
            return true;
        }

        /// <summary>Finds every live instance of an optional component type.</summary>
        public static UnityEngine.Object[] FindAll(Type type)
        {
            return type == null
                ? Array.Empty<UnityEngine.Object>()
                : UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
        }
    }
}
