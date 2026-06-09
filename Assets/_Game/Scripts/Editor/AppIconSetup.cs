#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Manual one-shot menu action that wires Assets/AppIcon/app_icon.png
    // into PlayerSettings' Default Icon slot. Default Icon is what Unity
    // feeds to every platform's icon set as a fallback, so a single 1024
    // master here generates the iOS AppIcon.appiconset sizes at build.
    //
    // Why a MenuItem (not [InitializeOnLoadMethod]): an auto-run hook with
    // any compile or runtime bug blocks the Editor from opening at all —
    // we hit that on the first attempt (100e6d2). A MenuItem only runs
    // when explicitly clicked, so even if the API surface changes in a
    // future Unity update the worst case is "menu does nothing", never
    // "project won't open".
    //
    // Why NamedBuildTarget.Unknown: in Unity 6 the older platform-string
    // overloads (GetIconsForPlatform("")/SetIconsForPlatform("",...))
    // are marked Obsolete with error severity, which was the actual
    // failure on the previous attempt. The NamedBuildTarget API is the
    // modern path; iOSBuildPostProcess.cs in this project already uses
    // it successfully so the using + namespace are proven safe.
    public static class AppIconSetup
    {
        const string ICON_PATH = "Assets/AppIcon/app_icon.png";

        [MenuItem("Tools/Gamex/Set Default App Icon")]
        static void SetDefaultIcon()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
            if (tex == null)
            {
                Debug.LogError("[AppIconSetup] Icon asset not found at " + ICON_PATH +
                               ". Make sure the PNG is imported (refresh Project view) and retry.");
                return;
            }

            try
            {
                // NamedBuildTarget.Unknown is the "Default Icon" slot —
                // its content propagates to every platform that hasn't
                // overridden its own icon set.
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Application);
                AssetDatabase.SaveAssets();
                Debug.Log("[AppIconSetup] Default Icon set to " + ICON_PATH);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AppIconSetup] Failed to set Default Icon: " + e.Message);
            }
        }
    }
}
#endif
