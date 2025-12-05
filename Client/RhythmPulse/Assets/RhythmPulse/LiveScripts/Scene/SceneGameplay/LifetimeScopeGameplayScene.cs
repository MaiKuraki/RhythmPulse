using VContainer;
using VContainer.Unity;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Gameplay;
using RhythmPulse.Media;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeGameplayScene : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleGameplayScene>();
            builder.RegisterComponentInHierarchy<GameplayManager>();
            builder.Register<IUnityMusicPlayer, UnityMusicPlayer>(Lifetime.Singleton);
            builder.RegisterComponentInHierarchy<UnityVideoProvider>().AsImplementedInterfaces();
            builder.Register<ITimeline, Timeline>(Lifetime.Singleton);
        }
    }
}