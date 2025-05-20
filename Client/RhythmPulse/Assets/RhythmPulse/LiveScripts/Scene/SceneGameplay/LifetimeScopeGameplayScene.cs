using VContainer;
using VContainer.Unity;
using MackySoft.Navigathena.SceneManagement.VContainer;
using RhythmPulse.Gameplay;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeGameplayScene : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleGameplayScene>();
            builder.RegisterComponentInHierarchy<GameplayManager>();
        }
    }
}