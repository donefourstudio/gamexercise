#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    // Pure-logic test of the step-based sim.
    // Unity -batchmode -quit -projectPath . -executeMethod Gamex.Game.LogicTest.Run
    public static class LogicTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool cond, string msg) { if (!cond) { Debug.LogError("[LOGIC FAIL] " + msg); fails++; } }

            // Schema migration runs first — independent of game state and the
            // later (stale) coin / catalog tests, so a regression here is
            // never masked by an earlier failure. JSON without a
            // schemaVersion field (the pre-2026-06 save format) deserialises
            // with the field at 0; Migrations.Apply bumps it to
            // CurrentVersion while preserving all other fields.
            var v0Json = "{\"level\":5,\"coins\":123}";
            var v0 = JsonUtility.FromJson<GameState>(v0Json);
            Check(v0.schemaVersion == 0, "legacy save deserialises as v0, got " + v0.schemaVersion);
            Migrations.Apply(v0);
            Check(v0.schemaVersion == Migrations.CurrentVersion,
                  $"Apply bumps to current v{Migrations.CurrentVersion}, got v{v0.schemaVersion}");
            Check(v0.level == 5 && v0.coins == 123,
                  "Apply preserves pre-existing fields, got lvl=" + v0.level + " coins=" + v0.coins);
            // Idempotency — re-running Apply on a current-version save is a
            // no-op (no field corruption, version stays put).
            Migrations.Apply(v0);
            Check(v0.schemaVersion == Migrations.CurrentVersion, "Apply is idempotent on current-version save");
            Check(v0.casino != null, "migration guarantees CasinoState on legacy saves");

            // v1 save (pre-casino, 2026-06 era) walks the ladder to v2 and
            // gains a CasinoState.
            var v1 = JsonUtility.FromJson<GameState>("{\"schemaVersion\":1,\"level\":7}");
            Migrations.Apply(v1);
            Check(v1.schemaVersion == Migrations.CurrentVersion && v1.casino != null && v1.level == 7,
                  "v1 walks the full ladder, adds CasinoState, preserves fields");

            // CasinoState + the unified coin wallet survive the save
            // round-trip (nested-object serialization through JsonUtility).
            var casGs = new GameState();
            casGs.coins = 4321;                     // unified wallet
            casGs.casino.ticketsBanked = 17;
            casGs.casino.permGoldenTouch = 2;
            var casBack = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(casGs));
            Check(casBack.casino != null && casBack.coins == 4321
                  && casBack.casino.ticketsBanked == 17 && casBack.casino.permGoldenTouch == 2,
                  "CasinoState + unified wallet survive save round-trip");

            // ==== Casino core economy ====
            // RNG is seeded so every run of this suite is reproducible.
            // Coins are the UNIFIED wallet (GameState.coins), so every
            // money assertion checks the host, not CasinoState.

            // Faucet: 1 ticket / 100 steps, remainder accumulates across ticks.
            var chost = new GameState();
            var cg = new CasinoGame(chost, new System.Random(12345));
            cg.GrantActivity(250, 0);
            Check(cg.state.ticketsBanked == 2 && cg.state.stepAccumulator == 50,
                  "250 steps -> 2 tickets + 50 acc, got " + cg.state.ticketsBanked + "/" + cg.state.stepAccumulator);
            cg.GrantActivity(50, 0);
            Check(cg.state.ticketsBanked == 3 && cg.state.stepAccumulator == 0, "accumulator completes a ticket");

            // Fix #1: a jackpot is guaranteed within the first 3 lifetime
            // plays (play #3 is rigged if none landed naturally).
            var p1 = cg.Play(); var p2 = cg.Play(); var p3 = cg.Play();
            Check(p1.jackpot || p2.jackpot || p3.jackpot, "first session guarantees a 777 within 3 plays");
            Check(cg.state.lifetimeJackpots >= 1 && cg.state.jackpotsThisRun >= 1, "jackpot counters tick");
            Check(chost.coins >= 2000, "jackpot coins land in the unified wallet");
            Check(cg.state.ticketsBanked == 0 && !cg.CanPlay, "plays consume tickets");
            Check(cg.Play().coins == 0 && cg.state.lifetimePlays == 3, "play at 0 tickets is a no-op");

            // Distribution: 50k plays at luck 0 -> EV within 72 +/- 5,
            // jackpot rate within 1.5% +/- 0.4%. (SEM at 50k ~ 1.1 coins /
            // 0.05%, so a fail means the table changed, not noise.)
            var dhost = new GameState();
            dhost.casino.lifetimeJackpots = 1;    // disarm the rig
            var cdist = new CasinoGame(dhost, new System.Random(777));
            cdist.state.ticketsBanked = 50000;
            long distCoins = 0; int distSevens = 0;
            for (int i = 0; i < 50000; i++) { var r = cdist.Play(); distCoins += r.coins; if (r.jackpot) distSevens++; }
            double distEv = distCoins / 50000.0, distSevenRate = distSevens / 50000.0;
            Check(distEv > 67 && distEv < 77, $"payout EV ~72, got {distEv:F1}");
            Check(distSevenRate > 0.011 && distSevenRate < 0.019, $"jackpot rate ~1.5%, got {distSevenRate:P2}");
            Check(dhost.coins == distCoins, "every payout lands in the host wallet");

            // Luck (run Luck + Loaded Dice) shifts the table: +0.3%/lvl into
            // Seven, busts -> cherries. EV at luck 5 ~ 72 + 6.185*5 = 102.9.
            var lhost = new GameState();
            lhost.casino.lifetimeJackpots = 1;
            var cluck = new CasinoGame(lhost, new System.Random(778));
            cluck.state.runLuck = 5;
            Check(System.Math.Abs(cluck.JackpotChance - 0.030f) < 1e-4, "luck 5 -> 3.0% jackpot odds");
            cluck.state.ticketsBanked = 50000;
            long luckCoins = 0; int luckSevens = 0;
            for (int i = 0; i < 50000; i++) { var r = cluck.Play(); luckCoins += r.coins; if (r.jackpot) luckSevens++; }
            double luckEv = luckCoins / 50000.0, luckSevenRate = luckSevens / 50000.0;
            Check(luckEv > 97 && luckEv < 109, $"EV at luck 5 ~102.9, got {luckEv:F1}");
            Check(luckSevenRate > 0.025 && luckSevenRate < 0.035, $"jackpot ~3% at luck 5, got {luckSevenRate:P2}");

            // Payout + Golden Touch multiply payouts (deterministic via the
            // rigged 777): 2000 * (1 + 5*0.08 + 2*0.05) = 3000.
            var mhost = new GameState();
            mhost.casino.lifetimePlays = 2;
            var cmult = new CasinoGame(mhost, new System.Random(1));
            cmult.state.runPayout = 5; cmult.state.permGoldenTouch = 2; cmult.state.ticketsBanked = 1;
            var rigged = cmult.Play();
            Check(rigged.rigged && rigged.coins == 3000,
                  "multipliers: rigged 777 pays 2000*1.5 = 3000, got " + rigged.coins);

            // Run upgrade curve + purchase gating (spends the unified wallet).
            var uhost = new GameState();
            var cup = new CasinoGame(uhost, new System.Random(2));
            Check(cup.RunUpgradeCost(CasinoGame.RunUpgrade.Payout) == 300, "Payout L1 costs 300");
            uhost.coins = 720;   // exactly L1 (300) + L2 (420)
            Check(cup.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Payout) && cup.state.runPayout == 1, "buy Payout L1");
            Check(cup.RunUpgradeCost(CasinoGame.RunUpgrade.Payout) == 420, "Payout L2 costs 420 (300*1.4)");
            Check(cup.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Payout) && uhost.coins == 0, "L2 drains the wallet");
            Check(!cup.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Payout), "refuses purchase without coins");
            uhost.coins = 1000000;
            for (int i = 0; i < 10; i++) cup.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Stride);
            Check(cup.state.runStride == 7 && cup.StepsPerTicket == 44,
                  "Stride caps at L7 -> 44 steps/ticket floor");

            // Scripted first prestige: pure-effort gate at 25 plays; grants
            // flat 5 PP; resets the unified coin balance + run levels; NEVER
            // touches banked tickets / lifetime counters (never punish
            // exercise).
            var phost = new GameState();
            phost.casino.lifetimeJackpots = 1;
            var cpre = new CasinoGame(phost, new System.Random(3));
            cpre.state.ticketsBanked = 30;
            for (int i = 0; i < 24; i++) cpre.Play();
            Check(!cpre.PrestigeEligible, "not eligible at 24 plays");
            cpre.Play();
            Check(cpre.PrestigeEligible, "first prestige eligible at 25 plays");
            phost.coins = 9999; cpre.state.runPayout = 3;
            int ticketsBefore = cpre.state.ticketsBanked;
            Check(cpre.TryPrestige(), "prestige succeeds");
            Check(System.Math.Abs(cpre.state.prestigePoints - 5f) < 1e-4, "first prestige grants flat 5 PP");
            Check(phost.coins == 0 && cpre.state.runPayout == 0, "prestige resets wallet + run levels");
            Check(cpre.state.ticketsBanked == ticketsBefore && cpre.state.lifetimePlays == 25,
                  "banked tickets + lifetime counters survive prestige");
            // n=2 gates: 2 jackpots OR 150 plays; PP = jackpots + plays/40.
            Check(cpre.GateJackpots == 2 && cpre.GatePlays == 150, "n=2 gates: J=2, C=150");
            cpre.state.jackpotsThisRun = 2; cpre.state.playsThisRun = 20;
            Check(cpre.PrestigeEligible, "2 jackpots satisfy the n=2 gate");
            Check(System.Math.Abs(cpre.PrestigePpPreview - (2 + 20 / 40f)) < 1e-4,
                  "PP preview = jackpots + plays/40 (effort floor)");

            // Permanent PP purchases.
            var permHost = new GameState();
            var cperm = new CasinoGame(permHost, new System.Random(4));
            cperm.state.prestigePoints = 5f;
            Check(cperm.PermUpgradeCost(CasinoGame.PermUpgrade.GoldenTouch) == 2, "Golden Touch L1 costs 2 PP");
            Check(cperm.TryBuyPermUpgrade(CasinoGame.PermUpgrade.GoldenTouch) && cperm.state.permGoldenTouch == 1,
                  "buy Golden Touch L1");
            Check(System.Math.Abs(cperm.state.prestigePoints - 3f) < 1e-4, "PP deducted");
            Check(!cperm.TryBuyPermUpgrade(CasinoGame.PermUpgrade.Marathoner),
                  "refuses Marathoner (costs 4, have 3)");

            // Auto Scratcher: flat 8-PP, strictly one-time (P4).
            var ahost = new GameState();
            var cauto = new CasinoGame(ahost, new System.Random(7));
            cauto.state.prestigePoints = 20f;
            Check(cauto.PermUpgradeCost(CasinoGame.PermUpgrade.AutoScratcher) == 8, "Auto Scratcher costs 8 PP");
            Check(!cauto.AutoScratcherOwned, "not owned initially");
            Check(cauto.TryBuyPermUpgrade(CasinoGame.PermUpgrade.AutoScratcher)
                  && cauto.AutoScratcherOwned && cauto.state.permAutoScratcher == 1,
                  "buy Auto Scratcher");
            Check(!cauto.TryBuyPermUpgrade(CasinoGame.PermUpgrade.AutoScratcher),
                  "Auto Scratcher is one-time (refuses re-buy)");
            Check(System.Math.Abs(cauto.state.prestigePoints - 12f) < 1e-4, "8 PP deducted, no double-charge");

            // ==== P5b game tables ====

            // Gold Rush: high-variance dig. 50k plays -> EV ~72.25,
            // motherlode (jackpot band) ~2%.
            var grhost = new GameState();
            grhost.casino.lifetimeJackpots = 1;   // disarm the rig
            var cgr = new CasinoGame(grhost, new System.Random(881));
            cgr.state.ticketsBanked = 50000;
            long grCoins = 0; int grLodes = 0;
            for (int i = 0; i < 50000; i++)
            {
                var r = cgr.PlayTable(CasinoGame.TableKind.GoldRush);
                grCoins += r.coins; if (r.jackpot) grLodes++;
            }
            double grEv = grCoins / 50000.0, grRate = grLodes / 50000.0;
            Check(grEv > 67 && grEv < 78, $"Gold Rush EV ~72.25, got {grEv:F1}");
            Check(grRate > 0.014 && grRate < 0.026, $"motherlode ~2%, got {grRate:P2}");
            Check(cgr.state.ticketsBanked == 0, "Gold Rush consumes tickets");

            // MEGA JACKPOT: 1-in-2,000 pays 100,000, else 20. 200k plays ->
            // EV ~70 (wide bounds — single-jackpot variance dominates).
            var mghost = new GameState();
            mghost.casino.lifetimeJackpots = 1;
            var cmg = new CasinoGame(mghost, new System.Random(882));
            cmg.state.ticketsBanked = 200000;
            long mgCoins = 0; int mgWins = 0;
            for (int i = 0; i < 200000; i++)
            {
                var r = cmg.PlayTable(CasinoGame.TableKind.Mega);
                mgCoins += r.coins; if (r.jackpot) mgWins++;
            }
            double mgEv = mgCoins / 200000.0;
            Check(mgEv > 55 && mgEv < 85, $"Mega EV ~70, got {mgEv:F1}");
            Check(mgWins > 60 && mgWins < 140, $"Mega hits ~1/2000 (expect ~100 in 200k), got {mgWins}");

            // The Ladder: fair double-or-fall, EV = 72 for any strategy.
            // Always-climb-to-cap over 20k ladders: banked/ladder ~72.
            var ldhost = new GameState();
            ldhost.casino.lifetimeJackpots = 1;
            var cld = new CasinoGame(ldhost, new System.Random(883));
            cld.state.ticketsBanked = 20000;
            Check(cld.TryLadderStart(), "ladder starts");
            Check(!cld.TryLadderStart(), "can't start a second ladder mid-climb");
            Check(cld.state.ticketsBanked == 19999, "ladder start consumed a ticket");
            long ldWallet0 = ldhost.coins;
            int ladders = 1;
            while (true)
            {
                while (cld.LadderActive && cld.state.ladderRung < CasinoGame.LADDER_MAX_RUNG)
                    cld.LadderClimb();
                if (cld.LadderActive) cld.LadderBank();   // reached the cap
                if (cld.state.ticketsBanked == 0) break;
                cld.TryLadderStart();
                ladders++;
            }
            double ldEv = (ldhost.coins - ldWallet0) / (double)ladders;
            Check(ldEv > 47 && ldEv < 97, $"Ladder climb-to-cap EV ~72, got {ldEv:F1} over {ladders} ladders");
            Check(ldhost.coins >= ldWallet0, "the Ladder never reduces the wallet");
            // Deterministic bank + jackpot flag at rung >= 6.
            var ld2host = new GameState();
            var cld2 = new CasinoGame(ld2host, new System.Random(884));
            cld2.state.ladderActive = true; cld2.state.ladderPot = 4608; cld2.state.ladderRung = 6;
            long banked = cld2.LadderBank();
            Check(banked == 4608 && ld2host.coins == 4608, "bank credits the pot");
            Check(cld2.state.jackpotsThisRun == 1, "rung-6 bank counts as a jackpot");
            Check(!cld2.LadderActive && cld2.LadderBank() == 0, "bank is idempotent");

            // High Stakes: wager rides a classic roll. Deterministic via the
            // rigged 777: wager 100 -> returns 1000 (net +900); Payout
            // upgrades must NOT multiply wagers.
            var hshost = new GameState();
            hshost.casino.lifetimePlays = 2;      // next play is the rigged Seven
            hshost.coins = 100;
            var chs = new CasinoGame(hshost, new System.Random(885));
            chs.state.runPayout = 5;              // would be x1.4 if it (wrongly) applied
            chs.state.ticketsBanked = 1;
            var hsr = chs.PlayHighStakes(100);
            Check(hsr.rigged && hsr.jackpot && hsr.coins == 1000,
                  "High Stakes 777 returns wager x10, got " + hsr.coins);
            Check(hshost.coins == 1000, "wallet: 100 - 100 escrow + 1000 return");
            Check(!chs.PlayHighStakes(100).jackpot && hshost.coins == 1000,
                  "refuses without a ticket (wallet untouched)");
            var hs0 = new CasinoGame(new GameState(), new System.Random(886));
            hs0.state.ticketsBanked = 1;
            Check(hs0.PlayHighStakes(100).coins == 0 && hs0.state.ticketsBanked == 1,
                  "refuses wager it can't cover (no ticket burned)");
            // EV: net per play ~ +5% of a 100 wager over 50k plays.
            var hsEvHost = new GameState();
            hsEvHost.casino.lifetimeJackpots = 1;
            hsEvHost.coins = 10000000;
            var chsEv = new CasinoGame(hsEvHost, new System.Random(887));
            chsEv.state.ticketsBanked = 50000;
            long hsBefore = hsEvHost.coins;
            for (int i = 0; i < 50000; i++) chsEv.PlayHighStakes(100);
            double hsNet = (hsEvHost.coins - hsBefore) / 50000.0;
            Check(hsNet > 2 && hsNet < 8, $"High Stakes net EV ~ +5/play on 100 wager, got {hsNet:F2}");

            // ==== The Desk (R2-1, Pivot 3) ====

            // Migration v2 -> v3 backfills DeskState; arrays round-trip.
            var v2d = JsonUtility.FromJson<GameState>("{\"schemaVersion\":2,\"level\":9}");
            Migrations.Apply(v2d);
            Check(v2d.schemaVersion == 3 && v2d.desk != null && v2d.level == 9,
                  "v2 -> v3 adds DeskState, preserves fields");
            var dGs = new GameState();
            dGs.desk.cardLevel[3] = 7; dGs.desk.rollsPending = 5; dGs.desk.loanOwed = 750;
            var dBack = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(dGs));
            Check(dBack.desk != null && dBack.desk.cardLevel[3] == 7
                  && dBack.desk.rollsPending == 5 && dBack.desk.loanOwed == 750,
                  "DeskState arrays survive save round-trip");

            // Stride rolls: 100 steps/roll, remainder accumulates; the
            // envelope gambles every roll (EV 4.2) and credits the wallet
            // AND the unlock bar.
            var ehost = new GameState();
            var dg = new DeskGame(ehost, new System.Random(41));
            Check(dg.GrantSteps(250) == 2 && dg.state.rollsPending == 2 && dg.state.stepAccumulator == 50,
                  "250 steps -> 2 rolls + 50 acc");
            dg.GrantSteps(50);
            Check(dg.state.rollsPending == 3, "accumulator completes a roll");
            dg.state.rollsPending = 5000;
            long gross = dg.TearEnvelope();
            double perRoll = gross / 5000.0;
            Check(perRoll > 3.9 && perRoll < 4.5, $"stride roll EV ~4.2, got {perRoll:F2}");
            Check(ehost.coins == gross && dg.state.earnedThisRun == gross && dg.state.rollsPending == 0,
                  "envelope credits wallet + unlock bar");

            // Per-card EV at Luck 0 / Lv 0 (house edge — the sucker phase).
            // Bands from sim v3: 90/94/92/89/79% of cost (Golden ~81%).
            {
                var evHost = new GameState();
                evHost.desk.lifetimeBigWins = 1;   // disarm the rigged 3rd play
                var evg = new DeskGame(evHost, new System.Random(42));
                (int idx, double lo, double hi)[] bands =
                {
                    (0, 8.2, 9.9), (1, 86, 102), (2, 1690, 2010), (3, 8300, 9500), (4, 140000, 176000),
                };
                foreach (var b in bands)
                {
                    evg.state.cardsOwned[b.idx] = 30000;
                    long net = 0;
                    for (int i = 0; i < 30000; i++)
                    {
                        evg.state.cardLevel[b.idx] = 0; evg.state.cardXp[b.idx] = 0;   // pin Lv0 (XP works!)
                        var r = evg.ScratchAll(b.idx); net += r.payout - r.penalty;
                    }
                    double e = net / 30000.0;
                    Check(e > b.lo && e < b.hi,
                          $"{DeskGame.CATALOG[b.idx].name} EV in [{b.lo},{b.hi}], got {e:F1}");
                }
                // Golden Ticket: 1-in-500 pays 400x. 200k plays, wide band.
                evg.state.cardsOwned[5] = 200000;
                long gnet = 0;
                for (int i = 0; i < 200000; i++)
                {
                    evg.state.cardLevel[5] = 0; evg.state.cardXp[5] = 0;
                    gnet += evg.ScratchAll(5).payout;
                }
                double gev = gnet / 200000.0;
                Check(gev > 620000 && gev < 1000000, $"Golden EV ~800k, got {gev:F0}");
            }

            // Luck removes junk: TwoWin EV at Luck 20 clearly beats Luck 0.
            {
                var lHost = new GameState();
                lHost.desk.lifetimeBigWins = 1;
                var lg = new DeskGame(lHost, new System.Random(43));
                lg.state.cardsOwned[0] = 60000;
                long n0 = 0;
                for (int i = 0; i < 30000; i++)
                { lg.state.cardLevel[0] = 0; lg.state.cardXp[0] = 0; n0 += lg.ScratchAll(0).payout; }
                lg.state.upLuck = 20;
                long n20 = 0;
                for (int i = 0; i < 30000; i++)
                { lg.state.cardLevel[0] = 0; lg.state.cardXp[0] = 0; n20 += lg.ScratchAll(0).payout; }
                Check(n20 > n0 * 1.2, $"Luck 20 lifts TwoWin EV >20% ({n0 / 30000.0:F2} -> {n20 / 30000.0:F2})");
            }

            // Deterministic resolve: crafted card, level multiplier, traps,
            // peek-and-bail (unrevealed traps don't hurt).
            {
                var cHost = new GameState();
                var cg2 = new DeskGame(cHost, new System.Random(44));
                cg2.state.cardLevel[0] = 10;   // +30% prizes
                var crafted = new DealtCard { cardIdx = 0, spots = new sbyte[] { 2, 2, Spot.JUNK } };
                var rr = cg2.ResolveCard(crafted, new[] { true, true, true });
                Check(rr.payout == 78 && rr.matchedSym == 2, "Lv10 top match pays 60*1.3 = 78, got " + rr.payout);
                // Sour Orchard traps: two revealed traps = -1000; hiding
                // them costs nothing (the bail decision is real).
                var trapCard = new DealtCard { cardIdx = 2, spots = new sbyte[] { Spot.TRAP, Spot.TRAP, 0, 0, Spot.JUNK, Spot.JUNK, Spot.JUNK, Spot.JUNK } };
                long before = cHost.coins;
                var tr = cg2.ResolveCard(trapCard, new[] { true, true, true, true, true, true, true, true });
                Check(tr.penalty == 1000 && tr.payout == 0 && cHost.coins == before - 1000,
                      "2 revealed traps cost 1000, 2-of-3 sym0 pays nothing");
                var tr2 = cg2.ResolveCard(trapCard, new[] { false, false, true, true, true, true, true, true });
                Check(tr2.penalty == 0, "unrevealed traps don't hurt (peek-and-bail)");
                // Debt floor: traps can't dig below -1000.
                cHost.coins = -900;
                cg2.ResolveCard(trapCard, new[] { true, true, false, false, false, false, false, false });
                Check(cHost.coins == DeskGame.DEBT_FLOOR, "trap losses clamp at the debt floor");
            }

            // The rigged 3rd play: top-symbol match on the third card ever.
            {
                var rigHost = new GameState();
                var rg = new DeskGame(rigHost, new System.Random(45));
                rg.state.cardsOwned[0] = 3;
                rg.ScratchAll(0); rg.ScratchAll(0);
                var third = rg.DealCard(0);
                Check(third.rigged, "3rd lifetime play is rigged");
                var rres = rg.ResolveCard(third, new[] { true, true, true });
                Check(rres.matchedSym == 2 && rres.payout == 60, "rig pays the top symbol (60)");
            }

            // Buying: locked cards refuse; funds gate; owned pile counts.
            {
                var bHost = new GameState();
                var bg = new DeskGame(bHost, new System.Random(46));
                bHost.coins = 1000;
                Check(!bg.Unlocked(1) && !bg.TryBuyCard(1), "Mini locked until 400 earned");
                Check(bg.TryBuyCard(0) && bHost.coins == 990 && bg.state.cardsOwned[0] == 1, "buy Two Win");
                bg.state.earnedThisRun = 400;
                Check(bg.Unlocked(1) && bg.TryBuyCard(1) && bHost.coins == 890, "Mini unlocks at 400 earned");
                bHost.coins = 5;
                Check(!bg.TryBuyCard(0), "refuses without funds");
            }

            // The loan phone: trigger, one-at-a-time, 50% garnish to repay.
            {
                var loHost = new GameState();
                var lo = new DeskGame(loHost, new System.Random(47));
                loHost.coins = 10;
                Check(lo.LoanAvailable && lo.TakeLoan(), "phone rings under 50 coins");
                Check(loHost.coins == 510 && lo.state.loanOwed == 750, "loan: +500 now, owe 750");
                Check(!lo.TakeLoan(), "one loan at a time");
                lo.state.rollsPending = 100;             // deterministic-ish income chunk
                long loanGross = lo.TearEnvelope();
                Check(lo.state.loanOwed == 750 - loanGross / 2 && loHost.coins == 510 + loanGross - loanGross / 2,
                      "garnish takes half of income");
                lo.state.loanOwed = 3;
                lo.state.rollsPending = 100;
                lo.TearEnvelope();
                Check(lo.state.loanOwed == 0, "loan repays fully, garnish stops");
            }

            // Upgrade costs + caps.
            {
                var uHost = new GameState();
                var ug = new DeskGame(uHost, new System.Random(48));
                Check(ug.UpgradeCost(DeskGame.Upgrade.Luck) == 60, "Luck L1 costs 60");
                uHost.coins = 153;   // 60 + 93
                Check(ug.TryBuyUpgrade(DeskGame.Upgrade.Luck) && ug.UpgradeCost(DeskGame.Upgrade.Luck) == 93,
                      "Luck L2 costs 93 (60*1.55)");
                Check(ug.TryBuyUpgrade(DeskGame.Upgrade.Luck) && uHost.coins == 0, "curve drains exactly");
                ug.state.upLuck = DeskGame.LUCK_CAP; uHost.coins = 999999;
                Check(!ug.TryBuyUpgrade(DeskGame.Upgrade.Luck), "Luck capped at 20");
            }

            // Prestige: gate, PP formula, the sacrifice vs the sacred.
            {
                var pHost = new GameState();
                var pg = new DeskGame(pHost, new System.Random(49));
                Check(!pg.PrestigeEligible, "not eligible at 0 earned");
                pg.state.earnedThisRun = DeskGame.PRESTIGE_AT;
                pg.state.loanOwed = 100;
                Check(!pg.PrestigeEligible, "loan blocks prestige");
                pg.state.loanOwed = 0;
                pHost.coins = 5000;
                pg.state.bigWinsThisRun = 3; pg.state.playsThisRun = 80;
                pg.state.upLuck = 7; pg.state.cardLevel[1] = 4; pg.state.cardsOwned[0] = 2;
                pg.state.rollsPending = 9; pg.state.stepAccumulator = 33;
                Check(System.Math.Abs(pg.PrestigePpPreview - 5f) < 1e-4, "PP = bigWins + plays/40 = 5");
                Check(pg.TryPrestige() && pg.state.prestigeCount == 1, "prestige fires");
                Check(pHost.coins == 0 && pg.state.upLuck == 0 && pg.state.cardLevel[1] == 0
                      && pg.state.cardsOwned[0] == 0 && pg.state.earnedThisRun == 0,
                      "sacrifice: wallet, upgrades, card levels, pile, bar");
                Check(pg.state.rollsPending == 9 && pg.state.stepAccumulator == 33
                      && System.Math.Abs(pg.state.prestigePoints - 5f) < 1e-4,
                      "sacred: pending rolls, accumulator, PP survive");
            }

            // Prestige tree (R2-4): costs, prereqs, caps, effects.
            {
                var tHost = new GameState();
                var tg = new DeskGame(tHost, new System.Random(50));
                tg.state.prestigePoints = 100f;
                Check(tg.PermCost(DeskGame.PERM_TINCOIN) == 3, "Tin Coin L1 costs 3 PP");
                Check(!tg.PermUnlockable(DeskGame.PERM_ROBOT) && !tg.TryBuyPerm(DeskGame.PERM_ROBOT),
                      "Robot Arm gated behind Tin Coin");
                Check(tg.TryBuyPerm(DeskGame.PERM_TINCOIN) && tg.PermLvl(DeskGame.PERM_TINCOIN) == 1, "buy Tin Coin");
                Check(tg.PermCost(DeskGame.PERM_TINCOIN) == 6, "Tin Coin L2 costs 6 (3*2)");
                Check(tg.TryBuyPerm(DeskGame.PERM_ROBOT) && tg.RobotOwned, "Robot Arm unlocks after Tin Coin");
                Check(!tg.TryBuyPerm(DeskGame.PERM_ROBOT), "Robot Arm is one-time");
                Check(System.Math.Abs(tg.state.prestigePoints - 85f) < 1e-3, "PP deducted (100-3-12)");

                tg.state.permNode[DeskGame.PERM_STICKY] = 4;
                tg.state.upLuck = 2;
                Check(tg.EffLuck == 6, "EffLuck = run luck + Sticky Thumb");
                tg.state.permNode[DeskGame.PERM_HAGGLER] = 3;
                Check(tg.CostOf(1) == 91, "Haggler: Mini costs 91 (100*0.91), got " + tg.CostOf(1));
                tg.state.permNode[DeskGame.PERM_SHOES] = 3;
                tg.state.permNode[DeskGame.PERM_FATSTACK] = 3;
                tg.state.rollsPending = 4000;
                long fat = tg.TearEnvelope();
                double fatPer = fat / 4000.0;   // EV = .9*6 + .1*30 = 8.4
                Check(fatPer > 7.9 && fatPer < 8.9, $"upgraded stride EV ~8.4, got {fatPer:F2}");
                tg.state.permNode[DeskGame.PERM_MAGNET] = 2;
                tg.state.bigWinsThisRun = 5; tg.state.playsThisRun = 200;
                Check(System.Math.Abs(tg.PrestigePpPreview - 12f) < 1e-3, "PP Magnet: (5+5)*1.2 = 12");
                tg.state.earnedThisRun = DeskGame.PRESTIGE_AT;
                tg.state.permNode[DeskGame.PERM_TINCOIN] = 3;
                Check(tg.TryPrestige() && tHost.coins == 600, "Tin Coin seeds 600 after prestige");
                Check(tg.PermLvl(DeskGame.PERM_STICKY) == 4, "perm nodes survive prestige");

                var lwHost = new GameState();
                var lw = new DeskGame(lwHost, new System.Random(51));
                lw.state.permNode[DeskGame.PERM_LAWYER] = 2;   // garnish 50% -> 30%
                lwHost.coins = 10;
                lw.TakeLoan();
                lw.state.rollsPending = 100;
                long lwGross = lw.TearEnvelope();
                Check(lw.state.loanOwed == 750 - (long)(lwGross * 0.3f),
                      "Better Lawyer garnishes only 30%");
            }

            // Marathoner: run-seconds add bonus step credit on top of the
            // pedometer steps (600 s * 2.8 steps/s * 2 lvl * 10% = 336).
            var rhost = new GameState();
            var crun = new CasinoGame(rhost, new System.Random(5));
            crun.state.permMarathoner = 2;
            crun.GrantActivity(1000, 600);
            Check(crun.state.ticketsBanked == 13 && crun.state.stepAccumulator == 36,
                  "marathoner: 1000+336 credit -> 13 tickets + 36 acc, got "
                  + crun.state.ticketsBanked + "/" + crun.state.stepAccumulator);

            // Ticket cap: over-cap step-chunks convert straight to coins.
            var caphost = new GameState();
            caphost.casino.ticketsBanked = 299;
            var ccap = new CasinoGame(caphost, new System.Random(6));
            ccap.GrantActivity(300, 0);
            Check(ccap.state.ticketsBanked == 300 && caphost.coins == 2 * CasinoGame.OVERFLOW_COINS_PER_TICKET,
                  "cap 300: 3 ticket-chunks -> 1 banked + 2 overflowed to coins");

            // Linear XP: 5000 steps per level, no curve.
            var g = new GamexGame();
            Check(g.state.level == 1, "starts at lv 1");
            g.AddActivity(5000, 0, 0);
            Check(g.state.level == 2, "5000 steps -> lv 2, got " + g.state.level);
            g.AddActivity(15000, 0, 0);
            Check(g.state.level == 5, "20000 total steps -> lv 5, got " + g.state.level);

            // Running counts 2x: 2500 run steps = 5000 effective XP.
            var g2 = new GamexGame();
            g2.AddActivity(2500, 2500, 0);   // all 2500 steps are running
            Check(g2.state.level == 2, "2500 run steps -> lv 2, got " + g2.state.level);

            // Lv 20 race trigger
            var g3 = new GamexGame { phase = AppPhase.Home };
            g3.AddActivity(95000, 0, 0);     // 95000 / 5000 = 19 levels, so lv 20
            Check(g3.state.level == 20, "Lv 20 reached, got " + g3.state.level);
            Check(g3.phase == AppPhase.RaceSelect, "race select triggered, got " + g3.phase);

            // Daily quests — rewards pay CASINO TICKETS (2/4/6/4/6 + an
            // 8-ticket all-clear bonus = 30/day max), converted from coins
            // when the unified wallet landed. The coin wallet must NOT move.
            var g4 = new GamexGame();
            g4.AddActivity(999, 0, 0);
            Check(g4.state.casino.ticketsBanked == 0, "no quest done at 999 steps, got " + g4.state.casino.ticketsBanked);
            g4.AddActivity(1, 0, 0);                    // total 1000        -> Walk1000 (+2)
            Check(g4.state.casino.ticketsBanked == 2, "walk 1000 -> +2 tickets, got " + g4.state.casino.ticketsBanked);
            g4.AddActivity(4000, 0, 0);                 // total 5000        -> Walk5000 (+4) = 6
            Check(g4.state.casino.ticketsBanked == 6, "walk 5000 -> +4 tickets, got " + g4.state.casino.ticketsBanked);
            g4.AddActivity(5000, 0, 0);                 // total 10000       -> Walk10000 (+6) = 12
            Check(g4.state.casino.ticketsBanked == 12, "walk 10000 -> +6 tickets, got " + g4.state.casino.ticketsBanked);
            g4.AddActivity(0, 0, 15 * 60);              // run 15 min        -> Run15 (+4) = 16
            Check(g4.state.casino.ticketsBanked == 16, "run 15 -> +4 tickets, got " + g4.state.casino.ticketsBanked);
            g4.AddActivity(0, 0, 15 * 60);              // total 30 min run  -> Run30 (+6) + all-bonus (+8) = 30
            Check(g4.state.casino.ticketsBanked == 30, "run 30 -> +6 +8 all-bonus tickets, got " + g4.state.casino.ticketsBanked);
            Check(g4.state.coins == 0, "quest rewards never touch the coin wallet");

            // EndDay resets daily counters, advances streak, weekly bonus
            var g5 = new GamexGame();
            for (int i = 0; i < 6; i++)
            {
                g5.AddActivity(1000, 0, 0);
                g5.EndDay();
            }
            Check(g5.state.streakDays == 6, "6-day streak, got " + g5.state.streakDays);
            int g5TicketsBefore = (int)g5.state.casino.ticketsBanked;
            g5.AddActivity(1000, 0, 0);                 // day 7
            g5.EndDay();
            Check(g5.state.streakDays == 7, "7-day streak, got " + g5.state.streakDays);
            // EndDay should also have credited the +5-TICKET streak bonus
            int dailyQuestTicketsAdded = 2;             // just walk-1000 today (+2)
            Check(g5.state.casino.ticketsBanked == g5TicketsBefore + dailyQuestTicketsAdded + 5,
                  "weekly streak bonus pays 5 tickets, got " + g5.state.casino.ticketsBanked);

            // Inactive day breaks streak
            var g6 = new GamexGame();
            g6.AddActivity(1000, 0, 0); g6.EndDay();
            Check(g6.state.streakDays == 1, "streak 1 after active day");
            g6.EndDay();                                 // no steps -> reset
            Check(g6.state.streakDays == 0, "streak resets on inactive day");

            // Shop (Phase 3): SetCatalog drives the storefront. Single-piece
            // buy still works through TryBuy; new TryBuySet pays the bundle
            // price (80% of summed pieces) and grants every piece atomically.
            var g7 = new GamexGame();
            g7.state.coins = 2000;
            // FindSet — SetCatalog index isn't stable across launch reorderings
            // (champ_dark_knight is now at [0], elf_paladin is in the deferred
            // section). Look up by id so the test survives future shuffling.
            var paladin = GamexGame.FindSet("elf_paladin");
            Check(paladin != null, "elf_paladin set exists in catalog");
            var sword = System.Array.Find(paladin.pieces, d => d.id == "elfpaladin_sword");
            Check(sword != null && sword.price == 200, "paladin sword = 200 coins, got " + (sword?.price.ToString() ?? "null"));
            Check(g7.TryBuy(sword), "buy paladin sword");
            Check(g7.state.coins == 1800, "1800 after sword, got " + g7.state.coins);
            g7.ToggleEquip("elfpaladin_sword");
            Check(g7.IsEquipped("elfpaladin_sword"), "equipped");
            g7.ToggleEquip("elfpaladin_sword");
            Check(!g7.IsEquipped("elfpaladin_sword"), "unequipped");

            // Bundle buy — Paladin set is a direct 900-coin bundle, all 4 pieces
            // granted. (Discount math was dropped — sets are atomic, bundlePrice
            // is set explicitly on each SetDef.)
            var g8 = new GamexGame();
            g8.state.coins = 100000;
            Check(paladin.BundlePrice == 900, "paladin bundle = 900, got " + paladin.BundlePrice);
            Check(g8.TryBuySet(paladin), "buy paladin set");
            Check(g8.state.owned.Count == 4, "owned 4 after bundle, got " + g8.state.owned.Count);
            Check(g8.state.coins == 100000 - 900, "coins after bundle");

            // Knight Set chain — 10 consecutive 5k days unlock each piece.
            // Pre-load lifetime steps so AddActivity's level recompute lands at Lv 20+.
            var g9 = new GamexGame();
            g9.state.totalSteps = 95_000;        // -> Lv 20 after the first 5k tick
            for (int i = 0; i < 10; i++)
            {
                g9.AddActivity(5000, 0, 0);
                g9.EndDay();
            }
            // Updated 2026-06 (commit 926ad41 + d9ef2b8): chain now grants the
            // WHOLE 6-piece set atomically after 10 days (was 10 days per piece).
            // knightChainStage = KnightSet.Length acts as the "earned" flag.
            Check(g9.IsOwned("knight_chest"),    "10 days @ 5k+ -> chest earned");
            Check(g9.IsOwned("knight_sword"),    "10 days @ 5k+ -> sword earned (atomic 6-piece grant)");
            Check(g9.state.knightChainStage == GamexGame.KnightSet.Length,
                  "chain marked earned, got " + g9.state.knightChainStage);
            // Earning the set short-circuits further chain ticking; progress
            // stays at 0 because the gate (knightChainStage < Length) is false.
            g9.AddActivity(5000, 0, 0); g9.EndDay();
            g9.EndDay();
            Check(g9.state.knightChainProgress == 0, "post-earn chain stays at 0 progress");
            // Chain inactive below Lv 20: pre-empty totalSteps stays at level 1.
            var g10 = new GamexGame();
            for (int i = 0; i < 10; i++) { g10.AddActivity(1000, 0, 0); g10.EndDay(); }
            Check(g10.state.knightChainStage == 0, "chain ignored below Lv 20");

            // Save round-trip — use elfpaladin_sword (the piece g7 bought
            // above) since Phase 2 dropped the old "sword_wood" tier item.
            g7.state.gender = (int)Gender.Female;
            g7.state.curse  = (int)Curse.Weakness;
            string json = JsonUtility.ToJson(g7.state);
            var loaded = JsonUtility.FromJson<GameState>(json);
            Check(loaded.gender == 2 && loaded.curse == 1 && loaded.owned.Contains("elfpaladin_sword"),
                  "save round-trip");

            if (fails == 0) Debug.Log("[LOGIC OK]");
            else Debug.LogError("[LOGIC FAILED] count=" + fails);
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }

    // Headless playmode smoke + screenshot of every UI panel.
    public static class SmokeTest
    {
        public static void Run()
        {
            SessionState.SetBool("GAMEX_SMOKE", true);
            EditorApplication.EnterPlaymode();
        }

        // App Store screenshot entry point — captures a curated set of polished
        // frames at iPhone 6.5" required resolution (1284x2778) for use in
        // App Store Connect's screenshot upload. Distinct from RunSmoke()
        // which captures every state at smoke-test resolution (720x1280).
        public static void RunAppStoreShots()
        {
            SessionState.SetBool("GAMEX_APPSTORE", true);
            EditorApplication.EnterPlaymode();
        }

        // Run BEFORE GameRunner.Awake() so SaveSystem.Load() returns null and
        // the runner spins up a fresh GameState. Without this the smoke test
        // inherits whatever the developer's last manual Play Mode session
        // wrote to gamex_save.json — owned items, race choice, equipped set
        // and all — which makes shop/inventory captures non-deterministic.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void WipeBeforeSmoke()
        {
            if (!SessionState.GetBool("GAMEX_SMOKE", false) &&
                !SessionState.GetBool("GAMEX_APPSTORE", false)) return;
            SaveSystem.Wipe();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (SessionState.GetBool("GAMEX_APPSTORE", false))
            {
                var go = new GameObject("Gamex_AppStore");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<AppStoreRunner>();
                return;
            }
            if (!SessionState.GetBool("GAMEX_SMOKE", false)) return;
            var smokeGo = new GameObject("Gamex_Smoke");
            Object.DontDestroyOnLoad(smokeGo);
            smokeGo.AddComponent<SmokeRunner>();
        }
    }

    public class SmokeRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);

            var runner = FindFirstObjectByType<GameRunner>();
            if (runner == null || runner.Game == null) { Debug.LogWarning("[Smoke] no runner"); EditorApplication.Exit(1); yield break; }

            // Title screen — cold-launch landing. Wait for the wordmark +
            // tagline fade-in (~0.7s) and a beat of the Start Game pulse so
            // the steady-state composition lands in the screenshot, then
            // click the Start Game button. The button arms a 0.4s exit
            // fade before the actual phase transition fires, so we capture
            // a mid-fade frame for the transition baseline and then wait
            // the rest of the fade out before continuing to the opening.
            yield return new WaitForSecondsRealtime(0.8f);
            Capture("/tmp/gamex_title.png");
            var startBtn = GameObject.Find("StartGame")?.GetComponent<UnityEngine.UI.Button>();
            if (startBtn != null) startBtn.onClick.Invoke();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_title_fadeout.png");                // mid-fade snapshot
            yield return new WaitForSecondsRealtime(0.4f);          // let exit fade complete + Refresh land

            // Opening sequence (first-run only).
            Capture("/tmp/gamex_op1_intro.png");
            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op2_hero.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_op3_curse_looms.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_curse.png");

            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_curse_anim.png");
            yield return new WaitForSecondsRealtime(0.9f);
            Capture("/tmp/gamex_op4_amnesia.png");

            runner.Game.TapAdvanceOpening();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_first_mirror.png");

            runner.Game.FinishFirstMirror();

            // Push to Lv 6 (still pre-race) so the home shot has a non-trivial avatar.
            runner.Game.AddActivity(25000, 0, 0);   // 5 levels worth
            runner.Game.state.coins = 60;
            runner.Game.state.streakDays = 7;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            // First-run tutorial step 1 is up — capture it before dismissing so
            // the coach-mark spotlight + caption is visible in smoke output.
            Capture("/tmp/gamex_tutorial_step1.png");

            // Click through all 3 tutorial steps so subsequent home captures
            // show the actual UI rather than the coach-mark overlay. The Next
            // button is re-found per iteration so we don't hold a stale ref
            // after the overlay re-deactivates on the final step. Capture
            // each step after-click so we have a screenshot per coach-mark.
            string[] tutCaptures = { "/tmp/gamex_tutorial_step2.png", "/tmp/gamex_tutorial_step3.png", null };
            for (int i = 0; i < 3; i++)
            {
                var tutGO = GameObject.Find("TutorialOverlay");
                var nextBtn = FindChildButton(tutGO, "Next");
                if (nextBtn == null) break;
                nextBtn.onClick.Invoke();
                yield return null; yield return new WaitForSecondsRealtime(0.1f);
                if (tutCaptures[i] != null) Capture(tutCaptures[i]);
            }
            yield return null;
            Capture("/tmp/gamex_home.png");

            // Trigger one more level-up after the prev-level tracker has initialised
            // so the LEVEL UP toast + Lv pulse are mid-animation in the capture.
            runner.Game.AddActivity(5000, 0, 0);
            yield return null; yield return new WaitForSecondsRealtime(0.35f);  // mid-bell of 1.2s
            Capture("/tmp/gamex_home_levelup.png");

            // Wait out the level-up window, then bump coins so only the
            // coin floater is animating in the next capture.
            yield return new WaitForSecondsRealtime(1.3f);
            runner.Game.state.coins += 25;
            yield return null; yield return new WaitForSecondsRealtime(0.4f);  // mid-rise of 1.5s
            Capture("/tmp/gamex_home_coinfloat.png");

            // Daily-ritual transition. Reset todaySteps + idle one Refresh so
            // the candle tracker baselines at 0, then step into 2 and 4
            // candles to capture mid-flash + crown-burst frames.
            yield return new WaitForSecondsRealtime(1.6f);
            runner.Game.state.todaySteps = 0;
            yield return null; yield return new WaitForSecondsRealtime(0.1f);   // baseline
            runner.Game.state.todaySteps = 2600;
            yield return null; yield return new WaitForSecondsRealtime(0.25f);  // mid-flash of 0.55s
            Capture("/tmp/gamex_ritual_2candles.png");
            yield return new WaitForSecondsRealtime(0.5f);                      // let candle flash settle
            runner.Game.state.todaySteps = 5100;
            yield return null; yield return new WaitForSecondsRealtime(0.4f);   // mid-crown-burst of 0.8s
            Capture("/tmp/gamex_ritual_crown.png");

            // Lv 20 race-select flow
            runner.Game.AddActivity(75000, 0, 0);   // total 100k -> lv 21
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_race_select.png");
            runner.Game.SetRaceAndGender(Race.Elf, Gender.Male);
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("/tmp/gamex_race_anim.png");
            yield return new WaitForSecondsRealtime(1.2f);

            // Phase 2: shop tier swords/armors retired. Equip the 5 knight
            // pieces (chain-quest reward in real play) so the home capture
            // shows the SPUM-coord overlay system rendering all five on the
            // chibi race form.
            foreach (var p in GamexGame.KnightSet)
            {
                if (!runner.Game.state.owned.Contains(p.id))   runner.Game.state.owned.Add(p.id);
                if (!runner.Game.state.equipped.Contains(p.id)) runner.Game.state.equipped.Add(p.id);
            }
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_home_after_race.png");

            // Force a partial Knight Set chain so the progress bar shows ~40%
            // fill in the capture (Lv 20+ unlocks the chain).
            runner.Game.state.knightChainProgress = 4;
            runner.Game.GoQuests();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_training.png");

            runner.Game.GoShop();
            runner.Game.state.coins = 2000;
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_shop.png");

            // Wait out the floater from the state.coins=2000 bump so the next
            // capture shows a clean "+50" and not the aggregated total.
            yield return new WaitForSecondsRealtime(1.6f);
            runner.Game.state.coins += 50;
            yield return null; yield return new WaitForSecondsRealtime(0.45f);   // ~30% into 1.5s
            Capture("/tmp/gamex_shop_coinfloat.png");
            yield return new WaitForSecondsRealtime(1.2f);                       // let floater settle

            // Buy two sets so the next shop capture shows the gold-glow tint
            // for owned set cards next to the default-tan unowned cards.
            var ownedSet = GamexGame.FindSet("champ_squire");
            if (ownedSet != null) { runner.Game.state.coins = 10000; runner.Game.TryBuySet(ownedSet); }
            var ownedSet2 = GamexGame.FindSet("champ_pink_archer");
            if (ownedSet2 != null) { runner.Game.TryBuySet(ownedSet2); }

            // Trigger a press bounce directly via the PressBounce component so
            // we capture mid-shrink without GoSetDetail swapping phases. This
            // verifies both the press scale tween AND the gold-glow tint side
            // by side (champion squire + pink archer owned, others not).
            var topCard = GameObject.Find("SetCard_champ_dark_knight");
            var bounce  = topCard != null ? topCard.GetComponent<PressBounce>() : null;
            if (bounce != null) bounce.Trigger();
            yield return null; yield return new WaitForSecondsRealtime(0.07f);  // ~30% into 0.22s, peak shrink
            Capture("/tmp/gamex_shop_owned.png");

            // Phase 3 set detail — tap the first set card programmatically.
            if (GamexGame.SetCatalog.Length > 0)
            {
                runner.Game.GoSetDetail(GamexGame.SetCatalog[0].id);
                yield return null; yield return new WaitForSecondsRealtime(0.2f);
                Capture("/tmp/gamex_set_detail.png");

                // Wait out any prior floater, then bump for a clean capture.
                yield return new WaitForSecondsRealtime(1.6f);
                runner.Game.state.coins += 75;
                yield return null; yield return new WaitForSecondsRealtime(0.45f);
                Capture("/tmp/gamex_set_detail_coinfloat.png");
            }

            // Phase 4 — buy + apply a skin so home + inventory show the
            // override portrait (knight gear is hidden because skins carry
            // their own painted-in art).
            // Buy a Champion set so the new outfit inventory has something to
            // show beyond a single skin cell.
            var darkKnight = GamexGame.FindSet("champ_dark_knight");
            if (darkKnight != null)
            {
                runner.Game.state.coins = darkKnight.BundlePrice + 10000;
                runner.Game.TryBuySet(darkKnight);
            }
            // Iron Knight (frameCount=11) exercises Phase 5e3 animation tick;
            // Golden Retriever pet alongside exercises the polish-round-3 pet
            // companion rendering inside the mirror.
            var demoSkin = GamexGame.FindSkin("lm_iron_knight");
            if (demoSkin != null)
            {
                if (runner.Game.state.coins < demoSkin.price + 500) runner.Game.state.coins = demoSkin.price + 500;
                runner.Game.TryBuySkin(demoSkin);
                runner.Game.ApplySkin(demoSkin.id);
                var pet = GamexGame.FindSkin("pet_dog_golden");
                if (pet != null)
                {
                    runner.Game.TryBuySkin(pet);
                    runner.Game.ApplySkin(pet.id);
                }
                runner.Game.GoHome();
                yield return null; yield return new WaitForSecondsRealtime(0.4f);
                Capture("/tmp/gamex_home_skin.png");
            }

            // M5f Inventory — outfits-only grid. With all 6 knight pieces in
            // state.owned (granted above) + the bought champ_dark_knight set
            // + the applied lm_iron_knight skin, the grid should show:
            //   1. champ_dark_knight outfit cell (fullyOwned via SetCatalog)
            //   2. Knight Set outfit cell (fullyOwned via KnightOutfit fallback)
            //   3. one skin cell per owned skin (lm_iron_knight active badge)
            // The Knight Set cell here is the regression check for the
            // "chain quest grants pieces but no inventory cell" bug.
            runner.Game.GoInventory();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_inventory.png");

            // Settings panel — audio mutes, HealthKit status, reset button.
            // Captured with the SFX row toggled off so the inverted state
            // reads in the screenshot ("Sound effects: OFF").
            runner.Game.state.sfxMuted = true;
            runner.Game.GoSettings();
            yield return null; yield return new WaitForSecondsRealtime(0.2f);
            Capture("/tmp/gamex_settings.png");

            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("[SmokeTest] ran cleanly, exiting");
            EditorApplication.Exit(0);
        }

        // Walk an overlay's transform tree to find a child Button by name.
        // Used only by the smoke harness to click through coach-marks without
        // exposing a public API on Hud.
        static UnityEngine.UI.Button FindChildButton(GameObject root, string childName)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName)
                    return t.GetComponent<UnityEngine.UI.Button>();
            return null;
        }

        static void Capture(string path) => Snap.Capture(path, 720, 1280);
    }

    // Shared screen-capture helper. Both SmokeRunner and AppStoreRunner route
    // through here so the canvas-temporary-reparenting logic stays in one place.
    // Width/height per call: smoke uses 720x1280 (fast iteration), App Store
    // uses 1284x2778 (Apple's required 6.5-inch portrait size).
    //
    // CanvasScaler override: CanvasScaler.ScaleWithScreenSize uses Screen.height
    // (= GameView height, typically ~600px in headless batchmode) for layout,
    // NOT the RT size. Without the override the UI gets laid out at GameView
    // dimensions then bilinear-upscaled to the RT — all text turns to mush
    // (most visible on the title subtitle: "Walk. Train. Reign." rendered as
    // "(2x). 9xxx. 9x999" garbage). We temporarily switch every CanvasScaler
    // to ConstantPixelSize with scaleFactor = RT.height / 1920 (the equivalent
    // of the original matchHeight=1 scaling, but pinned to our render target),
    // then restore on exit so this doesn't leak into the live editor session.
    internal static class Snap
    {
        public static void Capture(string path, int width, int height)
        {
            var cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            if (cam == null) { Debug.LogWarning("[Snap] no camera"); return; }

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var savedMode = new System.Collections.Generic.Dictionary<Canvas, RenderMode>();
            var savedCam  = new System.Collections.Generic.Dictionary<Canvas, Camera>();
            var savedDist = new System.Collections.Generic.Dictionary<Canvas, float>();
            var savedScalerMode   = new System.Collections.Generic.Dictionary<Canvas, UnityEngine.UI.CanvasScaler.ScaleMode>();
            var savedScalerFactor = new System.Collections.Generic.Dictionary<Canvas, float>();
            foreach (var c in canvases)
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    savedMode[c] = c.renderMode;
                    savedCam[c]  = c.worldCamera;
                    savedDist[c] = c.planeDistance;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 1f;
                }
                var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    savedScalerMode[c]   = scaler.uiScaleMode;
                    savedScalerFactor[c] = scaler.scaleFactor;
                    scaler.uiScaleMode   = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor   = height / 1920f;
                }
            }
            Canvas.ForceUpdateCanvases();

            var rt = new RenderTexture(width, height, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            foreach (var kv in savedMode)
            {
                kv.Key.renderMode = kv.Value;
                kv.Key.worldCamera = savedCam[kv.Key];
                kv.Key.planeDistance = savedDist[kv.Key];
            }
            foreach (var kv in savedScalerMode)
            {
                var scaler = kv.Key.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = kv.Value;
                    scaler.scaleFactor = savedScalerFactor[kv.Key];
                }
            }
            Canvas.ForceUpdateCanvases();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            try { File.WriteAllBytes(path, tex.EncodeToPNG()); Debug.Log($"[Snap] wrote {width}x{height} {path}"); }
            catch (System.Exception e) { Debug.LogWarning("[Snap] write failed: " + e.Message); }

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
        }

        // Supersampled capture for App Store screenshots: render at renderW×renderH
        // (chosen as an integer multiple of the 1080×1920 canvas reference so
        // CanvasScaler.scaleFactor is exactly an integer — every pixel-font glyph
        // stroke lands on an integer pixel boundary, no bilinear interpolation),
        // then downsample to outW×outH with nearest-neighbor so each output pixel
        // picks one discrete source pixel. The result avoids the soft "1.447x
        // bilinear stretch" look that direct 1284×2778 rendering produces — text
        // strokes stay at integer widths (1 or 2 pixels), no half-density grays.
        //
        // Cost: render buffer is 4x the pixels (31MB vs 11MB at 24-bit). Fine for
        // an offline screenshot pass, would be a problem in a hot loop.
        public static void CaptureSupersampled(string path, int renderW, int renderH, int outW, int outH)
        {
            var cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            if (cam == null) { Debug.LogWarning("[Snap] no camera"); return; }

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var savedMode = new System.Collections.Generic.Dictionary<Canvas, RenderMode>();
            var savedCam  = new System.Collections.Generic.Dictionary<Canvas, Camera>();
            var savedDist = new System.Collections.Generic.Dictionary<Canvas, float>();
            var savedScalerMode   = new System.Collections.Generic.Dictionary<Canvas, UnityEngine.UI.CanvasScaler.ScaleMode>();
            var savedScalerFactor = new System.Collections.Generic.Dictionary<Canvas, float>();
            foreach (var c in canvases)
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    savedMode[c] = c.renderMode;
                    savedCam[c]  = c.worldCamera;
                    savedDist[c] = c.planeDistance;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 1f;
                }
                var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    savedScalerMode[c]   = scaler.uiScaleMode;
                    savedScalerFactor[c] = scaler.scaleFactor;
                    scaler.uiScaleMode   = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor   = renderH / 1920f;
                }
            }
            Canvas.ForceUpdateCanvases();

            var rt = new RenderTexture(renderW, renderH, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            foreach (var kv in savedMode)
            {
                kv.Key.renderMode = kv.Value;
                kv.Key.worldCamera = savedCam[kv.Key];
                kv.Key.planeDistance = savedDist[kv.Key];
            }
            foreach (var kv in savedScalerMode)
            {
                var scaler = kv.Key.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = kv.Value;
                    scaler.scaleFactor = savedScalerFactor[kv.Key];
                }
            }
            Canvas.ForceUpdateCanvases();

            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var src = new Texture2D(renderW, renderH, TextureFormat.RGB24, false);
            src.ReadPixels(new Rect(0, 0, renderW, renderH), 0, 0);
            src.Apply();
            RenderTexture.active = prevActive;

            var dst = NearestNeighborDownscale(src, outW, outH);

            try { File.WriteAllBytes(path, dst.EncodeToPNG()); Debug.Log($"[Snap] wrote {outW}x{outH} (rendered {renderW}x{renderH}) {path}"); }
            catch (System.Exception e) { Debug.LogWarning("[Snap] write failed: " + e.Message); }

            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            Object.DestroyImmediate(rt);
        }

        // Pure-CPU nearest-neighbor resize via Color32 array math. Faster than
        // GetPixel/SetPixel per call by ~50x because GetPixels32 batches the
        // texture readback into a single managed array. For 2160×3840 -> 1284×2778
        // (~3.6M output pixels) this runs in ~80ms in batchmode, which is
        // negligible compared to the cam.Render + ReadPixels cost.
        static Texture2D NearestNeighborDownscale(Texture2D src, int dstW, int dstH)
        {
            var dst = new Texture2D(dstW, dstH, TextureFormat.RGB24, false);
            var srcPx = src.GetPixels32();
            var dstPx = new Color32[dstW * dstH];
            int srcW = src.width, srcH = src.height;
            for (int y = 0; y < dstH; y++)
            {
                int sy = (int)((long)y * srcH / dstH);
                if (sy >= srcH) sy = srcH - 1;
                int srcRow = sy * srcW;
                int dstRow = y * dstW;
                for (int x = 0; x < dstW; x++)
                {
                    int sx = (int)((long)x * srcW / dstW);
                    if (sx >= srcW) sx = srcW - 1;
                    dstPx[dstRow + x] = srcPx[srcRow + sx];
                }
            }
            dst.SetPixels32(dstPx);
            dst.Apply();
            return dst;
        }
    }

    // App Store screenshot orchestrator. Goes through the same opening flow
    // SmokeRunner uses but skips the per-step transition shots — produces
    // only 6 curated stills at 1284x2778 (iPhone 6.5") suitable for the
    // App Store Connect screenshot upload. Output: /tmp/appstore_*.png.
    public class AppStoreRunner : MonoBehaviour
    {
        // Direct render at Apple's required 6.5-inch portrait size. We
        // briefly tried a 2x supersample-then-NN-downscale pipeline to dodge
        // bilinear softness at the non-integer 1.447x canvas scale, but the
        // supersample render aspect (9:16, matching canvas reference) didn't
        // match the output aspect (9:19.5) — NN downscale across mismatched
        // aspects vertically stretched every UI element by ~22%. Direct
        // render preserves correct proportions and faithfully reproduces what
        // a real iPhone Pro Max user sees: minor bilinear softness at pixel
        // level, invisible at typical viewing distance + PPI.
        const int W = 1284, H = 2778;
        static void Shot(string name) => Snap.Capture($"/tmp/appstore_{name}.png", W, H);

        IEnumerator Start()
        {
            // Wait for first Refresh + canvas layout to settle.
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);

            var runner = FindFirstObjectByType<GameRunner>();
            if (runner == null || runner.Game == null)
            {
                Debug.LogWarning("[AppStore] no runner");
                EditorApplication.Exit(1);
                yield break;
            }

            // 1. Title — the marquee shot. Wait for the wordmark fade-in
            //    to complete so "Gamexercise" + tagline read clean.
            yield return new WaitForSecondsRealtime(1.0f);
            Shot("01_title");

            // Skip through the opening cinematic to set up gameplay state.
            // We won't capture the narrative beats — they're better as
            // App Preview video frames, not still screenshots.
            var startBtn = GameObject.Find("StartGame")?.GetComponent<UnityEngine.UI.Button>();
            if (startBtn != null) startBtn.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.6f);

            // OpeningHeroShown — the dark_knight reveal. This IS a marquee
            // shot: full-color knight, throne background, "tap to continue".
            // Sets up the curse narrative without spoiling the cursed form.
            runner.Game.TapAdvanceOpening();
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("02_opening_hero");

            // Burn through the rest of the opening + curse + amnesia + mirror
            // without capturing. End state: phase=Home with Lv 1 skeleton.
            runner.Game.TapAdvanceOpening();  // -> OpeningCurseLooms
            yield return new WaitForSecondsRealtime(0.2f);
            runner.Game.TapAdvanceOpening();  // -> CurseAnim (Gluttony retired; auto-sets Weakness)
            yield return new WaitForSecondsRealtime(2.0f);  // curse anim completes
            runner.Game.TapAdvanceOpening();  // OpeningAmnesia -> FirstMirror
            yield return new WaitForSecondsRealtime(0.3f);
            runner.Game.FinishFirstMirror();  // -> Home
            yield return new WaitForSecondsRealtime(0.3f);

            // Dismiss the first-run tutorial coach-marks (3 steps).
            for (int i = 0; i < 4; i++)
            {
                var tutGO = GameObject.Find("TutorialOverlay");
                if (tutGO == null || !tutGO.activeInHierarchy) break;
                var btn = FindButton(tutGO, "Next");
                if (btn == null) break;
                btn.onClick.Invoke();
                yield return null; yield return new WaitForSecondsRealtime(0.15f);
            }

            // Set up rich gameplay state: Lv 15 (post-race), realistic coin
            // balance, 7-day streak, dark_knight outfit fully equipped. Race=Elf
            // so the avatar renders with race-form-aware overlays + outfit
            // composite from Sets/champ_dark_knight.png.
            //
            // Coin count is set to 850 (not 12000) so the screenshot reads as
            // a believable mid-game state — the player has earned enough from
            // dailies + streak bonuses to buy a Champion set, kept ~850 spare.
            runner.Game.AddActivity(70000, 0, 0);  // ~Lv 15
            runner.Game.state.coins = 850;
            runner.Game.state.streakDays = 7;
            runner.Game.state.totalSteps = 70000;
            // Mark some daily quests as complete for the Quests shot.
            runner.Game.state.questDone[0] = true;  // Walk 1k
            runner.Game.state.questDone[1] = true;  // Walk 5k
            // Race awakens at Lv 20 normally; we force it here for a clean
            // outfit-composite render on the chibi race form. Set BEFORE
            // bumping XP so the next AddActivity doesn't gate on race==0.
            runner.Game.state.race = (int)Race.Elf;
            runner.Game.state.gender = (int)Gender.Male;
            // Equip dark_knight set.
            var darkKnight = GamexGame.FindSet("champ_dark_knight");
            if (darkKnight != null)
            {
                foreach (var p in darkKnight.pieces)
                {
                    if (!runner.Game.state.owned.Contains(p.id))    runner.Game.state.owned.Add(p.id);
                    if (!runner.Game.state.equipped.Contains(p.id)) runner.Game.state.equipped.Add(p.id);
                }
            }
            // Also own a couple of other sets so Shop + Inventory have variety.
            var skullWarrior = GamexGame.FindSet("champ_skull_warrior");
            if (skullWarrior != null)
                foreach (var p in skullWarrior.pieces)
                    if (!runner.Game.state.owned.Contains(p.id)) runner.Game.state.owned.Add(p.id);
            var crimsonWarrior = GamexGame.FindSet("champ_crimson_warrior");
            if (crimsonWarrior != null)
                foreach (var p in crimsonWarrior.pieces)
                    if (!runner.Game.state.owned.Contains(p.id)) runner.Game.state.owned.Add(p.id);

            runner.Game.GoHome();
            // Coin-floater rises for 1.5s then fades; AddActivity above earned
            // +9 from quest rewards which fires the floater, and assigning
            // state.coins=850 fires another. Wait long enough for both to
            // clear so the static counter reads "850" without "+841" sitting
            // on top of it (the 0.5s wait was the source of the prior bug
            // where Shop captured a still-rising floater overlapping the
            // total — "12000" + "+12000" rendered as "412000").
            yield return null; yield return new WaitForSecondsRealtime(2.5f);

            // 3. Home — the gameplay hero shot. Avatar in dark_knight set,
            //    coin counter, level, streak, XP bar all visible.
            Shot("03_home");

            // 4. Shop — set cards grid with dark_knight + skull_warrior +
            //    crimson_warrior owned (gold-glow tint) next to unowned.
            runner.Game.GoShop();
            yield return null; yield return new WaitForSecondsRealtime(0.5f);
            Shot("04_shop");

            // 5. Inventory — paper-doll with dark_knight equipped + the
            //    three owned-outfit cells in the storage grid below.
            runner.Game.GoInventory();
            yield return null; yield return new WaitForSecondsRealtime(0.5f);
            Shot("05_inventory");

            // 6. Quests — daily quests list with first two complete + the
            //    lifetime totals + knight chain progress widget.
            runner.Game.state.knightChainProgress = 6;
            runner.Game.GoQuests();
            yield return null; yield return new WaitForSecondsRealtime(0.5f);
            Shot("06_quests");

            yield return new WaitForSecondsRealtime(0.3f);
            Debug.Log("[AppStore] captured 6 shots to /tmp/appstore_*.png");
            EditorApplication.Exit(0);
        }

        static UnityEngine.UI.Button FindButton(GameObject root, string childName)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName)
                    return t.GetComponent<UnityEngine.UI.Button>();
            return null;
        }
    }
}
#endif
