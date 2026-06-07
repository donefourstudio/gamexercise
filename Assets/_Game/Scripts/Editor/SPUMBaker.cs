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

        // ---- Knight Set (M5d chain quest) ---------------------------------
        // Source prefab: SPUM human_11 — the silver-greathelm + silver-plate
        // chibi knight Jackson screenshotted as the visual target.
        // Each piece bakes ONE category of child renderers; the rest of the
        // prefab is hidden so each output PNG is a transparent overlay sitting
        // at the correct anatomical y on a SPUM body.
        const string KNIGHT_PREFAB_ARMOR = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Human/SPUM_20240911215639234.prefab"; // human_11 — silver bucket-helm + plate
        const string KNIGHT_PREFAB_SWORD = "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf/SPUM_20240911222346858.prefab";  // elf_07  — visible silver longsword in IDLE

        // Shop-set source prefabs. Each set's pieces are baked from this one
        // prefab via the per-slot keyword filter. The previewSprite (full-gear
        // render saved to Resources/Sets/) is the same prefab with keepEquip=true.
        static readonly System.Collections.Generic.Dictionary<string, string> SetSourcePrefab =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "elf_paladin", "Assets/SPUM/Resources/Addons/BasicPack/2_Prefab/Elf/SPUM_20240911222346858.prefab" }, // elf_07
        };

        // Keyword filter per slot — same convention the Knight bake uses but
        // shared so shop pieces can route by slot without restating the map.
        static readonly System.Collections.Generic.Dictionary<Gamex.Core.GamexGame.EquipSlot, string[]> SlotKeywords =
            new System.Collections.Generic.Dictionary<Gamex.Core.GamexGame.EquipSlot, string[]>
        {
            { Gamex.Core.GamexGame.EquipSlot.Head,   new[] { "helmet" } },
            { Gamex.Core.GamexGame.EquipSlot.Chest,  new[] { "bodyarmor" } },
            { Gamex.Core.GamexGame.EquipSlot.Legs,   new[] { "cloth", "clothbody" } },
            { Gamex.Core.GamexGame.EquipSlot.Wrists, new[] { "shoulder", "_arm", "carm" } },
            { Gamex.Core.GamexGame.EquipSlot.Feet,   new[] { "foot" } },
            { Gamex.Core.GamexGame.EquipSlot.Weapon, new[] { "weapon" } },
        };

        // Renderer-name keywords per slot. Matched as case-insensitive Contains
        // against the entire ancestor chain. Each tuple is (prefab, keywords).
        static readonly System.Collections.Generic.Dictionary<string, (string prefab, string[] keys)> KnightSlots =
            new System.Collections.Generic.Dictionary<string, (string, string[])>
        {
            { "knight_helmet",    (KNIGHT_PREFAB_ARMOR, new[] { "helmet" }) },
            { "knight_chest",     (KNIGHT_PREFAB_ARMOR, new[] { "bodyarmor" }) },
            // SPUM doesn't separate "leggings" from base cloth, so use the
            // cloth-bottom renderers (these draw the pants area). They overlay
            // on top of the bare body's plain pants for a subtle armored look.
            { "knight_leggings",  (KNIGHT_PREFAB_ARMOR, new[] { "cloth", "clothbody" }) },
            // Pauldrons + cuff armor go to "gauntlets" slot.
            { "knight_gauntlets", (KNIGHT_PREFAB_ARMOR, new[] { "shoulder", "_arm", "carm" }) },
            { "knight_boots",     (KNIGHT_PREFAB_ARMOR, new[] { "foot" }) },
            // 6th piece — pulled from elf_07 because human_11 (the armor source)
            // is a shield-only knight whose R_Weapon SR is empty in the IDLE
            // animation. elf_07's IDLE pose puts a silver longsword in the right
            // hand. Same body coordinate system so it lands in the hand on any
            // SPUM race form.
            { "knight_sword",     (KNIGHT_PREFAB_SWORD, new[] { "weapon" }) },
        };

        // Phase 3a — bake every shop set: full-gear preview + per-piece
        // overlays. Reads the SetCatalog (the runtime authoritative source
        // of which pieces a set contains) and SetSourcePrefab (bake-only
        // metadata mapping set id -> prefab path).
        public static void BakeShopSetsBatch()
        {
            string equipRoot = "Assets/_Game/Resources/Equip";
            string setsRoot  = "Assets/_Game/Resources/Sets";
            Directory.CreateDirectory(equipRoot);
            Directory.CreateDirectory(setsRoot);

            foreach (var set in Gamex.Core.GamexGame.SetCatalog)
            {
                if (!SetSourcePrefab.TryGetValue(set.id, out var prefabPath))
                {
                    Debug.LogWarning($"[SPUMBaker] no source prefab registered for set '{set.id}', skipping");
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[SPUMBaker] prefab not found for set '{set.id}': {prefabPath}");
                    continue;
                }
                // Full-gear preview for the shop card + set detail header.
                string previewPath = $"{setsRoot}/{set.id}.png";
                BakeOne(prefab, previewPath, keepEquip: true);
                Debug.Log($"[SPUMBaker] set preview {set.id}.png  <-  {Path.GetFileName(prefabPath)}");

                // Each piece — overlay sprite cropped to its slot's renderers.
                foreach (var p in set.pieces)
                {
                    if (!SlotKeywords.TryGetValue(p.slot, out var keys))
                    {
                        Debug.LogWarning($"[SPUMBaker] no keywords for slot {p.slot} on piece {p.id}");
                        continue;
                    }
                    string outPath = $"{equipRoot}/{p.id}.png";
                    BakeIsolated(prefab, outPath, keys);
                    Debug.Log($"[SPUMBaker] piece {p.id}.png  <-  {p.slot} via {string.Join('|', keys)}");
                }
            }
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }

        public static void BakeKnightSetBatch()
        {
            string outRoot = "Assets/_Game/Resources/Equip";
            Directory.CreateDirectory(outRoot);
            foreach (var kv in KnightSlots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value.prefab);
                if (prefab == null)
                {
                    Debug.LogError($"[SPUMBaker] prefab not found for {kv.Key}: {kv.Value.prefab}");
                    continue;
                }
                string outPath = $"{outRoot}/{kv.Key}.png";
                BakeIsolated(prefab, outPath, kv.Value.keys);
                Debug.Log($"[SPUMBaker] knight piece {kv.Key}.png  <-  {Path.GetFileName(kv.Value.prefab)}");
            }
            AssetDatabase.Refresh();
            EditorApplication.Exit(0);
        }

        // Renders the prefab with ONLY SpriteRenderers whose GameObject name
        // contains one of the keywords (case-insensitive). Everything else is
        // disabled — including Shadow, Body, hair — so the output PNG is the
        // isolated armor piece at its rest-pose body coordinate.
        static void BakeIsolated(GameObject prefab, string outPath, string[] keywords)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position   = Vector3.zero;
            instance.transform.localScale = Vector3.one;

            // SPUM nests the actual sprite on leaf children named "Front"/"Back"
            // under category pivots like "R_Weapon" / "L_Shield". Match against
            // the whole ancestor chain so the filter catches the real renderer.
            // We also force the matched GameObject AND its ancestors active so
            // a parent SetActive(false) in the prefab doesn't suppress render.
            foreach (var sr in instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string chain = "";
                for (var t = sr.transform; t != null; t = t.parent)
                    chain += "/" + t.gameObject.name.ToLowerInvariant();
                bool keep = false;
                foreach (var kw in keywords)
                    if (chain.Contains(kw.ToLowerInvariant())) { keep = true; break; }
                sr.enabled = keep;
                if (keep)
                {
                    for (var t = sr.transform; t != null && t != instance.transform.parent; t = t.parent)
                        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                }
            }

            // Same camera / sample-animator setup as race-form bake so the
            // output overlay aligns 1:1 with the body sprite.
            var camGO = new GameObject("BakeCam");
            var cam   = camGO.AddComponent<Camera>();
            cam.orthographic       = true;
            cam.orthographicSize   = 0.6f;
            cam.transform.position = new Vector3(0f, 0.35f, -10f);
            cam.clearFlags         = CameraClearFlags.SolidColor;
            cam.backgroundColor    = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask        = ~0;

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

            cam.targetTexture = null;
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

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
