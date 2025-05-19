using CycloneGames.Logger;
using CycloneGames.UIFramework;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    public class UIWindowTitle : UIWindow
    {
        [SerializeField] private Button buttonStart;

        protected override void Awake()
        {
            base.Awake();

            buttonStart.OnClickAsObservable().Subscribe(_ => ClickStart());
        }

        void ClickStart()
        {
            CLogger.LogInfo("[UIWindowTitle] ClickStart");
        }
    }
}