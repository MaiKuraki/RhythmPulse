using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.VContainer;
using VContainer;

namespace RhythmPulse.AOT
{
    public class LifecycleLaunchScene : ISceneLifecycle
    {
        [Inject][Key("Addressables")] IAssetModule assetModule;
        private const string DefaultPackage = "DefaultPackage";

        public UniTask OnEditorFirstPreInitialize(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public async UniTask OnEnter(ISceneDataReader reader, CancellationToken cancellationToken)
        {
            await GlobalSceneNavigator.Instance.Push(BuiltInSceneDefinitions.Initial);
        }

        public UniTask OnExit(ISceneDataWriter writer, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public UniTask OnFinalize(ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        public async UniTask OnInitialize(ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
        {
            await InitAssetPackagePipeline(cancellationToken);
        }

        private async UniTask<bool> InitAssetPackagePipeline(CancellationToken cancellationToken)
        {
            try
            {
                if (!assetModule.Initialized)
                {
                    UnityEngine.Debug.Log("[LifecycleSceneLaunch] Asset module not yet initialized, waiting...");
                    int waitCount = 0;
                    while (!assetModule.Initialized && waitCount < 100) // Wait up to 10 seconds
                    {
                        await UniTask.Delay(100, cancellationToken: cancellationToken);
                        waitCount++;
                    }

                    if (!assetModule.Initialized)
                    {
                        UnityEngine.Debug.LogError("[LifecycleSceneLaunch] Asset module initialization timeout!");
                        return false;
                    }
                }

                var pkg = await AssetPackageFactory.CreateAndInitializePackageAsync(
                                        module: assetModule,
                                        packageName: DefaultPackage,
                                        options: new AssetPackageInitOptions(),
                                        cancellationToken: cancellationToken);

                if (pkg == null)
                {
                    UnityEngine.Debug.LogError("[LifecycleSceneLaunch] Asset package initialization failed: package is null.");
                    return false;
                }

#if !UNITY_EDITOR
                // Addressables models catalogs, not version-addressable packages. The catalog maintenance
                // capability is optional: local-only builds simply do not expose it.
                if (pkg is IAddressablesCatalogMaintenance catalogMaintenance)
                {
                    var releaseVersion = await catalogMaintenance.ReadReleaseMetadataVersionAsync(cancellationToken);
                    UnityEngine.Debug.Log($"[LifecycleSceneLaunch] Release metadata version: {releaseVersion}");

                    // Try to refresh catalogs (for hot-update/online games)
                    // For standalone games, this will gracefully handle "content update not available"
                    var catalogUpdateSuccess = await catalogMaintenance.UpdateLatestCatalogsAsync(cancellationToken);
                    if (catalogUpdateSuccess)
                    {
                        UnityEngine.Debug.Log($"[LifecycleSceneLaunch] Addressables catalogs refreshed. Release version: {releaseVersion}");
                    }
                    else
                    {
                        // Update failed, but this might be expected for standalone games
                        // Continue anyway as we can use local content
                        UnityEngine.Debug.LogWarning($"[LifecycleSceneLaunch] Catalog update failed, but continuing with local content. Release version: {releaseVersion}");
                    }
                }
                else
                {
                    UnityEngine.Debug.Log("[LifecycleSceneLaunch] Package does not expose catalog maintenance; using bundled catalogs.");
                }
#endif

                return true;
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("[LifecycleSceneLaunch] Package initialization was cancelled.");
                return false;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LifecycleSceneLaunch] Exception during YooAsset initialization: {ex.Message}\nStack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}