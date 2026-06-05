#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    // Pure-logic test of the sim — runs without playmode.
    // Unity -batchmode -quit -projectPath . -executeMethod Gamex.Game.LogicTest.Run
    public static class LogicTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool cond, string msg) { if (!cond) { Debug.LogError("[LOGIC FAIL] " + msg); fails++; } }

            var g = new GamexGame();
            // basic rep / xp / level
            Check(g.state.level == 1, "starts at lv 1");
            Check(g.XpPerLevel == 20, "stage 0 cost 20");
            for (int i = 0; i < 20; i++) g.DoRep(Exercise.Pushup);
            Check(g.state.level == 2, "lv up after 20 reps, got " + g.state.level);
            Check(g.state.xp == 0, "xp wrapped");
            Check(g.state.coins == 20, "20 coins, got " + g.state.coins);

            // stage transitions
            var g2 = new GamexGame();
            for (int i = 0; i < 100; i++) g2.DoRep(Exercise.Pushup);
            Check(g2.state.level == 6, "100 reps → lv 6, got " + g2.state.level);
            Check(g2.XpPerLevel == 50, "stage 1 cost 50");
            Check(g2.MaintenanceToday == 10, "stage 1 maintenance 10");

            // maintenance + decay
            var g3 = new GamexGame { phase = AppPhase.Home };
            for (int i = 0; i < 100; i++) g3.DoRep(Exercise.Pushup);   // lv 6
            int lvBefore = g3.state.level;
            g3.state.repsToday = 0;
            g3.EndDay();                       // miss 1
            Check(g3.state.level == lvBefore, "1 missed day = no decay yet");
            g3.EndDay();                       // miss 2 → decay
            Check(g3.state.level == lvBefore - 1, "2 missed days → -1 level, got " + g3.state.level);

            // shop: buy + equip
            var g4 = new GamexGame();
            g4.state.coins = 1000;
            var wood = System.Array.Find(GamexGame.Catalog, d => d.id == "sword_wood");
            Check(g4.TryBuy(wood), "buy wood sword");
            Check(g4.state.coins == 950, "coins deducted");
            Check(g4.IsOwned("sword_wood"), "owned wood sword");
            g4.ToggleEquip("sword_wood");
            Check(g4.IsEquipped("sword_wood"), "equipped");
            g4.ToggleEquip("sword_wood");
            Check(!g4.IsEquipped("sword_wood"), "unequipped");

            // level requirement gate
            var g5 = new GamexGame();
            g5.state.coins = 100000;
            var iron = System.Array.Find(GamexGame.Catalog, d => d.id == "sword_iron");
            Check(!g5.TryBuy(iron), "can't buy iron sword at lv 1");

            // save round-trip
            g4.state.gender = (int)Gender.Female;
            g4.state.curse  = (int)Curse.Gluttony;
            string json = JsonUtility.ToJson(g4.state);
            var loaded = JsonUtility.FromJson<GameState>(json);
            Check(loaded.gender == 2 && loaded.curse == 2 && loaded.owned.Contains("sword_wood"), "save round-trip");

            if (fails == 0) Debug.Log("[LOGIC OK]");
            else Debug.LogError("[LOGIC FAILED] count=" + fails);
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }

    // Headless playmode smoke + screenshot of every UI panel.
    public static class SmokeTest
    {
        public static void Run()
        {
            SessionState.SetBool("GAMEX_SMOKE", true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!SessionState.GetBool("GAMEX_SMOKE", false)) return;
            var go = new GameObject("Gamex_Smoke");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SmokeRunner>();
        }
    }

    public class SmokeRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);

            var runner = FindFirstObjectByType<GameRunner>();
            if (runner == null || runner.Game == null) { Debug.LogWarning("[Smoke] no runner"); EditorApplication.Exit(1); yield break; }

            // Opening sequence (first-run only).
            Capture("/tmp/gamex_op1_intro.png");
            runner.Game.TapAdvanceOpening();   // -> OpeningHeroShown
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op2_hero.png");

            runner.Game.TapAdvanceOpening();   // -> OpeningCurseLooms
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op3_curse_looms.png");

            runner.Game.TapAdvanceOpening();   // -> CurseSelect
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_curse.png");

            runner.Game.SetCurse(Curse.Weakness);   // -> CurseAnim (auto-advances after ~1.5s)
            // capture mid-animation just past the sprite swap so the cursed self + shake are visible
            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_curse_anim.png");
            // wait for the auto-advance to land on OpeningAmnesia
            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_op4_amnesia.png");

            runner.Game.TapAdvanceOpening();   // -> GenderSelect
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_gender.png");

            runner.Game.SetGender(Gender.Male);   // -> FirstMirror
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_first_mirror.png");

            runner.Game.FinishFirstMirror();
            // Push to Lv 6 (stage 1, maintenance = 10) so the ritual icons have a
            // meaningful threshold to compare against. Then capture two states.
            runner.Game.state.level = 6;
            runner.Game.state.xp = 0;
            runner.Game.state.coins = 60;
            runner.Game.state.streakDays = 7;

            // (a) ritual not yet done — candles unlit, crown floating + grey
            runner.Game.state.repsToday = 3;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_home_undone.png");

            // (b) ritual done — candles lit, crown lands gold on the head
            runner.Game.state.repsToday = 15;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_home_done.png");
            // legacy filename for downstream tools that look for gamex_home.png
            Capture("/tmp/gamex_home.png");

            runner.Game.GoTraining();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_training.png");

            runner.Game.GoShop();
            runner.Game.state.coins = 5000;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_shop.png");

            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("[SmokeTest] ran cleanly, exiting");
            EditorApplication.Exit(0);
        }

        static void Capture(string path)
        {
            var cam = Camera.main;
            if (cam == null) cam = FindAnyObjectByType<Camera>();
            if (cam == null) { Debug.LogWarning("[Smoke] no camera"); return; }

            // overlay canvases don't draw into the RT — swap to ScreenSpaceCamera briefly
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var savedMode = new System.Collections.Generic.Dictionary<Canvas, RenderMode>();
            var savedCam  = new System.Collections.Generic.Dictionary<Canvas, Camera>();
            var savedDist = new System.Collections.Generic.Dictionary<Canvas, float>();
            foreach (var c in canvases)
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    savedMode[c] = c.renderMode;
                    savedCam[c]  = c.worldCamera;
                    savedDist[c] = c.planeDistance;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 1f;
                }
            }

            const int W = 720, H = 1280;
            var rt = new RenderTexture(W, H, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            foreach (var kv in savedMode)
            {
                kv.Key.renderMode = kv.Value;
                kv.Key.worldCamera = savedCam[kv.Key];
                kv.Key.planeDistance = savedDist[kv.Key];
            }

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            try { File.WriteAllBytes(path, tex.EncodeToPNG()); Debug.Log("[Smoke] wrote " + path); }
            catch (System.Exception e) { Debug.LogWarning("[Smoke] write failed: " + e.Message); }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }
    }
}
#endif
