using CycloneGames.Service;
using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.Scene
{
    public class LifetimeScopeInitialScene : SceneBaseLifetimeScope
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
            graphicsSettingService.ChangeRenderResolution(1080);
            graphicsSettingService.ChangeApplicationFrameRate(60);
        }
    }
}