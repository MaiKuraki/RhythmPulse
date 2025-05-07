using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.AOT
{
    public class LifetimeScopeLaunchScene : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleLaunchScene>();
        }
    }
}