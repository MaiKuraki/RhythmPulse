using System;
using CycloneGames.Logger;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    public class UIPageGameModeSelection : MonoBehaviour
    {
        private enum EGameMode // should not be public
        {
            Beats,
            Dance
        }

        [SerializeField] Button buttonBeatsGame;
        [SerializeField] Button buttonDanceGame;

        public Action EnterTraditionalBeatsGame;
        public Action EnterDanceGame;

        void Awake()
        {
            buttonBeatsGame.OnClickAsObservable().Subscribe(_ => EnterMusicSelection(EGameMode.Beats));
            buttonDanceGame.OnClickAsObservable().Subscribe(_ => EnterMusicSelection(EGameMode.Dance));
        }

        void EnterMusicSelection(EGameMode gameMode)
        {
            switch (gameMode)
            {
                case EGameMode.Beats:
                    CLogger.LogInfo("[UIWindowLobby] Enter Beats Game");
                    EnterTraditionalBeatsGame?.Invoke();
                    break;
                case EGameMode.Dance:
                    CLogger.LogInfo("[UIWindowLobby] Enter Dance Game");
                    EnterDanceGame?.Invoke();
                    break;
            }
        }
    }
}