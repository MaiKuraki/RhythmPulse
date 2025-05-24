using CycloneGames.UIFramework;
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

        protected override void Start()
        {
            base.Start();

            buttonPause.OnClickAsObservable().Subscribe(_ => ClickPause());
            buttonExit.OnClickAsObservable().Subscribe(_ => ClickExit());
            gameplayManager.OnUpdatePlaybackProgress -= UpdateProgressValue;
            gameplayManager.OnUpdatePlaybackProgress += UpdateProgressValue;
        }

        protected override void OnDestroy()
        {
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