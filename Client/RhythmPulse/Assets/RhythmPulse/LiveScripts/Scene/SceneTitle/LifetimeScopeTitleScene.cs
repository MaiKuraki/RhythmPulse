using VContainer;
using VContainer.Unity;
using MackySoft.Navigathena.SceneManagement.VContainer;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeTitleScene : SceneBaseLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleTitleScene>();
        }
    }
}