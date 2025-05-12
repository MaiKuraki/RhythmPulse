using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class UILayer : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UILayer]";
        [SerializeField] private string layerName;

        [Tooltip("The amount of window to expand when the window array is full")]
        [SerializeField] private int expansionAmount = 3;

        private Canvas uiCanvas;
        public Canvas UICanvas => uiCanvas;
        private GraphicRaycaster graphicRaycaster;
        public GraphicRaycaster WindowGraphicRaycaster => graphicRaycaster;
        public string LayerName => layerName;
        private UIWindow[] uiWindowArray;
        public UIWindow[] UIWindowArray => uiWindowArray;
        public int WindowCount { get; private set; }
        public bool IsFinishedLayerInit { get; private set; }

        protected void Awake()
        {
            uiCanvas = GetComponent<Canvas>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
            WindowGraphicRaycaster.blockingMask = LayerMask.GetMask("UI");
            InitLayer();
        }

        private void InitLayer()
        {
            if (transform.childCount == 0)
            {
                IsFinishedLayerInit = true;
                Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}");
                return;
            }

            var tempWindowArray = GetComponentsInChildren<UIWindow>();
            uiWindowArray = new UIWindow[tempWindowArray.Length];
            WindowCount = 0;

            foreach (UIWindow window in tempWindowArray)
            {
                window.SetWindowName(window.gameObject.name);
                window.SetUILayer(this);
                uiWindowArray[WindowCount++] = window;
            }

            SortUIWindowByPriority();
            IsFinishedLayerInit = true;
            Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}");
        }

        public UIWindow GetUIWindow(string InWindowName)
        {
            if (string.IsNullOrEmpty(InWindowName)) return null;
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i].WindowName == InWindowName)
                {
                    return uiWindowArray[i];
                }
            }
            return null;
        }

        public bool HasWindow(string InWindowName)
        {
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i].WindowName.Equals(InWindowName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void AddWindow(UIWindow newWindow)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} layer not init, current layer: {LayerName}");
                return;
            }

            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i].WindowName == newWindow.WindowName)
                {
                    Debug.LogError($"{DEBUG_FLAG} Window already exists: {newWindow.WindowName}");
                    return;
                }
            }

            newWindow.gameObject.name = newWindow.WindowName;
            newWindow.SetUILayer(this);
            newWindow.transform.SetParent(transform, false);

            if (WindowCount == (uiWindowArray?.Length ?? 0))
            {
                int newSize = (uiWindowArray?.Length ?? 0) + expansionAmount;
                System.Array.Resize(ref uiWindowArray, newSize);
            }

            int insertIndex = WindowCount;
            for (int i = WindowCount - 1; i >= 0; i--)
            {
                if (uiWindowArray[i].Priority > newWindow.Priority)
                {
                    insertIndex = i;
                }
                else if (uiWindowArray[i].Priority == newWindow.Priority)
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            for (int i = WindowCount; i > insertIndex; i--)
            {
                uiWindowArray[i] = uiWindowArray[i - 1];
            }

            uiWindowArray[insertIndex] = newWindow;
            WindowCount++;

            for (int i = insertIndex; i < WindowCount; i++)
            {
                uiWindowArray[i].transform.SetSiblingIndex(i);
            }
        }

        public void RemoveWindow(string InWindowName)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} layer not init, current layer: {LayerName}");
                return;
            }

            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i].WindowName == InWindowName)
                {
                    UIWindow window = uiWindowArray[i];
                    for (int j = i; j < WindowCount - 1; j++)
                    {
                        uiWindowArray[j] = uiWindowArray[j + 1];
                    }
                    WindowCount--;

                    window.Close();
                    window.SetUILayer(null);
                    return;
                }
            }
        }

        private void SortUIWindowByPriority()
        {
            if (WindowCount <= 1) return;
            System.Array.Sort(uiWindowArray, 0, WindowCount, Comparer<UIWindow>.Create((a, b) => a.Priority.CompareTo(b.Priority)));

            for (int i = 0; i < WindowCount; i++)
            {
                uiWindowArray[i].transform.SetSiblingIndex(i);
            }
        }

        public void OnDestroy()
        {
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i] != null)
                {
                    uiWindowArray[i].SetUILayer(null);
                }
            }
            WindowCount = 0;
        }
    }
}