using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    [ExecuteInEditMode] // Optional: Allows running in editor for setup
    public class UIBackgroundBlurController : MonoBehaviour
    {
        [Header("References")]
        public RawImage sourceRawImage; // The RawImage displaying the video / image (source)
        public RawImage blurredBackgroundRawImage; // The RawImage to display the blurred result

        [Header("Blur Settings")]
        [Range(0.1f, 60f)]
        public float blurRadius = 20f; // Corresponds to sigma in the shader

        [Range(1, 8)]
        public int downsampleFactor = 4; // Larger factor = more performance, potentially lower quality

        [Header("Shader & Material")]
        public Shader blurShader; // Assign the SeparableGaussianBlurURP shader here

        private Material _blurMaterial;
        private RenderTexture _rtTemp1; // For horizontal blur output
        private RenderTexture _rtFinal; // For vertical blur output (final blurred image)

        private int _lastSourceTextureInstanceID = 0;
        private int _lastSourceWidth = 0;
        private int _lastSourceHeight = 0;
        private float _lastBlurRadius = 0f;
        private int _lastDownsampleFactor = 0;

        void OnEnable()
        {
            if (blurShader == null)
            {
                Debug.LogError("Blur Shader is not assigned.", this);
                enabled = false;
                return;
            }

            if (_blurMaterial == null)
            {
                _blurMaterial = new Material(blurShader);
                _blurMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
            // Initial state reset to force RT creation
            _lastSourceTextureInstanceID = 0;
        }

        void OnDisable()
        {
            ReleaseRenderTextures();
            if (_blurMaterial != null)
            {
                DestroyImmediate(_blurMaterial);
                _blurMaterial = null;
            }
        }

        void LateUpdate()
        {
            if (sourceRawImage == null || blurredBackgroundRawImage == null || _blurMaterial == null)
            {
                if (blurredBackgroundRawImage && blurredBackgroundRawImage.texture != null)
                {
                    // Clear the texture if setup is invalid to avoid showing stale frame
                    blurredBackgroundRawImage.texture = null;
                }
                return;
            }

            Texture sourceTexture = sourceRawImage.texture;
            if (sourceTexture == null)
            {
                if (blurredBackgroundRawImage.texture != null)
                {
                    blurredBackgroundRawImage.texture = null;
                }
                return;
            }

            // Check if RTs need recreation
            bool settingsChanged = _lastSourceTextureInstanceID != sourceTexture.GetInstanceID() ||
                                   _lastSourceWidth != sourceTexture.width ||
                                   _lastSourceHeight != sourceTexture.height ||
                                   _lastBlurRadius != blurRadius ||
                                   _lastDownsampleFactor != downsampleFactor;

            if (settingsChanged)
            {
                InitializeRenderTextures(sourceTexture);
                _lastSourceTextureInstanceID = sourceTexture.GetInstanceID();
                _lastSourceWidth = sourceTexture.width;
                _lastSourceHeight = sourceTexture.height;
                _lastBlurRadius = blurRadius;
                _lastDownsampleFactor = downsampleFactor;
            }

            if (_rtTemp1 == null || _rtFinal == null)
            {
                if (blurredBackgroundRawImage.texture != null)
                {
                    blurredBackgroundRawImage.texture = null;
                }
                return; // RTs not ready
            }

            ApplyBlur(sourceTexture);
        }

        void InitializeRenderTextures(Texture source)
        {
            ReleaseRenderTextures(); // Release old ones first

            int width = Mathf.Max(1, source.width / downsampleFactor);
            int height = Mathf.Max(1, source.height / downsampleFactor);

            // Using ARGB32 format, common for UI and video. Adjust if needed.
            // No depth buffer needed for these temporary blit targets.
            _rtTemp1 = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            _rtFinal = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);

            _rtTemp1.filterMode = FilterMode.Bilinear;
            _rtFinal.filterMode = FilterMode.Bilinear;
        }

        void ReleaseRenderTextures()
        {
            if (_rtTemp1 != null)
            {
                RenderTexture.ReleaseTemporary(_rtTemp1);
                _rtTemp1 = null;
            }
            if (_rtFinal != null)
            {
                RenderTexture.ReleaseTemporary(_rtFinal);
                _rtFinal = null;
            }
        }

        void ApplyBlur(Texture source)
        {
            if (_blurMaterial == null || _rtTemp1 == null || _rtFinal == null) return;

            _blurMaterial.SetFloat("_BlurRadius", blurRadius);

            // Pass 0: Horizontal Blur
            // Source texture's texel size is implicitly used by _MainTex_TexelSize in shader when blitting from source
            // For the first pass, we read from 'source' and write to '_rtTemp1'.
            // The shader's _MainTex_TexelSize will be for 'source'.
            Graphics.Blit(source, _rtTemp1, _blurMaterial, 0);


            // Pass 1: Vertical Blur
            // For the second pass, we read from '_rtTemp1' and write to '_rtFinal'.
            // The shader's _MainTex_TexelSize will be for '_rtTemp1'.
            Graphics.Blit(_rtTemp1, _rtFinal, _blurMaterial, 1);

            // Assign the final blurred texture to the background RawImage
            blurredBackgroundRawImage.texture = _rtFinal;
            // Match UV Rect if the source has a custom one (e.g. video player might adjust it)
            blurredBackgroundRawImage.uvRect = sourceRawImage.uvRect;
        }

        // Optional: Editor helper to force update if parameters change
#if UNITY_EDITOR
        void OnValidate()
        {
            // This will force LateUpdate to re-evaluate and potentially re-initialize RTs
            // if parameters changed in editor.
            if (Application.isPlaying) // Only force re-init if playing for OnValidate changes
            {
                _lastBlurRadius = -1f; // Force re-check
            }
        }
#endif
    }
}