using System;

namespace Gamex.Core
{
    // Play outcome tiers, ordered worst -> best. Classic scratch-lottery
    // symbols: a win shows three of a kind; Seven = the 777 jackpot.
    public enum CasinoTier { Bust = 0, Cherry = 1, Bell = 2, Bar = 3, Seven = 4 }

    public struct PlayResult
    {
        public CasinoTier tier;
        public long coins;    // payout after Payout/Golden Touch multipliers
        public bool jackpot;  // tier == Seven (777)
        public bool rigged;   // the scripted first-session jackpot (Fix #1)
    }

    // Casino core logic (Phase R — docs/casino-mvp-plan.md). Pure C#, no
    // Unity types (same rule as GamexGame): the presentation layer reads
    // state every frame and routes player intents back through this class.
    // Owns every economy rule of the casino — the steps -> tickets faucet,
    // the pre-rolled payout table, run upgrades, prestige gates and PP
    // math. Every tunable dial is a const up top.
    //
    // WALLET: coins are UNIFIED with the RPG — this class reads/writes
    // host.coins (the same wallet quests pay into and the shop spends).
    // CasinoState (host.casino) holds only casino-specific progression.
    //
    // RNG is injected so LogicTest can seed it deterministically. Outcomes
    // are PRE-ROLLED at play time — the scratch cells / slot reels the UI
    // shows are pure theatre over the rolled tier (design rule: set the
    // payout distribution first, dress it as symbols second). One ticket =
    // one play, scratch or (P4) slots alike.
    public class CasinoGame
    {
        // ---- faucet ----
        public const int STEPS_PER_TICKET_BASE  = 100;   // 1 ticket / 100 steps
        public const int STEPS_PER_TICKET_FLOOR = 44;
        public const int TICKET_CAP_DEFAULT     = 300;
        // Over-cap steps convert straight to coins so walking is never
        // wasted. Deliberately below the ~72c play EV: draining the backlog
        // by playing always beats idling at the cap.
        public const int OVERFLOW_COINS_PER_TICKET = 50;
        // Marathoner (perm): running earns +10%/level ON TOP of the steps
        // HealthKit already counted. HK gives no per-activity step split,
        // so run-steps are estimated from workout seconds at ~2.8 steps/s
        // (~168 spm running cadence).
        public const float RUN_STEPS_PER_SECOND       = 2.8f;
        public const float MARATHONER_BONUS_PER_LEVEL = 0.10f;

        // ---- payout table (shared by scratchers + future slots) ----
        // Locked distribution (plan doc): EV = 72.0 coins at luck 0.
        //   Bust 45% -> 5   | Cherry 30% -> 25 | Bell 17% -> 75
        //   Bar 6.5% -> 300 | Seven 1.5% -> 2000 (the 777 jackpot)
        // Luck (runLuck + permLoadedDice) shifts it: each level moves
        // +0.3% into Seven and +1.0% from Bust into Cherry (busts become
        // small wins) -> EV grows ~+6.2/level.
        public const float P_SEVEN_BASE   = 0.015f;
        public const float P_BAR          = 0.065f;
        public const float P_BELL         = 0.17f;
        public const float P_CHERRY_BASE  = 0.30f;
        public const float SEVEN_PER_LUCK  = 0.003f;
        public const float CHERRY_PER_LUCK = 0.010f;
        public const int   LUCK_CAP        = 25;   // keeps the bust bucket >= ~12%
        public const int COINS_BUST = 5, COINS_CHERRY = 25, COINS_BELL = 75,
                         COINS_BAR = 300, COINS_SEVEN = 2000;

        // Fix #1 (onboarding): the 3rd ticket ever played is rigged to be
        // the player's first jackpot, so the 777 moment lands in session
        // one. Fires once per save, ever.
        public const int RIGGED_JACKPOT_AT = 2;   // 0-based lifetime index

        // ---- P5b game tables ----
        // Every game consumes 1 ticket and is EV-balanced near the classic
        // 72 so no game strictly dominates — what differs is VARIANCE.
        public enum TableKind { Classic, GoldRush, Mega }
        // Gold Rush — the high-variance dig: mostly dust, rare motherlode.
        // EV = .55*5 + .25*30 + .12*150 + .06*400 + .02*1000 = 72.25.
        public const float GR_P_SEVEN  = 0.02f;    // motherlode (counts as jackpot)
        public const float GR_P_BAR    = 0.06f;
        public const float GR_P_BELL   = 0.12f;
        public const float GR_P_CHERRY = 0.25f;
        public const int GR_COINS_BUST = 5, GR_COINS_CHERRY = 30, GR_COINS_BELL = 150,
                         GR_COINS_BAR = 400, GR_COINS_SEVEN = 1000;
        // MEGA JACKPOT — the white whale. 1-in-2,000 pays 100,000; every
        // other play is a deadpan 20. EV ≈ 69.99, variance is the product.
        // Odds are shown to the player (honest gambling).
        public const float MEGA_P_SEVEN   = 0.0005f;
        public const int   MEGA_COINS_WIN = 100000, MEGA_COINS_BUST = 20;

        // The Ladder — press-your-luck. Pot starts at the classic EV and
        // each rung is a fair double-or-fall (0.5 * 2 = 1), so EV = 72 for
        // EVERY strategy — the player is purely choosing their variance.
        // Banking from rung 6+ counts as a jackpot for PP.
        public const long  LADDER_BASE_POT    = 72;
        public const float LADDER_SURVIVE     = 0.5f;
        public const int   LADDER_MAX_RUNG    = 8;    // cap: 72 * 256 = 18,432
        public const int   LADDER_JACKPOT_RUNG = 6;

        // High Stakes — an opt-in coin wager riding a classic roll. Bust
        // (45%) eats the wager; wins return wager * multiplier. Return EV =
        // .30*1 + .17*2 + .065*4 + .015*10 = 1.05x — a deliberate ~+5%
        // player edge so the thrill never quietly drains the wallet
        // (mission rule: risk is opt-in, the house is friendly). Payout
        // upgrades deliberately do NOT multiply wagers.
        public const int HS_MULT_CHERRY = 1, HS_MULT_BELL = 2, HS_MULT_BAR = 4, HS_MULT_SEVEN = 10;

        // ---- run upgrades (coins; reset on prestige) ----
        // Flattened curve for the ~2-3 day prestige cadence (plan doc).
        public const int   PAYOUT_L1_COST = 300;  public const float PAYOUT_COST_MULT = 1.4f;
        public const float PAYOUT_COINS_PER_LEVEL = 0.08f;
        public const int   LUCKUP_L1_COST = 400;  public const float LUCKUP_COST_MULT = 1.5f;
        public const int   STRIDE_L1_COST = 600;  public const float STRIDE_COST_MULT = 1.5f;
        public const int   STRIDE_STEPS_PER_LEVEL = 8;
        public const int   STRIDE_MAX_LEVEL       = 7;   // 100 - 7*8 = 44 = floor

        // ---- permanent PP upgrades (never reset; MVP trio) ----
        public const int   GTOUCH_L1_COST = 2;      public const float GTOUCH_COST_MULT = 1.5f;
        public const float GTOUCH_COINS_PER_LEVEL = 0.05f;
        public const int   LDICE_L1_COST = 3;       public const float LDICE_COST_MULT = 1.6f;
        public const int   MARATHONER_L1_COST = 4;  public const float MARATHONER_COST_MULT = 1.6f;
        // One-time (P4). Priced at ~2 prestiges of PP so the hand-scratch
        // tedium has real time to bite first — automation is EARNED.
        public const int   AUTOSCR_COST = 8;

        // ---- prestige ----
        public const int   FIRST_PRESTIGE_PLAYS = 25;   // scripted tutorial gate (pure effort, day-1 reachable)
        public const float FIRST_PRESTIGE_PP    = 5f;   // guaranteed starter PP
        public const float PP_EFFORT_DIVISOR    = 40f;  // effort floor: + plays/40 (Fix #4)

        // The whole player save — the unified coin wallet lives here
        // (host.coins). Re-pointed by GameRunner on save reset.
        public GameState host;
        public CasinoState state => host.casino;
        public Action onSave;
        readonly Random _rng;

        public CasinoGame(GameState host, Random rng = null)
        {
            this.host = host ?? new GameState();
            if (this.host.casino == null) this.host.casino = new CasinoState();
            _rng = rng ?? new Random();
        }

        // ---- derived dials (also what the UI displays) ----
        public int   Luck            => Math.Min(state.runLuck + state.permLoadedDice, LUCK_CAP);
        public int   StepsPerTicket  => Math.Max(STEPS_PER_TICKET_FLOOR,
                                         STEPS_PER_TICKET_BASE - STRIDE_STEPS_PER_LEVEL * state.runStride);
        public int   TicketCap       => state.ticketCap > 0 ? state.ticketCap : TICKET_CAP_DEFAULT;
        public float CoinsMultiplier => 1f + PAYOUT_COINS_PER_LEVEL * state.runPayout
                                           + GTOUCH_COINS_PER_LEVEL * state.permGoldenTouch;
        public float JackpotChance   => P_SEVEN_BASE + SEVEN_PER_LUCK * Luck;
        public bool  CanPlay         => state.ticketsBanked > 0;

        // ---- faucet ----

        // Single entry point for real-world activity. `steps` = new steps
        // this tick (HealthKit delta or the Editor debug key); `runSeconds`
        // = running-workout seconds within the tick. Run STEPS are already
        // inside `steps` (the pedometer counts them like any other step) —
        // runSeconds only feeds the Marathoner bonus credit. Returns
        // tickets granted so the UI can toast.
        public int GrantActivity(int steps, int runSeconds)
        {
            if (steps < 0) steps = 0;
            if (runSeconds < 0) runSeconds = 0;

            int bonus = (int)Math.Round(runSeconds * RUN_STEPS_PER_SECOND
                        * MARATHONER_BONUS_PER_LEVEL * state.permMarathoner);
            long acc = (long)state.stepAccumulator + steps + bonus;

            int granted = 0;
            int spt = StepsPerTicket;
            while (acc >= spt)
            {
                acc -= spt;
                if (state.ticketsBanked < TicketCap) { state.ticketsBanked++; granted++; }
                else host.coins += OVERFLOW_COINS_PER_TICKET;   // at cap: walking never wasted
            }
            state.stepAccumulator = (int)acc;
            if (steps != 0 || runSeconds != 0) onSave?.Invoke();
            return granted;
        }

        // ---- the play (shared by scratchers / slots / find-the-cash;
        //      Gold Rush + Mega pass their own table) ----

        public PlayResult Play() => PlayTable(TableKind.Classic);

        public PlayResult PlayTable(TableKind kind)
        {
            if (state.ticketsBanked <= 0) return default;   // UI gates on CanPlay

            state.ticketsBanked--;
            bool rig = state.lifetimePlays == RIGGED_JACKPOT_AT
                       && state.lifetimeJackpots == 0;
            state.lifetimePlays++;
            state.playsThisRun++;

            CasinoTier tier = rig ? CasinoTier.Seven : RollTier(kind);
            long coins = (long)Math.Round(BaseCoins(kind, tier) * CoinsMultiplier);
            host.coins += coins;
            if (tier == CasinoTier.Seven)
            {
                state.jackpotsThisRun++;
                state.lifetimeJackpots++;
            }
            onSave?.Invoke();
            return new PlayResult { tier = tier, coins = coins, jackpot = tier == CasinoTier.Seven, rigged = rig };
        }

        CasinoTier RollTier(TableKind kind)
        {
            double r = _rng.NextDouble();
            double pSeven, pBar, pBell, pCherry;
            switch (kind)
            {
                case TableKind.GoldRush:
                    // Luck shifts Gold Rush the same way: more motherlodes,
                    // dust becomes small finds.
                    pSeven  = GR_P_SEVEN  + SEVEN_PER_LUCK  * Luck;
                    pBar    = GR_P_BAR;
                    pBell   = GR_P_BELL;
                    pCherry = GR_P_CHERRY + CHERRY_PER_LUCK * Luck;
                    break;
                case TableKind.Mega:
                    // Fixed odds, shown to the player. Luck doesn't move
                    // the white whale — 1 in 2,000, no fine print.
                    pSeven = MEGA_P_SEVEN; pBar = 0; pBell = 0; pCherry = 0;
                    break;
                default:
                    pSeven  = P_SEVEN_BASE  + SEVEN_PER_LUCK  * Luck;
                    pBar    = P_BAR;
                    pBell   = P_BELL;
                    pCherry = P_CHERRY_BASE + CHERRY_PER_LUCK * Luck;
                    break;
            }
            if ((r -= pSeven)  < 0) return CasinoTier.Seven;
            if ((r -= pBar)    < 0) return CasinoTier.Bar;
            if ((r -= pBell)   < 0) return CasinoTier.Bell;
            if ((r -= pCherry) < 0) return CasinoTier.Cherry;
            return CasinoTier.Bust;
        }

        static long BaseCoins(TableKind kind, CasinoTier t)
        {
            switch (kind)
            {
                case TableKind.GoldRush:
                    switch (t)
                    {
                        case CasinoTier.Seven:  return GR_COINS_SEVEN;
                        case CasinoTier.Bar:    return GR_COINS_BAR;
                        case CasinoTier.Bell:   return GR_COINS_BELL;
                        case CasinoTier.Cherry: return GR_COINS_CHERRY;
                        default:                return GR_COINS_BUST;
                    }
                case TableKind.Mega:
                    return t == CasinoTier.Seven ? MEGA_COINS_WIN : MEGA_COINS_BUST;
                default:
                    switch (t)
                    {
                        case CasinoTier.Seven:  return COINS_SEVEN;
                        case CasinoTier.Bar:    return COINS_BAR;
                        case CasinoTier.Bell:   return COINS_BELL;
                        case CasinoTier.Cherry: return COINS_CHERRY;
                        default:                return COINS_BUST;
                    }
            }
        }

        // ---- The Ladder (P5b) — press-your-luck, state persisted ----

        public bool LadderActive => state.ladderActive;
        public long LadderPot    => state.ladderPot;
        public int  LadderRung   => state.ladderRung;

        public bool TryLadderStart()
        {
            if (state.ladderActive || state.ticketsBanked <= 0) return false;
            state.ticketsBanked--;
            state.lifetimePlays++;
            state.playsThisRun++;
            state.ladderActive = true;
            state.ladderPot    = LADDER_BASE_POT;
            state.ladderRung   = 0;
            onSave?.Invoke();
            return true;
        }

        // One rung up: fair double-or-fall. Returns true if the climb
        // survived; on a fall the pot is gone (the wager was only ever
        // this ticket's own winnings — the wallet is never touched).
        public bool LadderClimb()
        {
            if (!state.ladderActive || state.ladderRung >= LADDER_MAX_RUNG) return false;
            bool survive = _rng.NextDouble() < LADDER_SURVIVE;
            if (survive)
            {
                state.ladderPot *= 2;
                state.ladderRung++;
            }
            else
            {
                state.ladderPot    = 0;
                state.ladderActive = false;
            }
            onSave?.Invoke();
            return survive;
        }

        public long LadderBank()
        {
            if (!state.ladderActive) return 0;
            long pot = state.ladderPot;
            host.coins += pot;
            if (state.ladderRung >= LADDER_JACKPOT_RUNG)
            {
                state.jackpotsThisRun++;
                state.lifetimeJackpots++;
            }
            state.ladderActive = false;
            state.ladderPot    = 0;
            state.ladderRung   = 0;
            onSave?.Invoke();
            return pot;
        }

        // ---- High Stakes (P5b) — opt-in coin wager on a classic roll ----

        // Consumes 1 ticket + escrows the wager; returns wager * tier
        // multiplier (0 on bust). PlayResult.coins = the amount RETURNED
        // (UI derives net). Wagers ignore CoinsMultiplier by design.
        public PlayResult PlayHighStakes(long wager)
        {
            if (state.ticketsBanked <= 0 || wager <= 0 || host.coins < wager) return default;

            state.ticketsBanked--;
            host.coins -= wager;
            bool rig = state.lifetimePlays == RIGGED_JACKPOT_AT
                       && state.lifetimeJackpots == 0;
            state.lifetimePlays++;
            state.playsThisRun++;

            CasinoTier tier = rig ? CasinoTier.Seven : RollTier(TableKind.Classic);
            long mult;
            switch (tier)
            {
                case CasinoTier.Seven:  mult = HS_MULT_SEVEN;  break;
                case CasinoTier.Bar:    mult = HS_MULT_BAR;    break;
                case CasinoTier.Bell:   mult = HS_MULT_BELL;   break;
                case CasinoTier.Cherry: mult = HS_MULT_CHERRY; break;
                default:                mult = 0;              break;
            }
            long returned = wager * mult;
            host.coins += returned;
            if (tier == CasinoTier.Seven)
            {
                state.jackpotsThisRun++;
                state.lifetimeJackpots++;
            }
            onSave?.Invoke();
            return new PlayResult { tier = tier, coins = returned, jackpot = tier == CasinoTier.Seven, rigged = rig };
        }

        // ---- run upgrades (coins) ----

        public enum RunUpgrade { Payout, Luck, Stride }

        public int RunUpgradeLevel(RunUpgrade u)
        {
            switch (u)
            {
                case RunUpgrade.Payout: return state.runPayout;
                case RunUpgrade.Luck:   return state.runLuck;
                default:                return state.runStride;
            }
        }

        // Cost of the NEXT level (base * mult^ownedLevels).
        public long RunUpgradeCost(RunUpgrade u)
        {
            int lvl = RunUpgradeLevel(u);
            switch (u)
            {
                case RunUpgrade.Payout: return Cost(PAYOUT_L1_COST, PAYOUT_COST_MULT, lvl);
                case RunUpgrade.Luck:   return Cost(LUCKUP_L1_COST, LUCKUP_COST_MULT, lvl);
                default:                return Cost(STRIDE_L1_COST, STRIDE_COST_MULT, lvl);
            }
        }

        public bool TryBuyRunUpgrade(RunUpgrade u)
        {
            if (u == RunUpgrade.Stride && state.runStride >= STRIDE_MAX_LEVEL) return false;
            long cost = RunUpgradeCost(u);
            if (host.coins < cost) return false;
            host.coins -= cost;
            switch (u)
            {
                case RunUpgrade.Payout: state.runPayout++; break;
                case RunUpgrade.Luck:   state.runLuck++;   break;
                default:                state.runStride++; break;
            }
            onSave?.Invoke();
            return true;
        }

        // ---- permanent PP upgrades ----

        public enum PermUpgrade { GoldenTouch, LoadedDice, Marathoner, AutoScratcher }

        public bool AutoScratcherOwned => state.permAutoScratcher > 0;

        public int PermUpgradeLevel(PermUpgrade u)
        {
            switch (u)
            {
                case PermUpgrade.GoldenTouch:   return state.permGoldenTouch;
                case PermUpgrade.LoadedDice:    return state.permLoadedDice;
                case PermUpgrade.AutoScratcher: return state.permAutoScratcher;
                default:                        return state.permMarathoner;
            }
        }

        public int PermUpgradeCost(PermUpgrade u)
        {
            int lvl = PermUpgradeLevel(u);
            switch (u)
            {
                case PermUpgrade.GoldenTouch:   return (int)Cost(GTOUCH_L1_COST,     GTOUCH_COST_MULT,     lvl);
                case PermUpgrade.LoadedDice:    return (int)Cost(LDICE_L1_COST,      LDICE_COST_MULT,      lvl);
                case PermUpgrade.AutoScratcher: return AUTOSCR_COST;   // flat, one-time
                default:                        return (int)Cost(MARATHONER_L1_COST, MARATHONER_COST_MULT, lvl);
            }
        }

        public bool TryBuyPermUpgrade(PermUpgrade u)
        {
            if (u == PermUpgrade.AutoScratcher && state.permAutoScratcher >= 1) return false;   // one-time
            int cost = PermUpgradeCost(u);
            if (state.prestigePoints < cost) return false;
            state.prestigePoints -= cost;
            switch (u)
            {
                case PermUpgrade.GoldenTouch:   state.permGoldenTouch++;   break;
                case PermUpgrade.LoadedDice:    state.permLoadedDice++;    break;
                case PermUpgrade.AutoScratcher: state.permAutoScratcher++; break;
                default:                        state.permMarathoner++;    break;
            }
            onSave?.Invoke();
            return true;
        }

        static long Cost(int l1, float mult, int ownedLevels)
            => (long)Math.Round(l1 * Math.Pow(mult, ownedLevels));

        // ---- prestige ----

        // Gates (plan doc). The FIRST prestige is the scripted tutorial
        // beat (Fix #1): pure effort, reachable day 1 at any activity
        // level. From n=2 on: J(n)=n jackpots OR C(n)=150+30*(n-2) plays,
        // whichever first — the plays path is the low-luck / low-activity
        // equity guarantee (Fix #4). Prestiging is always voluntary.
        public int NextPrestigeNumber => state.prestigeCount + 1;
        public int GateJackpots       => NextPrestigeNumber;
        public int GatePlays          => !state.firstPrestigeDone
                                         ? FIRST_PRESTIGE_PLAYS
                                         : 150 + 30 * (NextPrestigeNumber - 2);

        public bool PrestigeEligible => !state.firstPrestigeDone
            ? state.playsThisRun >= FIRST_PRESTIGE_PLAYS
            : state.jackpotsThisRun >= GateJackpots || state.playsThisRun >= GatePlays;

        // PP the pending prestige would grant (shown on the button).
        // First = flat scripted 5. After: jackpots + the effort floor, so a
        // jackpot-dry run still advances the permanent tree.
        public float PrestigePpPreview => !state.firstPrestigeDone
            ? FIRST_PRESTIGE_PP
            : state.jackpotsThisRun + state.playsThisRun / PP_EFFORT_DIVISOR;

        public bool TryPrestige()
        {
            if (!PrestigeEligible) return false;
            state.prestigePoints += PrestigePpPreview;
            state.prestigeCount++;
            state.firstPrestigeDone = true;
            // The sacrifice: the unified coin balance + run upgrade levels.
            // NEVER touched: banked tickets + step accumulator (walked-for
            // effort), PP, perm levels, lifetime counters, trophies — the
            // design's "never punish real exercise" rule.
            host.coins = 0;
            state.runPayout = 0; state.runLuck = 0; state.runStride = 0;
            state.playsThisRun = 0; state.jackpotsThisRun = 0;
            onSave?.Invoke();
            return true;
        }
    }
}
