namespace CycloneGames.UIFramework
{
    public class ClosingState : UIWindowState
    {
        public override void OnEnter(UIWindow page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Closing: {page.WindowName}");
        }

        public override void OnExit(UIWindow page)
        {
            
        }
    }
}