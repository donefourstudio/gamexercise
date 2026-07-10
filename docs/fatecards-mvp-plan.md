# Fate Cards — MVP Implementation Plan

**Status:** Phase 0 ✅ (scaffold + flag, `64e9a6c`) · Phase 1 ✅ (FateGame core + LogicTest economy coverage) · **Phase 2 next** (core screens + steps→cards wiring) · Phases 3–4 pending.
**Monetization (decided 2026-07):** free download + one-time **$5.99** non-consumable unlock — see the Monetization section below.
**Goal:** Pivot Gamexercise from a passive idle-RPG into an **exercise-fuelled scratch-card / casino game** where real walking/running is the only faucet. Make fitness addictive (daily retention, short-term dopamine, long-term habit) without ever letting the fun run without the walking.

> Guardrail: **steps are the ONLY currency faucet.** Never sell currency for real money. The base scratch is **pure-upside** (every card gains gold; variance is in *how much*). Real risk is opt-in and later. See `~/.claude` design memory `gamexercise-redesign-vision` for the full design rationale.

## Monetization — free + one-time unlock (decided 2026-07)

- **Free download.** The full core loop is playable free **through the first ascension + a few days** — the hook must set before any gate appears. The exact free/paid line is a Phase-4/TestFlight tuning decision.
- **One non-consumable IAP — "Break the Seal", $5.99** — permanently unlocks everything: all card types as they level-unlock, full AP-tree depth, unlimited ascensions. Restorable across devices; Family Sharing optional.
- **Never sold:** cards, gold, AP, steps. Money can never buy exercise progress. No ads, no subscriptions, no consumables. Because randomized rewards are never money-purchasable (directly or via intermediate currency), Apple's loot-box odds-disclosure rules don't apply.
- Launch at $5.99; raise later if reviews/reputation justify it (raising is easy; lowering reads as failure).
- Implementation: the StoreKit gate lands ~Phase 3–4. The economy core is monetization-agnostic — no Phase 1/2 impact.
- Rejected alternative: $8.99 paid-upfront (ethically ideal but strangles the install funnel for an unknown indie; the mission needs users to arrive).

---

## 1. Architecture — a parallel core behind a flag

The new mode **replaces** the app experience when its flag is on; the existing idle-RPG stays intact as the flag-off fallback. Mirror the current clean 3-layer split:

- **`FateGame.cs`** (new, `Gamex.Core`, pure C#) — all new logic (grant cards, scratch/roll, upgrades, ascension, AP). Parallel to `GamexGame`; headless-testable identically.
- **`FateState`** (new `[Serializable]`) — nested inside the existing `GameState` so it rides the current save + migration machinery for free.
- **`Hud.FateCards.cs`** (new HUD partial) — the new screens, built with the same `Make.*` factories, driven by the same `Refresh(g)` loop.
- **`GameRunner`** branches at boot: flag on → build `FateGame` + Fate screens, route step deltas to it; flag off → today's game, untouched.

**Nothing reaches the live App Store build until a new build is shipped — and even then it defaults flag-off**, flippable remotely for TestFlight / gradual rollout.

## 2. Reuse vs. build

| Reuse as-is | Build new |
|---|---|
| HealthKit step ingestion (`SyncHealthKit` → `AddActivity` delta pattern in `GameRunner`) | `FateGame` core + `FateState` |
| `SaveSystem` + `Migrations` (JsonUtility) | The scratch mechanic + scratch-off visual |
| `RemoteConfig` (the feature-flag layer) | Fate screens (Home / Scratch / Upgrades / Ascension) |
| `Hud` factories (`MkText/MkPanel/MkButton…`), `Sfx`, boot harness | Economy math (distribution, curves, AP) |
| `LogicTest` batchmode harness (headless verify) | Minimal AP-tree UI |

## 3. Data model — `FateState` (JsonUtility defaults to 0 on old saves)

`gold`, `cardsBanked`, `cardCap`, `stepAccumulator` (fractional steps→card), `lifetimeCardsScratched`, `cardsThisRun`, `jackpotsThisRun`, `ascensionPoints`, `ascensionCount`, `firstAscensionDone`, per-run levels (`runFortune / runFavor / runEndurance`), perm levels (`permMidas / permBlessedFate / permMarathoner`). Distribution + cost constants live in code, not state. Bump `Migrations.CurrentVersion`, add a no-op migration block.

## 4. Feature-flag wiring (concrete)

- `RemoteConfig.ConfigSchema` += `public bool fateCardsEnabled;` and a `public static bool FateCardsEnabled` (PlayerPrefs-cached like `FilterManualEntries`).
- `GameRunner.Awake` reads the **cached** flag (PlayerPrefs, before the async fetch) to pick the mode; a live flip applies next launch. Dev override via a `#if GAMEX_FATE` compile symbol to force it in Editor.
- In Fate mode, `SyncHealthKit` routes deltas to `_fate.GrantCards(delta)` instead of `_game.AddActivity`.

## 5. Screens (MVP)

1. **Fate Home** — gold, cards-banked, step→card progress, avatar (reuse existing), buttons: *Scratch*, *Upgrades*, *Ascend* (when eligible).
2. **Scratch** — the Three Fates card with reveal + result + juice, plus **Scratch-All** batch cascade (a base feature, per Fix #2).
3. **Upgrades** — 3 per-run upgrades (gold) + 2–3 AP permanents (AP).
4. **Ascension** — the scripted first-ascension beat + a minimal AP tree.

New `AppPhase` values (`FateHome`, `FateScratch`, `FateUpgrades`, `FateAscend`), appended per the enum convention.

## 6. The scratch mechanic — the one genuinely custom piece

True drag-to-scratch-off (erase a mask over a RenderTexture) is the trickiest UI. **Phased:** ship a **tap-to-reveal** version in MVP (each cell flips on tap — fast, still satisfying), then upgrade to real **drag-scratch** in polish. Outcome is **pre-rolled** on card creation; the reveal is theatre. Client-side RNG is fine for MVP (cosmetic economy, no real money); server-authoritative anti-cheat is a later concern (same posture as the existing HealthKit manual-entry filter).

## 7. Build order (each phase Editor-verifiable)

| Phase | Scope | Effort | Verify |
|---|---|---|---|
| **0 — Scaffold + flag** | RemoteConfig flag, `FateState` in save, GameRunner branch, empty Fate shell | S | Compile + LogicTest; old game unaffected (flag off), shell boots (flag on) |
| **1 — Core logic (no UI)** | `FateGame`: GrantCards, Scratch (Three Fates), run-upgrades, eligibility, Ascend, AP + effort floor | M | **LogicTest**: 10k-trial scratch EV ≈ target, upgrade costs, ascension math, day-1 eligibility across profiles |
| **2 — Core screens + scratch** | Home, Scratch (tap-reveal), Upgrades; wire steps→cards | M | Editor playtest w/ `T` = +1000-steps debug key |
| **3 — Ascension + juice** | Scripted first ascension + 2 perms; scratch / jackpot / scratch-all juice | M | Full loop playable in Editor |
| **4 — Polish + device** | True drag-scratch, tuning, iOS device test w/ real HealthKit | M–L | On-device |

## 8. Explicitly OUT of MVP (deferred, all designed)

Later card types (Omen Hunt → The Delve → The Climb → Constellation → Curse's Wager → Curse-Breaker), the full 5-branch AP tree, New Game+, the Curse-Breaker white-whale, Auto-Scratcher-as-idle (Scratch-All covers volume for MVP), leaderboards / social.

---

## 9. Design reference — the numbers (so this doc is self-contained)

**Faucet:** 1 Fate Card per **100 steps** (running wins naturally via ~1.5× cadence; count all steps equally, no run-detection needed). Card cap ~300–500; **over-cap steps convert straight to gold** (walking never wasted).

**Three Fates payout distribution** (pre-rolled; symbols are the reveal skin over a set distribution):

| Reveal | Gold | Chance |
|---|---|---|
| Bust (near-miss + dust) | 5 | 45% |
| ☾ Moon | 25 | 30% |
| ⛂ Coin | 75 | 17% |
| ♛ Crown | 300 | 6.5% |
| ☀ Sun — JACKPOT | 2,000 | 1.5% |

→ ~**72 gold/card** avg · **55%** win rate · jackpot ≈ 1 in 67. Avg walker (6k steps = 60 cards) ≈ **4,320 gold/day**.

**Per-run upgrades (flattened for ~2–3 day cadence; reset each ascension):**

| Upgrade | Effect / level | L1 cost | Curve |
|---|---|---|---|
| Fortune | +8% gold from all cards | 300 | ×1.4 |
| Fate's Favor | +0.3% jackpot odds + nudge busts→small wins | 400 | ×1.5 |
| Endurance | −8 steps/card (100 → 44 floor) | 600 | ×1.5 |

**Ascension:**
- **First** = scripted tutorial beat: triggers after ~25 cards scratched (reachable **day 1** by anyone), grants a guaranteed **5 starter AP** + a **rigged early jackpot** for the peak-dopamine moment.
- **2+ eligibility:** `jackpots ≥ J(n)` **OR** `cards-scratched ≥ C(n)`, whichever first, where `J(n)=n` (2,3,4,5…) and `C(n)=150+30·(n−2)`. Voluntary.
- **AP earned:** `jackpots(tier-weighted) × (1 + Memory) + cards_scratched / 40` (the cards term = the load-bearing effort floor, ~40–55% of AP).
- **Resets:** gold + per-run upgrade levels. **Never resets:** lifetime steps/streak, avatar identity, cosmetics, unlocked card types, AP + AP-tree. Reuses the existing race-transformation as the *first* ascension visual.

**AP prestige tree — 5 branches (build diversity = replay value):**
- **Fortune** (income): Midas Touch +%gold · Big Winner +%jackpot · Less Is More +%small-wins · Collector +% per card-type this run · High Roller (bigger jackpots / smaller smalls).
- **Fate** (odds): Blessed Fate +luck · Fortune's Pull +jackpot freq · Mythic Fate (Curse-Breaker white-whale less rare).
- **Familiar** (automation): Auto-Scratcher + speed/capacity tiers · Companion (a pet that auto-scratches & skips busts).
- **⭐ Vigor** (fitness-synergy — our signature): Marathoner (running +%) · Streak Master (+% per streak day) · Trailblazer (bonus past 8k/day) · permanent Endurance.
- **Ascendant** (meta): Fortune's Memory (+AP per jackpot) · Reawakened (start run with gold) · Quick Ascent (lowers next threshold).

Early AP costs ~2–8; deep tiers scale to hundreds (×1.5–2/lvl). MVP ships ~2–3 permanents (e.g. Midas Touch, Blessed Fate, Marathoner).

**Card-unlock ladder** (level-gated, permanent; ~1.2 lvl/day avg walker; early cards pure-upside, risk strictly opt-in + later):

| Level | Card | Mechanic | Risk |
|---|---|---|---|
| 1 | Three Fates | match-3, symbol sets gold | none |
| 5 | Omen Hunt | reveal cells to find matching omens | none |
| 9 | The Delve | dig for treasure, peek-and-bail, high variance | none |
| 13 | The Climb | press-your-luck ladder, bank or bust | opt-in |
| 18 | Constellation | multi-panel combo (rewards Collector) | opt-in |
| 24 | Curse's Wager | wager GOLD vs the Curse for a multiplier | opt-in gold risk |
| 30 | Curse-Breaker | mythic white-whale jackpot → New Game+ | none (celebratory) |

## 10. Pressure-test validation (30-day sim, all fixes on)

| Profile | Steps/day | 1st ascension | Ascensions/30d | Cadence | Gold (30d) |
|---|---|---|---|---|---|
| Sedentary | 3,000 | **Day 1** | 7 | ~5d | 166k |
| Average | 6,000 | **Day 1** | 11 | ~3d | 470k |
| Active | 11,000 | **Day 1** | 15 | ~2d | 998k |

Everyone hooked day 1; activity cleanly rewarded (active ≈ 2× ascensions, 6× gold vs sedentary) with no equity gap; numbers stay legible; income snowballs ~5×.

## 11. Notes / risks

- **Scratch-off UI** is the main custom-build risk → phased (tap-reveal first).
- **Number formatting** (K/M/B) needed as gold grows.
- **Save coexistence:** `FateState` nested in `GameState` keeps both modes' data isolated; flipping the flag never corrupts either.
- **Anti-cheat:** client RNG for MVP; server-authoritative rolls are a later concern.
- The plan touches ~5 new files + 2 small edits (`RemoteConfig`, `GameRunner`, `GameState`), leaving the recently-cleaned code almost entirely alone.
