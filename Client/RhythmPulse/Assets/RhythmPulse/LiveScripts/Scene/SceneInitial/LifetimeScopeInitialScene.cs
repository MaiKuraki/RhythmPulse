using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeInitialScene : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleInitialScene>();
        }
    }
}