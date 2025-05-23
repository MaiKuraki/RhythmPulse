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
        [Inject] private readonly GameplayManager gameplayManager;  //  TODO: Fix injection

        [SerializeField] private Button buttonPause;
        [SerializeField] private Button buttonExit;

        protected override void Awake()
        {
            base.Awake();


        }

        protected override void Start()
        {
            base.Start();

            buttonPause.OnClickAsObservable().Subscribe(_ => ClickPause());
            buttonExit.OnClickAsObservable().Subscribe(_ => ClickExit());
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        private bool bPaused = false;
        void ClickPause()
        {
            gameplayManager.Pause();
        }

        void ClickExit()
        {
            gameplayManager.Exit();
            Debug.Log("ClickExit");
        }
    }
}