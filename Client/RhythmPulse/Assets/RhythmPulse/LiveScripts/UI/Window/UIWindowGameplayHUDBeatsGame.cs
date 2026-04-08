using CycloneGames.UIFramework.Runtime;
using R3;
using RhythmPulse.APIGateway;
using RhythmPulse.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    public class UIWindowGameplayHUDBeatsGame : UIWindow
    {
        [Inject] private readonly ISceneManagementAPIGateway sceneManagementAPIGateway;
        [Inject] private readonly GameplayManager gameplayManager;
        [SerializeField] private Button buttonPause;
        [SerializeField] private Button buttonExit;
        [SerializeField] private Slider progressBar;

        protected override void Awake()
        {
            base.Awake();

            progressBar.value = 0;
        }

        void Start()
        {
            buttonPause.OnClickAsObservable().Subscribe(_ => ClickPause()).AddTo(this);
            buttonExit.OnClickAsObservable().Subscribe(_ => ClickExit()).AddTo(this);
            gameplayManager.OnUpdatePlaybackProgress -= UpdateProgressValue;
            gameplayManager.OnUpdatePlaybackProgress += UpdateProgressValue;
        }

        protected override void OnDestroy()
        {
            gameplayManager.OnUpdatePlaybackProgress -= UpdateProgressValue;
            base.OnDestroy();
        }

        void ClickPause()
        {
            gameplayManager.Pause();
        }

        void ClickExit()
        {
            gameplayManager.Exit();
        }

        void UpdateProgressValue(float progressValue)
        {
            progressBar.value = progressValue;
        }
    }
}