using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Factory.Runtime;
using RhythmPulse.Services;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.IO.Unity;
using Cysharp.Threading.Tasks;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay;
using RhythmPulse.Misc;
using RhythmPulse.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using CycloneGames.Logging;

namespace RhythmPulse
{
    public class ProjectSharedLifetimeScope : LifetimeScope
    {
        public ProjectSharedLifetimeScope Instance { get; private set; }
        [SerializeField] private AddressableResolverForDontDestroy assetResolver;
        protected override void Awake()
        {
            base.Awake();

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            builder.RegisterComponentInNewPrefab(assetResolver, Lifetime.Singleton).UnderTransform(transform);

            builder.Register<IMainCameraService, MainCameraService>(Lifetime.Singleton);
            builder.Register<ISceneManagementAPIGateway, SceneManagementAPIGateway>(Lifetime.Singleton);
            builder.Register<IAudioLoadService, AudioLoadService>(Lifetime.Singleton);
            builder.Register<IGraphicsSettingService, GraphicsSettingService>(Lifetime.Singleton);
            builder.Register<IAssetPathBuilderFactory, AssetPathBuilderFactory>(Lifetime.Singleton);
            // One owner for the "scene scope first, project scope otherwise" injection rule.
            builder.Register<SceneScopeResolver>(Lifetime.Singleton);
            builder.Register<RhythmPulseObjectSpawner>(Lifetime.Singleton);
            builder.Register<IUnityObjectSpawner>(resolver => resolver.Resolve<RhythmPulseObjectSpawner>(), Lifetime.Transient);
            // The UI service is built lazily: it needs the instantiated UIRoot and the initialized
            // asset package, both of which only exist after the initial scene has booted.
            // Transient so an early resolve cannot cache a null instance; the host always hands out
            // the same service after EnsureInitialized.
            builder.Register<UIServiceHost>(Lifetime.Singleton);
            builder.Register<IUIService>(resolver => resolver.Resolve<UIServiceHost>().Service, Lifetime.Transient);

            builder.RegisterBuildCallback(resolver =>
            {
                var assetResolver = resolver.Resolve<AddressableResolverForDontDestroy>();
                // Note: Component initialization happens automatically via RegisterComponentInNewPrefab
                // AudioManager will be instantiated by AddressableResolverForDontDestroy.InitializeAsync()
                // so SetAssetModule is called from LifecycleInitialScene.OnEnter() after initialization.
            });

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
            //  Or if you dont want to create class AudioSourceFactory.
            builder.Register<IFactory<GameAudioSource>>(container =>
                new MonoPrefabFactory<GameAudioSource>(
                    container.Resolve<IUnityObjectSpawner>(),
                    container.Resolve<IAudioLoadService>().AudioSourcePrefab
                ), Lifetime.Singleton);

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
                _mapStorage.AddBasePath("DownloadedMap", UnityFileLocation.PersistentData);
                _mapListManager.LoadAllMapsAsync(UnityEngine.Application.exitCancellationToken);
                RpLog.Channel.Info("[MapListGlobalInitializer] Initiated global map list");
            }
        }
    }
}