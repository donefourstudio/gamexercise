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
        //                    doors to SCRATCHERS / SLOTS(P4) / UPGRADES
        //   CasinoScratch  — the "Lucky 7s" ticket, tap-to-reveal (drag-
        //                    scratch lands in P3; Auto Scratcher is an
        //                    earned unlock, never a base batch button)
        //   CasinoUpgrades — run upgrades (coins) + permanent (PP)
        //
        // Unlike the old game's callback routing, these screens hold the
        // CasinoGame core directly (_casino, injected via the Hud ctor):
        // the play result must flow back into the reveal animation
        // synchronously, and every button is a 1:1 core call anyway.
        // Navigation goes through _onCasinoNav (incl. BACK to Home) so
        // GameRunner stays the owner of the phase field.
        //
        // Reveal model: CasinoGame pre-rolls the outcome the moment a
        // ticket is played (Play()). The three cells are pure theatre — a
        // win shows three matching symbols of the rolled tier; a bust shows
        // a near-miss (a pair + one odd symbol, occasionally two agonising
        // 7s). Because coins are credited at play time, the on-screen coin
        // counter subtracts the pending ticket's payout until the player
        // has flipped all three cells (CasinoDisplayCoins).
        // ============================================================

        GameObject _casinoLobbyPanel, _casinoScratchPanel, _casinoUpgradesPanel;

        // ---- Lobby ----
        TMP_Text _lobbyCoins, _lobbyPp, _lobbyTickets, _lobbySteps,
                 _lobbyBarLabel, _lobbyPrestigeHint;
        RectTransform _lobbyBarFill;
        const float CASINO_BAR_W = 700f;

        // ---- Scratch ----
        // Deliberately NO batch/"scratch all" here: every ticket is played
        // by hand. Backlog tedium is the designed pressure that makes the
        // earned Auto Scratcher unlock (PP tree, P3/P4) desirable —
        // automation is a carrot, never a freebie.
        TMP_Text _scCoins, _scTicketsLeft, _scResultLine, _scEmptyLabel;
        readonly GameObject[] _scCells = new GameObject[3];
        readonly TMP_Text[]   _scCellTexts = new TMP_Text[3];
        readonly bool[]       _scCellFlipped = new bool[3];
        readonly CasinoTier[] _scCellSymbols = new CasinoTier[3];
        PlayResult _scPending;
        bool   _scHasPending;
        GameObject _scNextBtn;

        // ---- Upgrades ----
        TMP_Text _upWallet;
        (TMP_Text lvl, TMP_Text cost, Button btn) _rowPayout, _rowLuck, _rowStride,
                                                  _rowGTouch, _rowLDice, _rowMara;

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

            // Foreshadow only — the cash-out ceremony itself lands in P3.
            _lobbyPrestigeHint = MkText("PrestigeHint", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -205f), new Vector2(960f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
            _lobbyPrestigeHint.text = "PRESTIGE READY";

            MkButton("Scratchers", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -370f), new Vector2(640f, 140f), "SCRATCHERS",
                () => _onCasinoNav((int)AppPhase.CasinoScratch));
            var slots = MkButton("Slots", _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -530f), new Vector2(640f, 110f), "SLOTS — SOON",
                () => { }, "btn_grey", "btn_grey_down");
            slots.GetComponent<Button>().interactable = false;   // P4 door, visible on purpose
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
            var c = _casino.state;
            _lobbyCoins.text   = $"{CasinoDisplayCoins:N0} COINS";
            _lobbyPp.text      = $"Prestige Points: {c.prestigePoints:0.#}";
            _lobbyTickets.text = c.ticketsBanked.ToString("N0");
            int spt = _casino.StepsPerTicket;
            float pct = Mathf.Clamp01(c.stepAccumulator / (float)spt);
            _lobbyBarFill.sizeDelta = new Vector2((CASINO_BAR_W - 8f) * pct, 28f);
            _lobbyBarLabel.text = $"next ticket: {c.stepAccumulator}/{spt} steps";
            _lobbySteps.text = $"Today: {g.state.todaySteps:N0} steps";
            _lobbyPrestigeHint.gameObject.SetActive(_casino.PrestigeEligible);
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

            // The ticket face: a framed panel holding the 3 reveal cells.
            var card = MkSpritePanel("Ticket", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(960f, 560f), "panel", PanelTint);
            card.GetComponent<Image>().raycastTarget = false;
            MkText("TicketTitle", card.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· LUCKY 7s ·";
            MkText("TicketRule", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
                new Vector2(900f, 44f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "match 3 to win";

            for (int i = 0; i < 3; i++)
            {
                int idx = i;   // capture per-cell index, not the loop variable
                var cell = MkSpritePanel("Cell" + i, card.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-300f + 300f * i, -20f), new Vector2(270f, 330f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = cell.GetComponent<Image>();
                btn.transition = Selectable.Transition.ColorTint;
                var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.7f, 1f); btn.colors = cb;
                btn.onClick.AddListener(() => ScratchFlipCell(idx));
                _scCells[i] = cell;
                _scCellTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(260f, 320f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
            }

            // Shares the ticket slot; shown when there's nothing to play.
            _scEmptyLabel = MkText("Empty", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -160f), new Vector2(1000f, 100f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            _scResultLine = MkText("Result", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -100f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            _scNextBtn = MkButton("Next", _casinoScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 130f), "NEXT TICKET",
                () => ScratchDealNext());

            MkButton("Back", _casinoScratchPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), sfx: "back");
        }

        // Phase-entry hook (called from Refresh's _lastPhase block): clear a
        // finished ticket / stale summary and auto-deal so the screen is
        // always ready to scratch the moment it appears.
        void OnEnterCasinoScratch()
        {
            if (!_scHasPending || TicketFullyRevealed)
                ScratchDealNext();
        }

        bool TicketFullyRevealed => _scCellFlipped[0] && _scCellFlipped[1] && _scCellFlipped[2];

        // Coins as shown on screen: the pending ticket's payout is already
        // in the wallet (credited at play time) but hasn't "happened"
        // visually until every cell is flipped, so hold it back.
        long CasinoDisplayCoins => _casino.host.coins
            - (_scHasPending && !TicketFullyRevealed ? _scPending.coins : 0);

        void ScratchDealNext()
        {
            if (!_casino.CanPlay) { _scHasPending = false; return; }
            _scPending = _casino.Play();
            _scHasPending = true;
            for (int i = 0; i < 3; i++) _scCellFlipped[i] = false;
            if (_scPending.tier == CasinoTier.Bust) RollBustSymbols();
            else for (int i = 0; i < 3; i++) _scCellSymbols[i] = _scPending.tier;
        }

        // Near-miss theatre for busts: a pair + one odd symbol. 15% of
        // busts tease a pair of 7s (the agonising almost-jackpot).
        // Presentation-only randomness — the payout was already rolled in
        // CasinoGame.
        void RollBustSymbols()
        {
            CasinoTier pair = UnityEngine.Random.value < 0.15f
                ? CasinoTier.Seven
                : (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Bar + 1);
            CasinoTier odd;
            do { odd = (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Seven + 1); }
            while (odd == pair);
            int oddSlot = UnityEngine.Random.Range(0, 3);
            for (int i = 0; i < 3; i++) _scCellSymbols[i] = i == oddSlot ? odd : pair;
        }

        void ScratchFlipCell(int i)
        {
            if (!_scHasPending || _scCellFlipped[i]) return;
            _scCellFlipped[i] = true;
            Sfx.Play("tap");
            if (TicketFullyRevealed)
            {
                if (_scPending.jackpot)                      Sfx.Play("milestone");
                else if (_scPending.tier != CasinoTier.Bust) Sfx.Play("coin");
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
            _scCoins.text       = $"Coins: {CasinoDisplayCoins:N0}";
            _scTicketsLeft.text = $"Tickets: {_casino.state.ticketsBanked:N0}";

            for (int i = 0; i < 3; i++)
            {
                _scCells[i].SetActive(_scHasPending);
                if (!_scHasPending) continue;
                bool up = _scCellFlipped[i];
                _scCellTexts[i].text  = up ? CasinoSymbolLabel(_scCellSymbols[i]) : "?";
                _scCellTexts[i].color = up ? CasinoTierColor(_scCellSymbols[i]) : TextDim;
            }

            if (_scHasPending && TicketFullyRevealed) _scResultLine.text = CasinoResultText(_scPending);
            else                                     _scResultLine.text = "";

            _scNextBtn.SetActive((!_scHasPending || TicketFullyRevealed) && _casino.CanPlay);

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
        }

        static void UpdateCasinoRow((TMP_Text lvl, TMP_Text cost, Button btn) row,
                                    int level, string costLabel, bool affordable, bool capped)
        {
            row.lvl.text  = "Lv " + level;
            row.cost.text = capped ? "MAX" : costLabel;
            row.btn.interactable = !capped && affordable;
        }
    }
}
