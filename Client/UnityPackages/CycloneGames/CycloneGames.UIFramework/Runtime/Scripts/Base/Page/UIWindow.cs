using UnityEngine;

namespace CycloneGames.UIFramework
{
    public class UIWindow : MonoBehaviour
    {
        [SerializeField, Header("Priority Override"), Range(-100, 400)] private int priority;
        public int Priority => priority;
        private string windowName;
        public string WindowName => windowName;
        private IUIWindowState currentState;
        private UILayer parentLayer;

        public void SetWindowName(string NewWindowName)
        {
            windowName = NewWindowName;
        }
        public void SetUILayer(UILayer layer)
        {
            parentLayer = layer;
        }

        public void Close()
        {
            OnStartClose();

            // TODO: maybe move to the end of closing animation
            OnFinishedClose();
        }

        private void ChangeState(IUIWindowState newState)
        {
            currentState?.OnExit(this);
            currentState = newState;
            currentState?.OnEnter(this);
        }

        protected virtual void OnStartOpen()
        {
            ChangeState(new OpeningState());
        }

        protected virtual void OnFinishedOpen()
        {
            ChangeState(new OpenedState());
        }

        protected virtual void OnStartClose()
        {
            ChangeState(new ClosingState());
        }

        protected virtual void OnFinishedClose()
        {
            ChangeState(new ClosedState());
            if (parentLayer != null)
            {
                parentLayer.RemoveWindow(windowName);
            }
            Destroy(gameObject);
        }

        protected virtual void Awake()
        {

        }

        protected virtual void Start()
        {
            // TODO: maybe move to the start of opening animation
            OnStartOpen();

            // TODO: maybe move to the end of opening animation
            OnFinishedOpen();
        }

        protected virtual void Update()
        {
            currentState?.Update(this);
        }

        protected virtual void OnDestroy()
        {
            if(parentLayer != null)
            {
                parentLayer.RemoveWindow(windowName);
                parentLayer = null;
            }
        }
    }
}