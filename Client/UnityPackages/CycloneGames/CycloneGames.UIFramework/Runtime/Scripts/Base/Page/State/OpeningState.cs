namespace CycloneGames.UIFramework
{
    public class OpeningState : UIWindowState
    {
        public override void OnEnter(UIWindow page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Opening: {page.WindowName}");
        }

        public override void OnExit(UIWindow page)
        {
            
        }
    }
}