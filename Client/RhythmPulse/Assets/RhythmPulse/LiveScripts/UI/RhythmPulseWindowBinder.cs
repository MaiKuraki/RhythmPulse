using CycloneGames.UIFramework.Runtime;
using RhythmPulse.Misc;

namespace RhythmPulse.UI
{
    /// <summary>
    /// Injects each opened window through the project's scene-first resolver.
    /// <para>
    /// The previous UIManager instantiated windows through <c>IUnityObjectSpawner</c>, which injected the
    /// window component implicitly. The new UIService instantiates the prefab itself and exposes
    /// <see cref="IUIWindowBinder"/> for exactly this purpose, so the same rule is re-applied here.
    /// Child components are still injected explicitly by the window (see
    /// <c>UIWindowLobby.RegisterElementsAfterDIInitialized</c>).
    /// </para>
    /// </summary>
    public sealed class RhythmPulseWindowBinder : IUIWindowBinder
    {
        private readonly SceneScopeResolver _sceneScopes;

        public RhythmPulseWindowBinder(SceneScopeResolver sceneScopes)
        {
            _sceneScopes = sceneScopes;
        }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            // Returning null means "no per-window lifetime work"; UIService treats that as optional.
            _sceneScopes.Inject(context.Window);
            return null;
        }
    }
}
