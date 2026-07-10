# The Casino — implementation plan

**Status:** Phases 0–2 ✅ (`64e9a6c`, `2a97408`, `9e1da54`) · Phase R ✅ (Casino pivot `71fa885`; quests→tickets `a5dc053`) · Phase 3 ✅ (drag-to-scratch + 777 fanfare + Prestige ceremony, `0ab223b`) · Phase 4 ✅ (SLOTS room + Auto Scratcher 8-PP one-time unlock) · **P5 next** (device/HealthKit + tuning + shop→trophies + StoreKit).
**Monetization (locked):** free download + one-time **$5.99** non-consumable **"Unlock Everything"** (free slice through the first prestige + a few days). Never sold: tickets, coins, PP, steps. No ads/subs/consumables.

## Concept (v2 — post-playtest pivot)

**Your steps buy scratch tickets.** A deadpan, literal casino inside Gamexercise — no story, no lore. The v1 "Fate Cards / cursed-hero" fantasy theme was rejected after the Phase-2 playtest (childish, cognitively confusing). Lesson: **familiar beats clever** — the addictive power is in instantly-recognized casino language: Cherry / Bell / Bar / **777**, Coins, Prestige. Tone: dry, adult, slightly cheeky ("Nothing. Zilch.").

> Guardrails: steps are the only faucet root. Never sell currency for real money. Base games are **pure-upside** (every play wins ≥ dust; variance is the thrill). Real risk only in later opt-in games. Scarcity is the mission — running dry is what sends the player outside.

## Architecture — the casino is a section, not a mode

- The original RPG stays primary. **Home gets a CASINO button** (flag `casinoEnabled`, RemoteConfig-cached, default off — flag only gates the button/UI; ticket accrual runs regardless, harmlessly).
- Nav: Home → **Casino Lobby** (SCRATCHERS / SLOTS / UPGRADES doors + coins/tickets/step-progress) → play screens → BACK → Home.
- Boot is always Title → Home (the old mode-replacement branch + phase clamp are gone).
- HealthKit gate covers casino phases like any gameplay phase.
- Core: `Core/CasinoGame.cs` (pure C#, RNG-injected, outcomes pre-rolled at play time) + `CasinoState` nested in `GameState` (schema v2). **Unified wallet:** casino wins/spends the RPG's existing `GameState.coins`; `CasinoState` holds no money of its own.

## Resource economy

| Resource | Earned by | Spent on | Prestige? |
|---|---|---|---|
| **Tickets** | steps (1 / 100, Stride floor 44) + daily quests (ticket chunks, ~+50% perfect day) + streak bonuses | plays — **1 ticket = 1 scratch or 1 spin** | survives |
| **Coins** | casino winnings (unified `GameState.coins`) | run upgrades (casino-internal) | **balance resets** |
| **PP** | prestiging | permanent upgrade tree | never |
| **Cosmetics** | **real-exercise milestone trophies** — never purchasable | — (trophy sets grant small casino perks, e.g. +2% payout) | never |

- **Achievements = the trophy system**, named after real-world distances (~2,000 steps/mile): Cross the Golden Gate ~3.5k (day-1 hook) → Manhattan ~28k → Marathon ~55k → London→Paris ~600k → Route 66 ~5.2M → **Walk Across America** ~5.9M → Around the World ~52M (white whale). Parallel streak + run lines. Monumental tiers also grant PP. Shop → **Wardrobe/Trophy gallery** (unlock conditions visible = fitness goals).
- **Daily quests stay** (finish lines, the 8pm nudge, run incentive, daily ritual) — rewards pay **tickets** (✅ converted, pulled forward from P5: 2/4/6/4/6 + 8 all-clear = 30 tickets/day max; weekly streak bonus = 5 tickets; over-cap rewards overflow to coins).
- Optional garnish (later): 1–2 jackpot-exclusive trophies (777-only drops).

## The numbers (validated by sim + 50k-trial LogicTest)

**Payout (per play):** Bust 45% → 5 · Cherry 30% → 25 · Bell 17% → 75 · Bar 6.5% → 300 · **Seven (777) 1.5% → 2,000** ⇒ EV ≈ 72 coins, 55% win rate. Luck: +0.3%/lvl → Seven, +1.0%/lvl Bust → Cherry. Rigged jackpot on the 3rd lifetime play (once ever). Ticket cap ~300; overflow → 50 coins/chunk (walking never wasted).

**Run upgrades (coins; reset at prestige):** Payout +8%/lvl (300, ×1.4) · Luck (400, ×1.5) · Stride −8 steps/ticket (600, ×1.5, max 7).
**Permanent (PP):** Golden Touch +5%/lvl (2, ×1.5) · Loaded Dice +0.3% odds/lvl (3, ×1.6) · Marathoner running +10%/lvl (4, ×1.6; run-steps ≈ 2.8/s from workout seconds).
**Prestige:** first = scripted at 25 plays, flat 5 PP; n≥2 gate = `J(n)=n` jackpots OR `C(n)=150+30(n−2)` plays; PP = jackpots + plays/40 (effort floor). Resets coin balance + run levels; never touches tickets, lifetime stats, trophies, PP.
**30-day sim:** first prestige day 1 for all profiles; sedentary 7 / average 11 / active 15 prestiges; ~5× income growth.

## Slots (P4)

Second room, same engine: a spin consumes a ticket, pays from the same pre-rolled table; reels stop one-by-one (the original near-miss). Differentiate variance/pacing later; machine tiers join the unlock ladder. Future scratcher ladder: Find the Cash / Gold Rush / The Ladder (press-luck) / High Stakes (opt-in wagers) / MEGA JACKPOT (white whale → New Game+).

## Roadmap

| Phase | Scope | Verify |
|---|---|---|
| R — Restructure + reskin ✅ | Casino-as-section + rename + unified wallet + Coins/777 strings | done `71fa885` |
| 3 — Prestige + juice ✅ | Prestige ceremony, 777 fanfare, coin showers, drag-to-scratch foil | done `0ab223b` |
| 4 — Slots + automation ✅ | SLOTS reel room; Auto Scratcher as earned 8-PP one-time unlock | done `5a7bc1b` |
| 5a — Art pass ✅ | Caz "Pixel Fantasy" packs (free, commercial-OK) imported to `Resources/Casino/`: layered slot cabinet (glass→symbols→shade→cabinet→crank up/down, windows measured at ±138.4/5.0 ×-offsets), real CHERRY/BELL/BAR/7 sprites on reels + scratch cells (text fallback kept), Caz coin flipbook in bursts, lobby cabinet dressing. Ticket-face composition + more dressing continues in 5b | Editor playtest |
| 5b — Full game roster ✅ | All seven games live: Scratchers, Slots + Find the Cash (Lv5, 3×3 hunt), Gold Rush (Lv9, dirt-foil dig, high-variance table), The Ladder (Lv13, press-luck, persisted), High Stakes (Lv24, wager presets 100/500/2000), MEGA JACKPOT (Lv30, golden ticket, odds printed). Lobby = 2-col level-gated game grid. Core `a3b9a1c` + screens | LogicTest EV per table ✅ + Editor playtest |
| **6 — Ship prep** | Real HealthKit on iPhone; tuning from real walks; **shop→trophy gallery + retroactive milestone credit**; StoreKit $5.99 unlock; **in-app credits incl. Caz (CC BY 4.0 attribution)**; TestFlight | device + TestFlight |

**Interim hybrid (dev-only):** quests now pay tickets (done); the old shop still charges coins until the P5 trophy conversion. Acceptable while the flag is off in prod — but the casino flag must be ON at ship (quest ticket rewards are invisible with it off).

## Notes / risks

- **No base batch-scratch.** Every ticket is played by hand; the backlog tedium at high volume is the *designed pressure* that makes the earned **Auto Scratcher** unlock desirable (Scritchy's automation arc). Automation is a carrot, never a freebie. (A free "Scratch All" existed briefly in P2 and was cut post-playtest.)
- Verification pattern: headless `LogicTest.Run` (compile + `[LOGIC OK]`); the Unity **Editor must be closed** first.
- Save compat: renaming the serialized field `fate`→`casino` is free — the flag never shipped, so no production save contains casino data. Migration v2 guarantees `casino != null`.
- Avoid real-money resemblance in copy ("Coins", never "$ cash you win") — App Store / brand safety.
- Client RNG fine for MVP; server-authoritative rolls are a post-ship concern.
