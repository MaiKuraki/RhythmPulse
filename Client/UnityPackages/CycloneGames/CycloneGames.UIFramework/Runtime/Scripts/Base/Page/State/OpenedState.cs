namespace CycloneGames.UIFramework
{
    public class OpenedState : UIWindowState
    {
        public override void OnEnter(UIWindow page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Opened: {page.WindowName}");
        }

        public override void OnExit(UIWindow page)
        {
            
        }
    }
}