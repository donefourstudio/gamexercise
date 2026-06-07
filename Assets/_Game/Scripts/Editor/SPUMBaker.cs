#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Pre-bakes SPUM character prefabs into static 256x256 PNGs so we can use
    // them as portrait sprites in the Canvas-based avatar without spinning up
    // a per-character render texture at runtime. Each prefab is instantiated
    // in an empty scene with a fixed-position orthographic camera, rendered
    // once, and dumped to /tmp/spum_previews/. From there Jackson and I pick
    // which prefabs become elf_male / elf_female / orc_male / orc_female and
    // copy them into Assets/_Game/Resources/Char/.
    public static class SPUMBaker
    {
        const int SIZE = 256;

        // Hide-list for "bare race form" bakes: equipment-related children get
        // their SpriteRenderers disabled before render so we can apply our own
        // equipment overlays on top. Match is case-sensitive substring; keep
        // generous so all variants of helmet/weapon naming are caught.
        static readonly string[] EquipChildHide = {
            "Helmet", "Weapon", "Shield", "Armor", "Back", "Mustache",
        };

        [MenuItem("Tools/Gamex/Bake SPUM Previews (all, full gear)")]
        public static void BakeAllPreviews()
        {
            string outRoot = "/tmp/spum_previews";
            Directory.CreateDirectory(outRoot);
            BakeFolder("Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf",   $"{outRoot}/elf",   keepEquip: true);
            BakeFolder("Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Devil", $"{outRoot}/devil", keepEquip: true);
            BakeFolder("Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Human", $"{outRoot}/human", keepEquip: true);
            Debug.Log($"[SPUMBaker] wrote previews to {outRoot}");
        }

        [MenuItem("Tools/Gamex/Bake SPUM Previews (bare, no equip)")]
        public static void BakeBarePreviews()
        {
            string outRoot = "/tmp/spum_bare";
            Directory.CreateDirectory(outRoot);
            BakeFolder("Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf",   $"{outRoot}/elf",   keepEquip: false);
            BakeFolder("Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Devil", $"{outRoot}/devil", keepEquip: false);
            Debug.Log($"[SPUMBaker] wrote bare previews to {outRoot}");
        }

        // Callable from -executeMethod batchmode.
        public static void BakeAllPreviewsBatch()
        {
            BakeAllPreviews();
            EditorApplication.Exit(0);
        }

        public static void BakeBarePreviewsBatch()
        {
            BakeBarePreviews();
            EditorApplication.Exit(0);
        }

        static void BakeFolder(string assetFolder, string outFolderPrefix, bool keepEquip)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { assetFolder });
            int i = 1;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                string outPath = $"{outFolderPrefix}_{i++:D2}.png";
                BakeOne(prefab, outPath, keepEquip);
                Debug.Log($"[SPUMBaker] {Path.GetFileName(outPath)}  <-  {path}");
            }
        }

        // Final-bake step: produces the 4 race-form PNGs in Resources/Char/ from
        // explicit prefab paths picked from the preview batch. Bare-body bake
        // (no helmet/weapon/shield/armor/back) so the equipment overlay system
        // can layer real items on top in Phase 2.
        public static void BakeRaceFormsBatch()
        {
            string outRoot = "Assets/_Game/Resources/Char";
            Directory.CreateDirectory(outRoot);
            var picks = new (string assetPath, string outName)[]
            {
                (RACE_FORM_ELF_MALE,   "elf_male.png"),
                (RACE_FORM_ELF_FEMALE, "elf_female.png"),
                (RACE_FORM_ORC_MALE,   "orc_male.png"),
                (RACE_FORM_ORC_FEMALE, "orc_female.png"),
            };
            foreach (var p in picks)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p.assetPath);
                if (prefab == null)
                {
                    Debug.LogError($"[SPUMBaker] race-form prefab not found: {p.assetPath}");
                    continue;
                }
                BakeOne(prefab, $"{outRoot}/{p.outName}", keepEquip: false);
                Debug.Log($"[SPUMBaker] race form {p.outName}  <-  {p.assetPath}");
            }
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }

        // Picked from the bare-bake preview batch (see /tmp/spum_bake.log).
        // Each maps to a chibi character whose helmet/weapon/shield/armor were
        // disabled at bake time so Phase 2 overlays can layer real items on top.
        //   elf_male   = bare elf_02 (blonde swordsman silhouette)
        //   elf_female = bare elf_06 (blonde ponytail warrior maiden)
        //   orc_male   = bare devil_02 (purple-skin red-hair brute)
        //   orc_female = bare devil_09 (pink-haired dark-vibe)
        // Update these if Jackson wants a different look later.
        const string RACE_FORM_ELF_MALE   = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf/SPUM_20240911215638140.prefab";
        const string RACE_FORM_ELF_FEMALE = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf/SPUM_20240911222235923.prefab";
        const string RACE_FORM_ORC_MALE   = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Devil/SPUM_20240911215637878.prefab";
        const string RACE_FORM_ORC_FEMALE = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Devil/SPUM_20240911215641087.prefab";

        static void BakeOne(GameObject prefab, string outPath, bool keepEquip = true)
        {
            // Fresh empty scene so leftover GameObjects from previous bakes
            // don't bleed into this render.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position   = Vector3.zero;
            instance.transform.localScale = Vector3.one;

            // For bare-body bakes, walk every SpriteRenderer and disable the
            // ones whose name contains any equipment-related keyword. Result:
            // the prefab still renders body + head + hair + cloth + boots, but
            // helmet / armor plate / weapons / shield / cape are gone.
            if (!keepEquip)
            {
                foreach (var sr in instance.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    string n = sr.gameObject.name;
                    foreach (var kw in EquipChildHide)
                    {
                        if (n.Contains(kw)) { sr.enabled = false; break; }
                    }
                }
            }

            // SPUM chibi characters are ~32 px tall. Default LPC PPU is 16,
            // SPUM seems to be 100 (Unity default). With orthographicSize = 1
            // the camera shows a 2-unit-tall world, which captures the whole
            // body with a bit of headroom even if the prefab lives in screen
            // space units. Slight y-offset puts the feet near bottom and the
            // head near top.
            var camGO = new GameObject("BakeCam");
            var cam   = camGO.AddComponent<Camera>();
            cam.orthographic       = true;
            cam.orthographicSize   = 0.6f;
            cam.transform.position = new Vector3(0f, 0.35f, -10f);
            cam.clearFlags         = CameraClearFlags.SolidColor;
            cam.backgroundColor    = new Color(0f, 0f, 0f, 0f);   // transparent
            cam.cullingMask        = ~0;

            // Animator may default to a non-rendering frame on first awake —
            // sample the IDLE state explicitly so the bake is consistent.
            foreach (var anim in instance.GetComponentsInChildren<Animator>())
            {
                if (anim.runtimeAnimatorController != null)
                {
                    anim.Update(0f);
                    anim.Play(0, 0, 0f);
                    anim.Update(0f);
                }
            }

            var rt = new RenderTexture(SIZE, SIZE, 32, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            // Detach RenderTexture before destroying so we can dispose it
            // without leaking, and destroy in dependency order.
            cam.targetTexture = null;
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
