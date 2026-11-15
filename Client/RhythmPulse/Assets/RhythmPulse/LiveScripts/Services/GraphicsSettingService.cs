using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CycloneGames.Logging;

namespace RhythmPulse.Services
{
    // Renamed from ScreenOrientation to avoid conflict with UnityEngine.ScreenOrientation
    public enum DisplayOrientation
    {
        Landscape = 0,
        Portrait = 1
    }

    public interface IGraphicsSettingService
    {
        // Quality & Frame Rate
        void SetQualityLevel(int qualityLevel);
        int CurrentQualityLevel { get; }
        IReadOnlyList<string> QualityLevels { get; }
        void SetTargetFrameRate(int targetFramerate);
        void SetVSyncCount(int vSyncCount);

        // Resolution & Display
        void SetRenderResolution(int shortEdgeResolution, DisplayOrientation orientation = DisplayOrientation.Landscape);
        Vector2Int TargetRenderResolution { get; }
        void SetRenderScale(float scale);
        float RenderScale { get; }
        void SetFullScreenMode(FullScreenMode mode);

        // Anti-Aliasing
        void SetAntiAliasing(int msaaLevel);
        int AntiAliasingLevel { get; }

        // Shadows
        void SetShadowDistance(float distance);

        // Textures
        void SetTextureQuality(int mipmapLimit);
        int TextureQuality { get; }
        void SetAnisotropicFiltering(AnisotropicFiltering mode);
        AnisotropicFiltering AnisotropicFilteringMode { get; }

        // LOD & Rendering Features
        void SetLodBias(float bias);
        float LodBias { get; }
        void SetSoftParticles(bool enabled);
        bool SoftParticlesEnabled { get; }

        // HDR (URP)
        void SetHDR(bool enabled, Camera camera = null);

        event Action OnSettingsApplied;
    }

    public sealed class GraphicsSettingService : IGraphicsSettingService, IDisposable
    {

        private int _currentQualityLevel = -1;
        private IReadOnlyList<string> _qualityLevels;
        private Vector2Int _targetRenderResolution;
        private CancellationTokenSource _resolutionCts;

        public int CurrentQualityLevel
        {
            get
            {
                if (_currentQualityLevel < 0)
                    _currentQualityLevel = QualitySettings.GetQualityLevel();
                return _currentQualityLevel;
            }
        }

        public IReadOnlyList<string> QualityLevels => _qualityLevels ??= QualitySettings.names;
        public Vector2Int TargetRenderResolution => _targetRenderResolution;
        public float RenderScale => UniversalRenderPipeline.asset?.renderScale ?? 1f;
        public int AntiAliasingLevel => QualitySettings.antiAliasing;
        public int TextureQuality => QualitySettings.globalTextureMipmapLimit;
        public AnisotropicFiltering AnisotropicFilteringMode => QualitySettings.anisotropicFiltering;
        public float LodBias => QualitySettings.lodBias;
        public bool SoftParticlesEnabled => QualitySettings.softParticles;

        public event Action OnSettingsApplied;

        public GraphicsSettingService()
        {
            _targetRenderResolution = new Vector2Int(Screen.width, Screen.height);
            RPLog.Channel.Info($"Initialized. Resolution: {Screen.width}x{Screen.height}");
        }

        #region Quality & Frame Rate

        public void SetQualityLevel(int qualityLevel)
        {
            if (qualityLevel < 0 || qualityLevel >= QualityLevels.Count)
            {
                RPLog.Channel.Error($"Invalid quality level: {qualityLevel}");
                return;
            }
            QualitySettings.SetQualityLevel(qualityLevel, true);
            _currentQualityLevel = qualityLevel;
            OnSettingsApplied?.Invoke();
        }

        public void SetTargetFrameRate(int targetFramerate)
        {
            Application.targetFrameRate = targetFramerate;
            OnSettingsApplied?.Invoke();
        }

        public void SetVSyncCount(int vSyncCount)
        {
            QualitySettings.vSyncCount = Mathf.Clamp(vSyncCount, 0, 4);
            OnSettingsApplied?.Invoke();
        }

        #endregion

        #region Resolution & Display

        public void SetRenderResolution(int shortEdgeResolution, DisplayOrientation orientation = DisplayOrientation.Landscape)
        {
            CancelPendingResolutionChange();
            _resolutionCts = new CancellationTokenSource();
            SetResolutionAsync(shortEdgeResolution, orientation, _resolutionCts.Token).Forget();
        }

        public void SetRenderScale(float scale)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                RPLog.Channel.Warning("URP asset not found, cannot set render scale");
                return;
            }
            asset.renderScale = Mathf.Clamp(scale, 0.1f, 2f);
            OnSettingsApplied?.Invoke();
        }

        public void SetFullScreenMode(FullScreenMode mode)
        {
            Screen.fullScreenMode = mode;
            OnSettingsApplied?.Invoke();
        }

        private async UniTaskVoid SetResolutionAsync(int shortEdge, DisplayOrientation orientation, CancellationToken ct)
        {
            try
            {
                int displayWidth = Display.main.systemWidth;
                int displayHeight = Display.main.systemHeight;

                float aspect = (displayWidth > 0 && displayHeight > 0)
                    ? (float)displayWidth / displayHeight
                    : 16f / 9f;

                var (w, h) = CalculateResolution(shortEdge, orientation, aspect);
                _targetRenderResolution = new Vector2Int(w, h);

                Screen.SetResolution(w, h, Screen.fullScreen);
                await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, ct);

                RPLog.Channel.Info($"Resolution changed to: {w}x{h} (Display: {displayWidth}x{displayHeight}, Aspect: {aspect:F2})");
                OnSettingsApplied?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                RPLog.Channel.Error($"Resolution change failed: {ex.Message}");
            }
        }

        private static (int width, int height) CalculateResolution(int shortEdge, DisplayOrientation orientation, float aspect)
        {
            if (aspect <= 0) aspect = 16f / 9f;

            int w, h;
            if (orientation == DisplayOrientation.Landscape)
            {
                h = shortEdge;
                w = Mathf.RoundToInt(shortEdge * aspect);
            }
            else
            {
                w = shortEdge;
                h = Mathf.RoundToInt(shortEdge / aspect);
            }
            return (Mathf.Max(1, w), Mathf.Max(1, h));
        }

        private void CancelPendingResolutionChange()
        {
            if (_resolutionCts != null)
            {
                _resolutionCts.Cancel();
                _resolutionCts.Dispose();
                _resolutionCts = null;
            }
        }

        #endregion

        #region Anti-Aliasing

        public void SetAntiAliasing(int msaaLevel)
        {
            int validLevel = msaaLevel switch
            {
                <= 0 => 0,
                <= 2 => 2,
                <= 4 => 4,
                _ => 8
            };
            QualitySettings.antiAliasing = validLevel;

            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.msaaSampleCount = validLevel == 0 ? 1 : validLevel;
            }
            OnSettingsApplied?.Invoke();
        }

        #endregion

        #region Shadows

        public void SetShadowDistance(float distance)
        {
            QualitySettings.shadowDistance = Mathf.Max(0, distance);
            OnSettingsApplied?.Invoke();
        }

        #endregion

        #region Textures

        public void SetTextureQuality(int mipmapLimit)
        {
            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(mipmapLimit, 0, 3);
            OnSettingsApplied?.Invoke();
        }

        public void SetAnisotropicFiltering(AnisotropicFiltering mode)
        {
            QualitySettings.anisotropicFiltering = mode;
            OnSettingsApplied?.Invoke();
        }

        #endregion

        #region LOD & Features

        public void SetLodBias(float bias)
        {
            QualitySettings.lodBias = Mathf.Clamp(bias, 0.3f, 2f);
            OnSettingsApplied?.Invoke();
        }

        public void SetSoftParticles(bool enabled)
        {
            QualitySettings.softParticles = enabled;
            OnSettingsApplied?.Invoke();
        }

        #endregion

        #region HDR

        public void SetHDR(bool enabled, Camera camera = null)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                asset.supportsHDR = enabled;
            }

            if (camera != null)
            {
                var cameraData = camera.GetUniversalAdditionalCameraData();
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = enabled;
                }
            }
            OnSettingsApplied?.Invoke();
        }

        #endregion

        public void Dispose()
        {
            CancelPendingResolutionChange();
        }
    }
}
