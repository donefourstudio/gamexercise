#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Post-build patcher for the generated Xcode project. Four jobs:
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
    //   4. Copy PrivacyInfo.xcprivacy from Assets/Plugins/iOS/ to the Xcode
    //      project root and add it to the main target's Copy Bundle Resources
    //      phase so it ships inside the .app bundle (Apple requires this
    //      manifest to be at the top level of the bundle, alongside Info.plist).
    //
    // Bundle ID convention: com.donefourstudio.<app-name>. Done Four Studio
    // is Jackson's app-publishing identity that all future App Store releases
    // share. Apple ties App Store Connect records to the Bundle ID, so this
    // value must be stable before the first TestFlight upload.
    public static class iOSBuildPostProcess
    {
        const string BUNDLE_ID            = "com.donefourstudio.gamexercise";
        const string HEALTHKIT_USAGE      = "Gamexercise tracks your daily steps to power your character's progression — gold earned, levels gained, and outfits unlocked all reflect your real-world activity.";
        const string ENTITLEMENTS_NAME    = "Unity-iPhone.entitlements";
        const string PRIVACY_MANIFEST_SRC = "Assets/Plugins/iOS/PrivacyInfo.xcprivacy";
        const string PRIVACY_MANIFEST_DST = "PrivacyInfo.xcprivacy";

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

            CopyPrivacyManifest(pathToBuiltProject, projPath, mainTargetGuid);
        }

        // Apple requires PrivacyInfo.xcprivacy at the top level of the app
        // bundle (same dir as Info.plist after install). Unity doesn't
        // auto-copy .xcprivacy files from Plugins/iOS, so we copy explicitly
        // then register the file under the main target's Copy Bundle
        // Resources phase via PBXProject — that gets it embedded at the
        // bundle root rather than buried inside Frameworks/.
        static void CopyPrivacyManifest(string pathToBuiltProject, string projPath, string mainTargetGuid)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string src = Path.Combine(projectRoot, PRIVACY_MANIFEST_SRC);
            if (!File.Exists(src))
            {
                Debug.LogWarning("[iOSBuildPostProcess] PrivacyInfo.xcprivacy missing at " + src + " — App Store submission will be rejected without it.");
                return;
            }
            string dst = Path.Combine(pathToBuiltProject, PRIVACY_MANIFEST_DST);
            File.Copy(src, dst, overwrite: true);

            // PBXProject works in terms of GUIDs. AddFile registers the file
            // path inside the project; AddFileToBuild attaches it to the
            // target's default build phase. For non-source non-framework
            // files like .xcprivacy, Xcode infers the Copy Bundle Resources
            // phase from the file extension, which is what we want.
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            string fileGuid = proj.AddFile(PRIVACY_MANIFEST_DST, PRIVACY_MANIFEST_DST, PBXSourceTree.Source);
            proj.AddFileToBuild(mainTargetGuid, fileGuid);
            proj.WriteToFile(projPath);
        }
    }
}
#endif
