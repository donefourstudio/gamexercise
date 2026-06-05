using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    // Pure-C# game logic. No Unity types. Presentation layer reads state every frame
    // and routes player intents (button clicks / fake reps) back through this class.
    public class GamexGame
    {
        public AppPhase phase = AppPhase.Boot;
        public GameState state = new GameState();
        public System.Action onSave;

        // 6 stages × 5 levels = 30 max. Stage 0 is levels 1-5.
        public const int MaxLevel = 30;
        static readonly int[] StageXpCost = { 20, 50, 100, 200, 400, 800 };
        static readonly int[] StageMaintenance = { 0, 10, 25, 50, 90, 150 };

        public int Stage => Math.Min((state.level - 1) / 5, 5);
        public int XpPerLevel => StageXpCost[Stage];
        public int MaintenanceToday => StageMaintenance[Stage];

        // ---- phase navigation ----

        // Each tap on an opening text screen advances to the next narrative beat.
        // OpeningIntro -> OpeningHeroShown -> OpeningCurseLooms -> CurseSelect
        // (CurseAnim, M2b-2)            -> OpeningAmnesia -> GenderSelect
        public void TapAdvanceOpening()
        {
            switch (phase)
            {
                case AppPhase.OpeningIntro:      phase = AppPhase.OpeningHeroShown;  break;
                case AppPhase.OpeningHeroShown:  phase = AppPhase.OpeningCurseLooms; break;
                case AppPhase.OpeningCurseLooms: phase = AppPhase.CurseSelect;       break;
                case AppPhase.OpeningAmnesia:    phase = AppPhase.GenderSelect;      break;
            }
            onSave?.Invoke();
        }

        public void SetCurse(Curse c)
        {
            state.curse = (int)c;
            phase = AppPhase.CurseAnim;       // Hud auto-advances after the animation finishes
            onSave?.Invoke();
        }

        // Called by Hud when the curse transformation cinematic ends.
        public void CompleteCurseAnim()
        {
            if (phase != AppPhase.CurseAnim) return;
            phase = AppPhase.OpeningAmnesia;
            onSave?.Invoke();
        }

        public void SetGender(Gender g)
        {
            state.gender = (int)g;
            phase = AppPhase.FirstMirror;
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

        // ---- gameplay ----

        // Called once per detected rep (real pose detection or fake button click).
        public void DoRep(Exercise e)
        {
            state.coins += 1;
            state.repsToday += 1;
            if (state.level < MaxLevel)
            {
                state.xp += 1;
                while (state.xp >= XpPerLevel && state.level < MaxLevel)
                {
                    state.xp -= XpPerLevel;
                    state.level++;
                }
                if (state.level >= MaxLevel) state.xp = 0;
            }
        }

        // Day rollover: check if today met maintenance, count missed days, apply decay.
        // Called when a real-world day boundary is crossed (or by the debug 'advance day' key).
        public void EndDay()
        {
            bool metMaintenance = state.repsToday >= MaintenanceToday;
            if (metMaintenance)
            {
                state.streakDays += 1;
                state.missedDays = 0;
            }
            else
            {
                state.missedDays += 1;
                if (state.missedDays >= 2 && state.level > 1)
                {
                    state.level -= 1;
                    state.missedDays = 0;
                }
            }
            state.repsToday = 0;
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

        public static readonly EquipmentDef[] Catalog = new[]
        {
            new EquipmentDef { id = "sword_wood",   name = "Wooden Sword",    tier = 1, minLevel = 1,  price = 50 },
            new EquipmentDef { id = "armor_cloth",  name = "Cloth Robe",      tier = 1, minLevel = 1,  price = 80 },
            new EquipmentDef { id = "sword_iron",   name = "Iron Sword",      tier = 2, minLevel = 10, price = 300 },
            new EquipmentDef { id = "armor_leather",name = "Leather Armor",   tier = 2, minLevel = 10, price = 400 },
            new EquipmentDef { id = "sword_silver", name = "Silver Sword",    tier = 3, minLevel = 20, price = 1500 },
            new EquipmentDef { id = "armor_silver", name = "Silver Armor",    tier = 3, minLevel = 20, price = 1800 },
            new EquipmentDef { id = "sword_legend", name = "Legendary Sword", tier = 4, minLevel = 30, price = 8000 },
            new EquipmentDef { id = "armor_legend", name = "Legendary Armor", tier = 4, minLevel = 30, price = 10000 },
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
