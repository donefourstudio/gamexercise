# The Casino — implementation plan

**Status:** Phases 0–2 ✅ (`64e9a6c`, `2a97408`, `9e1da54`) · Phase R ✅ (Casino pivot, `71fa885`; quests→tickets `a5dc053`) · Phase 3 ✅ (drag-to-scratch foil + 777 fanfare + Prestige ceremony) · **P4 next** (slots + Auto Scratcher unlock) → P5 device/tuning + RPG integration.
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
| **R — Restructure + reskin** (now) | Home CASINO button + lobby; kill mode-replacement; rename `Fate*`→`Casino*` end to end (flag, save field, phases, files — free: never shipped); unified wallet; Coins/777/deadpan strings | compile + LogicTest; Editor playtest |
| **3 — Prestige + juice** | Cash-out Prestige ceremony, 777 fanfare, coin showers, shake, **true drag-to-scratch** (the tactile foil-erase — the scratch IS the product) | Editor playtest |
| **4 — Slots + automation** | Reels room on the shared engine; **Auto Scratcher as an EARNED unlock** (PP tree) — processes the backlog once owned | Editor playtest |
| **5 — Device + integration** | Real HealthKit on iPhone; tuning; **RPG integration: shop→trophy gallery + retroactive milestone credit** (quests→tickets already done); StoreKit unlock | device + TestFlight |

**Interim hybrid (dev-only):** quests now pay tickets (done); the old shop still charges coins until the P5 trophy conversion. Acceptable while the flag is off in prod — but the casino flag must be ON at ship (quest ticket rewards are invisible with it off).

## Notes / risks

- **No base batch-scratch.** Every ticket is played by hand; the backlog tedium at high volume is the *designed pressure* that makes the earned **Auto Scratcher** unlock desirable (Scritchy's automation arc). Automation is a carrot, never a freebie. (A free "Scratch All" existed briefly in P2 and was cut post-playtest.)
- Verification pattern: headless `LogicTest.Run` (compile + `[LOGIC OK]`); the Unity **Editor must be closed** first.
- Save compat: renaming the serialized field `fate`→`casino` is free — the flag never shipped, so no production save contains casino data. Migration v2 guarantees `casino != null`.
- Avoid real-money resemblance in copy ("Coins", never "$ cash you win") — App Store / brand safety.
- Client RNG fine for MVP; server-authoritative rolls are a post-ship concern.
