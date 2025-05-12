namespace CycloneGames.UIFramework
{
    // CAUTION: if you modify this interface name,
    //          don't forget modify the link.xml file located in the CycloneGames.UIFramework/Scripts/Framework folder
    public interface IUIWindowState
    {
        void OnEnter(UIWindow page);
        void OnExit(UIWindow page);
        void Update(UIWindow page);
    }

    public abstract class UIWindowState : IUIWindowState
    {
        protected const string DEBUG_FLAG = "[UIPageState]";
        public abstract void OnEnter(UIWindow page);

        public abstract void OnExit(UIWindow page);

        public virtual void Update(UIWindow page) { }
    }
}