# The Desk — Scritchy-faithful recraft (Pivot #3)

**Status:** Spec ✅ (`9c2eaf1`) · R2-1 ✅ (`20a1377` — DeskGame core, sim-v3 tables, ~40 assertions) · R2-2 ✅ (the Desk scene replaces the seven rooms: wallet w/ debt-red, earnings unlock bar, paycheck envelope, catalog + upgrade rows, basic instant-play path; Hud.Casino.cs deleted, single AppPhase.Desk) · **R2-3 next** (the foil mat: real per-spot scratching, printed odds panels, peek-and-bail, trash box).
**Predecessor:** the seven-room "Casino" (P0–P5b, through `f2a8f11`) is retired — playtest verdict: no tension, no dopamine. Root cause: the pure-upside guardrail removed all risk, rooms-as-menus removed the *place*, and pre-rolled reveals removed decisions.
**Legal line (signed off):** mechanics-faithful clone of Scritchy Scratchy with our own art, name, theme + the steps twist. No copied assets/names/trade dress.

## The loop (decoded from footage)

One screen: **a desk.** Catalog sidebar (left, Tickets|Gadgets tabs) sells card types with prices + per-card LEVELS. Money-upgrades sidebar (right): Scratch Luck / Scratch Size / Coin material (Strength vs card Hardness). Top: wallet + **lifetime-earnings bar** that unlocks new card types (golden desk-burst celebration). Cards are physical: bought → land/pile on the desk → drag to the mat → scratch spots → match-K per the card's **printed odds table** → win/lose → trash box. **Everything is a gamble, including the day job** (Scritchy's plate-wash pays 95%→$2 / 5%→$25).

## Our inversion (signed off)

- **Steps are the day job.** Every ~100 steps = one **stride roll** (≈90% → 2c, 10% → 12c; EV ~3c — the faucet itself is a slot machine). Accrued rolls present as a **paycheck envelope on the desk — tear it open (scratch gesture) to collect.** Walking income is the ONLY untouchable money source.
- **Coins buy cards. Losses are real.** Cards are net-negative at Luck 0 / Card Lv 0 (~88–93% EV — a house edge), and flip profitable as Luck + card levels rise: the arc is *sucker → shark*. Trap spots (worms, black cats) subtract. Broke? Your legs are the bailout.
- **Debt + loans included (capped, comedic):** balance may go negative to a floor of ~2 days of walking income; the desk phone rings when broke; one loan at a time; repay 1.5× garnished from income; terms printed in full ("APR: yes.").

## Launch catalog (#1) — structures locked, numbers sim-tuned in R2-1

| Card | Cost | Rule | Character |
|---|---|---|---|
| Two Win | 10 | 3 spots, match 2 | starter, fast burn |
| Mini Scratch | 100 | 6 spots, match 3 | the classic |
| Apple Tree | 2,000 | 8 spots, match 3, **worm spots −value** | first trap; peek-and-bail matters |
| Quick Cash | 10,000 | 1 spot, instant 0×/1×/2×/5× | pure adrenaline |
| Lucky Cat | 200,000 | 9 spots, match 3, black-cat traps | high-roller trap |
| Golden Ticket | 1,000,000 | 1 spot, 1-in-500 → 100× | white whale (odds printed) |

Card XP per play → levels (+% prizes); Hardness rises with tier (Coin upgrade counters); unlock thresholds on the earnings bar (≈500 / 10k / 100k / 1M / 10M lifetime-earned); Catalog #2 = post-launch content. Every card shows its **odds panel** while on the mat.

**Gadgets (launch):** Robot Arm (auto-scratch — earned), Lucky Cat statue (every 8 plays → bonus), the Loan Phone, cosmetics (fan, lava lamp). Gadgets may automate *scratching*, never step income.
**Prestige:** PP kept; tree upgraded to a **node tree with prerequisites** + Challenges tab (post-launch OK). "Jackpots" for PP = wins ≥ 20× card cost.
**Quests:** re-target from tickets (dead) to **coin bonuses** (spikes on the step income). Trophy-cosmetics plan unchanged. Monetization unchanged: free + $5.99 "Unlock Everything" (full catalog + gadget slots).

## What survives / what dies

**Survives:** casinoEnabled flag + save/migration machinery, HealthKit plumbing, ScratchFoil (gains Hardness, brush size, per-spot mode), coin burst/juice helpers, prestige math core, art pipeline (Caz packs + CC BY 4.0 credit), LogicTest harness + sim methodology, RPG side untouched.
**Dies:** tickets, the lobby + seven rooms (desk replaces them; rooms' logic recycled into CardDefs), pure-upside guardrail.

## Rebuild phases

| Phase | Scope | Verify |
|---|---|---|
| **R2-1 Core + sim** | `DeskGame` core: stride-roll faucet, data-driven `CardDef` table, buy/scratch/resolve with real losses + trap spots, card XP/levels, money upgrades, earnings-bar unlocks, debt floor + loan, prestige hookup. Python 30-day sim (3 activity profiles: day-1 hook, no death-spiral, unlock pacing, sucker→shark arc) + LogicTest EV per card | sim + [LOGIC OK] |
| **R2-2 The desk** | Single desk scene replacing lobby/rooms: catalog + upgrades sidebars, mat, trash, wallet + earnings bar, **paycheck envelope** (step income collection moment) | Editor playtest |
| **R2-3 The cards** | All 6 launch cards on one data-driven spot-scratch framework (traps, multiplier spot, whale), printed odds panels, Hardness, XP/levels, unlock celebrations | playtest + per-card EV tests |
| **R2-4 Gadgets + loans + tree** | Robot Arm, Lucky Cat, Loan Phone + debt UI, prestige node tree | playtest |
| **R2-5 Juice** | Card toss physics, +% scratch ticks, debt-red panic, unlock bursts, real scratch audio, polish | playtest |
| **R2-6 Ship prep** | (old P6) device/HealthKit, tuning from real walks, shop→trophies, StoreKit, credits, TestFlight | device + TestFlight |

## Reference footage index (frames in /tmp/scritchy, videos on Desktop)

Desk anatomy + catalog v1@10s/225s · card odds panel v2@165s (Mini Scratch: 30/30/30/10% → $100/200/500/1000, Hardness 1) · Day Job plate v2@20s (95%→$2 / 5%→$25) · debt −511,188 + loan phone v1@280s · unlock burst v1@140s · prestige node tree v2@85s · gadget grid v2@140s · title v2@0s.
