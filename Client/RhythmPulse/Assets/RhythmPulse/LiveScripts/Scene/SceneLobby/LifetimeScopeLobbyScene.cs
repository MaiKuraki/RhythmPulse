using VContainer;
using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer.Unity;
using RhythmPulse.Gameplay.Media;

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
            builder.Register<IGameplayMusicPlayer, GameplayMusicPlayer>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<GameplayVideoPlayer>().AsImplementedInterfaces();
            builder.Register<ITimeline, Timeline>(Lifetime.Singleton);
        }
    }
}