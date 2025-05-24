using CycloneGames.UIFramework;
using R3;
using RhythmPulse.Gameplay;
using RhythmPulse.Scene;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    public class UIWindowGameplayResult : UIWindow
    {
        [Inject] private readonly GameplayManager gamepalyManager;

        [SerializeField] private Button buttonBackToLobby;

        protected override void Start()
        {
            base.Start();

            buttonBackToLobby.OnClickAsObservable().Subscribe(_ => ClickBackToLobby());
        }

        private void ClickBackToLobby()
        {
            gamepalyManager.Exit();
        }
    }
}