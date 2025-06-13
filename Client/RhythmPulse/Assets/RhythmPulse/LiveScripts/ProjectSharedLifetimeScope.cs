using VContainer;
using VContainer.Unity;
using CycloneGames.Logger;
using CycloneGames.Service;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay;
using CycloneGames.Utility.Runtime;
using CycloneGames.Factory.Runtime;

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
            builder.Register<IGraphicsSettingService, GraphicsSettingService>(Lifetime.Singleton);

            // Current registration: IGameplayMapListManager and IGameplayMapStorage are registered as Singleton in the root scope.
            //
            // TODO: Consider if IGameplayMapListManager should instead be instantiated only within the Lobby scene's scope.
            //
            // Rationale:
            // Initializing the gameplay map list involves potentially significant disk I/O (reading map info files),
            // especially if there are many maps or if their storage paths change.
            //
            // Trade-offs:
            // 1. Scene-Scoped Instance (e.g., Lobby Scene only):
            //    - Pros: Reduces memory footprint if the map list is not needed outside the lobby or if cleared on scene exit.
            //    - Cons: Leads to repeated disk I/O and loading times every time the Lobby scene is entered.
            //
            // 2. Global Singleton Instance (Current Approach):
            //    - Pros: Loads maps once at application startup, keeping the list cached in memory. This significantly reduces
            //            repeated disk access during gameplay and improves responsiveness when navigating to the lobby.
            //    - Cons: Higher continuous memory usage throughout the application's lifetime to hold all MapInfo objects.
            //
            // Decision: For now, maintaining a global singleton is preferred to optimize for minimal disk I/O and smoother
            // in-game performance, accepting the constant memory overhead. Re-evaluate if memory becomes a critical constraint.
            builder.Register<IGameplayMapStorage, GameplayMapStorage>(Lifetime.Singleton);
            builder.Register<IGameplayMapListManager, GameplayMapListManager>(Lifetime.Singleton);

            builder.RegisterEntryPoint<MapListGlobalInitializer>();
        }

        public class MapListGlobalInitializer : IStartable
        {
            private readonly IGameplayMapListManager _mapListManager;
            private readonly IGameplayMapStorage _mapStorage;

            public MapListGlobalInitializer(IGameplayMapListManager mapListManager, IGameplayMapStorage mapStorage)
            {
                _mapListManager = mapListManager;
                _mapStorage = mapStorage;
            }

            public void Start()
            {
                _mapStorage.AddBasePath("DownloadedMap", UnityPathSource.PersistentData);
                _mapListManager.LoadAllMapsAsync(UnityEngine.Application.exitCancellationToken);
                CLogger.LogInfo("[MapListGlobalInitializer] Initiated global map list");
            }
        }
    }
}