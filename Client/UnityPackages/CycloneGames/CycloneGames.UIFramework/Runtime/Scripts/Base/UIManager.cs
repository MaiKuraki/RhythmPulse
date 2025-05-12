using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Service;
using UnityEngine.AddressableAssets;
using Addler.Runtime.Core.LifetimeBinding;
using CycloneGames.Factory;

namespace CycloneGames.UIFramework
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner;
        private IMainCameraService mainCamera;
        private UIRoot uiRoot;
        private Dictionary<string, UniTaskCompletionSource<bool>> uiOpenTasks = new Dictionary<string, UniTaskCompletionSource<bool>>();

        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner objectSpawner, IMainCameraService mainCamera)
        {
            this.assetPathBuilder = assetPathBuilderFactory.Create("UI");
            if (this.assetPathBuilder == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid AssetPathBuilder, Check your [AssetPathBuilderFactory], make sure it contains 'UI' key.");
                return;
            }
            this.objectSpawner = objectSpawner;
            this.mainCamera = mainCamera;
        }

        private void Awake()
        {
            uiRoot = GameObject.FindFirstObjectByType<UIRoot>();
        }

        private void Start()
        {
            AddUICameraToMainCameraStack();
        }

        internal void OpenUI(string WindowName, System.Action<UIWindow> OnUIWindowCreated = null)
        {
            OpenUIAsync(WindowName, OnUIWindowCreated).Forget();
        }

        internal void CloseUI(string WindowName)
        {
            CloseUIAsync(WindowName).Forget();
        }

        async UniTask OpenUIAsync(string WindowName, System.Action<UIWindow> OnUIWindowCreated = null)
        {
            if (uiOpenTasks.ContainsKey(WindowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} Duplicated Open! WindowName: {WindowName}");
                return;
            }
            var tcs = new UniTaskCompletionSource<bool>();
            uiOpenTasks[WindowName] = tcs;

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to open UI: {WindowName}");
            string configPath = assetPathBuilder.GetAssetPath(WindowName);
            UIWindowConfiguration windowConfig = null;
            var uiWindowHandle = Addressables.LoadAssetAsync<UIWindowConfiguration>(configPath);
            try
            {
                await uiWindowHandle.Task;
                windowConfig = uiWindowHandle.Result;

                if (windowConfig == null || windowConfig.WindowPrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Invalid UI Prefab in WindowConfig, WindowName: {WindowName}");
                    uiOpenTasks.Remove(WindowName);
                    uiWindowHandle.Release();
                    return;
                }
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} An exception occurred while loading the UI: {WindowName}: {ex.Message}");
                uiOpenTasks.Remove(WindowName);
                uiWindowHandle.Release();
                return;
            }

            string layerName = windowConfig.Layer.LayerName;
            UILayer uiLayer = uiRoot.GetUILayer(layerName);
            if (uiLayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayer not found: {layerName}");
                uiOpenTasks.Remove(WindowName);
                uiWindowHandle.Release();
                return;
            }

            if (uiLayer.HasWindow(WindowName))
            {
                // Please note that within this framework, the opening of a UIWindow must be unique;
                // that is, UI window similar to Notifications should be managed within the window itself and should not be opened repeatedly for the same UI window.
                CLogger.LogError($"{DEBUG_FLAG} Window already exists: {WindowName}, layer: {uiLayer.LayerName}");
                uiOpenTasks.Remove(WindowName);
                uiWindowHandle.Release();
                return;
            }

            UIWindow uiWindow = objectSpawner.Create(windowConfig.WindowPrefab) as UIWindow;
            if (uiWindow == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to instantiate UIWindow prefab: {WindowName}");
                uiOpenTasks.Remove(WindowName);
                uiWindowHandle.Release();
                return;
            }
            await uiWindowHandle.BindTo(uiWindow.gameObject);
            uiWindow.SetWindowName(WindowName);
            uiLayer.AddWindow(uiWindow);
            OnUIWindowCreated?.Invoke(uiWindow);

            tcs.TrySetResult(true);
        }

        async UniTask CloseUIAsync(string UIWindowName)
        {
            if (uiOpenTasks.TryGetValue(UIWindowName, out var openTask))
            {
                await openTask.Task;
                uiOpenTasks.Remove(UIWindowName);
            }

            string preReleaseConfigPath = assetPathBuilder.GetAssetPath(UIWindowName);

            UILayer layer = uiRoot?.TryGetUILayerFromUIWindowName(UIWindowName);
            if (layer == null)
            {
                UIWindow preRemoveWindow = GetUIWindow(UIWindowName);
                if (preRemoveWindow != null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Layer not found, but window exists: {UIWindowName}");
                    preRemoveWindow.Close();
                }
                return;
            }

            layer.RemoveWindow(UIWindowName);
        }

        internal bool IsUIWindowValid(string UIWindowName)
        {
            UILayer layer = uiRoot.TryGetUILayerFromUIWindowName(UIWindowName);
            if (layer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Can not find layer from WindowName: {UIWindowName}");
                return false;
            }

            // If the window doesn't exist or isn't active, it's not valid.
            return layer.HasWindow(UIWindowName);
        }

        internal UIWindow GetUIWindow(string UIWindowName)
        {
            UILayer layer = uiRoot.TryGetUILayerFromUIWindowName(UIWindowName);
            if (layer == null)
            {
                return null;
            }
            return layer.GetUIWindow(UIWindowName);
        }

        public void AddUICameraToMainCameraStack()
        {
            mainCamera?.AddCameraToStack(uiRoot.UICamera);
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            mainCamera?.RemoveCameraFromStack(uiRoot.UICamera);
        }
    }
}