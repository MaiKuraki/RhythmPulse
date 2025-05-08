using VContainer;
using VContainer.Unity;
using CycloneGames.Logger;
using CycloneGames.Factory;
using CycloneGames.Service;

namespace RhythmPulse
{
    public class ProjectSharedLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            CLogger.Instance.AddLogger(new UnityLogger());
            builder.Register<IUnityObjectSpawner, RhythmObjectSpawner>(Lifetime.Singleton);
            builder.Register<IMainCameraService, MainCameraService>(Lifetime.Singleton);
        }
    }
}