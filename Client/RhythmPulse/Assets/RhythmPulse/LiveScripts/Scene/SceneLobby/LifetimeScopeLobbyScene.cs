using VContainer;
using MackySoft.Navigathena.SceneManagement.VContainer;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeLobbyScene : SceneBaseLifetimeScope
    {
        private const string DEBUG_FLAG = "[LifetimeScopeLobbyScene]";

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterSceneLifecycle<LifecycleLobbyScene>();
        }
    }
}