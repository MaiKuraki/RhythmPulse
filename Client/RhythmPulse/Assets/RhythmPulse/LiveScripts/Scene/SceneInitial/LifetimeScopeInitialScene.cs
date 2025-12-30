using CycloneGames.Service.Runtime;
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
            
            builder.RegisterEntryPoint<ApplicationInitialPresenter>();
        }
    }

    public class ApplicationInitialPresenter : IStartable
    {
        private readonly IGraphicsSettingService graphicsSettingService;
        public ApplicationInitialPresenter(IGraphicsSettingService graphicsSettingService)
        {
            this.graphicsSettingService = graphicsSettingService;
        }
        public void Start()
        {
            graphicsSettingService.SetRenderResolution(1080);
            graphicsSettingService.SetTargetFrameRate(60);
        }
    }
}