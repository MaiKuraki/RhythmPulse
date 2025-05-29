using CycloneGames.Logger;
using CycloneGames.UIFramework;
using R3;
using RhythmPulse.APIGateway;
using RhythmPulse.Scene;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace RhythmPulse.UI
{
    public class UIWindowLobby : UIWindow
    {
        [Inject] private readonly ISceneManagementAPIGateway sceneManagementAPIGateway;
        private enum EGameMode // should not be public
        {
            Beats,
            Dance
        }

        [SerializeField] Transform GameModeSelectionTF;
        [SerializeField] Transform MusicSelectionTF;
        [SerializeField] Button buttonBeatsGame;
        [SerializeField] Button buttonDanceGame;

        protected override void Awake()
        {
            base.Awake();

            buttonBeatsGame.OnClickAsObservable().Subscribe(_ => EnterMusicSelection(EGameMode.Beats));
            buttonDanceGame.OnClickAsObservable().Subscribe(_ => EnterMusicSelection(EGameMode.Dance));
        }

        void EnterMusicSelection(EGameMode gameMode)
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

        void EnterGameplay()
        {
            sceneManagementAPIGateway.Push(SceneDefinitions.Gameplay);
        }

        public void EnterGameModeSelection()
        {
            MusicSelectionTF.gameObject.SetActive(false);
            GameModeSelectionTF.gameObject.SetActive(true);
        }

        public void EnterMusicSelection()
        {
            GameModeSelectionTF.gameObject.SetActive(false);
            MusicSelectionTF.gameObject.SetActive(true);
        }
    }
}