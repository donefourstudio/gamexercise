using System;

namespace Gamex.Core
{
    // One card type in the catalog. Data-driven: every launch card is a
    // row in DeskGame.CATALOG; the scratch framework reads these fields.
    // symP/symPrize are the PRINTED odds (shown on the card's side panel);
    // junk probability = 1 - sum(symP) - trapChance. Best matched symbol
    // pays; revealed traps subtract.
    public class CardDef
    {
        public string id, name;
        public long   cost;
        public int    spots;
        public int    matchNeed;      // 1 = single-spot special (multiplier/whale)
        public float[] symP;
        public long[]  symPrize;
        public float  trapChance;
        public long   trapPenalty;
        public int    hardness;       // foil toughness (Coin upgrade counters; presentation)
        public long   unlockAt;       // earnedThisRun threshold
        public float[] multP;         // QuickCash-style: instant cost-multiplier table
        public float[] multX;
        public float  whaleP;         // GoldenTicket-style: whaleP -> cost * whaleX
        public float  whaleX;
    }

    // Spot codes inside a dealt card.
    public static class Spot
    {
        public const sbyte JUNK = -1;
        public const sbyte TRAP = -2;
        // >= 0 : index into CardDef.symP/symPrize
    }

    public struct DealtCard
    {
        public int     cardIdx;
        public sbyte[] spots;     // pre-rolled; UI reveals progressively (peek-and-bail)
        public bool    rigged;
        public long    instant;   // for matchNeed==1 specials: the pre-rolled payout
    }

    public struct DeskResult
    {
        public long payout;       // gross winnings credited (>= 0)
        public long penalty;      // trap damage subtracted (>= 0)
        public int  matchedSym;   // -1 = none
        public bool bigWin;       // payout >= BIGWIN_X * cost -> PP driver
    }

    // The Desk core (Pivot 3 — docs/casino-mvp-plan.md). Pure C#, RNG
    // injected, no Unity types. Scritchy-faithful loop: steps are the day
    // job (stride rolls, themselves a gamble), coins buy cards with a
    // HOUSE EDGE at Luck 0 that flips profitable as Luck + card levels
    // rise (sucker -> shark). Traps subtract; debt + one capped loan
    // exist; walking income is the only untouchable source.
    // All tables tuned by /tmp sim v3 (July 2026) — playtest-tunable.
    public class DeskGame
    {
        // ---- the day job: stride rolls ----
        public const int  STEPS_PER_ROLL = 100;
        public const long ROLL_SMALL = 3, ROLL_BIG = 15;
        public const float ROLL_BIG_P = 0.10f;             // EV 4.2 / roll

        // ---- luck (money upgrade): removes junk in favour of the
        //      cheapest symbol — fewer blanks, more small matches ----
        public const float LUCK_SHIFT_PER_LVL = 0.006f;
        public const int   LUCK_CAP = 20;
        public const int   LUCK_COST0 = 60;  public const float LUCK_COST_MULT = 1.55f;
        public const int   SIZE_COST0 = 100; public const float SIZE_COST_MULT = 1.7f;  public const int SIZE_CAP = 8;
        public const int   COIN_COST0 = 80;  public const float COIN_COST_MULT = 1.8f;  public const int COIN_CAP = 10;

        // ---- card leveling ----
        public const float CARDLVL_PRIZE = 0.03f;          // +3% prizes per level
        public const int   XP_PER_LEVEL  = 10;             // plays needed = 10 * (level+1)
        public const int   CARDLVL_CAP   = 10;

        // ---- loans + debt ----
        public const long  LOAN_AMOUNT = 500;
        public const float LOAN_REPAY_MULT = 1.5f;
        public const float LOAN_GARNISH = 0.5f;            // of all income until repaid
        public const long  LOAN_TRIGGER_BELOW = 50;        // phone rings under this
        public const long  DEBT_FLOOR = -1000;             // traps can't dig deeper

        // ---- session-1 hook: the 3rd card ever scratched is rigged to a
        //      top-symbol win. Fires once per save. ----
        public const int RIGGED_PLAY_AT = 2;

        // ---- prestige ----
        public const long  PRESTIGE_AT = 400_000;          // earnedThisRun gate
        public const float PP_EFFORT_DIVISOR = 40f;
        public const long  BIGWIN_X = 20;                  // payout >= 20x cost = "big win"

        // Launch catalog #1 — printed odds, sim-v3 tuned.
        // EV @ L0: 90 / 94 / 92 / 89 / 79 / 81 % of cost.
        public static readonly CardDef[] CATALOG =
        {
            new CardDef { id="two_win",   name="Two Win",     cost=10,   spots=3, matchNeed=2, hardness=1, unlockAt=0,
                symP=new[]{0.40f,0.20f,0.10f}, symPrize=new long[]{13,26,60} },
            new CardDef { id="mini",      name="Mini Scratch", cost=100,  spots=6, matchNeed=3, hardness=1, unlockAt=400,
                symP=new[]{0.25f,0.15f,0.10f,0.05f}, symPrize=new long[]{250,500,1200,4000} },
            new CardDef { id="orchard",   name="Sour Orchard", cost=2000, spots=8, matchNeed=3, hardness=2, unlockAt=5_000,
                trapChance=0.12f, trapPenalty=500,
                symP=new[]{0.22f,0.15f,0.10f,0.04f}, symPrize=new long[]{3800,7000,15000,45000} },
            new CardDef { id="quickcash", name="Quick Cash",  cost=10_000, spots=1, matchNeed=1, hardness=2, unlockAt=50_000,
                multP=new[]{0.52f,0.28f,0.13f,0.07f}, multX=new[]{0f,1f,2f,5f} },
            new CardDef { id="blackcat",  name="Black Cat",   cost=200_000, spots=9, matchNeed=3, hardness=3, unlockAt=500_000,
                trapChance=0.15f, trapPenalty=50_000,
                symP=new[]{0.20f,0.13f,0.08f,0.03f}, symPrize=new long[]{400_000,700_000,1_600_000,5_000_000} },
            new CardDef { id="golden",    name="Golden Ticket", cost=1_000_000, spots=1, matchNeed=1, hardness=3, unlockAt=5_000_000,
                whaleP=1f/500f, whaleX=400f },
        };

        public GameState host;                 // unified wallet = host.coins (may go negative)
        public DeskState state => host.desk;
        public Action onSave;
        readonly Random _rng;

        public DeskGame(GameState host, Random rng = null)
        {
            this.host = host ?? new GameState();
            if (this.host.desk == null) this.host.desk = new DeskState();
            _rng = rng ?? new Random();
        }

        // ---- income (the ONLY untouchable source) ----

        public int GrantSteps(int steps)
        {
            if (steps <= 0) return 0;
            long acc = (long)state.stepAccumulator + steps;
            int rolls = (int)(acc / STEPS_PER_ROLL);
            state.stepAccumulator = (int)(acc % STEPS_PER_ROLL);
            state.rollsPending += rolls;
            if (rolls > 0) onSave?.Invoke();
            return rolls;
        }

        // Tear open the paycheck envelope: every pending roll gambles
        // (90% small / 10% big — even the day job is a slot machine).
        public long TearEnvelope()
        {
            long gross = 0;
            for (int i = 0; i < state.rollsPending; i++)
                gross += _rng.NextDouble() < ROLL_BIG_P ? ROLL_BIG : ROLL_SMALL;
            state.rollsPending = 0;
            Credit(gross);
            onSave?.Invoke();
            return gross;
        }

        // All income funnels here: counts toward the unlock bar, then the
        // loan shark takes their garnish, then the wallet gets the rest.
        void Credit(long amount)
        {
            if (amount <= 0) return;
            state.earnedThisRun += amount;
            if (state.loanOwed > 0)
            {
                long g = Math.Min(state.loanOwed, (long)(amount * LOAN_GARNISH));
                state.loanOwed -= g;
                amount -= g;
            }
            host.coins += amount;
        }

        // ---- catalog / buying ----

        public bool Unlocked(int i) => state.earnedThisRun >= CATALOG[i].unlockAt;
        public bool CanBuy(int i)   => Unlocked(i) && host.coins >= CATALOG[i].cost;

        public bool TryBuyCard(int i)
        {
            if (!CanBuy(i)) return false;
            host.coins -= CATALOG[i].cost;
            state.cardsOwned[i]++;
            onSave?.Invoke();
            return true;
        }

        // ---- dealing + resolving (supports peek-and-bail) ----

        public DealtCard DealCard(int i)
        {
            if (state.cardsOwned[i] <= 0) return default;
            state.cardsOwned[i]--;
            var c = CATALOG[i];
            bool rig = state.lifetimePlays == RIGGED_PLAY_AT && state.lifetimeBigWins == 0;
            state.lifetimePlays++;
            state.playsThisRun++;

            var d = new DealtCard { cardIdx = i, spots = new sbyte[c.spots], rigged = rig };
            if (c.matchNeed == 1)
            {
                // instant specials pre-roll their payout
                float pm = 1f + CARDLVL_PRIZE * state.cardLevel[i];
                if (c.whaleX > 0)
                    d.instant = (rig || _rng.NextDouble() < c.whaleP)
                        ? (long)Math.Round(c.cost * c.whaleX * pm) : 0;
                else
                {
                    if (rig) d.instant = (long)Math.Round(c.cost * c.multX[c.multX.Length - 1] * pm);
                    else
                    {
                        double r = _rng.NextDouble(); float acc = 0f; d.instant = 0;
                        for (int k = 0; k < c.multP.Length; k++)
                        {
                            acc += c.multP[k];
                            if (r < acc) { d.instant = (long)Math.Round(c.cost * c.multX[k] * pm); break; }
                        }
                    }
                }
                d.spots[0] = 0;
                onSave?.Invoke();
                return d;
            }

            float junkShift = Math.Min(LUCK_SHIFT_PER_LVL * Math.Min(state.upLuck, LUCK_CAP),
                                       Math.Max(JunkP(c) - 0.05f, 0f));
            for (int s = 0; s < c.spots; s++)
            {
                double r = _rng.NextDouble();
                if (r < c.trapChance) { d.spots[s] = Spot.TRAP; continue; }
                r -= c.trapChance;
                sbyte hit = Spot.JUNK;
                for (int k = 0; k < c.symP.Length; k++)
                {
                    float p = c.symP[k] + (k == 0 ? junkShift : 0f);
                    if (r < p) { hit = (sbyte)k; break; }
                    r -= p;
                }
                d.spots[s] = hit;
            }
            if (rig)
            {
                // guarantee a top-symbol match for the session-1 peak
                int top = c.symP.Length - 1;
                for (int s = 0; s < c.matchNeed; s++) d.spots[s] = (sbyte)top;
            }
            onSave?.Invoke();
            return d;
        }

        public static float JunkP(CardDef c)
        {
            float s = c.trapChance;
            if (c.symP != null) foreach (var p in c.symP) s += p;
            return Math.Max(0f, 1f - s);
        }

        // Resolve with a reveal mask: unrevealed traps DON'T hurt and
        // unrevealed symbols don't count — abandoning early is a real,
        // Scritchy-faithful decision. Pass all-true for a full scratch.
        public DeskResult ResolveCard(DealtCard d, bool[] revealed)
        {
            var c = CATALOG[d.cardIdx];
            var res = new DeskResult { matchedSym = -1 };
            float pm = 1f + CARDLVL_PRIZE * state.cardLevel[d.cardIdx];

            if (c.matchNeed == 1)
            {
                res.payout = d.instant;
            }
            else
            {
                var counts = new int[c.symP.Length];
                int traps = 0;
                for (int s = 0; s < d.spots.Length; s++)
                {
                    if (revealed == null || s >= revealed.Length || !revealed[s]) continue;
                    if (d.spots[s] == Spot.TRAP) traps++;
                    else if (d.spots[s] >= 0) counts[d.spots[s]]++;
                }
                long best = 0;
                for (int k = 0; k < counts.Length; k++)
                    if (counts[k] >= c.matchNeed && c.symPrize[k] > best)
                    { best = c.symPrize[k]; res.matchedSym = k; }
                res.payout  = (long)Math.Round(best * pm);
                res.penalty = traps * c.trapPenalty;
            }

            if (res.payout > 0) Credit(res.payout);
            if (res.penalty > 0) host.coins = Math.Max(host.coins - res.penalty, DEBT_FLOOR);
            res.bigWin = res.payout >= c.cost * BIGWIN_X;
            if (res.bigWin) { state.bigWinsThisRun++; state.lifetimeBigWins++; }

            // card XP -> levels (+3% prizes each)
            int idx = d.cardIdx;
            if (state.cardLevel[idx] < CARDLVL_CAP)
            {
                state.cardXp[idx]++;
                if (state.cardXp[idx] >= XP_PER_LEVEL * (state.cardLevel[idx] + 1))
                { state.cardXp[idx] = 0; state.cardLevel[idx]++; }
            }
            onSave?.Invoke();
            return res;
        }

        // Convenience: deal + fully reveal (bots, tests, Robot Arm later).
        public DeskResult ScratchAll(int i)
        {
            var d = DealCard(i);
            if (d.spots == null) return default;
            var all = new bool[d.spots.Length];
            for (int s = 0; s < all.Length; s++) all[s] = true;
            return ResolveCard(d, all);
        }

        // ---- money upgrades ----

        public enum Upgrade { Luck, Size, Coin }

        public int UpgradeLevel(Upgrade u)
            => u == Upgrade.Luck ? state.upLuck : u == Upgrade.Size ? state.upSize : state.upCoin;

        public int UpgradeCap(Upgrade u)
            => u == Upgrade.Luck ? LUCK_CAP : u == Upgrade.Size ? SIZE_CAP : COIN_CAP;

        public long UpgradeCost(Upgrade u)
        {
            int l = UpgradeLevel(u);
            switch (u)
            {
                case Upgrade.Luck: return (long)Math.Round(LUCK_COST0 * Math.Pow(LUCK_COST_MULT, l));
                case Upgrade.Size: return (long)Math.Round(SIZE_COST0 * Math.Pow(SIZE_COST_MULT, l));
                default:           return (long)Math.Round(COIN_COST0 * Math.Pow(COIN_COST_MULT, l));
            }
        }

        public bool TryBuyUpgrade(Upgrade u)
        {
            if (UpgradeLevel(u) >= UpgradeCap(u)) return false;
            long cost = UpgradeCost(u);
            if (host.coins < cost) return false;
            host.coins -= cost;
            if      (u == Upgrade.Luck) state.upLuck++;
            else if (u == Upgrade.Size) state.upSize++;
            else                        state.upCoin++;
            onSave?.Invoke();
            return true;
        }

        // ---- the loan phone ----

        public bool LoanAvailable => state.loanOwed <= 0 && host.coins < LOAN_TRIGGER_BELOW;

        public bool TakeLoan()
        {
            if (!LoanAvailable) return false;
            host.coins += LOAN_AMOUNT;
            state.loanOwed = (long)(LOAN_AMOUNT * LOAN_REPAY_MULT);
            onSave?.Invoke();
            return true;
        }

        // ---- prestige ----

        public bool PrestigeEligible => state.earnedThisRun >= PRESTIGE_AT
                                        && state.loanOwed <= 0 && host.coins >= 0;

        public float PrestigePpPreview => state.bigWinsThisRun + state.playsThisRun / PP_EFFORT_DIVISOR;

        public bool TryPrestige()
        {
            if (!PrestigeEligible) return false;
            state.prestigePoints += PrestigePpPreview;
            state.prestigeCount++;
            // The sacrifice: wallet, upgrades, card levels/XP, unscratched
            // cards, and the unlock bar. NEVER touched: pending stride
            // rolls + accumulator (walked-for), PP, lifetime counters.
            host.coins = 0;
            state.upLuck = 0; state.upSize = 0; state.upCoin = 0;
            for (int i = 0; i < state.cardXp.Length; i++)
            { state.cardXp[i] = 0; state.cardLevel[i] = 0; state.cardsOwned[i] = 0; }
            state.earnedThisRun = 0;
            state.playsThisRun = 0;
            state.bigWinsThisRun = 0;
            onSave?.Invoke();
            return true;
        }
    }
}
