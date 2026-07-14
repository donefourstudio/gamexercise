using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    public enum AppPhase
    {
        Boot,
        // App title / launch screen — shown on every cold start (NOT on
        // background -> foreground resume since OnApplicationFocus doesn't
        // touch phase). Tap "Start Game" routes to OpeningIntro for new
        // players or Home (or RaceSelect if Lv 20+) for returning ones.
        Title,
        // Opening sequence (first run only). Order:
        // OpeningIntro       — black, "你曾是这个时代最强的勇士……"   (tap)
        // OpeningHeroShown   — hero standing, bright                  (tap)
        // OpeningCurseLooms  — black, "……直到诅咒降临。"             (tap)
        // CurseAnim          — shake + dim + sprite swap, auto-advance
        // OpeningAmnesia     — black, "你忘了自己是谁。"             (tap)
        // GenderSelect       — pick male / female
        // FirstMirror        — "「……这就是现在的我。」" + first rep
        OpeningIntro,
        OpeningHeroShown,
        OpeningCurseLooms,
        CurseAnim,
        OpeningAmnesia,
        FirstMirror,
        Home,
        Quests,
        Shop,
        // Tap-on-mirror destination: top half is the avatar with its 6 equipped
        // slot icons (paper-doll), bottom half is the storage grid of every item
        // the player owns. Tapping a stored item equips/swaps in its slot;
        // tapping an equipped slot icon unequips it.
        Inventory,
        // M5g (Phase 3): tapping a shop set card opens the set's detail page —
        // full-gear preview + per-piece buy buttons + a bundle-buy CTA.
        SetDetail,
        // Triggered when level reaches 20 and race is Unset (gender + race
        // are chosen together — Q2=C). RaceTransformAnim is the silhouette -> chosen
        // race cinematic (M3c+M3d), then back to Home.
        RaceSelect,
        RaceTransformAnim,
        // Audio toggles + HealthKit re-link + reset-save. Reachable from a
        // small button at the top-right of Home; closes back to Home.
        Settings,
        // Hard gate: appears whenever the player would otherwise enter
        // gameplay (Home / Quests / Shop / etc.) but HealthKit isn't
        // authorized. Player can only escape by granting HK; cinematic
        // phases and Title bypass this gate. Append-only at end of enum so
        // older saves (serialized as int) keep their phase values intact.
        HealthKitGate,
        // Tapping a Legend or Cyberpunk card in the shop opens the skin
        // detail page — big preview + price + Buy/Apply/Remove CTA. Mirrors
        // SetDetail for sets. Appended last for save-compat.
        SkinDetail,
        // The Desk (Pivot 3 — docs/casino-mvp-plan.md): the whole game is
        // one scene reached from Home's CASINO button. (The v2 room phases
        // that lived here were removed with the rooms — phase is never
        // persisted, so enum surgery is save-safe.)
        Desk,
    }

    public enum Gender { Unset = 0, Male = 1, Female = 2 }
    public enum Curse  { Unset = 0, Weakness = 1 }   // Gluttony (was value 2) retired pre-launch
    public enum Race   { Unset = 0, Elf = 1, Orc = 2 }     // Human dropped in M5c; Dwarf future
    public enum Exercise { Pushup, Situp, Squat }          // kept for legacy save compat

    // Daily quests — each rewards 1 coin once per day, resets on EndDay.
    // The 7-day streak bonus is separate (5 coins every 7 active days).
    public enum Quest
    {
        Walk1000,
        Walk5000,
        Walk10000,
        Run15Min,
        Run30Min,
        Count            // sentinel for sizing arrays
    }

    [Serializable]
    public class EquipmentDef
    {
        public string id;
        public string name;
        public int tier;       // 1-4 visual tier
        public int minLevel;   // can't buy below this
        public int price;
        // M5g: paper-doll slot this piece occupies. Set explicitly by
        // catalogs (KnightSet, SetCatalog.pieces); GamexGame.SlotOf reads
        // from a dictionary built off these declarations.
        public GamexGame.EquipSlot slot;
    }

    // M5g (Phase 4): a "skin" is a full-body portrait swap — buying one and
    // applying it replaces the SPUM race-form sprite entirely (Make.Portrait
    // returns the skin sprite instead). Skins do NOT compose with equipment
    // overlays — when activeSkin is set, the avatar shows the baked-in art
    // exactly as the skin author drew it. This is what lets us use packs
    // whose weapons/armor are painted into the body (Luiz Melo, Cyberpunk).
    [Serializable]
    public class SkinDef
    {
        public string id;            // Resources/Skins/<id>.png lookup key
        public string displayName;
        public int    price;
        public string source;        // "champion", "legend", "cyberpunk", "pet" — for grouping in UI
        // Phase 5e3 — animation. 0 / 1 == static (just Skins/<id>.png).
        // > 1 means Hud.UpdateAnimatedSkin cycles through Skins/<id>_00..N-1.png
        public int    frameCount;
        public float  frameSeconds = 0.12f;
        // Live-ops launch gate (polish round 7). 0 = visible immediately;
        // > 0 = unix-seconds timestamp after which the item appears in shop.
        // Use Catalogs.FUTURE for "TBD, will surface in a later content drop".
        public long   availableAtUnix;
    }

    // M5g (Phase 3a): a "set" is a coherent SPUM-prefab-sourced loadout
    // (e.g. Elven Paladin = silver sword + paladin chest + greaves + boots).
    // The shop sells sets up-front with an 80% bundle discount, and each
    // set's detail page lets the player buy individual pieces too — so a
    // motivated player can mix-and-match favourites across multiple sets
    // without being forced to commit to one full bundle at a time.
    [Serializable]
    public class SetDef
    {
        public string id;            // e.g. "elf_paladin"
        public string displayName;   // shown in shop
        public string previewSprite; // Resources path under "Sets/" — full-gear bake of the source prefab
        public string source;        // groups shop sections (Phase 5a): "champion", "legend", ...
        public EquipmentDef[] pieces;
        // Live-ops launch gate (same convention as SkinDef.availableAtUnix).
        public long   availableAtUnix;
        // Sets are sold atomically now (no per-piece purchase), so the
        // bundle price is a direct catalog value rather than sum(pieces)*0.8.
        // EquipmentDef.price still exists for inventory bookkeeping (each
        // piece is granted on Buy Set) but is no longer summed for display.
        public int bundlePrice;
        public int BundlePrice => bundlePrice;
    }

    // Live-ops vocabulary shared by SetDef + SkinDef. FUTURE = "TBD, hidden
    // until we flip the catalog entry for a content drop". Any specific
    // future date can be assembled via DateTimeOffset.FromUnixTimeSeconds.
    public static class Catalogs
    {
        public const long FUTURE = long.MaxValue;
        public static bool IsLive(long availableAtUnix)
            => availableAtUnix <= System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // Player progression — all of this serializes to disk via JsonUtility.
    [Serializable]
    public class GameState
    {
        // Save schema version. Zero means "pre-versioning save written before
        // 2026-06" — Migrations.Apply bumps these to the current version on
        // load, running any field-rename / type-change migrations along the
        // way. JsonUtility serialises this just like any other public field
        // and deserialises missing-in-JSON to 0 (the default), so old saves
        // light up the migration path automatically.
        //
        // **To roll a new schema version**: add a `if (s.schemaVersion < N)
        // { ... transform ... ; s.schemaVersion = N; }` block to
        // Migrations.Apply, then bump Migrations.CurrentVersion to N.
        public int schemaVersion;

        // ---- skins (Phase 4) ----
        public string activeSkin;      // empty/null = race form; otherwise SkinDef.id whose sprite to render
        public List<string> ownedSkins = new(); // every skin id the player has purchased
        // ---- pets (Phase 5e2) ----
        public string activePet;       // empty/null = no companion; otherwise pet SkinDef id rendered alongside the avatar
        public List<string> ownedPets = new();

        public int gender;             // 0=unset, 1=male, 2=female  (chosen at Lv20 RaceSelect)
        public int curse;              // 0=unset, 1=weakness
        public int race;               // 0=unset, 1=elf, 2=orc  (chosen at Lv20)
        public int level = 1;          // 1..unlimited (no cap in step-based model)
        public long coins;

        // ---- step counters (M5a) ----
        public long totalSteps;        // lifetime walking + running steps
        public long totalRunSteps;     // lifetime running-only (also counted in totalSteps; running gets 2x XP)
        public long totalRunSeconds;   // lifetime running duration (for "run X min" quests)

        public int  todaySteps;        // resets at EndDay
        public int  todayRunSteps;
        public int  todayRunSeconds;

        public int  streakDays;        // consecutive days with >=500 steps
        public long lastDayEnd;        // unix seconds of last day-end rollover
        public bool[] questDone = new bool[(int)Quest.Count];  // per-day quest completion
        public bool   questAllBonusToday;                       // all-5-clear bonus already paid today

        // ---- Knight Set chain quest (M5d) ----
        // Lv 20+ unlock: 10 consecutive days of >=5000 steps each grants the next
        // piece of the Knight Set in order (chest -> helmet -> leggings ->
        // gauntlets -> boots). Missing a day resets progress.
        public int knightChainStage;    // 0..5 (5 = all 5 pieces earned)
        public int knightChainProgress; // 0..10 days into the current piece

        public List<string> owned = new();
        public List<string> equipped = new();
        public bool firstMirrorDone;
        // First-run coach-mark walkthrough on Home (mirror, Quests, Shop).
        // Set true after the player taps "Got it" on the last step; never
        // shown again. Survives save migrations since JsonUtility defaults
        // to false for older payloads.
        public bool tutorialDone;

        // HealthKit sync state (iOS only; non-iOS builds keep both fields at 0).
        //   todayHealthKitSteps = cumulative HK step count at last sync. The
        //   GameRunner's sync routine computes delta = newSteps - this, calls
        //   AddActivity(delta, 0, 0), then writes the new total back. Reset
        //   to 0 in EndDay so a fresh day starts at zero baseline.
        //   todayHealthKitRunSeconds = same pattern for HKWorkoutActivityType
        //   .running total duration today. Delta vs last sync is fed into
        //   AddActivity(0, 0, delta) so the existing Run15/Run30 quest gates
        //   fire through the same path the editor debug shortcuts already use.
        //   healthKitAsked = true once the OS permission modal has been shown
        //   (regardless of the user's answer). Prevents the post-tutorial
        //   trigger from re-prompting on every app launch — iOS itself only
        //   shows the modal the first time anyway, but we gate at the C#
        //   layer too so the flow stays clean.
        public int  todayHealthKitSteps;
        public int  todayHealthKitRunSeconds;
        public bool healthKitAsked;

        // Audio mutes (inverted so the default-false JsonUtility deserialization
        // for old saves means "audio enabled" — matches user expectation that
        // upgrading the app doesn't suddenly silence the game).
        public bool sfxMuted;
        public bool bgmMuted;

        // ---- The Casino (v2, retired — replaced by The Desk below; kept
        // so pre-pivot dev saves load until the rooms are deleted) ----
        public CasinoState casino = new CasinoState();

        // ---- The Desk (v3 — docs/casino-mvp-plan.md) ----
        // Guaranteed non-null from schemaVersion 3. The wallet is `coins`
        // above and MAY GO NEGATIVE (debt) down to DeskGame.DEBT_FLOOR.
        public DeskState desk = new DeskState();
    }

    // The Desk progression state (Pivot 3). Fixed-size arrays leave room
    // for catalog growth; JsonUtility serializes int[] fine and missing
    // fields default sanely for older saves.
    [Serializable]
    public class DeskState
    {
        public int  stepAccumulator;     // 0..99 toward the next stride roll
        public int  rollsPending;        // uncollected rolls (the paycheck envelope)
        public long earnedThisRun;       // lifetime-earned bar — unlocks cards; resets on prestige
        public long loanOwed;            // outstanding repayment (0 = none); income is garnished

        public int[] cardXp     = new int[16];
        public int[] cardLevel  = new int[16];
        public int[] cardsOwned = new int[16];   // bought-but-unscratched pile per card type

        public int upLuck;               // money upgrades — reset on prestige
        public int upSize;               // brush size (presentation reads it)
        public int upCoin;               // coin strength vs card Hardness (presentation reads it)

        public int  playsThisRun;        // PP effort floor input
        public int  bigWinsThisRun;      // PP driver: wins >= 20x card cost
        public long lifetimePlays;       // never resets — also gates the rigged 3rd play
        public long lifetimeBigWins;
        public float prestigePoints;
        public int   prestigeCount;
    }

    // Casino progression state (docs/casino-mvp-plan.md). Nested inside
    // GameState so it rides the existing save + migration machinery. Every
    // field's zero-default is a valid "never entered the casino" state, so
    // fresh saves and migrated pre-casino saves both work with no value
    // backfill. Economy constants (payout table, upgrade costs, steps-per-
    // ticket) live in CasinoGame, not in state. Money is NOT here — the
    // unified wallet is GameState.coins.
    [Serializable]
    public class CasinoState
    {
        public int  ticketsBanked;       // unplayed tickets in hand (1 ticket = 1 scratch or 1 spin)
        public int  ticketCap;           // 0 = CasinoGame default (~300); >0 = upgraded cap
        public int  stepAccumulator;     // leftover steps not yet worth a ticket (0..stepsPerTicket-1)
        public long lifetimePlays;       // never resets — real-effort record
        public long lifetimeJackpots;    // never resets — gates the one-time rigged first 777 (Fix #1)

        // ---- current run (resets on Prestige) ----
        public int playsThisRun;         // effort-floor input for PP
        public int jackpotsThisRun;      // primary PP driver + prestige gate
        public int runPayout;            // run upgrade levels (coin-bought)
        public int runLuck;
        public int runStride;

        // ---- The Ladder (P5b) — persisted so an app quit mid-climb never
        // eats the pot (never punish the player for real life happening) ----
        public bool ladderActive;
        public long ladderPot;
        public int  ladderRung;

        // ---- permanent (never resets) ----
        public float prestigePoints;     // PP wallet (fractional — effort floor pays plays/40)
        public int   prestigeCount;
        public bool  firstPrestigeDone;  // the scripted tutorial prestige (Fix #1)
        public int   permGoldenTouch;    // PP-tree levels — MVP trio
        public int   permLoadedDice;
        public int   permMarathoner;
        public int   permAutoScratcher;  // 0/1 — one-time P4 unlock (batch-rips the backlog)
    }
}
