using VContainer;
using VContainer.Unity;
using CycloneGames.Factory;
using CycloneGames.Service;
using CycloneGames.UIFramework;

namespace RhythmPulse
{
    /// <summary> 
    /// The base lifetime scope class for the scene, which inherits from LifetimeScope provided by VContainer. 
    /// </summary> 
    public class SceneBaseLifetimeScope : LifetimeScope
    {
        public static class SharedServiceRegistrar
        {
            /// <summary> 
            /// Registers shared services with the container builder. 
            /// Services and the objects within them registered by this method should not be created multiple times and stored in DontDestroyOnLoad. 
            /// If necessary, they should be created as MonoBehavior singletons or managed by internal singletons. 
            /// </summary> 
            /// <param name="builder">The container builder used to register services.</param> 
            public static void RegisterSharedServices(IContainerBuilder builder)
            {
                builder.Register<IUnityObjectSpawner, RhythmObjectSpawner>(Lifetime.Singleton);
                builder.Register<IAssetPathBuilderFactory, AssetPathBuilderFactory>(Lifetime.Singleton);
                builder.Register<IUIService, UIService>(Lifetime.Singleton);
            }
        }
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            SharedServiceRegistrar.RegisterSharedServices(builder);
        }
    }
}