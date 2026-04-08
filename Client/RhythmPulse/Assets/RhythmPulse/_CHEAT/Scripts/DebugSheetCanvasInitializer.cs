using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RhythmPulse.Cheat
{
    public class DebugSheetCanvasInitializer : MonoBehaviour
    {
        [SerializeField] CanvasGroup canvasGroup;

        void Start()
        {
            DelayDisplayCanvas().Forget();
        }

        async UniTask DelayDisplayCanvas()
        {
            await UniTask.Delay(500);
            canvasGroup.alpha = 1;
        }
    }
}