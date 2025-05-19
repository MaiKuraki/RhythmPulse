using CycloneGames.Logger;
using CycloneGames.UIFramework;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    public class UIWindowLobby : UIWindow
    {
        private enum EGameMode // should not be public
        {
            Beats,
            Dance
        }
        [SerializeField] Button buttonBeatsGame;
        [SerializeField] Button buttonDanceGame;

        protected override void Awake()
        {
            base.Awake();

            buttonBeatsGame.OnClickAsObservable().Subscribe(_ => EnterGame(EGameMode.Beats));
            buttonDanceGame.OnClickAsObservable().Subscribe(_ => EnterGame(EGameMode.Dance));
        }

        void EnterGame(EGameMode gameMode)
        {
            switch (gameMode)
            {
                case EGameMode.Beats:
                CLogger.LogInfo("[UIWindowLobby] Enter Beats Game");
                    break;
                case EGameMode.Dance:
                CLogger.LogInfo("[UIWindowLobby] Enter Dance Game");
                    break;
            }
        }
    }
}