using CycloneGames.UIFramework.Runtime;
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

        void Start()
        {
            buttonBackToLobby.OnClickAsObservable().Subscribe(_ => ClickBackToLobby());
        }

        private void ClickBackToLobby()
        {
            gamepalyManager.Exit();
        }
    }
}