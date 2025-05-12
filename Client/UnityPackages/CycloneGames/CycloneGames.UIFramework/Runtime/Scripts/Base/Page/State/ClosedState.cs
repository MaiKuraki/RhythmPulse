namespace CycloneGames.UIFramework
{
    public class ClosedState : UIWindowState
    {
        public override void OnEnter(UIWindow page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Closed: {page.WindowName}");
        }

        public override void OnExit(UIWindow page)
        {
            
        }
    }
}