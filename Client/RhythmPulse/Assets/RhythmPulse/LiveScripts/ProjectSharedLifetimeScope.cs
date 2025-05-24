using VContainer;
using VContainer.Unity;
using CycloneGames.Logger;
using CycloneGames.Factory;
using CycloneGames.Service;
using CycloneGames.UIFramework;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;

namespace RhythmPulse
{
    public class ProjectSharedLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            CLogger.Instance.AddLogger(new UnityLogger());
            builder.Register<IMainCameraService, MainCameraService>(Lifetime.Singleton);
            builder.Register<ISceneManagementAPIGateway, SceneManagementAPIGateway>(Lifetime.Singleton);
            builder.Register<IAudioLoadService, AudioLoadService>(Lifetime.Singleton);
        }
    }
}