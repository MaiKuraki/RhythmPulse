using CycloneGames.AssetManagement.Runtime;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse.AOT
{
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<IAssetModule, AddressablesModule>(Lifetime.Singleton).Keyed("Addressables");

            builder.RegisterBuildCallback(resolver =>
            {
                var addressableModule = resolver.Resolve<IAssetModule>("Addressables");
                addressableModule.Initialize(new AssetManagementOptions());
                var pkg = addressableModule.CreatePackage("DefaultPackage");
                pkg.InitializeAsync(default);
            });
        }
    }
}