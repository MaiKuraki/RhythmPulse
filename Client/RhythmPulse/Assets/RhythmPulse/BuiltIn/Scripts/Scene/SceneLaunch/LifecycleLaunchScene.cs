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
        private bool bPackageInitialized = false;

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
                        bPackageInitialized = true;
                        return false;
                    }
                }

                var pkg = await AssetPackageFactory.CreateAndInitializePackageAsync(
                                        module: assetModule,
                                        packageName: DefaultPackage,
                                        options: new AssetPackageInitOptions(AssetPlayMode.Offline, null, bundleLoadingMaxConcurrencyOverride: 8),
                                        cancellationToken: cancellationToken);

                if (pkg == null)
                {
                    UnityEngine.Debug.LogError("[LifecycleSceneLaunch] Asset package initialization failed: package is null.");
                    bPackageInitialized = true;
                    return false;
                }

#if !UNITY_EDITOR
                var pkgVersion = await pkg.RequestPackageVersionAsync(cancellationToken: cancellationToken);
                UnityEngine.Debug.Log($"[LifecycleSceneLaunch] Package version: {pkgVersion}");

                // Try to update manifest (for hot-update/online games)
                // For standalone games, this will gracefully handle "Content update not available"
                var manifestUpdateSuccess = await pkg.UpdatePackageManifestAsync(packageVersion: pkgVersion, cancellationToken: cancellationToken);
                if (manifestUpdateSuccess)
                {
                    UnityEngine.Debug.Log($"[LifecycleSceneLaunch] Asset package initialized successfully. Package version: {pkgVersion}");
                }
                else
                {
                    // Update failed, but this might be expected for standalone games
                    // Continue anyway as we can use local content
                    UnityEngine.Debug.LogWarning($"[LifecycleSceneLaunch] Asset manifest update failed, but continuing with local content. Package version: {pkgVersion}");
                }
#endif
                bPackageInitialized = true;

                return true;
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("[LifecycleSceneLaunch] Package initialization was cancelled.");
                bPackageInitialized = true;
                return false;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LifecycleSceneLaunch] Exception during YooAsset initialization: {ex.Message}\nStack trace: {ex.StackTrace}");
                bPackageInitialized = true;
                return false;
            }
        }
    }
}