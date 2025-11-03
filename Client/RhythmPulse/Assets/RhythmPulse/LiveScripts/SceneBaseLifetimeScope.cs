using VContainer;
using VContainer.Unity;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.Factory.Runtime;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Service.Runtime;
using RhythmPulse.Audio;
using RhythmPulse.UI;

namespace RhythmPulse
{
    /// <summary> 
    /// The base lifetime scope class for the scene, which inherits from LifetimeScope provided by VContainer. 
    /// </summary> 
    public class SceneBaseLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            
            builder.Register<IAssetPathBuilderFactory, AssetPathBuilderFactory>(Lifetime.Singleton);
            builder.Register<IUnityObjectSpawner, RhythmObjectSpawner>(Lifetime.Singleton);
            builder.Register<IUIService, RhythmPulseUIService>(Lifetime.Singleton);

            builder.RegisterBuildCallback(resolver =>
            {
                var cameraService = resolver.Resolve<IMainCameraService>();
                var assetPathBuilderFactory = resolver.Resolve<IAssetPathBuilderFactory>();
                var objectSpawner = resolver.Resolve<IUnityObjectSpawner>();
                var assetModule = resolver.Resolve<IAssetModule>("Addressables");
                var pkg = assetModule.GetPackage("DefaultPackage");
                var uiService = resolver.Resolve<IUIService>();
                uiService.Initialize(assetPathBuilderFactory, objectSpawner, cameraService, pkg);
            });
            
            //  Or if you dont want to create class AudioSourceFactory.
            builder.Register<IFactory<GameAudioSource>>(container =>
                new MonoPrefabFactory<GameAudioSource>(
                    container.Resolve<IUnityObjectSpawner>(),
                    container.Resolve<IAudioLoadService>().AudioSourcePrefab
                ), Lifetime.Singleton);
        }
    }
}
