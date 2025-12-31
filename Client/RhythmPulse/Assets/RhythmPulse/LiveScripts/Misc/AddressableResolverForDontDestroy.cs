using System.Collections.Generic;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RhythmPulse.Misc
{
    /// <summary>
    /// Manages the loading and persistence of Addressable GameObjects that are intended to survive scene transitions
    /// via <see cref="UnityEngine.Object.DontDestroyOnLoad"/>. This class helps prevent issues such as lost references
    /// or premature unloading of Addressable assets that are marked for persistence across scenes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problem Addressed:
    /// When GameObjects are loaded via the Addressables system and then marked with <see cref="UnityEngine.Object.DontDestroyOnLoad"/>,
    /// issues can arise during scene transitions or when the Addressable assets' containing groups are managed.
    /// Specifically, references to these objects might be lost, or the objects themselves might be inadvertently destroyed
    /// if not handled correctly, particularly if they originate from Addressable scenes that are subsequently unloaded.
    /// </para>
    /// <para>
    /// Solution Provided by This Class:
    /// This class acts as a centralized resolver and manager for such persistent Addressable GameObjects.
    /// It ensures that:
    /// <list type="bullet">
    ///   <item><description>Specified GameObjects are loaded from their Addressable paths.</description></item>
    ///   <item><description>They are instantiated into the scene.</description></item>
    ///   <item><description>The instantiated GameObjects are correctly marked with <see cref="UnityEngine.Object.DontDestroyOnLoad"/>.</description></item>
    ///   <item><description>The Addressable load operations are bound to the lifetime of this resolver (if it's persistent),
    ///   ensuring that the underlying Addressable assets (handles) are appropriately managed and not prematurely released
    ///   as long as these objects are intended to persist.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Recommended Best Practice:
    /// GameObjects that need to be persistent across scenes using <see cref="UnityEngine.Object.DontDestroyOnLoad"/>
    /// and are managed by the Addressables system should ideally not be placed directly within Addressable scenes
    /// that are loaded and unloaded additively. Instead, they should be instantiated and managed by a dedicated,
    /// persistent service like this <see cref="AddressableResolverForDontDestroy"/> class. This approach ensures
    /// that the creation of these objects and the management of their Addressable handles are controlled independently
    /// of any individual scene's lifecycle.
    /// </para>
    /// </remarks>
    public class AddressableResolverForDontDestroy : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[AssetResolver]";

        [SerializeField]
        private List<AssetResolverData> dontDestroyAddressablePaths = new List<AssetResolverData>();

        // Track loaded handles to prevent memory leaks
        private List<IAssetHandle<GameObject>> _loadedHandles = new List<IAssetHandle<GameObject>>();

        public bool Initialized { get; private set; } = false;

        public async UniTask InitializeAsync(IAssetModule assetModule)
        {
            var pkg = assetModule.GetPackage("DefaultPackage");

            foreach (AssetResolverData pathData in dontDestroyAddressablePaths)
            {
                // Load the asset using the generic interface
                var handle = pkg.LoadAssetAsync<GameObject>(pathData.AddressablePath);
                _loadedHandles.Add(handle); // Track the handle

                await handle.Task;

                if (string.IsNullOrEmpty(handle.Error) && handle.AssetObject != null)
                {
                    // Instantiate the GameObject
                    var prefab = handle.AssetObject as GameObject;
                    if (prefab != null)
                    {
                        var instance = Instantiate(prefab);
                        DontDestroyOnLoad(instance);
                        CLogger.LogInfo($"{DEBUG_FLAG} Instantiate: {prefab.name}");
                    }
                    else
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Loaded asset is not a GameObject: {pathData.AddressablePath}");
                    }
                }
                else
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load asset: {pathData.AddressablePath}");
                }
            }

            Initialized = true;
        }

        private void OnDestroy()
        {
            // Release all handles when this resolver is destroyed
            foreach (var handle in _loadedHandles)
            {
                handle?.Dispose();
            }
            _loadedHandles.Clear();
            CLogger.LogInfo($"{DEBUG_FLAG} All handles disposed.");
        }
    }

    [System.Serializable]
    public class AssetResolverData
    {
        public string DisplayName;
        public string AddressablePath;
    }
}