using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.Media
{
    /// <summary>
    /// Handles rendering of the video texture to a UI RawImage.
    /// Automatically manages the required RawImage component.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class UnityVideoRender : MonoBehaviour
    {
        private RawImage videoImage;

        private void Awake()
        {
            if (videoImage == null)
            {
                videoImage = GetComponent<RawImage>();
            }
        }

        private void Reset()
        {
            videoImage = GetComponent<RawImage>();
            if (videoImage != null)
            {
                videoImage.raycastTarget = false;
                videoImage.color = Color.white;
            }
        }

        /// <summary>
        /// Updates the RawImage texture.
        /// </summary>
        /// <param name="texture">The RenderTexture from the VideoPlayer.</param>
        public void SetTargetTexture(Texture texture)
        {
            if (videoImage == null) videoImage = GetComponent<RawImage>();
            if (videoImage == null) return;

            // Only update if changed to avoid unnecessary UI rebuilds
            if (videoImage.texture != texture)
            {
                videoImage.texture = texture;

                // Ensure the RawImage is visible if it was potentially hidden
                if (texture != null)
                {
                    videoImage.enabled = true;
                }
            }
        }

        /// <summary>
        /// Helper to set the color (e.g. for fading effects)
        /// </summary>
        public void SetColor(Color color)
        {
            if (videoImage == null) videoImage = GetComponent<RawImage>();
            if (videoImage != null) videoImage.color = color;
        }
    }
}