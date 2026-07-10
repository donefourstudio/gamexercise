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
        readonly Image[]       _scCellIcons = new Image[3];   // P5a — Caz symbol sprites (text = fallback)
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
        // P5a: real cabinet from Caz's Pixel Fantasy Slot Machine. The pack
        // is LAYERED on one 816x624 canvas: glass backdrop -> symbols ->
        // translucent glass shade -> cabinet (reel windows are punched
        // holes) -> crank (up/pulled states swapped on spin). Reel centers
        // measured from the glass layer: (-125.5,-38.5) (+4.5,-38.5)
        // (+134.5,-38.5), windows 108x210 — scaled by 900/816 below.
        GameObject _casinoSlotsPanel, _slMachine, _slSpinBtn;
        TMP_Text _slCoins, _slTickets, _slResultLine, _slEmptyLabel;
        readonly TMP_Text[]   _slReelTexts = new TMP_Text[3];
        readonly Image[]      _slReelIcons = new Image[3];
        Image _slCrankUp, _slCrankDown;
        float _slCrankDownT;
        readonly CasinoTier[] _slFinal   = new CasinoTier[3];
        readonly bool[]       _slStopped = new bool[3];
        readonly float[]      _slStopAt  = new float[3];
        readonly float[]      _slStopPop = new float[3];
        PlayResult _slPending;
        bool  _slHasPending;
        float _slSpinT, _slCycleT, _slShakeT, _slResultPopT;
        float _slCoinsShown = -1f;
        static readonly Vector2 SL_MACHINE_POS  = new Vector2(0f, 330f);
        static readonly Vector2 SL_MACHINE_SIZE = new Vector2(900f, 688f);   // 816x624 * 1.1029
        static readonly Vector2[] SL_REEL_POS =
        {
            new Vector2(-138.4f, -42.5f), new Vector2(5.0f, -42.5f), new Vector2(148.4f, -42.5f),
        };
        bool SlotsAllStopped => _slStopped[0] && _slStopped[1] && _slStopped[2];

        // ---- Lobby game grid (P5b) ----
        readonly List<(Button btn, TMP_Text label, string name, int lv)> _lobbyGames = new();

        // ---- Find the Cash (P5b) — 3x3 hunt for 3-of-a-kind ----
        GameObject _casinoFindCashPanel;
        TMP_Text _fcCoins, _fcTickets, _fcResultLine, _fcEmptyLabel;
        readonly TMP_Text[]    _fcCellTexts = new TMP_Text[9];
        readonly Image[]       _fcCellIcons = new Image[9];
        readonly ScratchFoil[] _fcFoils = new ScratchFoil[9];
        readonly CasinoTier[]  _fcSymbols = new CasinoTier[9];
        readonly bool[]        _fcDud = new bool[9];
        readonly bool[]        _fcFlipped = new bool[9];
        PlayResult _fcPending; bool _fcHasPending;
        GameObject _fcCard, _fcNextBtn;
        float _fcPopT, _fcShakeT, _fcCoinsShown = -1f;
        static readonly Vector2 FC_CARD_POS = new Vector2(0f, 290f);

        // ---- Gold Rush (P5b) — the high-variance dig ----
        GameObject _casinoGoldRushPanel, _grField;
        TMP_Text _grCoins, _grTickets, _grResultLine, _grEmptyLabel;
        ScratchFoil _grFoil;
        readonly Image[] _grItems = new Image[12];
        PlayResult _grPending; bool _grHasPending, _grRevealed;
        GameObject _grNextBtn;
        float _grPopT, _grShakeT, _grCoinsShown = -1f;
        static readonly Vector2 GR_FIELD_POS = new Vector2(0f, 300f);

        // ---- The Ladder (P5b) — press-your-luck, core state persisted ----
        GameObject _casinoLadderPanel, _ldCard;
        TMP_Text _ldCoins, _ldTickets, _ldPot, _ldRungLine, _ldOddsLine, _ldResultLine, _ldEmptyLabel;
        GameObject _ldStartBtn, _ldClimbBtn, _ldBankBtn;
        TMP_Text _ldBankLabel;
        string _ldResult;
        float _ldPopT, _ldShakeT, _ldCoinsShown = -1f;
        static readonly Vector2 LD_CARD_POS = new Vector2(0f, 300f);

        // ---- High Stakes (P5b) — opt-in coin wager ----
        GameObject _casinoHighStakesPanel, _hsCard;
        TMP_Text _hsCoins, _hsTickets, _hsResultLine, _hsEmptyLabel, _hsCellText;
        Image _hsCellIcon; ScratchFoil _hsFoil;
        readonly Button[] _hsWagerBtns = new Button[3];
        static readonly long[] HS_WAGERS = { 100, 500, 2000 };
        PlayResult _hsPending; bool _hsHasPending, _hsRevealed; long _hsWager;
        float _hsPopT, _hsShakeT, _hsCoinsShown = -1f;
        static readonly Vector2 HS_CARD_POS = new Vector2(0f, 300f);

        // ---- MEGA JACKPOT (P5b) — the white whale ----
        GameObject _casinoMegaPanel, _mgCard;
        TMP_Text _mgCoins, _mgTickets, _mgResultLine, _mgEmptyLabel, _mgCellText;
        Image _mgCellIcon; ScratchFoil _mgFoil;
        PlayResult _mgPending; bool _mgHasPending, _mgRevealed;
        GameObject _mgNextBtn;
        float _mgPopT, _mgShakeT, _mgCoinsShown = -1f;
        static readonly Vector2 MG_CARD_POS = new Vector2(0f, 300f);

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
        Sprite[]  _coinFrames;   // P5a — Caz coin-flip flipbook (null entries = fallback)
        float     _coinFlipT;

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

            // P5b — the full game grid. Two columns, level-gated unlocks
            // (the casino grows as the player's real walking does).
            MkLobbyGame("SCRATCHERS",    0, 0, 0,                            AppPhase.CasinoScratch);
            MkLobbyGame("SLOTS",         1, 0, 0,                            AppPhase.CasinoSlots);
            MkLobbyGame("FIND THE CASH", 0, 1, CasinoGame.UNLOCK_FINDCASH,   AppPhase.CasinoFindCash);
            MkLobbyGame("GOLD RUSH",     1, 1, CasinoGame.UNLOCK_GOLDRUSH,   AppPhase.CasinoGoldRush);
            MkLobbyGame("THE LADDER",    0, 2, CasinoGame.UNLOCK_LADDER,     AppPhase.CasinoLadder);
            MkLobbyGame("HIGH STAKES",   1, 2, CasinoGame.UNLOCK_HIGHSTAKES, AppPhase.CasinoHighStakes);
            MkLobbyGame("MEGA JACKPOT",  0, 3, CasinoGame.UNLOCK_MEGA,       AppPhase.CasinoMega);
            MkLobbyGame("UPGRADES",      1, 3, 0,                            AppPhase.CasinoUpgrades);

            MkButton("Back", _casinoLobbyPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(300f, 90f), "BACK",
                () => _onCasinoNav((int)AppPhase.Home), sfx: "back");

            if (Application.isEditor)
                MkText("DebugHint", _casinoLobbyPanel.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 140f), new Vector2(960f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                    .text = "(Editor: T = +1000 steps)";
        }

        // One grid entry: col 0/1, row 0..3. Locked games show their level
        // gate and refuse the tap; the label refreshes per frame so an
        // unlock mid-session lights up immediately.
        void MkLobbyGame(string name, int col, int row, int unlockLv, AppPhase target)
        {
            var go = MkButton("G_" + name, _casinoLobbyPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(col == 0 ? -245f : 245f, -320f - 125f * row), new Vector2(460f, 110f),
                name, () =>
                {
                    if (_casino.host.level >= unlockLv) _onCasinoNav((int)target);
                    else Sfx.Play("error");
                });
            var lbl = go.GetComponentInChildren<TMP_Text>();
            lbl.fontSize = FS_LABEL;
            _lobbyGames.Add((go.GetComponent<Button>(), lbl, name, unlockLv));
        }

        void UpdateCasinoLobby(GamexGame g)
        {
            TickCoinBurst(Time.unscaledDeltaTime);
            foreach (var lg in _lobbyGames)
            {
                bool un = _casino.host.level >= lg.lv;
                lg.btn.interactable = un;
                lg.label.text  = un ? lg.name : $"{lg.name}\nLv {lg.lv}";
                lg.label.color = un ? new Color(0.20f, 0.12f, 0.05f) : new Color(0.42f, 0.33f, 0.22f);
            }
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
                // Sprite icon is primary (P5a); text is the fallback when
                // the art hasn't imported.
                _scCellTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(260f, 320f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
                _scCellIcons[i] = MkSpriteIcon("SymIcon", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(190f, 190f), (Sprite)null, Color.white).GetComponent<Image>();
                _scCellIcons[i].enabled = false;
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
        // until the reveal finishes, so hold it back — one term per room
        // with an unresolved play. High Stakes holds back its NET effect
        // (the escrowed wager already left the wallet).
        long CasinoDisplayCoins => _casino.host.coins
            - (_scHasPending && !TicketFullyRevealed ? _scPending.coins : 0)
            - (_slHasPending && !SlotsAllStopped ? _slPending.coins : 0)
            - (_fcHasPending && !FindCashAllRevealed ? _fcPending.coins : 0)
            - (_grHasPending && !_grRevealed ? _grPending.coins : 0)
            - (_mgHasPending && !_mgRevealed ? _mgPending.coins : 0)
            - (_hsHasPending && !_hsRevealed ? _hsPending.coins - _hsWager : 0);

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
                SetSymbolCell(_scCellIcons[i], _scCellTexts[i], _scCellSymbols[i]);
                _scFoils[i].ResetFoil();
            }
        }

        // Sprite-first symbol display with text fallback (art may be absent
        // in a fresh checkout before the Casino/ import).
        static Sprite CasinoSymbolSprite(CasinoTier t)
        {
            switch (t)
            {
                case CasinoTier.Seven:  return Make.Casino("sym_seven");
                case CasinoTier.Bar:    return Make.Casino("sym_bar");
                case CasinoTier.Bell:   return Make.Casino("sym_bell");
                case CasinoTier.Cherry: return Make.Casino("sym_cherry");
                default:                return null;
            }
        }

        static void SetSymbolCell(Image icon, TMP_Text label, CasinoTier t)
        {
            var s = CasinoSymbolSprite(t);
            if (icon != null && s != null)
            {
                icon.sprite  = s;
                icon.enabled = true;
                label.text   = "";
            }
            else
            {
                if (icon != null) icon.enabled = false;
                label.text  = CasinoSymbolLabel(t);
                label.color = CasinoTierColor(t);
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

            // The machine: Caz's layered cabinet. Bottom-to-top: glass
            // backdrop -> reel symbols -> translucent glass shade (reel
            // shading over the symbols) -> cabinet (windows are punched
            // holes) -> crank up/down. All layers share the same canvas so
            // stacked full-size Images stay registered. Falls back to bare
            // symbol text if the art is missing.
            _slMachine = MkPanel("Machine", _casinoSlotsPanel.transform, new Vector2(0.5f, 0.5f),
                SL_MACHINE_POS, SL_MACHINE_SIZE, new Color(0f, 0f, 0f, 0f));
            _slMachine.GetComponent<Image>().raycastTarget = false;

            MkSlotLayer("GlassBack", "slot_glass", Color.white);
            for (int i = 0; i < 3; i++)
            {
                _slReelTexts[i] = MkText("Sym" + i, _slMachine.transform, new Vector2(0.5f, 0.5f),
                    SL_REEL_POS[i], new Vector2(130f, 220f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
                _slReelTexts[i].text = "—";
                _slReelIcons[i] = MkSpriteIcon("SymIcon" + i, _slMachine.transform, new Vector2(0.5f, 0.5f),
                    SL_REEL_POS[i], new Vector2(106f, 106f), (Sprite)null, Color.white).GetComponent<Image>();
                _slReelIcons[i].enabled = false;
            }
            var shade = MkSlotLayer("GlassShade", "slot_glass", new Color(1f, 1f, 1f, 0.45f));
            MkSlotLayer("Cabinet", "slot_cabinet", Color.white);
            _slCrankUp   = MkSlotLayer("CrankUp",   "slot_crank_up",   Color.white);
            _slCrankDown = MkSlotLayer("CrankDown", "slot_crank_down", Color.white);
            if (_slCrankDown != null) _slCrankDown.enabled = false;

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

        // One full-canvas layer of the slot machine sandwich. Returns the
        // Image (disabled when the sprite hasn't been imported, so the
        // text-fallback view still works).
        Image MkSlotLayer(string name, string spriteName, Color tint)
        {
            var spr = Make.Casino(spriteName);
            var img = MkSpriteIcon(name, _slMachine.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, SL_MACHINE_SIZE, spr, tint).GetComponent<Image>();
            img.enabled = spr != null;
            return img;
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
            _slCrankDownT = 0.35f;   // pull the lever
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
                        SetSymbolCell(_slReelIcons[i], _slReelTexts[i], _slFinal[i]);
                        _slStopPop[i] = 0.25f;
                        Sfx.Play("tap");
                        if (i == 2) SlotsResolve();
                    }
                    else if (cycle)
                    {
                        var t = (CasinoTier)UnityEngine.Random.Range((int)CasinoTier.Cherry, (int)CasinoTier.Seven + 1);
                        SetSymbolCell(_slReelIcons[i], _slReelTexts[i], t);
                    }
                }
            }

            // Crank returns after the pull.
            if (_slCrankDownT > 0f) _slCrankDownT -= dt;
            bool crankDown = _slCrankDownT > 0f;
            if (_slCrankDown != null && _slCrankDown.sprite != null) _slCrankDown.enabled = crankDown;
            if (_slCrankUp   != null && _slCrankUp.sprite   != null) _slCrankUp.enabled   = !crankDown;

            // Per-reel stop pop.
            for (int i = 0; i < 3; i++)
            {
                float s = 1f;
                if (_slStopPop[i] > 0f)
                {
                    _slStopPop[i] -= dt;
                    float t01 = 1f - Mathf.Clamp01(_slStopPop[i] / 0.25f);
                    s = Mathf.Lerp(1.3f, 1f, t01);
                }
                _slReelTexts[i].rectTransform.localScale = new Vector3(s, s, 1f);
                _slReelIcons[i].rectTransform.localScale = new Vector3(s, s, 1f);
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
        // P5b shared juice helpers
        // ============================================================
        long TickCountUp(ref float shown, long target, float dt)
        {
            if (shown < 0f) shown = target;
            shown = Mathf.Abs(shown - target) < 1f
                ? target : Mathf.Lerp(shown, target, 1f - Mathf.Exp(-8f * dt));
            return (long)shown;
        }

        static void TickPop(RectTransform rt, ref float t, float dt)
        {
            float s = 1f;
            if (t > 0f)
            {
                t -= dt;
                float t01 = 1f - Mathf.Clamp01(t / 0.35f);
                s = Mathf.Lerp(1.35f, 1f, t01 * t01 * (3f - 2f * t01));
            }
            rt.localScale = new Vector3(s, s, 1f);
        }

        static void TickShake(GameObject card, Vector2 basePos, ref float t, float dt)
        {
            var rt = (RectTransform)card.transform;
            if (t <= 0f) return;
            t -= dt;
            float amp = Mathf.Lerp(0f, 16f, Mathf.Clamp01(t / 0.6f));
            rt.anchoredPosition = basePos + new Vector2(
                UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp, amp));
            if (t <= 0f) rt.anchoredPosition = basePos;
        }

        void PlayFanfare(PlayResult r, Transform panel, Vector2 origin, ref float shakeT)
        {
            if (r.jackpot)
            {
                Sfx.Play("milestone"); Sfx.Play("level_up");
                shakeT = 0.6f;
                SpawnCoinBurst(panel, origin, BURST_POOL);
            }
            else if (r.tier == CasinoTier.Bar)
            {
                Sfx.Play("coin"); Sfx.Play("quest_done", 0.7f);
                SpawnCoinBurst(panel, origin, 10);
            }
            else if (r.tier != CasinoTier.Bust) Sfx.Play("coin");
        }

        void MkCasinoHeader(GameObject panel, out TMP_Text coins, out TMP_Text tickets)
        {
            coins = MkText("Coins", panel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperLeft, TextWhite);
            tickets = MkText("Tickets", panel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperRight, TextWhite);
        }

        GameObject MkCasinoBack(GameObject panel)
            => MkButton("Back", panel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onCasinoNav((int)AppPhase.CasinoLobby), sfx: "back");

        // ============================================================
        // Find the Cash (P5b, Lv 5) — 3x3 grid, hunt 3-of-a-kind.
        // Classic table; the hunt is the theatre. Busts use a "—" dud
        // cell so no symbol ever reaches 3.
        // ============================================================
        void BuildCasinoFindCash(Transform root)
        {
            _casinoFindCashPanel = MkFullPanel("CasinoFindCash", root);
            MkCasinoHeader(_casinoFindCashPanel, out _fcCoins, out _fcTickets);

            _fcCard = MkSpritePanel("Card", _casinoFindCashPanel.transform, new Vector2(0.5f, 0.5f),
                FC_CARD_POS, new Vector2(760f, 680f), "panel", PanelTint);
            _fcCard.GetComponent<Image>().raycastTarget = false;
            MkText("Title", _fcCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(700f, 56f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· FIND THE CASH ·";
            MkText("Rule", _fcCard.transform, new Vector2(0.5f, 0f), new Vector2(0f, 22f),
                new Vector2(700f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "find 3 of a kind";

            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                int col = i % 3, row = i / 3;
                var cell = MkSpritePanel("Cell" + i, _fcCard.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-200f + 200f * col, 165f - 190f * row), new Vector2(180f, 176f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                _fcCellTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(170f, 170f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
                _fcCellIcons[i] = MkSpriteIcon("SymIcon", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(120f, 120f), (Sprite)null, Color.white).GetComponent<Image>();
                _fcCellIcons[i].enabled = false;
                var foilGo = new GameObject("Foil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                foilGo.transform.SetParent(cell.transform, false);
                var frt = foilGo.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(5f, 5f); frt.offsetMax = new Vector2(-5f, -5f);
                var foil = foilGo.AddComponent<ScratchFoil>();
                foil.Init();
                foil.onRevealed = () => OnFindCellRevealed(idx);
                _fcFoils[i] = foil;
            }

            _fcEmptyLabel = MkText("Empty", _casinoFindCashPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -190f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _fcResultLine = MkText("Result", _casinoFindCashPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -110f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);
            _fcNextBtn = MkButton("Next", _casinoFindCashPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 130f), "NEXT TICKET", () => FindCashDeal());
            MkCasinoBack(_casinoFindCashPanel);
        }

        bool FindCashAllRevealed
        {
            get { for (int i = 0; i < 9; i++) if (!_fcFlipped[i]) return false; return true; }
        }

        void OnEnterCasinoFindCash()
        {
            if (!_fcHasPending || FindCashAllRevealed) FindCashDeal();
        }

        void FindCashDeal()
        {
            if (!_casino.CanPlay) { _fcHasPending = false; return; }
            _fcPending = _casino.Play();   // classic table — the hunt is theatre
            _fcHasPending = true;
            FillFindGrid(_fcPending.tier);
            for (int i = 0; i < 9; i++)
            {
                _fcFlipped[i] = false;
                if (_fcDud[i])
                {
                    _fcCellIcons[i].enabled = false;
                    _fcCellTexts[i].text = "—";
                    _fcCellTexts[i].color = TextDim;
                }
                else SetSymbolCell(_fcCellIcons[i], _fcCellTexts[i], _fcSymbols[i]);
                _fcFoils[i].ResetFoil();
            }
        }

        // Win tier T: exactly 3 of T + 2 each of the other three symbols.
        // Bust: 2 of each symbol + one dud "—" (max possible with 4 symbol
        // types is 8 cells, so the 9th is the dud). Shuffled; presentation
        // randomness only — the payout was rolled in CasinoGame.
        void FillFindGrid(CasinoTier tier)
        {
            var syms = new List<CasinoTier>();
            var duds = new List<bool>();
            var all = new[] { CasinoTier.Cherry, CasinoTier.Bell, CasinoTier.Bar, CasinoTier.Seven };
            if (tier != CasinoTier.Bust)
            {
                for (int k = 0; k < 3; k++) { syms.Add(tier); duds.Add(false); }
                foreach (var o in all)
                    if (o != tier) { syms.Add(o); duds.Add(false); syms.Add(o); duds.Add(false); }
            }
            else
            {
                foreach (var o in all) { syms.Add(o); duds.Add(false); syms.Add(o); duds.Add(false); }
                syms.Add(CasinoTier.Bust); duds.Add(true);
            }
            for (int i = syms.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (syms[i], syms[j]) = (syms[j], syms[i]);
                (duds[i], duds[j]) = (duds[j], duds[i]);
            }
            for (int i = 0; i < 9; i++) { _fcSymbols[i] = syms[i]; _fcDud[i] = duds[i]; }
        }

        void OnFindCellRevealed(int i)
        {
            if (!_fcHasPending || _fcFlipped[i]) return;
            _fcFlipped[i] = true;
            if (!FindCashAllRevealed) return;
            _fcPopT = 0.35f;
            PlayFanfare(_fcPending, _casinoFindCashPanel.transform, FC_CARD_POS, ref _fcShakeT);
        }

        void UpdateCasinoFindCash()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            _fcCoins.text   = $"Coins: {TickCountUp(ref _fcCoinsShown, CasinoDisplayCoins, dt):N0}";
            _fcTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";
            _fcCard.SetActive(_fcHasPending);
            TickShake(_fcCard, FC_CARD_POS, ref _fcShakeT, dt);
            TickPop(_fcResultLine.rectTransform, ref _fcPopT, dt);
            _fcResultLine.text = _fcHasPending && FindCashAllRevealed ? CasinoResultText(_fcPending) : "";
            _fcNextBtn.SetActive((!_fcHasPending || FindCashAllRevealed) && _casino.CanPlay);
            bool dry = !_casino.CanPlay && (!_fcHasPending || FindCashAllRevealed);
            _fcEmptyLabel.gameObject.SetActive(dry);
            if (dry) _fcEmptyLabel.text =
                $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // Gold Rush (P5b, Lv 9) — dig through the dirt; whatever glints
        // is yours. High-variance table; buried coins peek out as you
        // scratch (the Scritchy corner-peek, reskinned as glints).
        // ============================================================
        void BuildCasinoGoldRush(Transform root)
        {
            _casinoGoldRushPanel = MkFullPanel("CasinoGoldRush", root);
            MkCasinoHeader(_casinoGoldRushPanel, out _grCoins, out _grTickets);

            _grField = MkSpritePanel("Field", _casinoGoldRushPanel.transform, new Vector2(0.5f, 0.5f),
                GR_FIELD_POS, new Vector2(900f, 620f), "panel", new Color(0.72f, 0.58f, 0.42f, 1f));
            _grField.GetComponent<Image>().raycastTarget = false;
            MkText("Title", _grField.transform, new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                new Vector2(840f, 52f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· GOLD RUSH ·";
            MkText("Table", _grField.transform, new Vector2(0.5f, 0f), new Vector2(0f, 18f),
                new Vector2(860f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                .text = "DUST 5 · POCKET 30 · CHEST 150 · VEIN 400 · MOTHERLODE 1,000";

            // Buried treasure layer (under the dirt foil).
            var coinSpr = Make.Casino("coin_gold_1");
            for (int i = 0; i < 12; i++)
            {
                _grItems[i] = coinSpr != null
                    ? MkSpriteIcon("Item" + i, _grField.transform, new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(72f, 60f), coinSpr, Color.white).GetComponent<Image>()
                    : MkSpriteIcon("Item" + i, _grField.transform, new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(56f, 56f), "coin", Color.white).GetComponent<Image>();
                _grItems[i].gameObject.SetActive(false);
            }

            // The dirt itself — one big scratchable foil.
            var foilGo = new GameObject("Dirt", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            foilGo.transform.SetParent(_grField.transform, false);
            var frt = foilGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(12f, 74f); frt.offsetMax = new Vector2(-12f, -70f);
            _grFoil = foilGo.AddComponent<ScratchFoil>();
            _grFoil.foilColor = new Color32(124, 94, 62, 255);   // dirt, not silver
            _grFoil.Init();
            _grFoil.onRevealed = OnGoldRushRevealed;

            _grEmptyLabel = MkText("Empty", _casinoGoldRushPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -190f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _grResultLine = MkText("Result", _casinoGoldRushPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -110f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);
            _grNextBtn = MkButton("Next", _casinoGoldRushPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 130f), "DIG AGAIN", () => GoldRushDeal());
            MkCasinoBack(_casinoGoldRushPanel);
        }

        void OnEnterCasinoGoldRush()
        {
            if (!_grHasPending || _grRevealed) GoldRushDeal();
        }

        void GoldRushDeal()
        {
            if (!_casino.CanPlay) { _grHasPending = false; return; }
            _grPending = _casino.PlayTable(CasinoGame.TableKind.GoldRush);
            _grHasPending = true;
            _grRevealed = false;
            int count;
            float scale;
            switch (_grPending.tier)
            {
                case CasinoTier.Seven:  count = 12; scale = 1.35f; break;   // MOTHERLODE
                case CasinoTier.Bar:    count = 8;  scale = 1.15f; break;
                case CasinoTier.Bell:   count = 5;  scale = 1f;    break;
                case CasinoTier.Cherry: count = 3;  scale = 0.9f;  break;
                default:                count = 1;  scale = 0.7f;  break;   // just dust
            }
            for (int i = 0; i < 12; i++)
            {
                bool on = i < count;
                _grItems[i].gameObject.SetActive(on);
                if (!on) continue;
                _grItems[i].rectTransform.anchoredPosition = new Vector2(
                    UnityEngine.Random.Range(-360f, 360f), UnityEngine.Random.Range(-190f, 200f));
                _grItems[i].rectTransform.localScale = Vector3.one * scale * UnityEngine.Random.Range(0.85f, 1.15f);
            }
            _grFoil.ResetFoil();
        }

        static string GoldRushResultText(PlayResult r)
        {
            switch (r.tier)
            {
                case CasinoTier.Seven:  return $"MOTHERLODE!  +{r.coins:N0} COINS";
                case CasinoTier.Bar:    return $"Struck a vein.  +{r.coins:N0} coins";
                case CasinoTier.Bell:   return $"A buried chest.  +{r.coins:N0} coins";
                case CasinoTier.Cherry: return $"A little pocket.  +{r.coins:N0} coins";
                default:                return $"Dust.  (+{r.coins:N0} coins)";
            }
        }

        void OnGoldRushRevealed()
        {
            if (!_grHasPending || _grRevealed) return;
            _grRevealed = true;
            _grPopT = 0.35f;
            PlayFanfare(_grPending, _casinoGoldRushPanel.transform, GR_FIELD_POS, ref _grShakeT);
        }

        void UpdateCasinoGoldRush()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            _grCoins.text   = $"Coins: {TickCountUp(ref _grCoinsShown, CasinoDisplayCoins, dt):N0}";
            _grTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";
            // (Foil visibility is owned by ResetFoil/Reveal; dug-up items
            // stay on show until the next dig re-buries the field.)
            TickShake(_grField, GR_FIELD_POS, ref _grShakeT, dt);
            TickPop(_grResultLine.rectTransform, ref _grPopT, dt);
            _grResultLine.text = _grHasPending && _grRevealed ? GoldRushResultText(_grPending) : "";
            _grNextBtn.SetActive((!_grHasPending || _grRevealed) && _casino.CanPlay);
            bool dry = !_casino.CanPlay && (!_grHasPending || _grRevealed);
            _grEmptyLabel.gameObject.SetActive(dry);
            if (dry) _grEmptyLabel.text =
                $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // The Ladder (P5b, Lv 13) — press-your-luck. EV 72 whatever you
        // do; the only choice is how much variance you can stomach.
        // ============================================================
        void BuildCasinoLadder(Transform root)
        {
            _casinoLadderPanel = MkFullPanel("CasinoLadder", root);
            MkCasinoHeader(_casinoLadderPanel, out _ldCoins, out _ldTickets);

            _ldCard = MkSpritePanel("Card", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                LD_CARD_POS, new Vector2(900f, 560f), "panel", PanelTint);
            _ldCard.GetComponent<Image>().raycastTarget = false;
            MkText("Title", _ldCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(840f, 56f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· THE LADDER ·";
            MkText("Cap", _ldCard.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                new Vector2(840f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                .text = "every rung: 50/50 to double — top of the ladder: 18,432";

            _ldPot = MkText("Pot", _ldCard.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(840f, 130f), FS_BIG, TextAnchor.MiddleCenter, TextWhite);
            _ldRungLine = MkText("Rung", _ldCard.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -50f), new Vector2(840f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
            _ldOddsLine = MkText("Odds", _ldCard.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -115f), new Vector2(840f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            _ldResultLine = MkText("Result", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -60f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);
            _ldEmptyLabel = MkText("Empty", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -140f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            _ldStartBtn = MkButton("Start", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -240f), new Vector2(600f, 140f), "START — 1 TICKET", LadderStartPressed);
            _ldClimbBtn = MkButton("Climb", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -240f), new Vector2(600f, 140f), "CLIMB", LadderClimbPressed);
            _ldBankBtn = MkButton("Bank", _casinoLadderPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -410f), new Vector2(600f, 120f), "BANK", LadderBankPressed,
                "btn_grey", "btn_grey_down");
            _ldBankLabel = _ldBankBtn.GetComponentInChildren<TMP_Text>();
            MkCasinoBack(_casinoLadderPanel);
        }

        void LadderStartPressed()
        {
            if (_casino.TryLadderStart()) _ldResult = null;
        }

        void LadderClimbPressed()
        {
            if (!_casino.LadderActive) return;
            if (_casino.LadderClimb())
            {
                Sfx.Play("coin");
                _ldPopT = 0.35f;
            }
            else
            {
                Sfx.Play("error");
                _ldShakeT = 0.5f;
                _ldResult = "The ladder snapped. Pot gone.";
                _ldPopT = 0.35f;
            }
        }

        void LadderBankPressed()
        {
            if (!_casino.LadderActive) return;
            int rung = _casino.LadderRung;
            long pot = _casino.LadderBank();
            _ldResult = $"Banked +{pot:N0} coins.";
            _ldPopT = 0.35f;
            if (rung >= CasinoGame.LADDER_JACKPOT_RUNG)
            {
                Sfx.Play("milestone");
                _ldShakeT = 0.4f;
                SpawnCoinBurst(_casinoLadderPanel.transform, LD_CARD_POS, BURST_POOL);
            }
            else Sfx.Play("coin");
        }

        void UpdateCasinoLadder()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            _ldCoins.text   = $"Coins: {TickCountUp(ref _ldCoinsShown, CasinoDisplayCoins, dt):N0}";
            _ldTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";

            bool active = _casino.LadderActive;
            bool atCap  = _casino.LadderRung >= CasinoGame.LADDER_MAX_RUNG;
            _ldPot.text      = active ? $"{_casino.LadderPot:N0}" : "—";
            _ldRungLine.text = active ? $"RUNG {_casino.LadderRung} / {CasinoGame.LADDER_MAX_RUNG}" : "buy a rung, start climbing";
            _ldOddsLine.text = active
                ? (atCap ? "top of the ladder — take it!" : $"climb: 50/50 → {_casino.LadderPot * 2:N0}")
                : $"a ticket starts the pot at {CasinoGame.LADDER_BASE_POT}";

            _ldStartBtn.SetActive(!active && _casino.CanPlay);
            _ldClimbBtn.SetActive(active && !atCap);
            _ldBankBtn.SetActive(active);
            if (active) _ldBankLabel.text = $"BANK {_casino.LadderPot:N0}";

            TickShake(_ldCard, LD_CARD_POS, ref _ldShakeT, dt);
            TickPop(_ldResultLine.rectTransform, ref _ldPopT, dt);
            _ldResultLine.text = _ldResult ?? "";

            bool dry = !active && !_casino.CanPlay;
            _ldEmptyLabel.gameObject.SetActive(dry);
            if (dry) _ldEmptyLabel.text =
                $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // High Stakes (P5b, Lv 24) — pick a wager, scratch one panel.
        // Bust eats the wager; CHERRY pushes, BELL x2, BAR x4, 7 x10.
        // The only room where banked coins are ever at risk — strictly
        // opt-in, multipliers printed on the card.
        // ============================================================
        void BuildCasinoHighStakes(Transform root)
        {
            _casinoHighStakesPanel = MkFullPanel("CasinoHighStakes", root);
            MkCasinoHeader(_casinoHighStakesPanel, out _hsCoins, out _hsTickets);

            _hsCard = MkSpritePanel("Card", _casinoHighStakesPanel.transform, new Vector2(0.5f, 0.5f),
                HS_CARD_POS, new Vector2(900f, 560f), "panel", PanelTint);
            _hsCard.GetComponent<Image>().raycastTarget = false;
            MkText("Title", _hsCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(840f, 56f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· HIGH STAKES ·";
            MkText("Table", _hsCard.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                new Vector2(860f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                .text = "CHERRY push · BELL x2 · BAR x4 · 7 x10 · bust eats the wager";

            var cell = MkSpritePanel("Cell", _hsCard.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -10f), new Vector2(270f, 330f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            _hsCellText = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(260f, 320f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
            _hsCellIcon = MkSpriteIcon("SymIcon", cell.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(190f, 190f), (Sprite)null, Color.white).GetComponent<Image>();
            _hsCellIcon.enabled = false;
            var hsFoilGo = new GameObject("Foil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            hsFoilGo.transform.SetParent(cell.transform, false);
            var hsFrt = hsFoilGo.GetComponent<RectTransform>();
            hsFrt.anchorMin = Vector2.zero; hsFrt.anchorMax = Vector2.one;
            hsFrt.offsetMin = new Vector2(8f, 8f); hsFrt.offsetMax = new Vector2(-8f, -8f);
            _hsFoil = hsFoilGo.AddComponent<ScratchFoil>();
            _hsFoil.Init();
            _hsFoil.onRevealed = OnHighStakesRevealed;
            _hsFoil.gameObject.SetActive(false);

            for (int i = 0; i < 3; i++)
            {
                long w = HS_WAGERS[i];
                var b = MkButton("Wager" + w, _casinoHighStakesPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(-310f + 310f * i, -140f), new Vector2(280f, 120f),
                    $"{w:N0}", () => HighStakesWager(w));
                _hsWagerBtns[i] = b.GetComponent<Button>();
            }
            MkText("WagerHint", _casinoHighStakesPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -230f), new Vector2(800f, 40f), FS_BODY, TextAnchor.MiddleCenter, TextDim)
                .text = "pick a wager — 1 ticket per play";

            _hsResultLine = MkText("Result", _casinoHighStakesPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -320f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);
            _hsEmptyLabel = MkText("Empty", _casinoHighStakesPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -400f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            MkCasinoBack(_casinoHighStakesPanel);
        }

        void HighStakesWager(long w)
        {
            if (_hsHasPending && !_hsRevealed) return;
            if (!_casino.CanPlay || _casino.host.coins < w) { Sfx.Play("error"); return; }
            _hsWager = w;
            _hsPending = _casino.PlayHighStakes(w);
            _hsHasPending = true;
            _hsRevealed = false;
            if (_hsPending.tier == CasinoTier.Bust)
            {
                _hsCellIcon.enabled = false;
                _hsCellText.text = "BUST";
                _hsCellText.color = new Color(0.6f, 0.35f, 0.3f);
            }
            else SetSymbolCell(_hsCellIcon, _hsCellText, _hsPending.tier);
            _hsFoil.ResetFoil();
        }

        void OnHighStakesRevealed()
        {
            if (!_hsHasPending || _hsRevealed) return;
            _hsRevealed = true;
            _hsPopT = 0.35f;
            if (_hsPending.tier == CasinoTier.Bust) { Sfx.Play("error"); _hsShakeT = 0.4f; }
            else PlayFanfare(_hsPending, _casinoHighStakesPanel.transform, HS_CARD_POS, ref _hsShakeT);
        }

        string HighStakesResultText()
        {
            var r = _hsPending;
            if (r.tier == CasinoTier.Bust) return $"Bust. The house keeps {_hsWager:N0}.";
            long net = r.coins - _hsWager;
            switch (r.tier)
            {
                case CasinoTier.Seven:  return $"7! WAGER x10 —  +{net:N0} COINS";
                case CasinoTier.Bar:    return $"BAR — wager x4.  +{net:N0} coins";
                case CasinoTier.Bell:   return $"BELL — wager x2.  +{net:N0} coins";
                default:                return "Cherry — push. Wager returned.";
            }
        }

        void UpdateCasinoHighStakes()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            _hsCoins.text   = $"Coins: {TickCountUp(ref _hsCoinsShown, CasinoDisplayCoins, dt):N0}";
            _hsTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";
            bool mid = _hsHasPending && !_hsRevealed;
            for (int i = 0; i < 3; i++)
                _hsWagerBtns[i].interactable = !mid && _casino.CanPlay && CasinoDisplayCoins >= HS_WAGERS[i];
            TickShake(_hsCard, HS_CARD_POS, ref _hsShakeT, dt);
            TickPop(_hsResultLine.rectTransform, ref _hsPopT, dt);
            _hsResultLine.text = _hsHasPending && _hsRevealed ? HighStakesResultText() : "";
            bool dry = !_casino.CanPlay && !mid;
            _hsEmptyLabel.gameObject.SetActive(dry);
            if (dry) _hsEmptyLabel.text =
                $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // MEGA JACKPOT (P5b, Lv 30) — the white whale. 1 in 2,000 for
        // 100,000 coins; every other ticket is a deadpan 20. Odds are
        // printed on the card — no fine print, ever.
        // ============================================================
        void BuildCasinoMega(Transform root)
        {
            _casinoMegaPanel = MkFullPanel("CasinoMega", root);
            MkCasinoHeader(_casinoMegaPanel, out _mgCoins, out _mgTickets);

            _mgCard = MkSpritePanel("Card", _casinoMegaPanel.transform, new Vector2(0.5f, 0.5f),
                MG_CARD_POS, new Vector2(760f, 640f), "panel", new Color(1f, 0.88f, 0.55f, 1f));
            _mgCard.GetComponent<Image>().raycastTarget = false;
            MkText("Title", _mgCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -26f),
                new Vector2(700f, 60f), FS_LABEL, TextAnchor.UpperCenter, new Color(0.45f, 0.28f, 0.05f))
                .text = "★ MEGA JACKPOT ★";
            MkText("Prize", _mgCard.transform, new Vector2(0.5f, 1f), new Vector2(0f, -86f),
                new Vector2(700f, 56f), FS_BTN, TextAnchor.UpperCenter, new Color(0.55f, 0.33f, 0.05f))
                .text = "TOP PRIZE: 100,000";
            MkText("Odds", _mgCard.transform, new Vector2(0.5f, 0f), new Vector2(0f, 20f),
                new Vector2(700f, 40f), FS_BODY, TextAnchor.LowerCenter, new Color(0.5f, 0.36f, 0.14f))
                .text = "1 in 2,000 — no fine print";

            var cell = MkSpritePanel("Cell", _mgCard.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -60f), new Vector2(340f, 380f),
                "panel_light", new Color(0.28f, 0.24f, 0.14f, 1f));
            _mgCellText = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(320f, 360f), FS_TITLE, TextAnchor.MiddleCenter, TextDim);
            _mgCellIcon = MkSpriteIcon("SymIcon", cell.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(240f, 240f), (Sprite)null, Color.white).GetComponent<Image>();
            _mgCellIcon.enabled = false;
            var mgFoilGo = new GameObject("Foil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            mgFoilGo.transform.SetParent(cell.transform, false);
            var mgFrt = mgFoilGo.GetComponent<RectTransform>();
            mgFrt.anchorMin = Vector2.zero; mgFrt.anchorMax = Vector2.one;
            mgFrt.offsetMin = new Vector2(8f, 8f); mgFrt.offsetMax = new Vector2(-8f, -8f);
            _mgFoil = mgFoilGo.AddComponent<ScratchFoil>();
            _mgFoil.foilColor = new Color32(206, 168, 74, 255);   // gold foil for the golden ticket
            _mgFoil.Init();
            _mgFoil.onRevealed = OnMegaRevealed;

            _mgResultLine = MkText("Result", _casinoMegaPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -110f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);
            _mgEmptyLabel = MkText("Empty", _casinoMegaPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -190f), new Vector2(1040f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _mgNextBtn = MkButton("Next", _casinoMegaPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(540f, 130f), "ANOTHER — 1 TICKET", () => MegaDeal());
            MkCasinoBack(_casinoMegaPanel);
        }

        void OnEnterCasinoMega()
        {
            if (!_mgHasPending || _mgRevealed) MegaDeal();
        }

        void MegaDeal()
        {
            if (!_casino.CanPlay) { _mgHasPending = false; return; }
            _mgPending = _casino.PlayTable(CasinoGame.TableKind.Mega);
            _mgHasPending = true;
            _mgRevealed = false;
            if (_mgPending.jackpot)
            {
                var seven = Make.Casino("sym_seven");
                if (seven != null) { _mgCellIcon.sprite = seven; _mgCellIcon.enabled = true; _mgCellText.text = ""; }
                else { _mgCellIcon.enabled = false; _mgCellText.text = "MEGA"; _mgCellText.color = new Color(1f, 0.3f, 0.25f); }
            }
            else
            {
                _mgCellIcon.enabled = false;
                _mgCellText.text = "20";
                _mgCellText.color = TextDim;
            }
            _mgFoil.ResetFoil();
        }

        void OnMegaRevealed()
        {
            if (!_mgHasPending || _mgRevealed) return;
            _mgRevealed = true;
            _mgPopT = 0.35f;
            if (_mgPending.jackpot)
            {
                // The whale. Double burst + long shake + everything we have.
                Sfx.Play("milestone"); Sfx.Play("level_up"); Sfx.Play("coin");
                _mgShakeT = 1.0f;
                SpawnCoinBurst(_casinoMegaPanel.transform, MG_CARD_POS, BURST_POOL);
                SpawnCoinBurst(_casinoMegaPanel.transform, MG_CARD_POS + new Vector2(0f, -300f), BURST_POOL);
            }
        }

        void UpdateCasinoMega()
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            _mgCoins.text   = $"Coins: {TickCountUp(ref _mgCoinsShown, CasinoDisplayCoins, dt):N0}";
            _mgTickets.text = $"Tickets: {_casino.state.ticketsBanked:N0}";
            _mgCard.SetActive(true);
            _mgFoil.transform.parent.gameObject.SetActive(_mgHasPending);
            TickShake(_mgCard, MG_CARD_POS, ref _mgShakeT, dt);
            TickPop(_mgResultLine.rectTransform, ref _mgPopT, dt);
            _mgResultLine.text = _mgHasPending && _mgRevealed
                ? (_mgPending.jackpot ? $"★ MEGA JACKPOT ★  +{_mgPending.coins:N0} COINS"
                                      : $"Not this time.  (+{_mgPending.coins:N0})")
                : "";
            _mgNextBtn.SetActive((!_mgHasPending || _mgRevealed) && _casino.CanPlay);
            bool dry = !_casino.CanPlay && (!_mgHasPending || _mgRevealed);
            _mgEmptyLabel.gameObject.SetActive(dry);
            if (dry) _mgEmptyLabel.text =
                $"Out of tickets — walk {_casino.StepsPerTicket - _casino.state.stepAccumulator} more steps.";
        }

        // ============================================================
        // Coin burst — pooled UI coins with gravity, for 777s + prestige.
        // ============================================================
        void EnsureBurstPool()
        {
            if (_burstCoins != null) return;
            // Caz coin-flip flipbook (P5a); falls back to the old UI coin.
            _coinFrames = new Sprite[6];
            bool anyFrame = false;
            for (int i = 0; i < 6; i++)
            {
                _coinFrames[i] = Make.Casino("coin_gold_" + (i + 1));
                anyFrame |= _coinFrames[i] != null;
            }
            if (!anyFrame) _coinFrames = null;

            _burstCoins = new Image[BURST_POOL];
            _burstVel   = new Vector2[BURST_POOL];
            _burstSpin  = new float[BURST_POOL];
            for (int i = 0; i < BURST_POOL; i++)
            {
                var go = _coinFrames != null
                    ? MkSpriteIcon("BurstCoin" + i, _casinoLobbyPanel.transform,
                        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 53f), _coinFrames[0], Color.white)
                    : MkSpriteIcon("BurstCoin" + i, _casinoLobbyPanel.transform,
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
            // Flipbook: all airborne coins share a frame — reads as glinting.
            Sprite frame = null;
            if (_coinFrames != null)
            {
                _coinFlipT += dt;
                frame = _coinFrames[(int)(_coinFlipT / 0.08f) % 6];
            }
            bool any = false;
            for (int i = 0; i < BURST_POOL; i++)
            {
                var img = _burstCoins[i];
                if (!img.gameObject.activeSelf) continue;
                if (frame != null) img.sprite = frame;
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
