#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Imports the TMP Essential Resources package on first run so that the
    // TMP_Settings.asset + default SDF font + default shaders land in
    // Assets/TextMesh Pro/. Runtime TMP_FontAsset.CreateFontAsset reads
    // TMP_Settings during atlas generation and crashes (NullReferenceException
    // inside get_clearDynamicDataOnBuild) without it — Unity normally surfaces
    // a one-time dialog asking the user to import, but batchmode never gets
    // that dialog, so we do it ourselves.
    //
    // Run once from batchmode:
    //   Unity -batchmode -executeMethod Gamex.EditorTools.TMPSetup.ImportEssentials
    // Idempotent: re-running after import is a no-op because the destination
    // assets already exist.
    public static class TMPSetup
    {
        const string PACKAGE_PATH = "Library/PackageCache/com.unity.ugui@a9ea81766fbd/Package Resources/TMP Essential Resources.unitypackage";
        const string SENTINEL = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static void ImportEssentials()
        {
            if (File.Exists(SENTINEL))
            {
                Debug.Log("[TMPSetup] essentials already imported, skipping");
                EditorApplication.Exit(0);
                return;
            }
            string pkg = Path.Combine(Application.dataPath, "..", PACKAGE_PATH);
            if (!File.Exists(pkg))
            {
                Debug.LogError("[TMPSetup] TMP Essential Resources.unitypackage missing at " + pkg);
                EditorApplication.Exit(1);
                return;
            }
            // ImportPackage is async — the editor queues asset writes and
            // processes them on the next editor-update tick, which never
            // arrives if we Exit() right after the call. Register the
            // completion callbacks and let the editor pump until one fires.
            AssetDatabase.importPackageCompleted += name =>
            {
                Debug.Log("[TMPSetup] import completed: " + name);
                AssetDatabase.Refresh();
                EditorApplication.Exit(0);
            };
            AssetDatabase.importPackageFailed += (name, err) =>
            {
                Debug.LogError("[TMPSetup] import failed: " + err);
                EditorApplication.Exit(1);
            };
            AssetDatabase.importPackageCancelled += name =>
            {
                Debug.LogWarning("[TMPSetup] import cancelled: " + name);
                EditorApplication.Exit(1);
            };
            AssetDatabase.ImportPackage(pkg, interactive: false);
            Debug.Log("[TMPSetup] ImportPackage queued for " + pkg);
        }
    }
}
#endif
