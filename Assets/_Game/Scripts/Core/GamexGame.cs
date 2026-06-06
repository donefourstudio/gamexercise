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
        public void GoTraining() => phase = AppPhase.Training;
        public void GoShop()     => phase = AppPhase.Shop;

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

        // Crosses any midnights between lastDayEnd and now.
        public void CatchUpDays()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (state.lastDayEnd == 0) { state.lastDayEnd = now; return; }
            long elapsed = now - state.lastDayEnd;
            int days = (int)(elapsed / 86400L);
            for (int i = 0; i < days; i++) EndDay();
        }

        // ---- shop ----

        // M5a repricing — Legendary tier aimed at ~1 year for a steady 5 coin/day player.
        // Wood sword 10 coins = 2-3 days, keeps early ownership reachable.
        public static readonly EquipmentDef[] Catalog = new[]
        {
            new EquipmentDef { id = "sword_wood",   name = "Wooden Sword",    tier = 1, minLevel = 1,  price = 10 },
            new EquipmentDef { id = "armor_cloth",  name = "Cloth Robe",      tier = 1, minLevel = 1,  price = 15 },
            new EquipmentDef { id = "sword_iron",   name = "Iron Sword",      tier = 2, minLevel = 10, price = 80 },
            new EquipmentDef { id = "armor_leather",name = "Leather Armor",   tier = 2, minLevel = 10, price = 100 },
            new EquipmentDef { id = "sword_silver", name = "Silver Sword",    tier = 3, minLevel = 20, price = 400 },
            new EquipmentDef { id = "armor_silver", name = "Silver Armor",    tier = 3, minLevel = 20, price = 500 },
            new EquipmentDef { id = "sword_legend", name = "Legendary Sword", tier = 4, minLevel = 30, price = 1500 },
            new EquipmentDef { id = "armor_legend", name = "Legendary Armor", tier = 4, minLevel = 30, price = 1800 },
        };

        public bool TryBuy(EquipmentDef def)
        {
            if (def == null) return false;
            if (state.level < def.minLevel) return false;
            if (state.coins < def.price) return false;
            if (state.owned.Contains(def.id)) return false;
            state.coins -= def.price;
            state.owned.Add(def.id);
            onSave?.Invoke();
            return true;
        }

        public void ToggleEquip(string id)
        {
            if (state.equipped.Contains(id)) state.equipped.Remove(id);
            else if (state.owned.Contains(id)) state.equipped.Add(id);
            onSave?.Invoke();
        }

        public bool IsOwned(string id)    => state.owned.Contains(id);
        public bool IsEquipped(string id) => state.equipped.Contains(id);
    }
}
