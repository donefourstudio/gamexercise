#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamex.EditorTools
{
    // Headless iOS build entry point — produces an Xcode project at
    // Builds/iOS/ that's ready to open in Xcode, archive, and upload to
    // TestFlight. Run from batch mode:
    //
    //   Unity -batchmode -force-metal -projectPath . \
    //     -executeMethod Gamex.EditorTools.iOSBuilder.Build \
    //     -logFile /tmp/build.log
    //
    // Project has no hand-authored .unity scenes — everything is spawned at
    // runtime by GameRunner via [RuntimeInitializeOnLoadMethod]. Unity still
    // requires at least one scene in the build, so we create + save a
    // throwaway empty scene as the bootstrap.
    public static class iOSBuilder
    {
        const string BUILD_DIR        = "Builds/iOS";
        const string BOOT_SCENE_PATH  = "Assets/_Game/Scenes/Bootstrap.unity";
        const string SCENES_FOLDER    = "Assets/_Game/Scenes";

        public static void Build()
        {
            Directory.CreateDirectory(SCENES_FOLDER);

            // Create or refresh the bootstrap scene. It stays empty — every
            // runtime object is injected by [RuntimeInitializeOnLoadMethod]
            // hooks in GameRunner / SentryConfig / etc.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, BOOT_SCENE_PATH);
            Debug.Log("[iOSBuilder] bootstrap scene saved at " + BOOT_SCENE_PATH);

            // Force iOS target so PlayerSettings.iOS.* is what gets baked.
            // EditorUserBuildSettings.SwitchActiveBuildTarget returns false
            // when the SDK is missing — bail loudly in that case.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                if (!switched)
                {
                    Debug.LogError("[iOSBuilder] could not switch to iOS — is the iOS Build Support module installed?");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            Directory.CreateDirectory(BUILD_DIR);
            string outPath = Path.GetFullPath(BUILD_DIR);

            var opts = new BuildPlayerOptions
            {
                scenes           = new[] { BOOT_SCENE_PATH },
                locationPathName = outPath,
                target           = BuildTarget.iOS,
                targetGroup      = BuildTargetGroup.iOS,
                options          = BuildOptions.None,
            };

            Debug.Log("[iOSBuilder] starting BuildPipeline.BuildPlayer -> " + outPath);
            var report = UnityEditor.BuildPipeline.BuildPlayer(opts);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log("[iOSBuilder] SUCCEEDED. Xcode project at " + outPath +
                          " (open Unity-iPhone.xcodeproj inside). Build size: " +
                          (summary.totalSize / 1024 / 1024) + " MB; took " +
                          summary.totalTime.TotalSeconds.ToString("F1") + "s.");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[iOSBuilder] FAILED. Result=" + summary.result +
                               ". Errors=" + summary.totalErrors +
                               ". See log above for diagnostics.");
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
