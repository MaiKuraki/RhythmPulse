using CycloneGames.Logger;
using VContainer;
using VContainer.Unity;

namespace RhythmPulse
{
    public class ProjectSharedLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            CLogger.Instance.AddLogger(new UnityLogger());
        }
    }
}