using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.Media
{
    [RequireComponent(typeof(RawImage))]
    public sealed class UnityVideoRender : MonoBehaviour
    {
        private RawImage _videoImage;

        private void Awake()
        {
            _videoImage = GetComponent<RawImage>();
        }

        private void Reset()
        {
            _videoImage = GetComponent<RawImage>();
            if (_videoImage != null)
            {
                _videoImage.raycastTarget = false;
                _videoImage.color = Color.white;
            }
        }

        public void SetTargetTexture(Texture texture)
        {
            if (_videoImage == null) return;
            
            // Only update if changed to avoid UI rebuild
            if (!ReferenceEquals(_videoImage.texture, texture))
            {
                _videoImage.texture = texture;
                if (texture != null) _videoImage.enabled = true;
            }
        }

        public void SetColor(Color color)
        {
            if (_videoImage != null) _videoImage.color = color;
        }
    }
}