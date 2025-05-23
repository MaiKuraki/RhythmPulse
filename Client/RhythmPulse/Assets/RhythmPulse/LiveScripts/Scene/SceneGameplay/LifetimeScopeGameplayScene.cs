using VContainer;
using VContainer.Unity;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Gameplay;
using RhythmPulse.Gameplay.Media;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeGameplayScene : SceneBaseLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleGameplayScene>();
            builder.RegisterComponentInHierarchy<GameplayManager>();
            builder.Register<IGameplayMusicPlayer, GameplayMusicPlayer>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<GameplayVideoPlayer>().AsImplementedInterfaces();
            builder.Register<ITimeline, Timeline>(Lifetime.Singleton);
        }
    }
}