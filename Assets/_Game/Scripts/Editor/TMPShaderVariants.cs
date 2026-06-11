#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gamex.EditorTools
{
    // Creates + maintains a ShaderVariantCollection that pins the TMP_SDF
    // shader variants we need at runtime (OUTLINE_ON for the title-screen
    // wordmark/tagline outline, UNDERLAY_ON for their drop shadow). Without
    // this, Unity's build-time variant stripping removes the keyword-enabled
    // pass combinations, so our runtime EnableKeyword + outlineWidth=0.4f
    // calls flip the flag on a stripped shader and produce nothing visible.
    //
    // The package-shipped LiberationSans materials in
    // Assets/TextMesh Pro/Resources/Fonts & Materials/ already have these
    // keywords set, which SHOULD preserve the variants — but in practice
    // doesn't, presumably because Unity 6's variant analysis treats those
    // materials as "unused" since no scene loads them. Explicit collection
    // referenced from PreloadedShaders is the authoritative path.
    //
    // Run via menu (idempotent — re-running just refreshes the asset):
    //   Tools > Gamex > Rebuild TMP Shader Variant Collection
    // Or via batch:
    //   Unity -batchmode -executeMethod Gamex.EditorTools.TMPShaderVariants.Rebuild
    public static class TMPShaderVariants
    {
        const string COLLECTION_PATH = "Assets/_Game/Resources/Shaders/TMP_SDF_Variants.shadervariants";
        const string SHADER_NAME     = "TextMeshPro/Distance Field";

        [MenuItem("Tools/Gamex/Rebuild TMP Shader Variant Collection")]
        public static void Rebuild()
        {
            var shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError("[TMPShaderVariants] TMP_SDF shader not found (looked for '" + SHADER_NAME + "'). Is the TMP Essential Resources package imported?");
                return;
            }

            System.IO.Directory.CreateDirectory("Assets/_Game/Resources/Shaders");

            // Fresh collection — easier to keep deterministic than reading
            // and merging into an existing one, and the file is small.
            var svc = new ShaderVariantCollection();
            svc.name = "TMP_SDF_Variants";

            void AddVariant(string[] keywords)
            {
                var v = new ShaderVariantCollection.ShaderVariant
                {
                    shader     = shader,
                    passType   = UnityEngine.Rendering.PassType.Normal,
                    keywords   = keywords,
                };
                svc.Add(v);
            }

            // Baseline + each keyword we actually flip at runtime, plus the
            // combined variant used by the Title screen (both outline +
            // underlay enabled at once on the wordmark + tagline).
            AddVariant(new string[0]);
            AddVariant(new[] { "OUTLINE_ON" });
            AddVariant(new[] { "UNDERLAY_ON" });
            AddVariant(new[] { "OUTLINE_ON", "UNDERLAY_ON" });

            AssetDatabase.CreateAsset(svc, COLLECTION_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log("[TMPShaderVariants] Saved collection at " + COLLECTION_PATH + " with " + svc.variantCount + " variants.");

            // Wire into GraphicsSettings.PreloadedShaders so the variants
            // get included in player builds. SerializedObject editing is the
            // only public path — there's no high-level API for this list.
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogError("[TMPShaderVariants] Could not load GraphicsSettings.asset to add the collection to PreloadedShaders.");
                return;
            }
            var so = new SerializedObject(graphicsSettings);
            var preloaded = so.FindProperty("m_PreloadedShaders");
            if (preloaded == null)
            {
                Debug.LogError("[TMPShaderVariants] m_PreloadedShaders property not found in GraphicsSettings — Unity API may have changed.");
                return;
            }

            // De-dupe: skip if our asset is already in the list (re-running
            // the menu item shouldn't double-up the entry).
            bool alreadyPresent = false;
            for (int i = 0; i < preloaded.arraySize; i++)
            {
                var element = preloaded.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == svc) { alreadyPresent = true; break; }
            }
            if (!alreadyPresent)
            {
                preloaded.arraySize++;
                preloaded.GetArrayElementAtIndex(preloaded.arraySize - 1).objectReferenceValue = svc;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[TMPShaderVariants] Added collection to GraphicsSettings.PreloadedShaders.");
            }
            else
            {
                Debug.Log("[TMPShaderVariants] Collection already present in PreloadedShaders, no edit needed.");
            }
        }
    }
}
#endif
