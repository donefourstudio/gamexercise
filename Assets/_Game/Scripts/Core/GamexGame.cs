using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    // Pure-C# game logic. No Unity types. Presentation layer reads state every frame
    // and routes player intents (button clicks / step ingestion) back through this class.
    public class GamexGame
    {
        public AppPhase phase = AppPhase.Boot;
        public GameState state = new GameState();
        public System.Action onSave;

        // ---- step-based mechanic (M5a) ----
        // Linear XP: every 5000 effective steps = 1 level. Running counts 2x via
        // EffectiveSteps = totalSteps + totalRunSteps (running steps are double-counted).
        // No level cap. Visual body stages still split into the same 6 buckets, but
        // anything beyond Stage 3 (Lv 20) is the chosen race form — no further auto
        // body change.
        public const int STEPS_PER_LEVEL = 5000;

        // Daily quest thresholds + reward
        const int Q_WALK_1000     = 1000;
        const int Q_WALK_5000     = 5000;
        const int Q_WALK_10000    = 10000;
        const int Q_RUN_15_MIN_S  = 15 * 60;
        const int Q_RUN_30_MIN_S  = 30 * 60;
        const int QUEST_COIN_REWARD = 1;
        const int STREAK_ACTIVE_THRESHOLD = 500;   // 500 steps = "active day" for streak
        const int STREAK_WEEKLY_BONUS     = 5;     // every 7 streak days

        public long EffectiveSteps => state.totalSteps + state.totalRunSteps;

        // Stage caps at 3 (skeleton end / pre-race). After race choice the visual
        // is race form regardless — Make.Portrait ignores stage when race != Unset.
        public int Stage => Math.Min((state.level - 1) / 5, 3);
        public int XpInCurrentLevel => (int)(EffectiveSteps % STEPS_PER_LEVEL);
        public int XpToNextLevel    => STEPS_PER_LEVEL;

        // ---- phase navigation ----

        // Each tap on an opening text screen advances to the next narrative beat.
        public void TapAdvanceOpening()
        {
            switch (phase)
            {
                case AppPhase.OpeningIntro:      phase = AppPhase.OpeningHeroShown;  break;
                case AppPhase.OpeningHeroShown:  phase = AppPhase.OpeningCurseLooms; break;
                case AppPhase.OpeningCurseLooms: phase = AppPhase.CurseSelect;       break;
                case AppPhase.OpeningAmnesia:    phase = AppPhase.FirstMirror;       break;
            }
            onSave?.Invoke();
        }

        public void SetCurse(Curse c)
        {
            state.curse = (int)c;
            phase = AppPhase.CurseAnim;
            onSave?.Invoke();
        }

        public void CompleteCurseAnim()
        {
            if (phase != AppPhase.CurseAnim) return;
            phase = AppPhase.OpeningAmnesia;
            onSave?.Invoke();
        }

        public void SetGender(Gender g)
        {
            state.gender = (int)g;
            onSave?.Invoke();
        }

        public void SetRaceAndGender(Race r, Gender g)
        {
            if (phase != AppPhase.RaceSelect) return;
            state.race   = (int)r;
            state.gender = (int)g;
            phase = AppPhase.RaceTransformAnim;
            onSave?.Invoke();
        }

        public void CompleteRaceAnim()
        {
            if (phase != AppPhase.RaceTransformAnim) return;
            phase = AppPhase.Home;
            onSave?.Invoke();
        }

        public void FinishFirstMirror()
        {
            state.firstMirrorDone = true;
            phase = AppPhase.Home;
            onSave?.Invoke();
        }

        public void GoHome()     => phase = AppPhase.Home;
        public void GoQuests()   => phase = AppPhase.Quests;
        public void GoShop()     => phase = AppPhase.Shop;
        public void GoInventory(){ phase = AppPhase.Inventory; onSave?.Invoke(); }

        // ---- step ingestion (M5a) ----

        // Single entry point for any activity. On iOS this is fed from HealthKit;
        // in Editor from the debug "+1000 steps" key. All three counters increment
        // both today and the lifetime totals.
        //   newSteps      — total new steps this tick (includes any running steps)
        //   newRunSteps   — running steps within newSteps (counted again for the 2x XP)
        //   newRunSeconds — running session time within this tick
        public void AddActivity(int newSteps, int newRunSteps, int newRunSeconds)
        {
            if (newSteps    < 0) newSteps    = 0;
            if (newRunSteps < 0) newRunSteps = 0;
            if (newRunSeconds < 0) newRunSeconds = 0;
            if (newRunSteps > newSteps) newRunSteps = newSteps;

            state.todaySteps      += newSteps;
            state.todayRunSteps   += newRunSteps;
            state.todayRunSeconds += newRunSeconds;
            state.totalSteps      += newSteps;
            state.totalRunSteps   += newRunSteps;
            state.totalRunSeconds += newRunSeconds;

            // Recompute level from effective steps (steps + run-steps, with running
            // double-counted). Linear, no cap.
            int prevLevel = state.level;
            int newLevel  = 1 + (int)(EffectiveSteps / STEPS_PER_LEVEL);
            state.level   = newLevel;

            // Race transformation trigger — fires the first time level crosses 20.
            if (state.level >= 20 && state.race == 0 &&
                phase != AppPhase.RaceSelect && phase != AppPhase.RaceTransformAnim)
            {
                phase = AppPhase.RaceSelect;
            }

            CheckQuests();
            onSave?.Invoke();
        }

        // Legacy debug entry: each "rep" = 1 walking step.
        public void DoRep(Exercise e) => AddActivity(1, 0, 0);

        void CheckQuests()
        {
            // Each completion grants QUEST_COIN_REWARD coins and flips the flag so
            // the same quest can't be re-claimed today. EndDay resets the flags.
            void Try(Quest q, bool met)
            {
                int idx = (int)q;
                if (!state.questDone[idx] && met)
                {
                    state.questDone[idx] = true;
                    state.coins += QUEST_COIN_REWARD;
                }
            }
            Try(Quest.Walk1000,  state.todaySteps      >= Q_WALK_1000);
            Try(Quest.Walk5000,  state.todaySteps      >= Q_WALK_5000);
            Try(Quest.Walk10000, state.todaySteps      >= Q_WALK_10000);
            Try(Quest.Run15Min,  state.todayRunSeconds >= Q_RUN_15_MIN_S);
            Try(Quest.Run30Min,  state.todayRunSeconds >= Q_RUN_30_MIN_S);
        }

        // Day rollover: advance streak (if today was active), reset daily counters
        // and per-day quest flags. No decay — the step model removes the punitive
        // "lose a level if you miss" mechanic Jackson asked to drop.
        public void EndDay()
        {
            bool activeToday = state.todaySteps >= STREAK_ACTIVE_THRESHOLD;
            if (activeToday)
            {
                state.streakDays += 1;
                // Weekly streak bonus
                if (state.streakDays > 0 && state.streakDays % 7 == 0)
                    state.coins += STREAK_WEEKLY_BONUS;
            }
            else
            {
                state.streakDays = 0;
            }

            // Knight Set chain — only ticks once Lv 20 is reached and chain not complete.
            // Hit 5000 today -> +1 progress day; miss -> reset to 0. At 10 days the
            // current piece is granted and the next slot opens.
            if (state.level >= KNIGHT_CHAIN_UNLOCK_LEVEL && state.knightChainStage < KnightSet.Length)
            {
                if (state.todaySteps >= KNIGHT_CHAIN_DAILY_STEPS)
                {
                    state.knightChainProgress += 1;
                    if (state.knightChainProgress >= KNIGHT_CHAIN_DAYS)
                    {
                        string pieceId = KnightSet[state.knightChainStage].id;
                        if (!state.owned.Contains(pieceId)) state.owned.Add(pieceId);
                        state.knightChainStage    += 1;
                        state.knightChainProgress  = 0;
                    }
                }
                else
                {
                    state.knightChainProgress = 0;
                }
            }

            state.todaySteps      = 0;
            state.todayRunSteps   = 0;
            state.todayRunSeconds = 0;
            if (state.questDone == null || state.questDone.Length != (int)Quest.Count)
                state.questDone = new bool[(int)Quest.Count];
            else
                Array.Clear(state.questDone, 0, state.questDone.Length);

            state.lastDayEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            onSave?.Invoke();
        }

        // Crosses any LOCAL-CALENDAR midnights between lastDayEnd and now.
        // (Was 24-hour elapsed seconds — that mis-fires the common "play late at
        // night, reopen early morning of the next day" case where <24 h has
        // elapsed but a real day boundary has been crossed.)
        // Called from GameRunner.Awake() on every cold start.
        public void CatchUpDays()
        {
            DateTime todayLocal = DateTime.Now.Date;

            if (state.lastDayEnd == 0)
            {
                state.lastDayEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return;
            }

            DateTime lastLocal = DateTimeOffset.FromUnixTimeSeconds(state.lastDayEnd).LocalDateTime.Date;
            int daysBetween = (todayLocal - lastLocal).Days;
            for (int i = 0; i < daysBetween; i++) EndDay();
        }

        // ---- shop ----

        // Phase 2 cleanup: dropped the 4 sword + 4 armor tier items. The
        // SPUM library is being added in Phase 3-5 as full sets sold whole
        // (with mix-and-match piece purchases inside each set), and Jackson
        // wants those to be the shop content rather than the old generic
        // "iron sword / silver armor" placeholders. Knight Set still comes
        // from the M5d chain quest, not the shop, so it stays out of Catalog.
        public static readonly EquipmentDef[] Catalog = new EquipmentDef[0];

        // Knight Set — earned through the M5d chain quest, not via the shop.
        // 6 pieces total: chest -> helmet -> leggings -> gauntlets -> boots -> sword.
        // At 10 chain days per piece (M5d) this is a 60-day commitment for the
        // full silver-knight loadout, with the sword as the climax reward.
        public static readonly (string id, string name)[] KnightSet = new[]
        {
            ("knight_chest",     "Knight Chestplate"),
            ("knight_helmet",    "Knight Helmet"),
            ("knight_leggings",  "Knight Leggings"),
            ("knight_gauntlets", "Knight Gauntlets"),
            ("knight_boots",     "Knight Boots"),
            ("knight_sword",     "Knight Longsword"),
        };
        public const int KNIGHT_CHAIN_DAILY_STEPS = 5000;
        public const int KNIGHT_CHAIN_DAYS        = 10;
        public const int KNIGHT_CHAIN_UNLOCK_LEVEL = 20;

        public bool TryBuy(EquipmentDef def)
        {
            if (def == null) return false;
            if (state.coins < def.price) return false;
            if (state.owned.Contains(def.id)) return false;
            state.coins -= def.price;
            state.owned.Add(def.id);
            onSave?.Invoke();
            return true;
        }

        // Equipment slots — the Inventory paper-doll has exactly one cell per slot,
        // so equipping a new item replaces whatever else is in that slot. Catalog
        // IDs follow naming conventions so this routing can be done by string
        // prefix; the Knight Set is hand-mapped because it doesn't share prefixes.
        public enum EquipSlot { None, Weapon, Chest, Head, Legs, Wrists, Feet }
        public static readonly EquipSlot[] AllSlots = {
            EquipSlot.Head, EquipSlot.Chest, EquipSlot.Wrists,
            EquipSlot.Weapon, EquipSlot.Legs, EquipSlot.Feet,
        };
        public static EquipSlot SlotOf(string id)
        {
            if (id == null) return EquipSlot.None;
            if (id.StartsWith("sword_"))    return EquipSlot.Weapon;
            if (id.StartsWith("armor_"))    return EquipSlot.Chest;
            if (id == "knight_chest")       return EquipSlot.Chest;
            if (id == "knight_helmet")      return EquipSlot.Head;
            if (id == "knight_leggings")    return EquipSlot.Legs;
            if (id == "knight_gauntlets")   return EquipSlot.Wrists;
            if (id == "knight_boots")       return EquipSlot.Feet;
            if (id == "knight_sword")       return EquipSlot.Weapon;
            return EquipSlot.None;
        }
        public string EquippedInSlot(EquipSlot slot)
        {
            for (int i = state.equipped.Count - 1; i >= 0; i--)
                if (SlotOf(state.equipped[i]) == slot) return state.equipped[i];
            return null;
        }

        // Equipping a new item evicts the previous occupant of the same slot so
        // the paper-doll never shows two of the same kind. Unequip is just remove.
        public void EquipItem(string id)
        {
            if (!state.owned.Contains(id)) return;
            var slot = SlotOf(id);
            if (slot == EquipSlot.None) return;
            state.equipped.RemoveAll(e => SlotOf(e) == slot);
            state.equipped.Add(id);
            onSave?.Invoke();
        }
        public void Unequip(string id)
        {
            if (state.equipped.Remove(id)) onSave?.Invoke();
        }
        // Kept for back-compat (shop list still toggles). Routes through the
        // slot-aware Equip path so multi-equip can't happen via shop either.
        public void ToggleEquip(string id)
        {
            if (state.equipped.Contains(id)) Unequip(id);
            else                              EquipItem(id);
        }

        public bool IsOwned(string id)    => state.owned.Contains(id);
        public bool IsEquipped(string id) => state.equipped.Contains(id);
    }
}
