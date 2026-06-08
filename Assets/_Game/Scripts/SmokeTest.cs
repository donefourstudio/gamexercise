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

            // Schema migration runs first — independent of game state and the
            // later (stale) coin / catalog tests, so a regression here is
            // never masked by an earlier failure. JSON without a
            // schemaVersion field (the pre-2026-06 save format) deserialises
            // with the field at 0; Migrations.Apply bumps it to
            // CurrentVersion while preserving all other fields.
            var v0Json = "{\"level\":5,\"coins\":123}";
            var v0 = JsonUtility.FromJson<GameState>(v0Json);
            Check(v0.schemaVersion == 0, "legacy save deserialises as v0, got " + v0.schemaVersion);
            Migrations.Apply(v0);
            Check(v0.schemaVersion == Migrations.CurrentVersion,
                  $"Apply bumps to current v{Migrations.CurrentVersion}, got v{v0.schemaVersion}");
            Check(v0.level == 5 && v0.coins == 123,
                  "Apply preserves pre-existing fields, got lvl=" + v0.level + " coins=" + v0.coins);
            // Idempotency — re-running Apply on a current-version save is a
            // no-op (no field corruption, version stays put).
            Migrations.Apply(v0);
            Check(v0.schemaVersion == Migrations.CurrentVersion, "Apply is idempotent on current-version save");

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

            // Daily quests — rewards are tiered (1 / 3 / 5 / 3 / 5) with a
            // +10 all-clear bonus when every quest is done in the same day.
            // Updated 2026-06: rewards were 1g flat in M5b but were tuned up
            // during the launch-cohort balance pass to make daily completion
            // pay enough for casual shop purchases.
            var g4 = new GamexGame();
            g4.AddActivity(999, 0, 0);
            Check(g4.state.coins == 0, "no quest done at 999 steps, got " + g4.state.coins);
            g4.AddActivity(1, 0, 0);                    // total 1000        -> Walk1000 (+1)
            Check(g4.state.coins == 1, "walk 1000 -> +1, got " + g4.state.coins);
            g4.AddActivity(4000, 0, 0);                 // total 5000        -> Walk5000 (+3) = 4
            Check(g4.state.coins == 4, "walk 5000 -> +3, got " + g4.state.coins);
            g4.AddActivity(5000, 0, 0);                 // total 10000       -> Walk10000 (+5) = 9
            Check(g4.state.coins == 9, "walk 10000 -> +5, got " + g4.state.coins);
            g4.AddActivity(0, 0, 15 * 60);              // run 15 min        -> Run15 (+3) = 12
            Check(g4.state.coins == 12, "run 15 -> +3, got " + g4.state.coins);
            g4.AddActivity(0, 0, 15 * 60);              // total 30 min run  -> Run30 (+5) + all-bonus (+10) = 27
            Check(g4.state.coins == 27, "run 30 -> +5 +10 all-bonus, got " + g4.state.coins);

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

            // Shop (Phase 3): SetCatalog drives the storefront. Single-piece
            // buy still works through TryBuy; new TryBuySet pays the bundle
            // price (80% of summed pieces) and grants every piece atomically.
            var g7 = new GamexGame();
            g7.state.coins = 2000;
            // FindSet — SetCatalog index isn't stable across launch reorderings
            // (champ_dark_knight is now at [0], elf_paladin is in the deferred
            // section). Look up by id so the test survives future shuffling.
            var paladin = GamexGame.FindSet("elf_paladin");
            Check(paladin != null, "elf_paladin set exists in catalog");
            var sword = System.Array.Find(paladin.pieces, d => d.id == "elfpaladin_sword");
            Check(sword != null && sword.price == 200, "paladin sword = 200 coins, got " + (sword?.price.ToString() ?? "null"));
            Check(g7.TryBuy(sword), "buy paladin sword");
            Check(g7.state.coins == 1800, "1800 after sword, got " + g7.state.coins);
            g7.ToggleEquip("elfpaladin_sword");
            Check(g7.IsEquipped("elfpaladin_sword"), "equipped");
            g7.ToggleEquip("elfpaladin_sword");
            Check(!g7.IsEquipped("elfpaladin_sword"), "unequipped");

            // Bundle buy — 700g total -> 560 with discount, all 4 pieces granted.
            var g8 = new GamexGame();
            g8.state.coins = 100000;
            Check(paladin.BundlePrice == 560, "paladin bundle = 560, got " + paladin.BundlePrice);
            Check(g8.TryBuySet(paladin), "buy paladin set");
            Check(g8.state.owned.Count == 4, "owned 4 after bundle, got " + g8.state.owned.Count);
            Check(g8.state.coins == 100000 - 560, "coins after bundle");

            // Knight Set chain — 10 consecutive 5k days unlock each piece.
            // Pre-load lifetime steps so AddActivity's level recompute lands at Lv 20+.
            var g9 = new GamexGame();
            g9.state.totalSteps = 95_000;        // -> Lv 20 after the first 5k tick
            for (int i = 0; i < 10; i++)
            {
                g9.AddActivity(5000, 0, 0);
                g9.EndDay();
            }
            // Updated 2026-06 (commit 926ad41 + d9ef2b8): chain now grants the
            // WHOLE 6-piece set atomically after 10 days (was 10 days per piece).
            // knightChainStage = KnightSet.Length acts as the "earned" flag.
            Check(g9.IsOwned("knight_chest"),    "10 days @ 5k+ -> chest earned");
            Check(g9.IsOwned("knight_sword"),    "10 days @ 5k+ -> sword earned (atomic 6-piece grant)");
            Check(g9.state.knightChainStage == GamexGame.KnightSet.Length,
                  "chain marked earned, got " + g9.state.knightChainStage);
            // Earning the set short-circuits further chain ticking; progress
            // stays at 0 because the gate (knightChainStage < Length) is false.
            g9.AddActivity(5000, 0, 0); g9.EndDay();
            g9.EndDay();
            Check(g9.state.knightChainProgress == 0, "post-earn chain stays at 0 progress");
            // Chain inactive below Lv 20: pre-empty totalSteps stays at level 1.
            var g10 = new GamexGame();
            for (int i = 0; i < 10; i++) { g10.AddActivity(1000, 0, 0); g10.EndDay(); }
            Check(g10.state.knightChainStage == 0, "chain ignored below Lv 20");

            // Save round-trip — use elfpaladin_sword (the piece g7 bought
            // above) since Phase 2 dropped the old "sword_wood" tier item.
            g7.state.gender = (int)Gender.Female;
            g7.state.curse  = (int)Curse.Gluttony;
            string json = JsonUtility.ToJson(g7.state);
            var loaded = JsonUtility.FromJson<GameState>(json);
            Check(loaded.gender == 2 && loaded.curse == 2 && loaded.owned.Contains("elfpaladin_sword"),
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

        // Run BEFORE GameRunner.Awake() so SaveSystem.Load() returns null and
        // the runner spins up a fresh GameState. Without this the smoke test
        // inherits whatever the developer's last manual Play Mode session
        // wrote to gamex_save.json — owned items, race choice, equipped set
        // and all — which makes shop/inventory captures non-deterministic.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void WipeBeforeSmoke()
        {
            if (!SessionState.GetBool("GAMEX_SMOKE", false)) return;
            SaveSystem.Wipe();
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

            // Title screen — cold-launch landing. Wait for the wordmark +
            // tagline fade-in (~0.7s) and a beat of the Start Game pulse so
            // the steady-state composition lands in the screenshot, then
            // click the Start Game button so subsequent captures continue
            // through the opening flow.
            yield return new WaitForSecondsRealtime(0.8f);
            Capture("/tmp/gamex_title.png");
            var startBtn = GameObject.Find("StartGame")?.GetComponent<UnityEngine.UI.Button>();
            if (startBtn != null) startBtn.onClick.Invoke();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);

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
            // First-run tutorial step 1 is up — capture it before dismissing so
            // the coach-mark spotlight + caption is visible in smoke output.
            Capture("/tmp/gamex_tutorial_step1.png");

            // Click through all 3 tutorial steps so subsequent home captures
            // show the actual UI rather than the coach-mark overlay. The Next
            // button is re-found per iteration so we don't hold a stale ref
            // after the overlay re-deactivates on the final step. Capture
            // each step after-click so we have a screenshot per coach-mark.
            string[] tutCaptures = { "/tmp/gamex_tutorial_step2.png", "/tmp/gamex_tutorial_step3.png", null };
            for (int i = 0; i < 3; i++)
            {
                var tutGO = GameObject.Find("TutorialOverlay");
                var nextBtn = FindChildButton(tutGO, "Next");
                if (nextBtn == null) break;
                nextBtn.onClick.Invoke();
                yield return null; yield return new WaitForSecondsRealtime(0.1f);
                if (tutCaptures[i] != null) Capture(tutCaptures[i]);
            }
            yield return null;
            Capture("/tmp/gamex_home.png");

            // Trigger one more level-up after the prev-level tracker has initialised
            // so the LEVEL UP toast + Lv pulse are mid-animation in the capture.
            runner.Game.AddActivity(5000, 0, 0);
            yield return null; yield return new WaitForSecondsRealtime(0.35f);  // mid-bell of 1.2s
            Capture("/tmp/gamex_home_levelup.png");

            // Wait out the level-up window, then bump coins so only the
            // coin floater is animating in the next capture.
            yield return new WaitForSecondsRealtime(1.3f);
            runner.Game.state.coins += 25;
            yield return null; yield return new WaitForSecondsRealtime(0.4f);  // mid-rise of 1.5s
            Capture("/tmp/gamex_home_coinfloat.png");

            // Daily-ritual transition. Reset todaySteps + idle one Refresh so
            // the candle tracker baselines at 0, then step into 2 and 4
            // candles to capture mid-flash + crown-burst frames.
            yield return new WaitForSecondsRealtime(1.6f);
            runner.Game.state.todaySteps = 0;
            yield return null; yield return new WaitForSecondsRealtime(0.1f);   // baseline
            runner.Game.state.todaySteps = 2600;
            yield return null; yield return new WaitForSecondsRealtime(0.25f);  // mid-flash of 0.55s
            Capture("/tmp/gamex_ritual_2candles.png");
            yield return new WaitForSecondsRealtime(0.5f);                      // let candle flash settle
            runner.Game.state.todaySteps = 5100;
            yield return null; yield return new WaitForSecondsRealtime(0.4f);   // mid-crown-burst of 0.8s
            Capture("/tmp/gamex_ritual_crown.png");

            // Lv 20 race-select flow
            runner.Game.AddActivity(75000, 0, 0);   // total 100k -> lv 21
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_race_select.png");
            runner.Game.SetRaceAndGender(Race.Elf, Gender.Male);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("/tmp/gamex_race_anim.png");
            yield return new WaitForSecondsRealtime(1.2f);

            // Phase 2: shop tier swords/armors retired. Equip the 5 knight
            // pieces (chain-quest reward in real play) so the home capture
            // shows the SPUM-coord overlay system rendering all five on the
            // chibi race form.
            foreach (var p in GamexGame.KnightSet)
            {
                if (!runner.Game.state.owned.Contains(p.id))   runner.Game.state.owned.Add(p.id);
                if (!runner.Game.state.equipped.Contains(p.id)) runner.Game.state.equipped.Add(p.id);
            }
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_home_after_race.png");

            // Force a partial Knight Set chain so the progress bar shows ~40%
            // fill in the capture (Lv 20+ unlocks the chain).
            runner.Game.state.knightChainProgress = 4;
            runner.Game.GoQuests();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_training.png");

            runner.Game.GoShop();
            runner.Game.state.coins = 2000;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_shop.png");

            // Wait out the floater from the state.coins=2000 bump so the next
            // capture shows a clean "+50" and not the aggregated total.
            yield return new WaitForSecondsRealtime(1.6f);
            runner.Game.state.coins += 50;
            yield return null; yield return new WaitForSecondsRealtime(0.45f);   // ~30% into 1.5s
            Capture("/tmp/gamex_shop_coinfloat.png");
            yield return new WaitForSecondsRealtime(1.2f);                       // let floater settle

            // Buy two sets so the next shop capture shows the gold-glow tint
            // for owned set cards next to the default-tan unowned cards.
            var ownedSet = GamexGame.FindSet("champ_squire");
            if (ownedSet != null) { runner.Game.state.coins = 10000; runner.Game.TryBuySet(ownedSet); }
            var ownedSet2 = GamexGame.FindSet("champ_pink_archer");
            if (ownedSet2 != null) { runner.Game.TryBuySet(ownedSet2); }

            // Trigger a press bounce directly via the PressBounce component so
            // we capture mid-shrink without GoSetDetail swapping phases. This
            // verifies both the press scale tween AND the gold-glow tint side
            // by side (champion squire + pink archer owned, others not).
            var topCard = GameObject.Find("SetCard_champ_dark_knight");
            var bounce  = topCard != null ? topCard.GetComponent<PressBounce>() : null;
            if (bounce != null) bounce.Trigger();
            yield return null; yield return new WaitForSecondsRealtime(0.07f);  // ~30% into 0.22s, peak shrink
            Capture("/tmp/gamex_shop_owned.png");

            // Phase 3 set detail — tap the first set card programmatically.
            if (GamexGame.SetCatalog.Length > 0)
            {
                runner.Game.GoSetDetail(GamexGame.SetCatalog[0].id);
                yield return null; yield return new WaitForSecondsRealtime(0.2f);
                Capture("/tmp/gamex_set_detail.png");

                // Wait out any prior floater, then bump for a clean capture.
                yield return new WaitForSecondsRealtime(1.6f);
                runner.Game.state.coins += 75;
                yield return null; yield return new WaitForSecondsRealtime(0.45f);
                Capture("/tmp/gamex_set_detail_coinfloat.png");
            }

            // Phase 4 — buy + apply a skin so home + inventory show the
            // override portrait (knight gear is hidden because skins carry
            // their own painted-in art).
            // Buy a Champion set so the new outfit inventory has something to
            // show beyond a single skin cell.
            var darkKnight = GamexGame.FindSet("champ_dark_knight");
            if (darkKnight != null)
            {
                runner.Game.state.coins = darkKnight.BundlePrice + 10000;
                runner.Game.TryBuySet(darkKnight);
            }
            // Iron Knight (frameCount=11) exercises Phase 5e3 animation tick;
            // Golden Retriever pet alongside exercises the polish-round-3 pet
            // companion rendering inside the mirror.
            var demoSkin = GamexGame.FindSkin("lm_iron_knight");
            if (demoSkin != null)
            {
                if (runner.Game.state.coins < demoSkin.price + 500) runner.Game.state.coins = demoSkin.price + 500;
                runner.Game.TryBuySkin(demoSkin);
                runner.Game.ApplySkin(demoSkin.id);
                var pet = GamexGame.FindSkin("pet_dog_golden");
                if (pet != null)
                {
                    runner.Game.TryBuySkin(pet);
                    runner.Game.ApplySkin(pet.id);
                }
                runner.Game.GoHome();
                yield return null; yield return new WaitForSecondsRealtime(0.4f);
                Capture("/tmp/gamex_home_skin.png");
            }

            // M5f Inventory — outfits-only grid. With all 6 knight pieces in
            // state.owned (granted above) + the bought champ_dark_knight set
            // + the applied lm_iron_knight skin, the grid should show:
            //   1. champ_dark_knight outfit cell (fullyOwned via SetCatalog)
            //   2. Knight Set outfit cell (fullyOwned via KnightOutfit fallback)
            //   3. one skin cell per owned skin (lm_iron_knight active badge)
            // The Knight Set cell here is the regression check for the
            // "chain quest grants pieces but no inventory cell" bug.
            runner.Game.GoInventory();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_inventory.png");

            // Settings panel — audio mutes, HealthKit status, reset button.
            // Captured with the SFX row toggled off so the inverted state
            // reads in the screenshot ("Sound effects: OFF").
            runner.Game.state.sfxMuted = true;
            runner.Game.GoSettings();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_settings.png");

            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("[SmokeTest] ran cleanly, exiting");
            EditorApplication.Exit(0);
        }

        // Walk an overlay's transform tree to find a child Button by name.
        // Used only by the smoke harness to click through coach-marks without
        // exposing a public API on Hud.
        static UnityEngine.UI.Button FindChildButton(GameObject root, string childName)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName)
                    return t.GetComponent<UnityEngine.UI.Button>();
            return null;
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
