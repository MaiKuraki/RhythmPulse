using VContainer;
using VContainer.Unity;
using CycloneGames.Logger;
using CycloneGames.Service;
using RhythmPulse.APIGateway;
using RhythmPulse.Audio;
using RhythmPulse.Gameplay;

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

            // TODO:
            // This class might be better instantiated only in the Lobby scene rather than as a global singleton,
            // because initializing the gameplay map list requires significant disk read/write operations,
            // especially when there are many files. Keeping it in memory globally could help reduce repeated
            // disk I/O and improve performance during gameplay.
            //
            // NOTE:
            // Consider the tradeoff between memory usage and I/O cost:
            // - Creating a new instance each time (e.g., only in Lobby) leads to higher disk I/O on each initialization.
            // - Maintaining a global singleton instance keeps the map list cached in memory, reducing disk access overhead.
            builder.Register<IGameplayMapStorage, GameplayMapStorage>(Lifetime.Singleton);
        }
    }
}