using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    public enum AppPhase
    {
        Boot,
        // Opening sequence (first run only). Order:
        // OpeningIntro       — black, "你曾是这个时代最强的勇士……"   (tap)
        // OpeningHeroShown   — hero standing, bright                  (tap)
        // OpeningCurseLooms  — black, "……直到诅咒降临。"             (tap)
        // CurseSelect        — pick weakness / gluttony
        // CurseAnim          — shake + dim + sprite swap, auto-advance
        // OpeningAmnesia     — black, "你忘了自己是谁。"             (tap)
        // GenderSelect       — pick male / female
        // FirstMirror        — "「……这就是现在的我。」" + first rep
        OpeningIntro,
        OpeningHeroShown,
        OpeningCurseLooms,
        CurseSelect,
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
    }

    public enum Gender { Unset = 0, Male = 1, Female = 2 }
    public enum Curse  { Unset = 0, Weakness = 1, Gluttony = 2 }
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
        public string source;        // "spum_bundle", "luizmelo", "cyberpunk" — for grouping in UI
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
        public EquipmentDef[] pieces;
        public const float BUNDLE_DISCOUNT = 0.8f;   // 20% off the sum of piece prices
        public int BundlePrice
        {
            get
            {
                int sum = 0;
                if (pieces != null) foreach (var p in pieces) sum += p.price;
                return UnityEngine.Mathf.RoundToInt(sum * BUNDLE_DISCOUNT);
            }
        }
    }

    // Player progression — all of this serializes to disk via JsonUtility.
    [Serializable]
    public class GameState
    {
        // ---- skins (Phase 4) ----
        public string activeSkin;      // empty/null = race form; otherwise SkinDef.id whose sprite to render
        public List<string> ownedSkins = new(); // every skin id the player has purchased

        public int gender;             // 0=unset, 1=male, 2=female  (chosen at Lv20 RaceSelect)
        public int curse;              // 0=unset, 1=weakness, 2=gluttony
        public int race;               // 0=unset, 1=human, 2=orc  (chosen at Lv20)
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

        // ---- Knight Set chain quest (M5d) ----
        // Lv 20+ unlock: 10 consecutive days of >=5000 steps each grants the next
        // piece of the Knight Set in order (chest -> helmet -> leggings ->
        // gauntlets -> boots). Missing a day resets progress.
        public int knightChainStage;    // 0..5 (5 = all 5 pieces earned)
        public int knightChainProgress; // 0..10 days into the current piece

        public List<string> owned = new();
        public List<string> equipped = new();
        public bool firstMirrorDone;
    }
}
