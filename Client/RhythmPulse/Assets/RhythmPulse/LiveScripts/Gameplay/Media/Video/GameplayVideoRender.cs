using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.Gameplay.Media
{
    public class GameplayVideoRender : MonoBehaviour
    {
        [SerializeField] RawImage videoImage;

        public void SetTargetTexture(Texture texture)
        {
            videoImage.texture = texture;
        }
    }
}

