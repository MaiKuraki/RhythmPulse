using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    [ExecuteInEditMode]
    public sealed class UIBackgroundBlurController : MonoBehaviour
    {
        #region Enums

        /// <summary>
        /// Blur quality level - affects GPU sample count
        /// </summary>
        public enum BlurQuality
        {
            /// <summary>5 effective samples per pass, best performance</summary>
            Low,
            /// <summary>7 effective samples per pass, balanced</summary>
            Medium,
            /// <summary>9 effective samples per pass, best quality</summary>
            High
        }

        /// <summary>
        /// Update frequency mode
        /// </summary>
        public enum UpdateMode
        {
            /// <summary>Update blur every frame (for video/animated content)</summary>
            EveryFrame,
            /// <summary>Update only when source changes or ForceUpdate() is called</summary>
            OnChange,
            /// <summary>Manual update via ForceUpdate() only</summary>
            Manual
        }

        #endregion

        #region Serialized Fields

        [Header("References")]
        [Tooltip("The RawImage displaying the source video/image")]
        [SerializeField] private RawImage _sourceRawImage;

        [Tooltip("The RawImage to display the blurred result")]
        [SerializeField] private RawImage _blurredBackgroundRawImage;

        [Header("Blur Settings")]
        [Tooltip("Blur intensity (affects sample spread)")]
        [Range(0.1f, 60f)]
        [SerializeField] private float _blurRadius = 20f;

        [Tooltip("Downsample factor - higher = better performance, lower quality")]
        [Range(1, 8)]
        [SerializeField] private int _downsampleFactor = 4;

        [Tooltip("Number of blur iterations - more = stronger blur")]
        [Range(1, 4)]
        [SerializeField] private int _iterations = 2;

        [Tooltip("Blur quality level")]
        [SerializeField] private BlurQuality _quality = BlurQuality.Medium;

        [Tooltip("Update frequency mode")]
        [SerializeField] private UpdateMode _updateMode = UpdateMode.EveryFrame;

        [Header("Shader")]
        [Tooltip("Assign the SeparableGaussianBlurURP shader")]
        [SerializeField] private Shader _blurShader;

        #endregion

        #region Shader Property IDs (Zero-GC)

        // Cached shader property IDs - eliminates string allocation every frame
        private static readonly int BlurRadiusID = Shader.PropertyToID("_BlurRadius");

        // Shader keyword names (cached as static readonly to avoid string allocation)
        private static readonly string KeywordQualityLow = "_BLUR_QUALITY_LOW";
        private static readonly string KeywordQualityHigh = "_BLUR_QUALITY_HIGH";

        #endregion

        #region Private State

        private Material _blurMaterial;
        private RenderTexture _rtTemp1;
        private RenderTexture _rtTemp2;
        private RenderTexture _rtFinal;

        // Change detection state (reference-based, no GC)
        private Texture _cachedSourceTexture;
        private int _cachedSourceWidth;
        private int _cachedSourceHeight;
        private float _cachedBlurRadius;
        private int _cachedDownsampleFactor;
        private int _cachedIterations;
        private BlurQuality _cachedQuality;
        private Rect _cachedUvRect;

        private bool _isDirty = true;
        private bool _isInitialized;

        #endregion

        #region Public Properties

        /// <summary>Source RawImage to blur</summary>
        public RawImage SourceRawImage
        {
            get => _sourceRawImage;
            set
            {
                if (_sourceRawImage != value)
                {
                    _sourceRawImage = value;
                    _isDirty = true;
                }
            }
        }

        /// <summary>Target RawImage to display blurred result</summary>
        public RawImage BlurredBackgroundRawImage
        {
            get => _blurredBackgroundRawImage;
            set => _blurredBackgroundRawImage = value;
        }

        /// <summary>Blur radius (intensity)</summary>
        public float BlurRadius
        {
            get => _blurRadius;
            set
            {
                if (!Mathf.Approximately(_blurRadius, value))
                {
                    _blurRadius = Mathf.Clamp(value, 0.1f, 60f);
                    _isDirty = true;
                }
            }
        }

        /// <summary>Downsample factor</summary>
        public int DownsampleFactor
        {
            get => _downsampleFactor;
            set
            {
                int clamped = Mathf.Clamp(value, 1, 8);
                if (_downsampleFactor != clamped)
                {
                    _downsampleFactor = clamped;
                    _isDirty = true;
                }
            }
        }

        /// <summary>Number of blur iterations</summary>
        public int Iterations
        {
            get => _iterations;
            set
            {
                int clamped = Mathf.Clamp(value, 1, 4);
                if (_iterations != clamped)
                {
                    _iterations = clamped;
                    _isDirty = true;
                }
            }
        }

        /// <summary>Blur quality level</summary>
        public BlurQuality Quality
        {
            get => _quality;
            set
            {
                if (_quality != value)
                {
                    _quality = value;
                    UpdateShaderKeywords();
                    _isDirty = true;
                }
            }
        }

        /// <summary>Update mode</summary>
        public UpdateMode Mode
        {
            get => _updateMode;
            set => _updateMode = value;
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _sourceRawImage == null || _blurredBackgroundRawImage == null)
            {
                ClearOutput();
                return;
            }

            Texture sourceTexture = _sourceRawImage.texture;
            if (sourceTexture == null)
            {
                ClearOutput();
                return;
            }

            // Determine if we need to update based on mode
            bool shouldUpdate = false;

            switch (_updateMode)
            {
                case UpdateMode.EveryFrame:
                    shouldUpdate = true;
                    break;

                case UpdateMode.OnChange:
                    shouldUpdate = CheckForChanges(sourceTexture);
                    break;

                case UpdateMode.Manual:
                    shouldUpdate = _isDirty;
                    break;
            }

            if (shouldUpdate)
            {
                EnsureRenderTextures(sourceTexture);
                ApplyBlur(sourceTexture);
                _isDirty = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && _isInitialized)
            {
                _isDirty = true;
                UpdateShaderKeywords();
            }
        }
#endif

        #endregion

        #region Public Methods

        /// <summary>
        /// Force an immediate blur update regardless of update mode.
        /// </summary>
        public void ForceUpdate()
        {
            _isDirty = true;
        }

        /// <summary>
        /// Set blur parameters in a single call to minimize property checks.
        /// </summary>
        /// <param name="radius">Blur radius</param>
        /// <param name="downsample">Downsample factor</param>
        /// <param name="iterations">Number of iterations</param>
        /// <param name="quality">Quality level</param>
        public void SetParameters(float radius, int downsample, int iterations, BlurQuality quality)
        {
            _blurRadius = Mathf.Clamp(radius, 0.1f, 60f);
            _downsampleFactor = Mathf.Clamp(downsample, 1, 8);
            _iterations = Mathf.Clamp(iterations, 1, 4);

            if (_quality != quality)
            {
                _quality = quality;
                UpdateShaderKeywords();
            }

            _isDirty = true;
        }

        #endregion

        #region Private Methods

        private void Initialize()
        {
            if (_blurShader == null)
            {
                Debug.LogError("[UIBackgroundBlurController] Blur Shader is not assigned.", this);
                enabled = false;
                return;
            }

            if (_blurMaterial == null)
            {
                _blurMaterial = new Material(_blurShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            UpdateShaderKeywords();
            _isDirty = true;
            _isInitialized = true;
        }

        private void Cleanup()
        {
            ReleaseRenderTextures();

            if (_blurMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_blurMaterial);
                }
                else
                {
                    DestroyImmediate(_blurMaterial);
                }
                _blurMaterial = null;
            }

            _isInitialized = false;
        }

        private void UpdateShaderKeywords()
        {
            if (_blurMaterial == null) return;

            // Disable all quality keywords first
            _blurMaterial.DisableKeyword(KeywordQualityLow);
            _blurMaterial.DisableKeyword(KeywordQualityHigh);

            // Enable the appropriate one
            switch (_quality)
            {
                case BlurQuality.Low:
                    _blurMaterial.EnableKeyword(KeywordQualityLow);
                    break;
                case BlurQuality.High:
                    _blurMaterial.EnableKeyword(KeywordQualityHigh);
                    break;
                    // Medium is default (no keyword needed)
            }
        }

        private bool CheckForChanges(Texture sourceTexture)
        {
            // Reference comparison instead of GetInstanceID()
            if (!ReferenceEquals(_cachedSourceTexture, sourceTexture))
            {
                _cachedSourceTexture = sourceTexture;
                return true;
            }

            if (_cachedSourceWidth != sourceTexture.width ||
                _cachedSourceHeight != sourceTexture.height)
            {
                _cachedSourceWidth = sourceTexture.width;
                _cachedSourceHeight = sourceTexture.height;
                return true;
            }

            if (!Mathf.Approximately(_cachedBlurRadius, _blurRadius) ||
                _cachedDownsampleFactor != _downsampleFactor ||
                _cachedIterations != _iterations ||
                _cachedQuality != _quality)
            {
                _cachedBlurRadius = _blurRadius;
                _cachedDownsampleFactor = _downsampleFactor;
                _cachedIterations = _iterations;
                _cachedQuality = _quality;
                return true;
            }

            // UV rect comparison without boxing
            Rect sourceRect = _sourceRawImage.uvRect;
            if (_cachedUvRect.x != sourceRect.x ||
                _cachedUvRect.y != sourceRect.y ||
                _cachedUvRect.width != sourceRect.width ||
                _cachedUvRect.height != sourceRect.height)
            {
                _cachedUvRect = sourceRect;
                return true;
            }

            return _isDirty;
        }

        private void EnsureRenderTextures(Texture source)
        {
            int width = Mathf.Max(1, source.width / _downsampleFactor);
            int height = Mathf.Max(1, source.height / _downsampleFactor);

            // Check if we need to recreate RTs
            if (_rtFinal != null && _rtFinal.width == width && _rtFinal.height == height)
            {
                return;
            }

            ReleaseRenderTextures();

            // Use temporary RTs for better memory management
            _rtTemp1 = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            _rtTemp2 = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            _rtFinal = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);

            _rtTemp1.filterMode = FilterMode.Bilinear;
            _rtTemp2.filterMode = FilterMode.Bilinear;
            _rtFinal.filterMode = FilterMode.Bilinear;

            _cachedSourceWidth = source.width;
            _cachedSourceHeight = source.height;
        }

        private void ReleaseRenderTextures()
        {
            if (_rtTemp1 != null)
            {
                RenderTexture.ReleaseTemporary(_rtTemp1);
                _rtTemp1 = null;
            }
            if (_rtTemp2 != null)
            {
                RenderTexture.ReleaseTemporary(_rtTemp2);
                _rtTemp2 = null;
            }
            if (_rtFinal != null)
            {
                RenderTexture.ReleaseTemporary(_rtFinal);
                _rtFinal = null;
            }
        }

        private void ApplyBlur(Texture source)
        {
            if (_blurMaterial == null || _rtTemp1 == null || _rtFinal == null) return;

            _blurMaterial.SetFloat(BlurRadiusID, _blurRadius);

            // Multi-iteration blur for stronger effect
            RenderTexture currentSource = null;
            RenderTexture currentDest = _rtTemp1;

            for (int i = 0; i < _iterations; i++)
            {
                Texture blitSource = (i == 0) ? source : currentSource;

                // Pass 0: Horizontal blur
                Graphics.Blit(blitSource, currentDest, _blurMaterial, 0);

                // Swap for vertical pass
                RenderTexture temp = (currentDest == _rtTemp1) ? _rtTemp2 : _rtTemp1;

                // Pass 1: Vertical blur
                Graphics.Blit(currentDest, temp, _blurMaterial, 1);

                // Setup for next iteration
                currentSource = temp;
                currentDest = (temp == _rtTemp1) ? _rtTemp2 : _rtTemp1;
            }

            // Final blit to output RT
            if (currentSource != _rtFinal)
            {
                Graphics.Blit(currentSource, _rtFinal);
            }

            // Assign to output RawImage (no allocation - just reference assignment)
            _blurredBackgroundRawImage.texture = _rtFinal;
            _blurredBackgroundRawImage.uvRect = _sourceRawImage.uvRect;
        }

        private void ClearOutput()
        {
            if (_blurredBackgroundRawImage != null && _blurredBackgroundRawImage.texture != null)
            {
                _blurredBackgroundRawImage.texture = null;
            }
        }

        #endregion
    }
}