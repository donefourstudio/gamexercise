#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    // Pure-logic test of the step-based sim.
    // Unity -batchmode -quit -projectPath . -executeMethod Gamex.Game.LogicTest.Run
    public static class LogicTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool cond, string msg) { if (!cond) { Debug.LogError("[LOGIC FAIL] " + msg); fails++; } }

            // Linear XP: 5000 steps per level, no curve.
            var g = new GamexGame();
            Check(g.state.level == 1, "starts at lv 1");
            g.AddActivity(5000, 0, 0);
            Check(g.state.level == 2, "5000 steps -> lv 2, got " + g.state.level);
            g.AddActivity(15000, 0, 0);
            Check(g.state.level == 5, "20000 total steps -> lv 5, got " + g.state.level);

            // Running counts 2x: 2500 run steps = 5000 effective XP.
            var g2 = new GamexGame();
            g2.AddActivity(2500, 2500, 0);   // all 2500 steps are running
            Check(g2.state.level == 2, "2500 run steps -> lv 2, got " + g2.state.level);

            // Lv 20 race trigger
            var g3 = new GamexGame { phase = AppPhase.Home };
            g3.AddActivity(95000, 0, 0);     // 95000 / 5000 = 19 levels, so lv 20
            Check(g3.state.level == 20, "Lv 20 reached, got " + g3.state.level);
            Check(g3.phase == AppPhase.RaceSelect, "race select triggered, got " + g3.phase);

            // Daily quests
            var g4 = new GamexGame();
            g4.AddActivity(999, 0, 0);
            Check(g4.state.coins == 0, "no quest done at 999 steps");
            g4.AddActivity(1, 0, 0);                    // total 1000
            Check(g4.state.coins == 1, "walk 1000 -> 1 coin, got " + g4.state.coins);
            g4.AddActivity(4000, 0, 0);                 // total 5000
            Check(g4.state.coins == 2, "walk 5000 -> +1 coin, got " + g4.state.coins);
            g4.AddActivity(5000, 0, 0);                 // total 10000
            Check(g4.state.coins == 3, "walk 10000 -> +1 coin, got " + g4.state.coins);
            g4.AddActivity(0, 0, 15 * 60);              // run 15 min
            Check(g4.state.coins == 4, "run 15 -> +1 coin, got " + g4.state.coins);
            g4.AddActivity(0, 0, 15 * 60);              // total 30 min run
            Check(g4.state.coins == 5, "run 30 -> +1 coin, got " + g4.state.coins);

            // EndDay resets daily counters, advances streak, weekly bonus
            var g5 = new GamexGame();
            for (int i = 0; i < 6; i++)
            {
                g5.AddActivity(1000, 0, 0);
                g5.EndDay();
            }
            Check(g5.state.streakDays == 6, "6-day streak, got " + g5.state.streakDays);
            int coinsBefore = (int)g5.state.coins;
            g5.AddActivity(1000, 0, 0);                 // day 7
            g5.EndDay();
            Check(g5.state.streakDays == 7, "7-day streak, got " + g5.state.streakDays);
            // EndDay should also have credited +5 streak bonus
            int dailyQuestCoinsAdded = 1;               // just walk-1000 today
            Check(g5.state.coins == coinsBefore + dailyQuestCoinsAdded + 5,
                  "weekly streak bonus credited, got " + g5.state.coins);

            // Inactive day breaks streak
            var g6 = new GamexGame();
            g6.AddActivity(1000, 0, 0); g6.EndDay();
            Check(g6.state.streakDays == 1, "streak 1 after active day");
            g6.EndDay();                                 // no steps -> reset
            Check(g6.state.streakDays == 0, "streak resets on inactive day");

            // Shop: new prices, buy + equip
            var g7 = new GamexGame();
            g7.state.coins = 2000;
            var wood = System.Array.Find(GamexGame.Catalog, d => d.id == "sword_wood");
            Check(wood.price == 10, "wood sword = 10 coins, got " + wood.price);
            Check(g7.TryBuy(wood), "buy wood sword");
            Check(g7.state.coins == 1990, "1990 after wood, got " + g7.state.coins);
            g7.ToggleEquip("sword_wood");
            Check(g7.IsEquipped("sword_wood"), "equipped");
            g7.ToggleEquip("sword_wood");
            Check(!g7.IsEquipped("sword_wood"), "unequipped");

            // No level gate (M5d): legend sword buyable at Lv 1 if you have the gold.
            var g8 = new GamexGame();
            g8.state.coins = 100000;
            var legend = System.Array.Find(GamexGame.Catalog, d => d.id == "sword_legend");
            Check(legend.price == 1500, "legend sword = 1500, got " + legend.price);
            Check(g8.TryBuy(legend), "buy legend sword at lv 1 (no level gate)");

            // Knight Set chain — 10 consecutive 5k days unlock each piece.
            // Pre-load lifetime steps so AddActivity's level recompute lands at Lv 20+.
            var g9 = new GamexGame();
            g9.state.totalSteps = 95_000;        // -> Lv 20 after the first 5k tick
            for (int i = 0; i < 10; i++)
            {
                g9.AddActivity(5000, 0, 0);
                g9.EndDay();
            }
            Check(g9.IsOwned("knight_chest"), "10 days @ 5k+ -> chest earned");
            Check(g9.state.knightChainStage == 1, "chain advanced to helmet slot, got " + g9.state.knightChainStage);
            // Miss a day -> progress resets
            g9.AddActivity(5000, 0, 0); g9.EndDay();   // progress = 1
            g9.EndDay();                                // no steps -> reset
            Check(g9.state.knightChainProgress == 0, "missed day resets chain progress");
            // Chain inactive below Lv 20: pre-empty totalSteps stays at level 1.
            var g10 = new GamexGame();
            for (int i = 0; i < 10; i++) { g10.AddActivity(1000, 0, 0); g10.EndDay(); }
            Check(g10.state.knightChainStage == 0, "chain ignored below Lv 20");

            // Save round-trip
            g7.state.gender = (int)Gender.Female;
            g7.state.curse  = (int)Curse.Gluttony;
            string json = JsonUtility.ToJson(g7.state);
            var loaded = JsonUtility.FromJson<GameState>(json);
            Check(loaded.gender == 2 && loaded.curse == 2 && loaded.owned.Contains("sword_wood"),
                  "save round-trip");

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
            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op2_hero.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op3_curse_looms.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_curse.png");

            runner.Game.SetCurse(Curse.Weakness);
            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_curse_anim.png");
            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_op4_amnesia.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_first_mirror.png");

            runner.Game.FinishFirstMirror();

            // Push to Lv 6 (still pre-race) so the home shot has a non-trivial avatar.
            runner.Game.AddActivity(25000, 0, 0);   // 5 levels worth
            runner.Game.state.coins = 60;
            runner.Game.state.streakDays = 7;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_home.png");

            // Lv 20 race-select flow
            runner.Game.AddActivity(75000, 0, 0);   // total 100k -> lv 21
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_race_select.png");
            runner.Game.SetRaceAndGender(Race.Elf, Gender.Male);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("/tmp/gamex_race_anim.png");
            yield return new WaitForSecondsRealtime(1.2f);
            Capture("/tmp/gamex_home_after_race.png");

            runner.Game.GoQuests();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_training.png");

            runner.Game.GoShop();
            runner.Game.state.coins = 2000;
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
