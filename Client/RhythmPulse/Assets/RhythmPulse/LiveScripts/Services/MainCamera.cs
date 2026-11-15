using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CycloneGames.Logging;

namespace RhythmPulse.Services
{
    /// <summary>
    /// Owns the game's main camera and the URP overlay camera stack that sits on top of it.
    /// </summary>
    public class MainCamera : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[MainCamera]";

        [SerializeField] private Camera _camera;
        [SerializeField] private bool _singleton = true;

        public static MainCamera Instance { get; private set; }
        public Camera CameraInst => _camera;

        private UniversalAdditionalCameraData _urpCameraData;

        // Extensibility events — subscribe from outside without modifying this component
        public event Action<Camera> OnCameraAddedToStack;
        public event Action<Camera> OnCameraRemovedFromStack;
        public event Action OnCameraStackCleared;

        void Awake()
        {
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            _urpCameraData = CameraInst?.GetUniversalAdditionalCameraData();
        }

        public void AddCameraToStack(Camera inCamera, int index = 0)
        {
            if (!inCamera) return;

            _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();
            if (_urpCameraData == null)
            {
                RpLog.Channel.Warning($"{DEBUG_FLAG} Invalid URP camera data");
                return;
            }

            var cameraData = inCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
            {
                cameraData.renderType = CameraRenderType.Overlay;
            }

            if (!_urpCameraData.cameraStack.Contains(inCamera))
            {
                int clampedIndex = Mathf.Clamp(index, 0, _urpCameraData.cameraStack.Count);
                _urpCameraData.cameraStack.Insert(clampedIndex, inCamera);
                OnCameraAddedToStack?.Invoke(inCamera);
            }
        }

        public void RemoveCameraFromStack(Camera inCamera)
        {
            if (!inCamera) return;

            _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();
            if (_urpCameraData == null)
            {
                RpLog.Channel.Warning($"{DEBUG_FLAG} Invalid URP camera data");
                return;
            }

            if (_urpCameraData.cameraStack.Remove(inCamera))
            {
                OnCameraRemovedFromStack?.Invoke(inCamera);
            }
        }

        public void ClearCameraStack()
        {
            _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();
            if (_urpCameraData == null) return;

            _urpCameraData.cameraStack.Clear();
            OnCameraStackCleared?.Invoke();
        }

        public int CameraStackCount
        {
            get
            {
                _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();
                return _urpCameraData?.cameraStack?.Count ?? 0;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
