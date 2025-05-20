using System.Threading;
using CycloneGames.Service;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace RhythmPulse.Gameplay
{
    public class GameplayManager : MonoBehaviour
    {
        private bool IsDIInitialized = false;
        private IMainCameraService mainCameraService;
        [SerializeField] Camera gameplayCamera;

        [Inject]
        public void Construct(IMainCameraService mainCameraService)
        {
            this.mainCameraService = mainCameraService;

            IsDIInitialized = true;
        }

        private async UniTask RunAfterDIInitialized(CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => IsDIInitialized, PlayerLoopTiming.Update, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            mainCameraService.AddCameraToStack(gameplayCamera);
        }

        void Awake()
        {
            RunAfterDIInitialized(destroyCancellationToken).Forget();
        }

        void OnDestroy()
        {
            mainCameraService?.RemoveCameraFromStack(gameplayCamera);
        }
    }
}