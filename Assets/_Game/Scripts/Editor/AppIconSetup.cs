#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Gamex.EditorTools
{
    // One-time editor hook that points Unity's "Default Icon" PlayerSetting
    // at Assets/AppIcon/app_icon.png. Unity uses Default Icon as the source
    // for every platform's icon set (including iOS AppIcon.appiconset) and
    // generates the various sizes the App Store requires from this one
    // 1024x1024 master at build time.
    //
    // [InitializeOnLoadMethod] runs whenever the Editor reloads scripts;
    // the body is idempotent (only writes when the assigned icon differs)
    // so it doesn't churn ProjectSettings.asset between reloads.
    public static class AppIconSetup
    {
        const string ICON_PATH = "Assets/AppIcon/app_icon.png";

        [InitializeOnLoadMethod]
        static void EnsureDefaultIcon()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
            if (tex == null)
            {
                Debug.LogWarning("[AppIconSetup] " + ICON_PATH + " not found; Default Icon left unchanged.");
                return;
            }

            // Default Icon — the empty-string platform key targets the
            // "Default Icon" slot in PlayerSettings, which feeds every
            // platform's icon set as a fallback.
            var current = PlayerSettings.GetIconsForPlatform("");
            if (current.Length == 1 && current[0] == tex) return;   // already pointed at the right asset

            PlayerSettings.SetIconsForPlatform("", new[] { tex });
            AssetDatabase.SaveAssets();
            Debug.Log("[AppIconSetup] Default Icon set to " + ICON_PATH);
        }
    }
}
#endif
