using UnityEngine;
using Gamex.Core;
using Gamex.Platform;

namespace Gamex.Game
{
    // Bootstraps the whole app from a runtime hook so pressing Play in any scene
    // just works. Same pattern as RouJiaMoKing.
    public class GameRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameRunner>() != null) return;
            new GameObject("Gamex_Root").AddComponent<GameRunner>();
        }

        public GamexGame Game => _game;
        public Hud Hud => _hud;

        GamexGame _game;
        Hud _hud;
        // The Desk economy core (Pivot 3 — docs/casino-mvp-plan.md).
        // Always constructed and always fed steps — stride rolls accrue
        // harmlessly even while the RemoteConfig flag hides the button.
        DeskGame _desk;
        public DeskGame Desk => _desk;

        void Awake()
        {
            DisableDefaultSceneObjects();
            // lock to portrait — this app is one-handed phone/iPad only
            Screen.orientation = ScreenOrientation.Portrait;

            _game = new GamexGame();
            var loaded = SaveSystem.Load();
            if (loaded != null) _game.state = loaded;
            _game.onSave = () => SaveSystem.Save(_game.state);
            _game.CatchUpDays();

            _desk = new DeskGame(_game.state);
            _desk.onSave = () => SaveSystem.Save(_game.state);

            // Cold start always lands on the Title screen — tapping "Start
            // Game" routes to DetermineInitialPhase() which picks the same
            // resume / new-player branch the bootstrap used to pick directly.
            // (The Casino is an in-game section off Home, not a boot mode.)
            _game.phase = AppPhase.Title;

            var camGO = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.10f);

            _hud = new Hud(
                onTapAdvanceOpening:  () => _game.TapAdvanceOpening(),
                onCurseAnimDone:      () => _game.CompleteCurseAnim(),
                onSelectRaceAndGender:(r, g) => _game.SetRaceAndGender((Race)r, (Gender)g),
                onRaceAnimDone:       () => _game.CompleteRaceAnim(),
                onFinishFirstMirror:  () =>
                {
                    _game.DoRep(Exercise.Pushup);
                    _game.FinishFirstMirror();
                    // First-time entry to the HK gate happens here: the
                    // player has just been through the opening cinematic +
                    // mirror reveal so they understand WHY we need steps.
                    if (NeedsHealthKitGate()) _game.RequestHealthKitGate();
                },
                onGoQuests:           () => _game.GoQuests(),
                onGoShop:             () => _game.GoShop(),
                onGoHome:             () => _game.GoHome(),
                onGoInventory:        () => _game.GoInventory(),
                onFakeRep:            () => _game.DoRep(Exercise.Pushup),
                onBuy:                id =>
                {
                    if (_game.TryBuy(GamexGame.FindPiece(id))) Sfx.Play("purchase");
                    else                                       Sfx.Play("error");
                },
                onToggleEquip:        id => { _game.ToggleEquip(id); Sfx.Play("apply_outfit"); },
                onGoSetDetail:        id => _game.GoSetDetail(id),
                onBuySet:             id =>
                {
                    if (_game.TryBuySet(GamexGame.FindSet(id))) Sfx.Play("purchase");
                    else                                        Sfx.Play("error");
                },
                onSkinAction:         id =>
                {
                    var skin = GamexGame.FindSkin(id);
                    if (skin == null) return;
                    if      (!_game.IsSkinOwned(id))
                    {
                        if (_game.TryBuySkin(skin)) Sfx.Play("purchase");
                        else                        Sfx.Play("error");
                    }
                    else if (!_game.IsSkinActive(id)) { _game.ApplySkin(id);    Sfx.Play("apply_outfit"); }
                    else if (skin.source == "pet")    { _game.RemoveActivePet(); Sfx.Play("apply_outfit"); }
                    else                              { _game.RemoveActiveSkin(); Sfx.Play("apply_outfit"); }
                },
                onGoSkinDetail:       id => _game.GoSkinDetail(id),
                onApplyOutfit:       id =>
                {
                    // Inventory taps. Owned-set + already-active set -> race
                    // form (un-equip everything); otherwise just apply.
                    if (GamexGame.FindSet(id) != null && _game.IsOutfitActive(id))
                    {
                        _game.state.equipped.Clear();
                        Sfx.Play("apply_outfit");
                        return;
                    }
                    if (GamexGame.FindSkin(id) != null && _game.IsSkinActive(id))
                    {
                        _game.RemoveActiveSkin();
                        Sfx.Play("apply_outfit");
                        return;
                    }
                    _game.ApplyOutfit(id);
                    Sfx.Play("apply_outfit");
                },
                onGoSettings:    () => _game.GoSettings(),
                onToggleSfx:     () => { _game.state.sfxMuted = !_game.state.sfxMuted; Sfx.Enabled = !_game.state.sfxMuted; _game.onSave?.Invoke(); if (Sfx.Enabled) Sfx.Play("tap"); },
                onToggleBgm:     () => { _game.state.bgmMuted = !_game.state.bgmMuted; Bgm.Enabled = !_game.state.bgmMuted; if (_game.state.bgmMuted) Bgm.Stop(); _game.onSave?.Invoke(); Sfx.Play("tap"); },
                onResetProgress: () =>
                {
                    SaveSystem.Wipe();
                    _game.state = new GameState();
                    _desk.host = _game.state;   // re-point at the fresh save
                    _game.phase = AppPhase.Title;   // back to the front door
                    // SyncHealthKit reads state.todayHealthKitSteps which is
                    // now 0, so the next focus event will write the full HK
                    // total as one delta — same as a fresh-install flow.
                    Sfx.Play("milestone");   // gives the reset some weight
                },
                onLeaveTitle:    () => _game.phase = DetermineInitialPhase(),
                onConnectHealthKit: () =>
                {
                    // Two-state button on the gate: NotDetermined -> trigger
                    // OS modal; Denied -> deep-link to Privacy → Health →
                    // Gamexercise (iOS won't show the modal a second time).
                    if (HealthKitBridge.CurrentStatus() == HealthKitBridge.AuthStatus.Denied)
                    {
                        Hud.OpenHealthKitSettings();
                        return;
                    }
                    _game.state.healthKitAsked = true;
                    _game.onSave?.Invoke();
                    HealthKitBridge.RequestAuthorization(status =>
                    {
                        if (status == HealthKitBridge.AuthStatus.Authorized)
                        {
                            _game.CompleteHealthKitGate();
                            SyncHealthKit();
                        }
                        // Denied / NotDetermined: stay on gate; UI auto-updates.
                    });
                },
                // The Desk. Nav sets the phase directly (same pattern as
                // onLeaveTitle — including BACK to AppPhase.Home). The
                // DeskGame ref is handed straight to the Hud — unlike
                // GamexGame's callback routing — because play results must
                // flow back into the reveal animation synchronously.
                onDeskNav:            p => _game.phase = (AppPhase)p,
                desk:                 _desk);

            // Mirror persisted audio mutes into the Sfx/Bgm singletons so the
            // very first Refresh after Hud construction respects them. The
            // GamexGame state was loaded from disk in Awake before we got
            // here; flipping the toggles via Settings keeps these in sync.
            Sfx.Enabled = !_game.state.sfxMuted;
            Bgm.Enabled = !_game.state.bgmMuted;
        }

        void Start()
        {
            // Kick off the remote-config fetch first. It applies the cached
            // filter value synchronously in its Awake; the live fetch then
            // updates if the GitHub-Pages JSON differs. This runs before
            // the first SyncHealthKit so the workout query already uses the
            // right filter mode.
            RemoteConfig.EnsureStarted();
            // Pull whatever steps HealthKit already has for today (e.g. user
            // walked around before opening the game). No-op on non-iOS and
            // when the user hasn't granted HK permission yet.
            SyncHealthKit();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) return;
            // Re-sync when the player returns from another app. iOS doesn't
            // give us a reliable "step update" push, so we lazily catch up
            // each time the game becomes interactive.
            SyncHealthKit();
            // If the user revoked HealthKit while we were backgrounded (iOS
            // Settings -> Privacy -> Health -> Gamexercise -> off), divert
            // back to the gate so they can re-authorize before continuing.
            // Conversely, if they granted access from the gate's deep-link
            // and then refocused, exit the gate and resume play.
            if (NeedsHealthKitGate() && IsGameplayPhase(_game.phase))
                _game.RequestHealthKitGate();
            else if (_game.phase == AppPhase.HealthKitGate && !NeedsHealthKitGate())
                _game.CompleteHealthKitGate();
        }

        // True when we're on a real iOS device that's missing HealthKit
        // authorization. Editor + non-iOS builds always return false so the
        // dev/test flow stays unblocked. iOS device without HK kit (rare —
        // Apple TV / iPod-class hardware that lacks the framework) also
        // returns true so we surface the device-not-supported gate UI.
        bool NeedsHealthKitGate()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return false;
            return HealthKitBridge.CurrentStatus() != HealthKitBridge.AuthStatus.Authorized;
        }

        static bool IsGameplayPhase(AppPhase p)
            => p == AppPhase.Home || p == AppPhase.Quests || p == AppPhase.Shop
            || p == AppPhase.Inventory || p == AppPhase.SetDetail || p == AppPhase.Settings
            || p == AppPhase.Desk;

        void Update()
        {
            _hud?.Refresh(_game);

            // (Old post-tutorial soft prompt removed — HealthKit is now a
            // hard gate driven by AppPhase.HealthKitGate. See onConnectHealthKit
            // wiring above + NeedsHealthKitGate / IsGameplayPhase helpers.)

            // debug keys for fast iteration
            if (Input.GetKeyDown(KeyCode.E)) _game.EndDay();                  // advance one day
            if (Input.GetKeyDown(KeyCode.R) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                // Jackson asked: R for fast shop testing should drop a usable
                // chunk of gold instead of nudging step count by 1k. 10000
                // gold per press buys a Legend + a few Champion sets so the
                // shop / inventory flows are easy to exercise.
                _game.state.coins += 10000;
            }
            // T = +1000 steps. Brought back as a separate key after the R
            // remap took step injection away from shop testing — Jackson
            // still needs both during a single play session.
            if (Input.GetKeyDown(KeyCode.T))
            {
                _game.AddActivity(1000, 0, 0);
                // The same debug steps also fill the Desk's stride rolls
                // so the Editor loop is playable end to end.
                _desk.GrantSteps(1000);
            }
            // Shift+R from anywhere: nuke save + replay opening (dev / QA only).
            if (Input.GetKeyDown(KeyCode.R) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                SaveSystem.Wipe();
                _game.state = new GameState();
                _desk.host = _game.state;   // re-point at the fresh save
                _game.phase = AppPhase.OpeningIntro;
                Debug.Log("[Gamex] save wiped, replaying opening");
            }
        }

        // Returns the phase a returning or new player should resume into
        // after leaving the title screen. Same branching the cold-boot path
        // used to do directly: new players run the opening sequence; players
        // who already saw the first mirror go to Home (or RaceSelect if
        // they've crossed Lv 20 but haven't picked race + gender yet).
        AppPhase DetermineInitialPhase()
        {
            if (_game.state.firstMirrorDone)
            {
                // Hard HealthKit gate: returning iOS players hit this if
                // they never authorized (or revoked since last launch).
                // Editor + non-iOS skip via NeedsHealthKitGate() == false.
                if (NeedsHealthKitGate()) return AppPhase.HealthKitGate;
                if (_game.state.race == 0 && _game.state.level >= 20)
                    return AppPhase.RaceSelect;
                return AppPhase.Home;
            }
            if (_game.state.curse != 0) return AppPhase.OpeningAmnesia;
            return AppPhase.OpeningIntro;
        }

        // Pulls today's cumulative step total + running-workout seconds from
        // HealthKit and feeds the delta since the last sync into the normal
        // AddActivity pipeline. Same "delta vs last known total" pattern for
        // both metrics so quest progress / gold drops / level-ups all fire
        // through the same code path the debug step key already exercises,
        // and HK data and Editor manual injections are interchangeable.
        // EndDay resets both baselines to 0 so the first sync of a new day
        // writes the full new-day totals as one big delta each.
        //
        // Two independent queries — step (HKStatisticsQuery, cumulative sum)
        // and run-workout (HKSampleQuery, summed HKWorkout.duration). They
        // run in parallel; whichever completes first credits its delta and
        // the other follows.
        void SyncHealthKit()
        {
            if (HealthKitBridge.CurrentStatus() != HealthKitBridge.AuthStatus.Authorized) return;
            HealthKitBridge.QueryTodaySteps(steps =>
            {
                if (steps < 0 || _game == null) return;
                int delta = steps - _game.state.todayHealthKitSteps;
                if (delta > 0)
                {
                    _game.AddActivity(delta, 0, 0);
                    // Dual feed: levels / streaks (sacred stats) keep
                    // ticking through the old pipeline while the same delta
                    // earns Desk stride rolls (the day job).
                    _desk.GrantSteps(delta);
                }
                _game.state.todayHealthKitSteps = steps;
                _game.onSave?.Invoke();
            });
            HealthKitBridge.QueryTodayRunSeconds(runSec =>
            {
                if (runSec < 0 || _game == null) return;
                int delta = runSec - _game.state.todayHealthKitRunSeconds;
                if (delta > 0)
                {
                    _game.AddActivity(0, 0, delta);
                    // (Desk run-bonus perk arrives with the prestige tree —
                    // running already pays via its higher step cadence.)
                }
                _game.state.todayHealthKitRunSeconds = runSec;
                _game.onSave?.Invoke();
            });
        }

        static void DisableDefaultSceneObjects()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))      c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))       l.gameObject.SetActive(false);
            foreach (var a in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) a.enabled = false;
        }
    }
}
