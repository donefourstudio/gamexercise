using System;

namespace Gamex.Core
{
    // Scratch outcome tiers, ordered worst -> best. The UI maps these onto
    // the reveal symbols (Bust = mismatched omens ... Sun = the jackpot).
    public enum FateTier { Bust = 0, Moon = 1, Coin = 2, Crown = 3, Sun = 4 }

    public struct FateScratchResult
    {
        public FateTier tier;
        public long gold;     // payout after Fortune/Midas multipliers
        public bool jackpot;  // tier == Sun
        public bool rigged;   // the scripted first-session jackpot (Fix #1)
    }

    // Fate Cards core logic (M_fate Phase 1 — docs/fatecards-mvp-plan.md).
    // Pure C#, no Unity types (same rule as GamexGame): the presentation
    // layer reads state every frame and routes player intents back through
    // this class. Owns every economy rule of the mode — the steps -> cards
    // faucet, the Three Fates payout roll, per-run upgrades, the ascension
    // gates and the AP math. Every tunable dial is a const up top.
    //
    // RNG is injected so LogicTest can seed it deterministically. Outcomes
    // are PRE-ROLLED at scratch time — whatever symbols the UI reveals are
    // pure theatre over the rolled tier (design rule: set the payout
    // distribution first, dress it as matching symbols second).
    public class FateGame
    {
        // ---- faucet ----
        public const int STEPS_PER_CARD_BASE  = 100;   // 1 card / 100 steps
        public const int STEPS_PER_CARD_FLOOR = 44;
        public const int CARD_CAP_DEFAULT     = 300;
        // Over-cap steps convert straight to gold so walking is never
        // wasted. Deliberately below the ~72g scratch EV: draining the
        // backlog by scratching always beats idling at the cap.
        public const int OVERFLOW_GOLD_PER_CARD = 50;
        // Marathoner (perm): running earns +10%/level ON TOP of the steps
        // HealthKit already counted. HK gives no per-activity step split,
        // so run-steps are estimated from workout seconds at ~2.8 steps/s
        // (~168 spm running cadence).
        public const float RUN_STEPS_PER_SECOND      = 2.8f;
        public const float MARATHONER_BONUS_PER_LEVEL = 0.10f;

        // ---- Three Fates (Lv-1 card) payout table ----
        // Locked distribution (plan §9): EV = 72.0 gold at luck 0.
        //   Bust 45% -> 5   | Moon 30% -> 25 | Coin 17% -> 75
        //   Crown 6.5% -> 300 | Sun 1.5% -> 2000 (jackpot)
        // Luck (runFavor + permBlessedFate) shifts it: each level moves
        // +0.3% into Sun and +1.0% from Bust into Moon (the "busts become
        // small wins" nudge) -> EV grows ~+6.2/level.
        public const float P_SUN_BASE   = 0.015f;
        public const float P_CROWN      = 0.065f;
        public const float P_COIN       = 0.17f;
        public const float P_MOON_BASE  = 0.30f;
        public const float SUN_PER_LUCK  = 0.003f;
        public const float MOON_PER_LUCK = 0.010f;
        public const int   LUCK_CAP      = 25;   // keeps the bust bucket >= ~12%
        public const int GOLD_BUST = 5, GOLD_MOON = 25, GOLD_COIN = 75,
                         GOLD_CROWN = 300, GOLD_SUN = 2000;

        // Fix #1 (onboarding): the 3rd card ever scratched is rigged to be
        // the player's first jackpot, so the peak "Fortune smiles!" moment
        // lands in session one. Fires once per save, ever.
        public const int RIGGED_JACKPOT_AT = 2;   // 0-based lifetime index

        // ---- per-run upgrades (gold-bought; reset on ascension) ----
        // Flattened curve for the ~2-3 day ascension cadence (plan §9).
        public const int   FORTUNE_L1_COST = 300;  public const float FORTUNE_COST_MULT = 1.4f;
        public const float FORTUNE_GOLD_PER_LEVEL = 0.08f;
        public const int   FAVOR_L1_COST   = 400;  public const float FAVOR_COST_MULT   = 1.5f;
        public const int   ENDUR_L1_COST   = 600;  public const float ENDUR_COST_MULT   = 1.5f;
        public const int   ENDUR_STEPS_PER_LEVEL  = 8;
        public const int   ENDUR_MAX_LEVEL        = 7;   // 100 - 7*8 = 44 = floor

        // ---- permanent AP upgrades (never reset; MVP trio) ----
        public const int   MIDAS_L1_COST = 2;       public const float MIDAS_COST_MULT = 1.5f;
        public const float MIDAS_GOLD_PER_LEVEL = 0.05f;
        public const int   BFATE_L1_COST = 3;       public const float BFATE_COST_MULT = 1.6f;
        public const int   MARATHONER_L1_COST = 4;  public const float MARATHONER_COST_MULT = 1.6f;

        // ---- ascension ----
        public const int   FIRST_ASC_CARDS   = 25;   // scripted tutorial gate (pure effort, day-1 reachable)
        public const float FIRST_ASC_AP      = 5f;   // guaranteed starter AP
        public const float AP_EFFORT_DIVISOR = 40f;  // effort floor: + cards/40 (Fix #4)

        public FateState state;
        public Action onSave;
        readonly Random _rng;

        public FateGame(FateState state, Random rng = null)
        {
            this.state = state ?? new FateState();
            _rng = rng ?? new Random();
        }

        // ---- derived dials (also what the UI displays) ----
        public int   Luck           => Math.Min(state.runFavor + state.permBlessedFate, LUCK_CAP);
        public int   StepsPerCard   => Math.Max(STEPS_PER_CARD_FLOOR,
                                        STEPS_PER_CARD_BASE - ENDUR_STEPS_PER_LEVEL * state.runEndurance);
        public int   CardCap        => state.cardCap > 0 ? state.cardCap : CARD_CAP_DEFAULT;
        public float GoldMultiplier => 1f + FORTUNE_GOLD_PER_LEVEL * state.runFortune
                                          + MIDAS_GOLD_PER_LEVEL   * state.permMidas;
        public float JackpotChance  => P_SUN_BASE + SUN_PER_LUCK * Luck;
        public bool  CanScratch     => state.cardsBanked > 0;

        // ---- faucet ----

        // Single entry point for real-world activity. `steps` = new steps
        // this tick (HealthKit delta or the Editor debug key); `runSeconds`
        // = running-workout seconds within the tick. Run STEPS are already
        // inside `steps` (the pedometer counts them like any other step) —
        // runSeconds only feeds the Marathoner bonus credit. Returns cards
        // granted so the UI can toast.
        public int GrantActivity(int steps, int runSeconds)
        {
            if (steps < 0) steps = 0;
            if (runSeconds < 0) runSeconds = 0;

            int bonus = (int)Math.Round(runSeconds * RUN_STEPS_PER_SECOND
                        * MARATHONER_BONUS_PER_LEVEL * state.permMarathoner);
            long acc = (long)state.stepAccumulator + steps + bonus;

            int granted = 0;
            int spc = StepsPerCard;
            while (acc >= spc)
            {
                acc -= spc;
                if (state.cardsBanked < CardCap) { state.cardsBanked++; granted++; }
                else state.gold += OVERFLOW_GOLD_PER_CARD;   // at cap: walking never wasted
            }
            state.stepAccumulator = (int)acc;
            if (steps != 0 || runSeconds != 0) onSave?.Invoke();
            return granted;
        }

        // ---- the scratch ----

        public FateScratchResult ScratchCard()
        {
            if (state.cardsBanked <= 0) return default;   // UI gates on CanScratch

            state.cardsBanked--;
            bool rig = state.lifetimeCardsScratched == RIGGED_JACKPOT_AT
                       && state.lifetimeJackpots == 0;
            state.lifetimeCardsScratched++;
            state.cardsThisRun++;

            FateTier tier = rig ? FateTier.Sun : RollTier();
            long gold = (long)Math.Round(BaseGold(tier) * GoldMultiplier);
            state.gold += gold;
            if (tier == FateTier.Sun)
            {
                state.jackpotsThisRun++;
                state.lifetimeJackpots++;
            }
            onSave?.Invoke();
            return new FateScratchResult { tier = tier, gold = gold, jackpot = tier == FateTier.Sun, rigged = rig };
        }

        FateTier RollTier()
        {
            double r = _rng.NextDouble();
            double pSun  = P_SUN_BASE  + SUN_PER_LUCK  * Luck;
            double pMoon = P_MOON_BASE + MOON_PER_LUCK * Luck;
            if ((r -= pSun)    < 0) return FateTier.Sun;
            if ((r -= P_CROWN) < 0) return FateTier.Crown;
            if ((r -= P_COIN)  < 0) return FateTier.Coin;
            if ((r -= pMoon)   < 0) return FateTier.Moon;
            return FateTier.Bust;
        }

        static long BaseGold(FateTier t)
        {
            switch (t)
            {
                case FateTier.Sun:   return GOLD_SUN;
                case FateTier.Crown: return GOLD_CROWN;
                case FateTier.Coin:  return GOLD_COIN;
                case FateTier.Moon:  return GOLD_MOON;
                default:             return GOLD_BUST;
            }
        }

        // ---- per-run upgrades (gold) ----

        public enum RunUpgrade { Fortune, Favor, Endurance }

        public int RunUpgradeLevel(RunUpgrade u)
        {
            switch (u)
            {
                case RunUpgrade.Fortune: return state.runFortune;
                case RunUpgrade.Favor:   return state.runFavor;
                default:                 return state.runEndurance;
            }
        }

        // Cost of the NEXT level (base * mult^ownedLevels).
        public long RunUpgradeCost(RunUpgrade u)
        {
            int lvl = RunUpgradeLevel(u);
            switch (u)
            {
                case RunUpgrade.Fortune: return Cost(FORTUNE_L1_COST, FORTUNE_COST_MULT, lvl);
                case RunUpgrade.Favor:   return Cost(FAVOR_L1_COST,   FAVOR_COST_MULT,   lvl);
                default:                 return Cost(ENDUR_L1_COST,   ENDUR_COST_MULT,   lvl);
            }
        }

        public bool TryBuyRunUpgrade(RunUpgrade u)
        {
            if (u == RunUpgrade.Endurance && state.runEndurance >= ENDUR_MAX_LEVEL) return false;
            long cost = RunUpgradeCost(u);
            if (state.gold < cost) return false;
            state.gold -= cost;
            switch (u)
            {
                case RunUpgrade.Fortune: state.runFortune++;   break;
                case RunUpgrade.Favor:   state.runFavor++;     break;
                default:                 state.runEndurance++; break;
            }
            onSave?.Invoke();
            return true;
        }

        // ---- permanent AP upgrades ----

        public enum PermUpgrade { Midas, BlessedFate, Marathoner }

        public int PermUpgradeLevel(PermUpgrade u)
        {
            switch (u)
            {
                case PermUpgrade.Midas:       return state.permMidas;
                case PermUpgrade.BlessedFate: return state.permBlessedFate;
                default:                      return state.permMarathoner;
            }
        }

        public int PermUpgradeCost(PermUpgrade u)
        {
            int lvl = PermUpgradeLevel(u);
            switch (u)
            {
                case PermUpgrade.Midas:       return (int)Cost(MIDAS_L1_COST,      MIDAS_COST_MULT,      lvl);
                case PermUpgrade.BlessedFate: return (int)Cost(BFATE_L1_COST,      BFATE_COST_MULT,      lvl);
                default:                      return (int)Cost(MARATHONER_L1_COST, MARATHONER_COST_MULT, lvl);
            }
        }

        public bool TryBuyPermUpgrade(PermUpgrade u)
        {
            int cost = PermUpgradeCost(u);
            if (state.ascensionPoints < cost) return false;
            state.ascensionPoints -= cost;
            switch (u)
            {
                case PermUpgrade.Midas:       state.permMidas++;       break;
                case PermUpgrade.BlessedFate: state.permBlessedFate++; break;
                default:                      state.permMarathoner++;  break;
            }
            onSave?.Invoke();
            return true;
        }

        static long Cost(int l1, float mult, int ownedLevels)
            => (long)Math.Round(l1 * Math.Pow(mult, ownedLevels));

        // ---- ascension ----

        // Gates (plan §9). The FIRST ascension is the scripted tutorial
        // beat (Fix #1): pure effort, reachable day 1 at any activity
        // level. From n=2 on: J(n)=n jackpots OR C(n)=150+30*(n-2) cards,
        // whichever first — the card path is the low-luck / low-activity
        // equity guarantee (Fix #4). Ascending is always voluntary.
        public int NextAscensionNumber => state.ascensionCount + 1;
        public int GateJackpots        => NextAscensionNumber;
        public int GateCards           => !state.firstAscensionDone
                                          ? FIRST_ASC_CARDS
                                          : 150 + 30 * (NextAscensionNumber - 2);

        public bool AscensionEligible => !state.firstAscensionDone
            ? state.cardsThisRun >= FIRST_ASC_CARDS
            : state.jackpotsThisRun >= GateJackpots || state.cardsThisRun >= GateCards;

        // AP the pending ascension would grant (shown on the Ascend button).
        // First = flat scripted 5. After: jackpots + the effort floor, so a
        // jackpot-dry run still advances the permanent tree.
        public float AscensionApPreview => !state.firstAscensionDone
            ? FIRST_ASC_AP
            : state.jackpotsThisRun + state.cardsThisRun / AP_EFFORT_DIVISOR;

        public bool TryAscend()
        {
            if (!AscensionEligible) return false;
            state.ascensionPoints += AscensionApPreview;
            state.ascensionCount++;
            state.firstAscensionDone = true;
            // The sacrifice: gold + per-run upgrade levels. NEVER touched:
            // banked cards + step accumulator (walked-for effort), AP, perm
            // levels, lifetime counters — the design's "never punish real
            // exercise" rule.
            state.gold = 0;
            state.runFortune = 0; state.runFavor = 0; state.runEndurance = 0;
            state.cardsThisRun = 0; state.jackpotsThisRun = 0;
            onSave?.Invoke();
            return true;
        }
    }
}
