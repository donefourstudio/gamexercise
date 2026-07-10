using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using Gamex.Core;
using Gamex.Platform;

namespace Gamex.Game
{
    public partial class Hud
    {
        // ============================================================
        // The Casino (docs/casino-mvp-plan.md) — a flag-gated section off
        // Home. Deadpan, literal scratch-lottery: no lore, instantly
        // legible symbols (CHERRY / BELL / BAR / 7), Coins, Prestige.
        //   CasinoLobby    — coins / PP / tickets + step->ticket progress,
        //                    doors to SCRATCHERS / SLOTS(P4) / UPGRADES,
        //                    PRESTIGE button when eligible
        //   CasinoScratch  — the "Lucky 7s" ticket with true drag-to-
        //                    scratch foil (P3), 777 fanfare, coin count-up
        //   CasinoUpgrades — run upgrades (coins) + permanent (PP)
        //   CasinoPrestige — cash-out confirm + celebration ceremony (P3)
        //
        // Unlike the old game's callback routing, these screens hold the
        // CasinoGame core directly (_casino, injected via the Hud ctor):
        // the play result must flow back into the reveal animation
        // synchronously, and every button is a 1:1 core call anyway.
        // Navigation goes through _onCasinoNav (incl. BACK to Home) so
        // GameRunner stays the owner of the phase field.
        //
        // Reveal model: CasinoGame pre-rolls the outcome the moment a
        // ticket is played (Play()). The symbols are set under the foil at
        // deal time — a win shows three matching symbols of the rolled
        // tier; a bust shows a near-miss (a pair + one odd symbol,
        // occasionally two agonising 7s). Scratching the foil (ScratchFoil)
        // physically uncovers them; a cell counts as revealed at ~55%
        // scratched. Because coins are credited at play time, the on-screen
        // counter subtracts the pending ticket's payout until every cell is
        // revealed (CasinoDisplayCoins), then counts up to the new total.
        //
        // NO batch/"scratch all": every ticket is hand-played. Backlog
        // tedium is the designed pressure that sells the earned Auto
        // Scratcher unlock (P4) — automation is a carrot, never a freebie.
        // ============================================================

        GameObject _casinoLobbyPanel, _casinoScratchPanel, _casinoUpgradesPanel, _casinoPrestigePanel;

        // ---- Lobby ----
        TMP_Text _lobbyCoins, _lobbyPp, _lobbyTickets, _lobbySteps, _lobbyBarLabel;
        RectTransform _lobbyBarFill;
        GameObject _lobbyPrestigeBtn;
        const float CASINO_BAR_W = 700f;

        // ---- Scratch ----
        TMP_Text _scCoins, _scTicketsLeft, _scResultLine, _scEmptyLabel;
        GameObject _scCard;
        readonly GameObject[]  _scCells = new GameObject[3];
        readonly TMP_Text[]    _scCellTexts = new TMP_Text[3];
        readonly bool[]        _scCellFlipped = new bool[3];
        readonly CasinoTier[]  _scCellSymbols = new CasinoTier[3];
        readonly ScratchFoil[] _scFoils = new ScratchFoil[3];
        PlayResult _scPending;
        bool  _scHasPending;
        string _scSummary;                      // Auto Scratcher batch summary, shown until the next deal
        GameObject _scNextBtn, _scAutoBtn;
        TMP_Text   _scAutoLabel;
        // juice state
        float _scShakeT, _scResultPopT;
        float _scCoinsShown = -1f;              // -1 = snap to target on first frame
        static readonly Vector2 SC_CARD_POS = new Vector2(0f, 250f);

        // ---- Slots (P4) — same pre-rolled engine, reel theatre ----
        GameObject _casinoSlotsPanel, _slMachine, _slSpinBtn;
        TMP_Text _slCoins, _slTickets, _slResultLine, _slEmptyLabel;
        readonly TMP_Text[]   _slReelTexts = new TMP_Text[3];
        readonly CasinoTier[] _slFinal   = new CasinoTier[3];
        readonly bool[]       _slStopped = new bool[3];
        readonly float[]      _slStopAt  = new float[3];
        readonly float[]      _slStopPop = new float[3];
        PlayResult _slPending;
        bool  _slHasPending;
        float _slSpinT, _slCycleT, _slShakeT, _slResultPopT;
        float _slCoinsShown = -1f;
        static readonly Vector2 SL_MACHINE_POS = new Vector2(0f, 280f);
        bool SlotsAllStopped => _slStopped[0] && _slStopped[1] && _slStopped[2];

        // ---- Upgrades ----
        TMP_Text _upWallet;
        (TMP_Text lvl, TMP_Text cost, Button btn) _rowPayout, _rowLuck, _rowStride,
                                                  _rowGTouch, _rowLDice, _rowMara, _rowAuto;

        // ---- Prestige ----
        GameObject _prestigeConfirmGroup, _prestigeCelebGroup;
        TMP_Text _prestigeGainLine, _prestigeResetLine, _prestigeCelebTitle, _prestigeCelebPp;
        float _prestigeCelebT;
        bool  _prestigeCelebrating;

        // ---- coin burst pool (jackpots + prestige ceremony) ----
        const int BURST_POOL = 26;
        Image[]   _burstCoins;
        Vector2[] _burstVel;
        float[]   _burstSpin;
        bool      _burstAny;

        // ============================================================
        // Lobby
        // ============================================================
        void BuildCasinoLobby(Transform root)
        {
            _casinoLobbyPanel = MkFullPanel("CasinoLobby", root);

            MkText("Title", _casinoLobbyPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - SAFE_AREA_TOP_INSET), new Vector2(900f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "CASINO";

            _lobbyCoins = MkText("Coins", _casinoLobbyPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -260f - SAFE_AREA_TOP_INSET), new Vector2(900f, 70f),
                FS_BTN, TextAnchor.UpperCenter, TextWhite);
            _lobbyPp = MkText("Pp", _casinoLobbyPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -340f - SAFE_AREA_TOP_INSET), new Vector2(900f, 50f),
                FS_LABEL, TextAnchor.UpperCenter, TextDim);

            // Ticket stock — the big center readout.
            _lobbyTickets = MkText("Tickets", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 210f), new Vector2(900f, 170f), FS_HUGE, TextAnchor.MiddleCenter, TextWhite);
            MkText("TicketsLabel", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 80f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "TICKETS";

            // Step -> ticket progress bar.
            var barBg = MkPanel("BarBg", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -20f), new Vector2(CASINO_BAR_W, 36f), new Color(0.10f, 0.09f, 0.16f, 1f));
            var fill = MkPanel("Fill", barBg.transform, new Vector2(0f, 0.5f),
                new Vector2(4f, 0f), new Vector2(0f, 28f), AccentGold);
            _lobbyBarFill = fill.GetComponent<RectTransform>();
            _lobbyBarLabel = MkText("BarLabel", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -80f), new Vector2(900f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            _lobbySteps = MkText("Steps", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -135f), new Vector2(900f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            // Appears when a cash-out is available (P3 ceremony).
            _lobbyPrestigeBtn = MkButton("Prestige", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -215f), new Vector2(420f, 100f), "PRESTIGE",
                () => _onCasinoNav((int)AppPhase.CasinoPrestige));

            MkButton("Scratchers", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -370f), new Vector2(640f, 140f), "SCRATCHERS",
                () => _onCasinoNav((int)AppPhase.CasinoScratch));
            MkButton("Slots", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -530f), new Vector2(640f, 110f), "SLOTS",
                () => _onCasinoNav((int)AppPhase.CasinoSlots));
            MkButton("Upgrades", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -670f), new Vector2(640f, 110f), "UPGRADES",
                () => _onCasinoNav((int)AppPhase.CasinoUpgrades));

            MkButton("Back", _casinoLobbyPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(300f, 90f), "BACK",
                () => _onCasinoNav((int)AppPhase.Home), sfx: "back");

            if (Application.isEditor)
                MkText("DebugHint", _casinoLobbyPanel.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 140f), new Vector2(960f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                    .text = "(Editor: T = +1000 steps)";
        }

        void UpdateCasinoLobby(GamexGame g)
        {
            TickCoinBurst(Time.unscaledDeltaTime);
            var c = _casino.state;
            _lobbyCoins.text   = $"{CasinoDisplayCoins:N0} COINS";
            _lobbyPp.text      = $"Prestige Points: {c.prestigePoints:0.#}";
            _lobbyTickets.text = c.ticketsBanked.ToString("N0");
            int spt = _casino.StepsPerTicket;
            float pct = Mathf.Clamp01(c.stepAccumulator / (float)spt);
            _lobbyBarFill.sizeDelta = new Vector2((CASINO_BAR_W - 8f) * pct, 28f);
            _lobbyBarLabel.text = $"next ticket: {c.stepAccumulator}/{spt} steps";
            _lobbySteps.text = $"Today: {g.state.todaySteps:N0} steps";
            _lobbyPrestigeBtn.SetActive(_casino.PrestigeEligible);
        }

        // ============================================================
        // Scratch — the "Lucky 7s" ticket
        // ============================================================
        void BuildCasinoScratch(Transform root)
        {
            _casinoScratchPanel = MkFullPanel("CasinoScratch", root);

            _scCoins = MkText("Coins", _casinoScratchPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperLeft, TextWhite);
            _scTicketsLeft = MkText("TicketsLeft", _casinoScratchPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperRight, TextWhite);

            // The ticket face: a framed panel holding the 3 scratch cells.
            _scCard = MkSpritePanel("Ticket", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                SC_CARD_POS, new Vector2(960f, 560f), "panel", PanelTint);
            _scCard.GetComponent<Image>().raycastTarget = false;
            MkText("TicketTitle", _scCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· LUCKY 7s ·";
            MkText("TicketRule", _scCard.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
                new Vector2(900f, 44f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "match 3 to win — scratch!";

            for (int i = 0; i < 3; i++)
            {
                int idx = i;   // capture per-cell index, not the loop variable
                var cell = MkSpritePanel("Cell" + i, _scCard.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-300f + 300f * i, -20f), new Vector2(270f, 330f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                _scCells[i] = cell;
                // Symbol sits UNDER the foil — scratching uncovers it.
                _scCellTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(260f, 320f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
                // The scratchable foil (P3): RawImage + runtime texture.
                var foilGo = new GameObject("Foil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                foilGo.transform.SetParent(cell.transform, false);
                var frt = foilGo.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(8f, 8f);
                frt.offsetMax = new Vector2(-8f, -8f);
                var foil = foilGo.AddComponent<ScratchFoil>();
                foil.Init();
                foil.onRevealed = () => OnCellRevealed(idx);
                _scFoils[i] = foil;
            }

            // Shares the ticket slot; shown when there's nothing to play.
            _scEmptyLabel = MkText("Empty", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -160f), new Vector2(1000f, 100f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            _scResultLine = MkText("Result", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -100f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            _scNextBtn = MkButton("Next", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 130f), "NEXT TICKET",
                () => ScratchDealNext());

            // The earned Auto Scratcher (P4) — hidden until the 8-PP
            // unlock is owned. One press rips the whole backlog.
            _scAutoBtn = MkButton("Auto", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -460f), new Vector2(540f, 110f), "AUTO-SCRATCH",
                AutoScratchAll, "btn_grey", "btn_grey_down");
            _scAutoLabel = _scAutoBtn.GetComponentInChildren<TMP_Text>();

            MkButton("Back", _casinoScratchPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), sfx: "back");
        }

        // Phase-entry hook (called from Refresh's _lastPhase block): clear a
        // finished ticket and auto-deal so the screen is always ready to
        // scratch the moment it appears.
        void OnEnterCasinoScratch()
        {
            if (!_scHasPending || TicketFullyRevealed)
                ScratchDealNext();
        }

        bool TicketFullyRevealed => _scCellFlipped[0] && _scCellFlipped[1] && _scCellFlipped[2];

        // Coins as shown on screen: a pending play's payout is already in
        // the wallet (credited at play time) but hasn't "happened" visually
        // until the reveal finishes, so hold it back — for the scratch
        // ticket AND a mid-spin slot play alike.
        long CasinoDisplayCoins => _casino.host.coins
            - (_scHasPending && !TicketFullyRevealed ? _scPending.coins : 0)
            - (_slHasPending && !SlotsAllStopped ? _slPending.coins : 0);

        void ScratchDealNext()
        {
            _scSummary = null;
            if (!_casino.CanPlay) { _scHasPending = false; return; }
            _scPending = _casino.Play();
            _scHasPending = true;
            for (int i = 0; i < 3; i++) _scCellFlipped[i] = false;
            if (_scPending.tier == CasinoTier.Bust) FillBustSymbols(_scCellSymbols);
            else for (int i = 0; i < 3; i++) _scCellSymbols[i] = _scPending.tier;
            for (int i = 0; i < 3; i++)
            {
                _scCellTexts[i].text  = CasinoSymbolLabel(_scCellSymbols[i]);
                _scCellTexts[i].color = CasinoTierColor(_scCellSymbols[i]);
                _scFoils[i].ResetFoil();
            }
        }

        // Near-miss theatre for busts (shared by scratchers + slots): a
        // pair + one odd symbol. 15% of busts tease a pair of 7s (the
        // agonising almost-jackpot). Presentation-only randomness — the
        // payout was already rolled in CasinoGame.
        static void FillBustSymbols(CasinoTier[] symbols)
        {
            CasinoTier pair = UnityEngine.Random.value < 0.15f
                ? CasinoTier.Seven
                : (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Bar + 1);
            CasinoTier odd;
            do { odd = (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Seven + 1); }
            while (odd == pair);
            int oddSlot = UnityEngine.Random.Range(0, 3);
            for (int i = 0; i < 3; i++) symbols[i] = i == oddSlot ? odd : pair;
        }

        // The earned Auto Scratcher (P4): folds in the on-screen ticket if
        // its reveal wasn't finished, drains the whole bank, reports one
        // aggregate line. Only reachable once the one-time unlock is owned.
        void AutoScratchAll()
        {
            long coins = 0; int count = 0, jackpots = 0;
            if (_scHasPending && !TicketFullyRevealed)
            {
                for (int i = 0; i < 3; i++) _scCellFlipped[i] = true;
                coins += _scPending.coins; count++;
                if (_scPending.jackpot) jackpots++;
            }
            while (_casino.CanPlay)
            {
                var r = _casino.Play();
                coins += r.coins; count++;
                if (r.jackpot) jackpots++;
            }
            if (count == 0) return;
            _scHasPending = false;   // machine ate the stack; cells hide
            _scSummary = jackpots > 0
                ? $"Auto-scratched {count}: +{coins:N0} coins — {jackpots} JACKPOT{(jackpots > 1 ? "S" : "")}!"
                : $"Auto-scratched {count}: +{coins:N0} coins";
            _scResultPopT = 0.35f;
            if (jackpots > 0)
            {
                Sfx.Play("milestone");
                _scShakeT = 0.5f;
                SpawnCoinBurst(_casinoScratchPanel.transform, SC_CARD_POS, BURST_POOL);
            }
            else Sfx.Play("coin");
        }

        // A foil hit its reveal threshold. The LAST cell fires the payoff:
        // result pop + tier sfx, and for 777 the full fanfare (shake +
        // coin shower).
        void OnCellRevealed(int i)
        {
            if (!_scHasPending || _scCellFlipped[i]) return;
            _scCellFlipped[i] = true;
            if (!TicketFullyRevealed) return;

            _scResultPopT = 0.35f;
            if (_scPending.jackpot)
            {
                Sfx.Play("milestone");
                Sfx.Play("level_up");
                _scShakeT = 0.6f;
                SpawnCoinBurst(_casinoScratchPanel.transform, SC_CARD_POS, BURST_POOL);
            }
            else if (_scPending.tier == CasinoTier.Bar)
            {
                Sfx.Play("coin");
                Sfx.Play("quest_done", 0.7f);
                SpawnCoinBurst(_casinoScratchPanel.transform, SC_CARD_POS, 10);
            }
            else if (_scPending.tier != CasinoTier.Bust)
            {
                Sfx.Play("coin");
            }
        }

        static string CasinoSymbolLabel(CasinoTier t)
        {
            switch (t)
            {
                case CasinoTier.Seven:  return "7";
                case CasinoTier.Bar:    return "BAR";
                case CasinoTier.Bell:   return "BELL";
                default:                return "CHERRY";
            }
        }

        static Color CasinoTierColor(CasinoTier t)
        {
            switch (t)
            {
                case CasinoTier.Seven:  return new Color(1f,    0.30f, 0.25f);   // classic red 7
                case CasinoTier.Bar:    return new Color(0.88f, 0.90f, 0.96f);
                case CasinoTier.Bell:   return new Color(1f,    0.84f, 0.30f);
                case CasinoTier.Cherry: return new Color(1f,    0.50f, 0.55f);
                default:                return new Color(0.55f, 0.50f, 0.45f);
            }
        }

        static string CasinoResultText(PlayResult r)
        {
            switch (r.tier)
            {
                case CasinoTier.Seven:  return $"JACKPOT!  7·7·7  +{r.coins:N0} COINS";
                case CasinoTier.Bar:    return $"Triple bars.  +{r.coins:N0} coins";
                case CasinoTier.Bell:   return $"Ding ding ding.  +{r.coins:N0} coins";
                case CasinoTier.Cherry: return $"Cherries.  +{r.coins:N0} coins";
                default:                return $"Nothing. Zilch.  (+{r.coins:N0} coins)";
            }
        }

        void UpdateCasinoScratch()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);

            // Coin counter counts UP toward the real total after a reveal —
            // the classic casino tally. Snaps on first frame / big jumps
            // finish fast (exponential approach).
            long target = CasinoDisplayCoins;
            if (_scCoinsShown < 0f) _scCoinsShown = target;
            _scCoinsShown = Mathf.Abs(_scCoinsShown - target) < 1f
                ? target
                : Mathf.Lerp(_scCoinsShown, target, 1f - Mathf.Exp(-8f * dt));
            _scCoins.text       = $"Coins: {(long)_scCoinsShown:N0}";
            _scTicketsLeft.text = $"Tickets: {_casino.state.ticketsBanked:N0}";

            for (int i = 0; i < 3; i++)
                _scCells[i].SetActive(_scHasPending);

            // 777 shake — decaying random offset on the whole ticket.
            var cardRt = (RectTransform)_scCard.transform;
            if (_scShakeT > 0f)
            {
                _scShakeT -= dt;
                float amp = Mathf.Lerp(0f, 16f, Mathf.Clamp01(_scShakeT / 0.6f));
                cardRt.anchoredPosition = SC_CARD_POS + new Vector2(
                    UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp, amp));
                if (_scShakeT <= 0f) cardRt.anchoredPosition = SC_CARD_POS;
            }

            // Result line pops in (scale bounce), then rests.
            if (_scResultPopT > 0f)
            {
                _scResultPopT -= dt;
                float t01 = 1f - Mathf.Clamp01(_scResultPopT / 0.35f);
                float s = Mathf.Lerp(1.35f, 1f, t01 * t01 * (3f - 2f * t01));
                _scResultLine.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            else _scResultLine.rectTransform.localScale = Vector3.one;

            if (_scSummary != null) _scResultLine.text = _scSummary;
            else _scResultLine.text = _scHasPending && TicketFullyRevealed
                ? CasinoResultText(_scPending) : "";

            _scNextBtn.SetActive((!_scHasPending || TicketFullyRevealed) && _casino.CanPlay);

            // Auto Scratcher button — only once owned, only with work to do.
            bool canAuto = _casino.AutoScratcherOwned
                && (_casino.state.ticketsBanked > 0 || (_scHasPending && !TicketFullyRevealed));
            _scAutoBtn.SetActive(canAuto);
            if (canAuto)
            {
                int autoCount = _casino.state.ticketsBanked + (_scHasPending && !TicketFullyRevealed ? 1 : 0);
                _scAutoLabel.text = $"AUTO-SCRATCH ({autoCount:N0})";
            }

            bool dry = !_casino.CanPlay && (!_scHasPending || TicketFullyRevealed);
            _scEmptyLabel.gameObject.SetActive(dry);
            if (dry)
                _scEmptyLabel.text = _scHasPending
                    ? $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps for the next one."
                    : $"Out of tickets.\nWalk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps for the next one.";
        }

        // ============================================================
        // Upgrades
        // ============================================================
        void BuildCasinoUpgrades(Transform root)
        {
            _casinoUpgradesPanel = MkFullPanel("CasinoUpgrades", root);

            MkText("Title", _casinoUpgradesPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - SAFE_AREA_TOP_INSET), new Vector2(900f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "UPGRADES";
            _upWallet = MkText("Wallet", _casinoUpgradesPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -250f - SAFE_AREA_TOP_INSET), new Vector2(960f, 56f),
                FS_LABEL, TextAnchor.UpperCenter, TextWhite);

            // ---- run upgrades (coins; reset on Prestige) ----
            _rowPayout = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, 400f,
                "Payout", "+8% coins from every ticket / level",
                () => Sfx.Play(_casino.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Payout) ? "purchase" : "error"));
            _rowLuck = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, 230f,
                "Luck", "+0.3% jackpot odds / level",
                () => Sfx.Play(_casino.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Luck) ? "purchase" : "error"));
            _rowStride = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, 60f,
                "Stride", "-8 steps per ticket / level",
                () => Sfx.Play(_casino.TryBuyRunUpgrade(CasinoGame.RunUpgrade.Stride) ? "purchase" : "error"));

            MkText("PermHeader", _casinoUpgradesPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -70f), new Vector2(960f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "— PERMANENT · Prestige Points —";

            // ---- permanent (PP; survive Prestige) ----
            _rowGTouch = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, -200f,
                "Golden Touch", "+5% coins from every ticket / level",
                () => Sfx.Play(_casino.TryBuyPermUpgrade(CasinoGame.PermUpgrade.GoldenTouch) ? "purchase" : "error"));
            _rowLDice = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, -370f,
                "Loaded Dice", "+0.3% jackpot odds / level",
                () => Sfx.Play(_casino.TryBuyPermUpgrade(CasinoGame.PermUpgrade.LoadedDice) ? "purchase" : "error"));
            _rowMara = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, -540f,
                "Marathoner", "running earns +10% tickets / level",
                () => Sfx.Play(_casino.TryBuyPermUpgrade(CasinoGame.PermUpgrade.Marathoner) ? "purchase" : "error"));
            _rowAuto = MkCasinoUpgradeRow(_casinoUpgradesPanel.transform, -710f,
                "Auto Scratcher", "rips your whole ticket stack — one-time",
                () => Sfx.Play(_casino.TryBuyPermUpgrade(CasinoGame.PermUpgrade.AutoScratcher) ? "purchase" : "error"));

            MkButton("Back", _casinoUpgradesPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), sfx: "back");
        }

        (TMP_Text lvl, TMP_Text cost, Button btn) MkCasinoUpgradeRow(
            Transform parent, float y, string name, string effect, Action onBuy)
        {
            var row = MkSpritePanel("Row_" + name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(980f, 150f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            row.GetComponent<Image>().raycastTarget = false;
            MkText("Name", row.transform, new Vector2(0f, 1f), new Vector2(30f, -16f),
                new Vector2(600f, 52f), FS_LABEL, TextAnchor.UpperLeft, TextWhite).text = name;
            MkText("Effect", row.transform, new Vector2(0f, 0f), new Vector2(30f, 16f),
                new Vector2(660f, 42f), FS_BODY, TextAnchor.LowerLeft, TextDim).text = effect;
            var lvl = MkText("Lvl", row.transform, new Vector2(1f, 1f), new Vector2(-290f, -16f),
                new Vector2(170f, 52f), FS_LABEL, TextAnchor.UpperRight, AccentGold);
            var btnGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f),
                new Vector2(250f, 112f), "—", onBuy);
            var cost = btnGo.GetComponentInChildren<TMP_Text>();
            cost.fontSize = FS_LABEL;
            return (lvl, cost, btnGo.GetComponent<Button>());
        }

        void UpdateCasinoUpgrades()
        {
            var c = _casino.state;
            _upWallet.text = $"Coins: {CasinoDisplayCoins:N0}    ·    PP: {c.prestigePoints:0.#}";

            long coins = _casino.host.coins;
            UpdateCasinoRow(_rowPayout, c.runPayout,
                _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Payout) + "c",
                coins >= _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Payout), false);
            UpdateCasinoRow(_rowLuck, c.runLuck,
                _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Luck) + "c",
                coins >= _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Luck), false);
            UpdateCasinoRow(_rowStride, c.runStride,
                _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Stride) + "c",
                coins >= _casino.RunUpgradeCost(CasinoGame.RunUpgrade.Stride),
                c.runStride >= CasinoGame.STRIDE_MAX_LEVEL);

            UpdateCasinoRow(_rowGTouch, c.permGoldenTouch,
                _casino.PermUpgradeCost(CasinoGame.PermUpgrade.GoldenTouch) + " PP",
                c.prestigePoints >= _casino.PermUpgradeCost(CasinoGame.PermUpgrade.GoldenTouch), false);
            UpdateCasinoRow(_rowLDice, c.permLoadedDice,
                _casino.PermUpgradeCost(CasinoGame.PermUpgrade.LoadedDice) + " PP",
                c.prestigePoints >= _casino.PermUpgradeCost(CasinoGame.PermUpgrade.LoadedDice), false);
            UpdateCasinoRow(_rowMara, c.permMarathoner,
                _casino.PermUpgradeCost(CasinoGame.PermUpgrade.Marathoner) + " PP",
                c.prestigePoints >= _casino.PermUpgradeCost(CasinoGame.PermUpgrade.Marathoner), false);
            bool autoOwned = _casino.AutoScratcherOwned;
            UpdateCasinoRow(_rowAuto, c.permAutoScratcher,
                autoOwned ? "OWNED" : _casino.PermUpgradeCost(CasinoGame.PermUpgrade.AutoScratcher) + " PP",
                !autoOwned && c.prestigePoints >= _casino.PermUpgradeCost(CasinoGame.PermUpgrade.AutoScratcher),
                false);
        }

        static void UpdateCasinoRow((TMP_Text lvl, TMP_Text cost, Button btn) row,
                                    int level, string costLabel, bool affordable, bool capped)
        {
            row.lvl.text  = "Lv " + level;
            row.cost.text = capped ? "MAX" : costLabel;
            row.btn.interactable = !capped && affordable;
        }

        // ============================================================
        // Prestige — cash-out confirm + celebration (P3)
        // ============================================================
        void BuildCasinoPrestige(Transform root)
        {
            _casinoPrestigePanel = MkFullPanel("CasinoPrestige", root);

            MkText("Title", _casinoPrestigePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - SAFE_AREA_TOP_INSET), new Vector2(900f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "PRESTIGE";

            // ---- confirm ----
            _prestigeConfirmGroup = MkFullPanel("Confirm", _casinoPrestigePanel.transform);
            MkText("Ask", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 430f), new Vector2(1000f, 60f), FS_BTN, TextAnchor.MiddleCenter, TextWhite)
                .text = "Cash out this run?";
            _prestigeGainLine = MkText("Gain", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 290f), new Vector2(1000f, 80f), FS_TITLE, TextAnchor.MiddleCenter, AccentGold);
            _prestigeResetLine = MkText("Resets", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 180f), new Vector2(1000f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            MkText("Keeps", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 110f), new Vector2(1020f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "Keeps: tickets · PP · permanent upgrades · lifetime stats";

            MkButton("CashOut", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -120f), new Vector2(640f, 150f), "CASH OUT", DoPrestige);
            MkButton("NotYet", _prestigeConfirmGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(400f, 100f), "NOT YET",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), "btn_grey", "btn_grey_down", "back");

            // ---- celebration ----
            _prestigeCelebGroup = MkFullPanel("Celeb", _casinoPrestigePanel.transform);
            _prestigeCelebTitle = MkText("CelebTitle", _prestigeCelebGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 260f), new Vector2(1040f, 130f), FS_BIG, TextAnchor.MiddleCenter, AccentGold);
            _prestigeCelebPp = MkText("CelebPp", _prestigeCelebGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 110f), new Vector2(1000f, 80f), FS_TITLE, TextAnchor.MiddleCenter, TextWhite);
            MkText("CelebSub", _prestigeCelebGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(1000f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "Every step is worth more now.";
            MkText("Hint", _prestigeCelebGroup.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "(tap to continue)";
            // Full-screen tap-through back to the lobby.
            var celebImg = _prestigeCelebGroup.GetComponent<Image>();
            celebImg.raycastTarget = true;
            var cont = _prestigeCelebGroup.AddComponent<Button>();
            cont.targetGraphic = celebImg;
            cont.transition = Selectable.Transition.None;
            cont.onClick.AddListener(() => _onCasinoNav((int)AppPhase.CasinoLobby));

            _prestigeCelebGroup.SetActive(false);
        }

        void OnEnterCasinoPrestige()
        {
            _prestigeCelebrating = false;
            _prestigeConfirmGroup.SetActive(true);
            _prestigeCelebGroup.SetActive(false);
        }

        void DoPrestige()
        {
            float pp = _casino.PrestigePpPreview;
            if (!_casino.TryPrestige()) return;
            _prestigeCelebrating = true;
            _prestigeCelebT = 0f;
            _prestigeCelebTitle.text = "PRESTIGE " + _casino.state.prestigeCount;
            _prestigeCelebPp.text    = $"+{pp:0.#} PP";
            _prestigeConfirmGroup.SetActive(false);
            _prestigeCelebGroup.SetActive(true);
            Sfx.Play("milestone");
            Sfx.Play("level_up");
            SpawnCoinBurst(_casinoPrestigePanel.transform, new Vector2(0f, 120f), BURST_POOL);
            _scCoinsShown = -1f;   // play-screen counters snap to the fresh (zeroed) wallet
            _slCoinsShown = -1f;
        }

        void UpdateCasinoPrestige()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);

            if (!_prestigeCelebrating)
            {
                _prestigeGainLine.text  = $"+{_casino.PrestigePpPreview:0.#} Prestige Points";
                _prestigeResetLine.text = $"Resets: {_casino.host.coins:N0} coins · run upgrades";
                return;
            }
            _prestigeCelebT += dt;
            float t = Mathf.Clamp01(_prestigeCelebT / 0.45f);
            float s = Mathf.Lerp(1.8f, 1f, t * t * (3f - 2f * t));
            _prestigeCelebTitle.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        // ============================================================
        // Slots (P4) — the reel room. A spin consumes one ticket via the
        // SAME pre-rolled CasinoGame.Play(); the reels are theatre: they
        // cycle random symbols, then stop left-to-right on the rolled
        // outcome. When the first two reels match, the third hangs an
        // extra beat — the near-miss tension slots invented.
        // ============================================================
        void BuildCasinoSlots(Transform root)
        {
            _casinoSlotsPanel = MkFullPanel("CasinoSlots", root);

            _slCoins = MkText("Coins", _casinoSlotsPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperLeft, TextWhite);
            _slTickets = MkText("Tickets", _casinoSlotsPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperRight, TextWhite);

            _slMachine = MkSpritePanel("Machine", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                SL_MACHINE_POS, new Vector2(960f, 460f), "panel", PanelTint);
            _slMachine.GetComponent<Image>().raycastTarget = false;
            MkText("MachineTitle", _slMachine.transform, new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                new Vector2(900f, 56f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· SLOTS ·";

            for (int i = 0; i < 3; i++)
            {
                var win = MkSpritePanel("Reel" + i, _slMachine.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-300f + 300f * i, -20f), new Vector2(270f, 250f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                win.GetComponent<Image>().raycastTarget = false;
                _slReelTexts[i] = MkText("Sym", win.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(260f, 240f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
                _slReelTexts[i].text = "—";
            }

            _slEmptyLabel = MkText("Empty", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -150f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            _slResultLine = MkText("Result", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -70f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            _slSpinBtn = MkButton("Spin", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 150f), "SPIN", SlotsSpin);
            MkText("SpinCost", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -410f), new Vector2(600f, 40f), FS_BODY, TextAnchor.MiddleCenter, TextDim)
                .text = "1 ticket per spin";

            MkButton("Back", _casinoSlotsPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), sfx: "back");
        }

        void SlotsSpin()
        {
            if (!_casino.CanPlay || (_slHasPending && !SlotsAllStopped)) return;
            _slPending = _casino.Play();
            _slHasPending = true;
            if (_slPending.tier == CasinoTier.Bust) FillBustSymbols(_slFinal);
            else for (int i = 0; i < 3; i++) _slFinal[i] = _slPending.tier;
            _slSpinT = 0f;
            _slStopAt[0] = 0.7f; _slStopAt[1] = 1.5f; _slStopAt[2] = 2.3f;
            // The slots classic: two matching reels make the third hang an
            // extra beat — maximum tension whether it hits or heartbreaks.
            if (_slFinal[0] == _slFinal[1]) _slStopAt[2] += 0.7f;
            for (int i = 0; i < 3; i++) _slStopped[i] = false;
        }

        void SlotsResolve()
        {
            _slResultPopT = 0.35f;
            if (_slPending.jackpot)
            {
                Sfx.Play("milestone");
                Sfx.Play("level_up");
                _slShakeT = 0.6f;
                SpawnCoinBurst(_casinoSlotsPanel.transform, SL_MACHINE_POS, BURST_POOL);
            }
            else if (_slPending.tier == CasinoTier.Bar)
            {
                Sfx.Play("coin");
                Sfx.Play("quest_done", 0.7f);
                SpawnCoinBurst(_casinoSlotsPanel.transform, SL_MACHINE_POS, 10);
            }
            else if (_slPending.tier != CasinoTier.Bust)
            {
                Sfx.Play("coin");
            }
        }

        void UpdateCasinoSlots()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);

            long target = CasinoDisplayCoins;
            if (_slCoinsShown < 0f) _slCoinsShown = target;
            _slCoinsShown = Mathf.Abs(_slCoinsShown - target) < 1f
                ? target
                : Mathf.Lerp(_slCoinsShown, target, 1f - Mathf.Exp(-8f * dt));
            _slCoins.text   = $"Coins: {(long)_slCoinsShown:N0}";
            _slTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";

            bool spinning = _slHasPending && !SlotsAllStopped;
            if (spinning)
            {
                _slSpinT  += dt;
                _slCycleT += dt;
                bool cycle = _slCycleT >= 0.06f;
                if (cycle) _slCycleT = 0f;
                for (int i = 0; i < 3; i++)
                {
                    if (_slStopped[i]) continue;
                    if (_slSpinT >= _slStopAt[i])
                    {
                        _slStopped[i] = true;
                        _slReelTexts[i].text  = CasinoSymbolLabel(_slFinal[i]);
                        _slReelTexts[i].color = CasinoTierColor(_slFinal[i]);
                        _slStopPop[i] = 0.25f;
                        Sfx.Play("tap");
                        if (i == 2) SlotsResolve();
                    }
                    else if (cycle)
                    {
                        var t = (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Seven + 1);
                        _slReelTexts[i].text  = CasinoSymbolLabel(t);
                        _slReelTexts[i].color = TextDim;
                    }
                }
            }

            // Per-reel stop pop.
            for (int i = 0; i < 3; i++)
            {
                if (_slStopPop[i] > 0f)
                {
                    _slStopPop[i] -= dt;
                    float t01 = 1f - Mathf.Clamp01(_slStopPop[i] / 0.25f);
                    float s = Mathf.Lerp(1.3f, 1f, t01);
                    _slReelTexts[i].rectTransform.localScale = new Vector3(s, s, 1f);
                }
                else _slReelTexts[i].rectTransform.localScale = Vector3.one;
            }

            // 777 machine shake.
            var mrt = (RectTransform)_slMachine.transform;
            if (_slShakeT > 0f)
            {
                _slShakeT -= dt;
                float amp = Mathf.Lerp(0f, 16f, Mathf.Clamp01(_slShakeT / 0.6f));
                mrt.anchoredPosition = SL_MACHINE_POS + new Vector2(
                    UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp, amp));
                if (_slShakeT <= 0f) mrt.anchoredPosition = SL_MACHINE_POS;
            }

            // Result pop + line.
            if (_slResultPopT > 0f)
            {
                _slResultPopT -= dt;
                float t01 = 1f - Mathf.Clamp01(_slResultPopT / 0.35f);
                float s = Mathf.Lerp(1.35f, 1f, t01 * t01 * (3f - 2f * t01));
                _slResultLine.rectTransform.localScale = new Vector3(s, s, 1f);
            }
            else _slResultLine.rectTransform.localScale = Vector3.one;
            _slResultLine.text = _slHasPending && SlotsAllStopped ? CasinoResultText(_slPending) : "";

            _slSpinBtn.GetComponent<Button>().interactable = _casino.CanPlay && !spinning;

            bool dry = !_casino.CanPlay && !spinning;
            _slEmptyLabel.gameObject.SetActive(dry);
            if (dry)
                _slEmptyLabel.text = $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // Coin burst — pooled UI coins with gravity, for 777s + prestige.
        // ============================================================
        void EnsureBurstPool()
        {
            if (_burstCoins != null) return;
            _burstCoins = new Image[BURST_POOL];
            _burstVel   = new Vector2[BURST_POOL];
            _burstSpin  = new float[BURST_POOL];
            for (int i = 0; i < BURST_POOL; i++)
            {
                var go = MkSpriteIcon("BurstCoin" + i, _casinoLobbyPanel.transform,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f), "coin", Color.white);
                go.SetActive(false);
                _burstCoins[i] = go.GetComponent<Image>();
            }
        }

        void SpawnCoinBurst(Transform parent, Vector2 origin, int n)
        {
            EnsureBurstPool();
            int spawned = 0;
            for (int i = 0; i < BURST_POOL && spawned < n; i++)
            {
                var img = _burstCoins[i];
                if (img.gameObject.activeSelf) continue;
                img.transform.SetParent(parent, false);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = origin + new Vector2(
                    UnityEngine.Random.Range(-140f, 140f), UnityEngine.Random.Range(-40f, 40f));
                rt.localRotation = Quaternion.identity;
                _burstVel[i]  = new Vector2(UnityEngine.Random.Range(-420f, 420f),
                                            UnityEngine.Random.Range(500f, 1050f));
                _burstSpin[i] = UnityEngine.Random.Range(-540f, 540f);
                img.gameObject.SetActive(true);
                spawned++;
            }
            _burstAny = true;
        }

        void TickCoinBurst(float dt)
        {
            if (!_burstAny || _burstCoins == null) return;
            bool any = false;
            for (int i = 0; i < BURST_POOL; i++)
            {
                var img = _burstCoins[i];
                if (!img.gameObject.activeSelf) continue;
                var rt = img.rectTransform;
                _burstVel[i].y -= 2400f * dt;
                rt.anchoredPosition += _burstVel[i] * dt;
                rt.Rotate(0f, 0f, _burstSpin[i] * dt);
                if (rt.anchoredPosition.y < -1400f) img.gameObject.SetActive(false);
                else any = true;
            }
            _burstAny = any;
        }
    }
}
