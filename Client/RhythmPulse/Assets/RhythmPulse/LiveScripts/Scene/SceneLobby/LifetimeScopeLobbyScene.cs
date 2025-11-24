using VContainer;
using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer.Unity;
using RhythmPulse.Media;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeLobbyScene : SceneBaseLifetimeScope
    {
        private const string DEBUG_FLAG = "[LifetimeScopeLobbyScene]";

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleLobbyScene>();

            //  Preview media players share the same class of GameplayMediaPlayers, This just registered in this scope.
            builder.Register<IUnityMusicPlayer, UnityMusicPlayer>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<UnityVideoProvider>().AsImplementedInterfaces();
            builder.Register<ITimeline, Timeline>(Lifetime.Singleton);
        }
    }
}