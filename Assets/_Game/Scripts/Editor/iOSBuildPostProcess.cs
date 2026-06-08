#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Post-build patcher for the generated Xcode project. Three jobs:
    //   1. Inject NSHealthShareUsageDescription into Info.plist so the
    //      HealthKit permission modal shows our copy (Apple rejects builds
    //      that prompt for HK without a non-empty description).
    //   2. Wire the HealthKit capability + .entitlements file via the
    //      ProjectCapabilityManager — this also adds the HealthKit framework
    //      to the link step so the native plugin's symbols resolve.
    //   3. Force PlayerSettings.iOS.applicationIdentifier to a known value
    //      *before* Unity writes the project, so the Bundle ID in the Xcode
    //      project always matches what we expect (avoids accidental rebuilds
    //      under a stale com.DefaultCompany.gamexercise).
    //
    // TODO(jackson): change BUNDLE_ID to your final reverse-DNS identifier
    // BEFORE the first TestFlight upload. The current value is a placeholder
    // and rejecting a build with the wrong bundle ID forces a fresh App Store
    // Connect record — keep this in sync with whatever you register there.
    public static class iOSBuildPostProcess
    {
        const string BUNDLE_ID         = "com.jackson.gamexercise";
        const string HEALTHKIT_USAGE   = "Gamexercise tracks your daily steps to power your character's progression — gold earned, levels gained, and outfits unlocked all reflect your real-world activity.";
        const string ENTITLEMENTS_NAME = "Unity-iPhone.entitlements";

        // PreprocessBuild — runs before the Xcode project is written. Sets
        // bundle ID via PlayerSettings so the generated .xcodeproj has it
        // baked in (rather than us editing the .xcodeproj after the fact).
        [InitializeOnLoadMethod]
        static void EnsureBundleId()
        {
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS) != BUNDLE_ID)
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BUNDLE_ID);
        }

        [PostProcessBuild(45)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSHealthShareUsageDescription", HEALTHKIT_USAGE);
            plist.WriteToFile(plistPath);

            string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            string mainTargetGuid = proj.GetUnityMainTargetGuid();

            var capabilityManager = new ProjectCapabilityManager(projPath, ENTITLEMENTS_NAME, null, mainTargetGuid);
            capabilityManager.AddHealthKit();
            capabilityManager.WriteToFile();
        }
    }
}
#endif
