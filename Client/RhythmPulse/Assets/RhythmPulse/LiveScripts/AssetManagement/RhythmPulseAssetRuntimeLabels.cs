using CycloneGames.AssetManagement.Runtime;

namespace RhythmPulse.AssetManagement
{
    /// <summary>
    /// Single place for the bucket/tag/owner strings the asset layer uses, plus the stable entry ids of
    /// <see cref="AssetResolver"/>.
    /// <para>
    /// Buckets give handles a shared lifetime: everything loaded through a scope can be released as a
    /// group. Owners identify who asked for an asset, which is what shows up in leak diagnostics.
    /// </para>
    /// </summary>
    public static class RhythmPulseAssetRuntimeLabels
    {
        public static class Buckets
        {
            public static class Foundation
            {
                public const string Resolver = "Foundation.Resolver";
            }
        }

        public static class Tags
        {
            public const string FoundationPrefab = "Foundation.Prefab";
        }

        public static class Owners
        {
            public const string AssetResolver = "AssetResolver";
        }

        /// <summary>
        /// Stable entry ids authored on the AssetResolver prefab. Runtime code addresses persistent
        /// objects through these instead of searching the scene.
        /// </summary>
        public static class Entries
        {
            public const string EventSystem = "event-system";
            public const string MainCamera = "main-camera";
            public const string AudioManager = "audio-manager";
            public const string UiFramework = "ui-framework";
            public const string FpsCounter = "fps-counter";
            public const string IngameDebugConsole = "ingame-debug-console";
            public const string DebugSheetCanvas = "debug-sheet-canvas";
            public const string DebugSheetManager = "debug-sheet-manager";
        }

        public static AssetBucketScope CreateAssetResolverScope(IAssetPackage package)
        {
            return package.CreateBucketScope(
                Buckets.Foundation.Resolver,
                Tags.FoundationPrefab,
                Owners.AssetResolver);
        }
    }
}
