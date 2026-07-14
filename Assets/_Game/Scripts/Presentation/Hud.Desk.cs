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
        // The Desk (Pivot 3 — docs/casino-mvp-plan.md). One scene:
        //   top     — wallet (red in debt) + lifetime-earnings unlock bar
        //   desk    — the paycheck envelope (stride-roll collection) +
        //             pile summary; the scratch MAT with real per-spot
        //             scratching arrives in R2-3
        //   rows    — the catalog (6 cards: cost / Lv / owned / BUY /
        //             PLAY) + the 3 money upgrades
        // R2-2 ships a BASIC play path (instant full-reveal via
        // ScratchAll) so the whole loop is playable; R2-3 replaces it
        // with the foil mat, printed odds panels and peek-and-bail.
        // DeskGame is held directly (results feed animations
        // synchronously); nav goes through _onDeskNav.
        // ============================================================

        GameObject _deskPanel;
        TMP_Text _dkCoins, _dkLoanLine, _dkBarLabel, _dkStepsLine, _dkPileLine, _dkResultLine;
        RectTransform _dkBarFill;
        GameObject _dkEnvelopeBtn;
        TMP_Text   _dkEnvelopeLabel;
        string _dkResult;
        float _dkPopT, _dkShakeT, _dkCoinsShown = -1f;
        GameObject _dkDesk;
        const float DK_BAR_W = 720f;
        static readonly Vector2 DK_DESK_POS = new Vector2(0f, 430f);

        (TMP_Text name, TMP_Text info, Button buy, TMP_Text buyLbl, Button play)[] _dkCardRows;
        (TMP_Text name, TMP_Text info, Button buy, TMP_Text buyLbl)[] _dkUpRows;
        long _dkPrevEarned = -1;   // unlock-celebration edge detector

        // ---- R2-4: robot arm, the phone, the prestige tree ----
        GameObject _robotBtn, _phoneBtn, _phonePanel, _prestigePanel, _prestigeBtn, _pgGoBtn;
        TMP_Text _prestigeBtnLbl, _pgPp, _pgElig, _pgGoLbl;
        float _pgArmedUntil;
        (TMP_Text name, TMP_Text desc, Button buy, TMP_Text buyLbl)[] _pgRows;

        // ---- the MAT (R2-3): real spot-by-spot scratching ----
        GameObject _matPanel, _matCardGo, _matBailBtn, _matNextBtn, _matDoneBtn;
        TMP_Text _matTitle, _matResult, _matStatus, _matOdds, _matBailLbl, _matNextLbl;
        readonly GameObject[]  _matCells = new GameObject[9];
        readonly Image[]       _matIcons = new Image[9];
        readonly TMP_Text[]    _matTexts = new TMP_Text[9];
        readonly ScratchFoil[] _matFoils = new ScratchFoil[9];
        readonly bool[]        _matRevealed = new bool[9];
        DealtCard  _matCard;
        DeskResult _matRes;
        bool  _matActive, _matResolved;
        int   _matPrevLevel;
        float _matPopT, _matShakeT;
        static readonly Vector2 MAT_CARD_POS = new Vector2(0f, 180f);
        static readonly string[] MAT_SYM_SPRITES = { "sym_cherry", "sym_bell", "sym_bar", "sym_seven" };
        static readonly string[] MAT_SYM_NAMES   = { "CHERRY", "BELL", "BAR", "SEVEN" };

        // ---- coin burst pool (shared juice, migrated from the casino) ----
        const int BURST_POOL = 26;
        Image[]   _burstCoins;
        Vector2[] _burstVel;
        float[]   _burstSpin;
        bool      _burstAny;
        Sprite[]  _coinFrames;
        float     _coinFlipT;

        void BuildDesk(Transform root)
        {
            _deskPanel = MkFullPanel("Desk", root);

            // ---- header ----
            _dkCoins = MkText("Coins", _deskPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -75f - SAFE_AREA_TOP_INSET), new Vector2(640f, 70f),
                FS_TITLE, TextAnchor.UpperLeft, TextWhite);
            MkButton("Back", _deskPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -70f - SAFE_AREA_TOP_INSET), new Vector2(220f, 76f), "HOME",
                () => _onDeskNav((int)AppPhase.Home), "btn_grey", "btn_grey_down", "back");
            _dkLoanLine = MkText("Loan", _deskPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -150f - SAFE_AREA_TOP_INSET), new Vector2(640f, 44f),
                FS_BODY, TextAnchor.UpperLeft, new Color(1f, 0.45f, 0.4f));

            var barBg = MkPanel("BarBg", _deskPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -210f - SAFE_AREA_TOP_INSET), new Vector2(DK_BAR_W, 30f),
                new Color(0.10f, 0.09f, 0.16f, 1f));
            var fill = MkPanel("Fill", barBg.transform, new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(0f, 24f), AccentGold);
            _dkBarFill = fill.GetComponent<RectTransform>();
            _dkBarLabel = MkText("BarLabel", _deskPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -244f - SAFE_AREA_TOP_INSET), new Vector2(900f, 40f),
                FS_BODY, TextAnchor.UpperCenter, TextDim);

            // ---- the desk surface ----
            _dkDesk = MkSpritePanel("Surface", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                DK_DESK_POS, new Vector2(1000f, 460f), "panel", new Color(0.62f, 0.45f, 0.30f, 1f));
            _dkDesk.GetComponent<Image>().raycastTarget = false;

            _dkEnvelopeBtn = MkButton("Envelope", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 490f), new Vector2(330f, 180f), "PAYCHECK",
                DeskTearEnvelope);
            _dkEnvelopeLabel = _dkEnvelopeBtn.GetComponentInChildren<TMP_Text>();
            _dkStepsLine = MkText("Steps", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 370f), new Vector2(420f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);
            // the earned automation (R2-4) — appears once the tree node is bought
            _robotBtn = MkButton("Robot", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 280f), new Vector2(330f, 84f), "ROBOT ARM",
                DeskRobotArm, "btn_grey", "btn_grey_down");
            _robotBtn.GetComponentInChildren<TMP_Text>().fontSize = FS_LABEL;

            _dkPileLine = MkText("Pile", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(230f, 555f), new Vector2(470f, 90f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _prestigeBtn = MkButton("Prestige", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(230f, 440f), new Vector2(360f, 95f), "PRESTIGE",
                () => { _pgArmedUntil = 0f; _prestigePanel.SetActive(true); });
            _prestigeBtnLbl = _prestigeBtn.GetComponentInChildren<TMP_Text>();
            _prestigeBtnLbl.fontSize = FS_LABEL;
            // the loan shark's phone — only rings when you're broke
            _phoneBtn = MkButton("Phone", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(230f, 300f), new Vector2(360f, 84f), "THE PHONE RINGS",
                () => _phonePanel.SetActive(true), "btn_grey", "btn_grey_down");
            _phoneBtn.GetComponentInChildren<TMP_Text>().fontSize = FS_BODY;

            _dkResultLine = MkText("Result", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 150f), new Vector2(1050f, 64f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            // ---- catalog rows ----
            int n = DeskGame.CATALOG.Length;
            _dkCardRows = new (TMP_Text, TMP_Text, Button, TMP_Text, Button)[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float y = 60f - 112f * i;
                var row = MkSpritePanel("Card_" + i, _deskPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(1000f, 104f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                var nameL = MkText("Name", row.transform, new Vector2(0f, 1f), new Vector2(26f, -10f),
                    new Vector2(480f, 46f), FS_LABEL, TextAnchor.UpperLeft, TextWhite);
                var infoL = MkText("Info", row.transform, new Vector2(0f, 0f), new Vector2(26f, 10f),
                    new Vector2(560f, 38f), FS_BODY, TextAnchor.LowerLeft, TextDim);
                var buyGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-190f, 0f),
                    new Vector2(160f, 84f), "BUY", () => DeskBuy(idx));
                var buyLbl = buyGo.GetComponentInChildren<TMP_Text>();
                buyLbl.fontSize = FS_LABEL;
                var playGo = MkButton("Play", row.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f),
                    new Vector2(160f, 84f), "PLAY", () => DeskEnterMat(idx), "btn_grey", "btn_grey_down");
                playGo.GetComponentInChildren<TMP_Text>().fontSize = FS_LABEL;
                _dkCardRows[i] = (nameL, infoL, buyGo.GetComponent<Button>(), buyLbl, playGo.GetComponent<Button>());
            }

            // ---- money upgrades ----
            MkText("UpHeader", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -620f), new Vector2(900f, 40f), FS_BODY, TextAnchor.MiddleCenter, TextDim)
                .text = "— UPGRADES —";
            var ups = new[]
            {
                (DeskGame.Upgrade.Luck, "Scratch Luck", "fewer blank spots on every card"),
                (DeskGame.Upgrade.Size, "Scratch Size", "a fatter thumb (bigger brush)"),
                (DeskGame.Upgrade.Coin, "Lucky Coin",   "stronger coin vs tough foil"),
            };
            _dkUpRows = new (TMP_Text, TMP_Text, Button, TMP_Text)[3];
            for (int i = 0; i < 3; i++)
            {
                var u = ups[i];
                float y = -692f - 88f * i;
                var row = MkSpritePanel("Up_" + i, _deskPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(1000f, 80f),
                    "panel_light", new Color(0.14f, 0.16f, 0.24f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                var nameL = MkText("Name", row.transform, new Vector2(0f, 0.5f), new Vector2(26f, 8f),
                    new Vector2(520f, 42f), FS_LABEL, TextAnchor.MiddleLeft, TextWhite);
                nameL.text = u.Item2;
                var infoL = MkText("Info", row.transform, new Vector2(0f, 0.5f), new Vector2(26f, -22f),
                    new Vector2(600f, 32f), FS_BODY, TextAnchor.MiddleLeft, TextDim);
                infoL.text = u.Item3;
                var kind = u.Item1;
                var buyGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f),
                    new Vector2(200f, 66f), "—",
                    () => Sfx.Play(_desk.TryBuyUpgrade(kind) ? "purchase" : "error"));
                var buyLbl = buyGo.GetComponentInChildren<TMP_Text>();
                buyLbl.fontSize = FS_BODY;
                _dkUpRows[i] = (nameL, infoL, buyGo.GetComponent<Button>(), buyLbl);
            }

            BuildDeskPhone();
            BuildDeskPrestige();
            BuildDeskMat();   // topmost child — the scratch overlay
        }

        // ============================================================
        // The phone (R2-4) — comedically transparent loan terms.
        // ============================================================
        void BuildDeskPhone()
        {
            _phonePanel = MkFullPanel("PhoneModal", _deskPanel.transform);
            var dim = _phonePanel.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;
            var paper = MkSpritePanel("Paper", _phonePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 160f), new Vector2(880f, 760f), "panel",
                new Color(0.96f, 0.93f, 0.82f, 1f));
            paper.GetComponent<Image>().raycastTarget = false;
            MkText("Title", paper.transform, new Vector2(0.5f, 1f), new Vector2(0f, -36f),
                new Vector2(800f, 64f), FS_TITLE, TextAnchor.UpperCenter, new Color(0.25f, 0.15f, 0.08f))
                .text = "A LOAN, FRIEND?";
            MkText("Terms", paper.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f),
                new Vector2(780f, 460f), FS_LABEL, TextAnchor.MiddleCenter, new Color(0.30f, 0.20f, 0.10f))
                .text = "the deal:\n\n+500 coins, right now\nyou owe 750\nwe take HALF of everything\nyou earn until it's paid\n\nAPR: yes\nno hidden fees (this is all of them)";
            MkButton("Sign", _phonePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-240f, -330f), new Vector2(420f, 120f), "SIGN HERE", () =>
                {
                    if (_desk.TakeLoan())
                    {
                        _dkResult = "borrowed 500. the phone is pleased.";
                        _dkPopT = 0.35f;
                        Sfx.Play("coin");
                    }
                    _phonePanel.SetActive(false);
                });
            MkButton("No", _phonePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(240f, -330f), new Vector2(420f, 120f), "NO THANKS",
                () => _phonePanel.SetActive(false), "btn_grey", "btn_grey_down", "back");
            _phonePanel.SetActive(false);
        }

        // ============================================================
        // Prestige (R2-4) — cash out the run, spend PP on the tree.
        // ============================================================
        void BuildDeskPrestige()
        {
            _prestigePanel = MkFullPanel("PrestigeModal", _deskPanel.transform);
            var dim = _prestigePanel.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.80f);
            dim.raycastTarget = true;

            MkText("Title", _prestigePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -90f - SAFE_AREA_TOP_INSET), new Vector2(900f, 70f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "PRESTIGE";
            _pgPp = MkText("Pp", _prestigePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -170f - SAFE_AREA_TOP_INSET), new Vector2(900f, 50f),
                FS_LABEL, TextAnchor.UpperCenter, TextWhite);
            _pgElig = MkText("Elig", _prestigePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -228f - SAFE_AREA_TOP_INSET), new Vector2(1000f, 46f),
                FS_BODY, TextAnchor.UpperCenter, TextDim);

            _pgGoBtn = MkButton("Go", _prestigePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 560f), new Vector2(680f, 120f), "PRESTIGE", DeskPrestigePressed);
            _pgGoLbl = _pgGoBtn.GetComponentInChildren<TMP_Text>();
            _pgGoLbl.fontSize = FS_LABEL;

            int n = DeskGame.PERMS.Length;
            _pgRows = new (TMP_Text, TMP_Text, Button, TMP_Text)[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float y = 420f - 96f * i;
                var row = MkSpritePanel("Node_" + i, _prestigePanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(1010f, 88f),
                    "panel_light", new Color(0.14f, 0.16f, 0.24f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                var nameL = MkText("Name", row.transform, new Vector2(0f, 1f), new Vector2(24f, -8f),
                    new Vector2(560f, 44f), FS_LABEL, TextAnchor.UpperLeft, TextWhite);
                var descL = MkText("Desc", row.transform, new Vector2(0f, 0f), new Vector2(24f, 8f),
                    new Vector2(700f, 34f), FS_BODY, TextAnchor.LowerLeft, TextDim);
                var buyGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f),
                    new Vector2(210f, 70f), "—",
                    () => Sfx.Play(_desk.TryBuyPerm(idx) ? "purchase" : "error"));
                var buyLbl = buyGo.GetComponentInChildren<TMP_Text>();
                buyLbl.fontSize = FS_BODY;
                _pgRows[i] = (nameL, descL, buyGo.GetComponent<Button>(), buyLbl);
            }

            MkButton("Close", _prestigePanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 40f), new Vector2(300f, 92f), "CLOSE",
                () => _prestigePanel.SetActive(false), "btn_grey", "btn_grey_down", "back");
            _prestigePanel.SetActive(false);
        }

        void DeskPrestigePressed()
        {
            if (!_desk.PrestigeEligible) return;
            if (Time.unscaledTime >= _pgArmedUntil)
            {
                _pgArmedUntil = Time.unscaledTime + 3f;   // two-tap confirm
                Sfx.Play("tap");
                return;
            }
            float pp = _desk.PrestigePpPreview;
            int num = _desk.state.prestigeCount + 1;
            _desk.TryPrestige();
            _pgArmedUntil = 0f;
            _dkResult = $"★ PRESTIGE {num} ★  +{pp:0.#} PP";
            _dkPopT = 0.5f;
            Sfx.Play("milestone"); Sfx.Play("level_up");
            SpawnCoinBurst(_prestigePanel.transform, new Vector2(0f, 300f), BURST_POOL);
            _dkCoinsShown = -1f;
        }

        void UpdateDeskPrestige()
        {
            var d = _desk.state;
            _pgPp.text = $"Prestige Points: {d.prestigePoints:0.#}   ·   prestige #{d.prestigeCount + 1}";
            bool elig = _desk.PrestigeEligible;
            string why = elig
                ? $"ready — cash out for +{_desk.PrestigePpPreview:0.#} PP (wallet, upgrades and card levels reset)"
                : $"prestige at {DeskGame.PRESTIGE_AT:N0} earned this run — you're at {d.earnedThisRun:N0}";
            if (d.loanOwed > 0) why += "  ·  repay the loan first";
            if (_desk.host.coins < 0) why += "  ·  climb out of debt first";
            _pgElig.text = why;
            bool armed = Time.unscaledTime < _pgArmedUntil;
            _pgGoBtn.GetComponent<Button>().interactable = elig;
            _pgGoLbl.text = !elig ? "PRESTIGE"
                : armed ? "SURE? EVERYTHING RESETS"
                : $"PRESTIGE  +{_desk.PrestigePpPreview:0.#} PP";

            for (int i = 0; i < _pgRows.Length; i++)
            {
                var p = DeskGame.PERMS[i];
                var row = _pgRows[i];
                int lvl = _desk.PermLvl(i);
                bool open = _desk.PermUnlockable(i);
                bool capped = lvl >= p.maxLvl;
                row.name.text = $"{p.name}   Lv {lvl}/{p.maxLvl}";
                row.name.color = open ? TextWhite : TextDim;
                row.desc.text = open ? p.desc : $"needs {DeskGame.PERMS[p.prereq].name}";
                row.buyLbl.text = capped ? "MAX" : $"{_desk.PermCost(i):N0} PP";
                row.buy.interactable = open && !capped && d.prestigePoints >= _desk.PermCost(i);
            }
        }

        // The Robot Arm: rips the whole pile, one aggregate report.
        void DeskRobotArm()
        {
            long pay = 0, pen = 0; int count = 0, bigs = 0;
            for (int i = 0; i < DeskGame.CATALOG.Length; i++)
                while (_desk.state.cardsOwned[i] > 0)
                {
                    var r = _desk.ScratchAll(i);
                    pay += r.payout; pen += r.penalty; count++;
                    if (r.bigWin) bigs++;
                }
            if (count == 0) { Sfx.Play("error"); return; }
            _dkResult = $"the arm scratched {count}: +{pay:N0}"
                      + (pen > 0 ? $" · traps −{pen:N0}" : "")
                      + (bigs > 0 ? $" · {bigs} BIG WIN{(bigs > 1 ? "S" : "")}!" : "");
            _dkPopT = 0.4f;
            if (bigs > 0)
            {
                Sfx.Play("milestone");
                _dkShakeT = 0.5f;
                SpawnCoinBurst(_deskPanel.transform, DK_DESK_POS, BURST_POOL);
            }
            else Sfx.Play("coin");
        }

        void DeskBuy(int i)
        {
            Sfx.Play(_desk.TryBuyCard(i) ? "purchase" : "error");
        }

        // ============================================================
        // The MAT — a dealt card, its printed odds, and a thumb. Spots
        // are pre-rolled (DealCard); scratching a spot's foil reveals it;
        // revealed traps are committed. BAIL / CASH OUT resolves with only
        // what you've revealed — the Scritchy peek-and-bail, for real.
        // ============================================================
        void BuildDeskMat()
        {
            _matPanel = MkFullPanel("MatOverlay", _deskPanel.transform);
            var dim = _matPanel.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;   // swallow taps to the desk behind

            _matCardGo = MkSpritePanel("MatCard", _matPanel.transform, new Vector2(0.5f, 0.5f),
                MAT_CARD_POS, new Vector2(1010f, 1050f), "panel", PanelTint);
            _matCardGo.GetComponent<Image>().raycastTarget = false;
            _matTitle = MkText("Title", _matCardGo.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(940f, 56f), FS_LABEL, TextAnchor.UpperCenter, AccentGold);
            _matResult = MkText("Result", _matCardGo.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -88f), new Vector2(980f, 60f), FS_BTN, TextAnchor.UpperCenter, AccentGold);
            _matStatus = MkText("Status", _matCardGo.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 148f), new Vector2(940f, 46f), FS_LABEL, TextAnchor.LowerCenter, TextWhite);
            _matOdds = MkText("Odds", _matCardGo.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(960f, 108f), FS_BODY, TextAnchor.LowerCenter, TextDim);

            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var cell = MkSpritePanel("Spot" + i, _matCardGo.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(210f, 210f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                _matCells[i] = cell;
                _matTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(200f, 200f), FS_TITLE, TextAnchor.MiddleCenter, TextDim);
                _matIcons[i] = MkSpriteIcon("Icon", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(140f, 140f), (Sprite)null, Color.white).GetComponent<Image>();
                _matIcons[i].enabled = false;
                var foilGo = new GameObject("Foil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                foilGo.transform.SetParent(cell.transform, false);
                var frt = foilGo.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(6f, 6f); frt.offsetMax = new Vector2(-6f, -6f);
                var foil = foilGo.AddComponent<ScratchFoil>();
                foil.Init();
                foil.onRevealed = () => OnMatSpot(idx);
                _matFoils[i] = foil;
            }

            _matBailBtn = MkButton("Bail", _matPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-260f, -450f), new Vector2(400f, 110f), "BAIL",
                MatResolveNow, "btn_grey", "btn_grey_down");
            _matBailLbl = _matBailBtn.GetComponentInChildren<TMP_Text>();
            _matNextBtn = MkButton("Next", _matPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-260f, -450f), new Vector2(400f, 110f), "NEXT",
                () => { int i = _matCard.cardIdx; ExitMat(); DeskEnterMat(i); });
            _matNextLbl = _matNextBtn.GetComponentInChildren<TMP_Text>();
            _matDoneBtn = MkButton("Done", _matPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(260f, -450f), new Vector2(400f, 110f), "DONE",
                ExitMat, "btn_grey", "btn_grey_down", "back");

            _matPanel.SetActive(false);
        }

        static Vector2[] MatLayout(int spots)
        {
            switch (spots)
            {
                case 1: return new[] { new Vector2(0f, 60f) };
                case 3: return new[] { new Vector2(-300f, 60f), new Vector2(0f, 60f), new Vector2(300f, 60f) };
                case 6: return new[]
                {
                    new Vector2(-280f, 190f), new Vector2(0f, 190f), new Vector2(280f, 190f),
                    new Vector2(-280f, -70f), new Vector2(0f, -70f), new Vector2(280f, -70f),
                };
                case 8: return new[]
                {
                    new Vector2(-280f, 230f), new Vector2(0f, 230f), new Vector2(280f, 230f),
                    new Vector2(-280f, 10f),  new Vector2(0f, 10f),  new Vector2(280f, 10f),
                    new Vector2(-145f, -210f), new Vector2(145f, -210f),
                };
                default: return new[]
                {
                    new Vector2(-260f, 230f), new Vector2(0f, 230f), new Vector2(260f, 230f),
                    new Vector2(-260f, 10f),  new Vector2(0f, 10f),  new Vector2(260f, 10f),
                    new Vector2(-260f, -210f), new Vector2(0f, -210f), new Vector2(260f, -210f),
                };
            }
        }

        static string MatOddsText(CardDef c)
        {
            if (c.whaleX > 0)
                return $"1 in {Mathf.RoundToInt(1f / c.whaleP):N0} pays {c.whaleX:N0}x the cost\nno fine print · hardness {c.hardness}";
            if (c.multP != null)
            {
                var parts = new List<string>();
                for (int k = 0; k < c.multP.Length; k++)
                    parts.Add($"x{c.multX[k]:0} {c.multP[k]:P0}");
                return string.Join("  ·  ", parts) + $"\ninstant multiplier · hardness {c.hardness}";
            }
            var lines = new List<string>();
            for (int k = 0; k < c.symP.Length; k++)
                lines.Add($"{MAT_SYM_NAMES[k]} {c.symP[k]:P0}—{c.symPrize[k]:N0}");
            string meta = $"match {c.matchNeed} · blank {DeskGame.JunkP(c):P0}";
            if (c.trapChance > 0) meta += $" · TRAP {c.trapChance:P0}—minus {c.trapPenalty:N0}";
            return string.Join("  ", lines) + "\n" + meta + $" · hardness {c.hardness}";
        }

        void DeskEnterMat(int i)
        {
            if (_desk.state.cardsOwned[i] <= 0) { Sfx.Play("error"); return; }
            _matPrevLevel = _desk.state.cardLevel[i];
            _matCard = _desk.DealCard(i);
            if (_matCard.spots == null) return;
            _matActive = true;
            _matResolved = false;
            _matResult.text = "";
            var c = DeskGame.CATALOG[i];
            _matTitle.text = $"· {c.name.ToUpper()} ·   Lv {_desk.state.cardLevel[i]}";
            _matOdds.text = MatOddsText(c);

            float brush = (1f + 0.10f * _desk.state.upSize) * (1f + 0.08f * _desk.state.upCoin)
                        / (0.55f + 0.45f * c.hardness);
            var pos = MatLayout(c.spots);
            float size = c.spots == 1 ? 360f : c.spots == 3 ? 260f : c.spots == 6 ? 240f : 205f;
            for (int s = 0; s < 9; s++)
            {
                bool on = s < c.spots;
                _matCells[s].SetActive(on);
                if (!on) continue;
                _matRevealed[s] = false;
                var rt = (RectTransform)_matCells[s].transform;
                rt.anchoredPosition = pos[s];
                rt.sizeDelta = new Vector2(size, size);
                SetMatSpotFace(s, c);
                _matFoils[s].brushScale = brush;
                _matFoils[s].ResetFoil();
            }
            _matPanel.SetActive(true);
        }

        // What's printed under the foil for spot s.
        void SetMatSpotFace(int s, CardDef c)
        {
            var icon = _matIcons[s];
            var txt  = _matTexts[s];
            icon.enabled = false;
            txt.text = "";
            if (c.matchNeed == 1)
            {
                // instant card: show the pre-rolled outcome
                if (_matCard.instant <= 0) { txt.text = "—"; txt.color = TextDim; return; }
                if (c.whaleX > 0)
                {
                    var seven = Make.Casino("sym_seven");
                    if (seven != null) { icon.sprite = seven; icon.enabled = true; }
                    txt.text = seven == null ? "MEGA" : "";
                    txt.color = new Color(1f, 0.35f, 0.3f);
                }
                else
                {
                    long mult = _matCard.instant / Math.Max(1, c.cost);
                    txt.text = $"x{mult}";
                    txt.color = mult >= 5 ? new Color(1f, 0.35f, 0.3f) : mult >= 2 ? AccentGold : TextWhite;
                }
                return;
            }
            sbyte v = _matCard.spots[s];
            if (v == Spot.TRAP) { txt.text = "✗"; txt.color = new Color(1f, 0.35f, 0.3f); return; }
            if (v == Spot.JUNK) { txt.text = "—"; txt.color = TextDim; return; }
            var spr = Make.Casino(MAT_SYM_SPRITES[v]);
            if (spr != null) { icon.sprite = spr; icon.enabled = true; }
            else { txt.text = MAT_SYM_NAMES[v]; txt.color = AccentGold; }
        }

        void OnMatSpot(int s)
        {
            if (!_matActive || _matResolved || _matRevealed[s]) return;
            _matRevealed[s] = true;
            var c = DeskGame.CATALOG[_matCard.cardIdx];
            if (c.matchNeed > 1 && _matCard.spots[s] == Spot.TRAP) Sfx.Play("error");
            bool all = true;
            for (int k = 0; k < c.spots; k++) if (!_matRevealed[k]) { all = false; break; }
            if (all) MatResolveNow();
        }

        void MatResolveNow()
        {
            if (!_matActive || _matResolved) return;
            var c = DeskGame.CATALOG[_matCard.cardIdx];
            var mask = new bool[c.spots];
            for (int s = 0; s < c.spots; s++) mask[s] = _matRevealed[s];
            _matRes = _desk.ResolveCard(_matCard, mask);
            _matResolved = true;
            // reveal any remaining foil so the player sees what they bailed on
            for (int s = 0; s < c.spots; s++) _matFoils[s].gameObject.SetActive(false);

            string t;
            if (_matRes.payout > 0)
                t = $"+{_matRes.payout:N0}" + (_matRes.penalty > 0 ? $"  · traps −{_matRes.penalty:N0}" : "");
            else if (_matRes.penalty > 0)
                t = $"traps −{_matRes.penalty:N0}. Ouch.";
            else
                t = "nothing. Zilch.";
            if (_desk.state.cardLevel[_matCard.cardIdx] > _matPrevLevel)
                t += "  · CARD LEVEL UP!";
            if (_matRes.bigWin)
            {
                t = "★ BIG WIN ★  " + t;
                Sfx.Play("milestone"); Sfx.Play("level_up");
                _matShakeT = 0.6f;
                SpawnCoinBurst(_matPanel.transform, MAT_CARD_POS, BURST_POOL);
            }
            else if (_matRes.payout >= c.cost) Sfx.Play("coin");
            else if (_matRes.payout > 0) Sfx.Play("coin", 0.6f);
            _matResult.text = t;
            _matPopT = 0.35f;
        }

        void ExitMat()
        {
            if (_matActive && !_matResolved) MatResolveNow();   // leaving = bailing
            _matActive = false;
            _matPanel.SetActive(false);
        }

        void UpdateDeskMat(float dt)
        {
            TickShake(_matCardGo, MAT_CARD_POS, ref _matShakeT, dt);
            TickPop(_matResult.rectTransform, ref _matPopT, dt);
            var c = DeskGame.CATALOG[_matCard.cardIdx];

            if (!_matResolved)
            {
                // live match status + committed traps (the near-miss engine)
                string status = "";
                if (c.matchNeed > 1)
                {
                    var counts = new int[c.symP.Length];
                    int traps = 0;
                    for (int s = 0; s < c.spots; s++)
                    {
                        if (!_matRevealed[s]) continue;
                        if (_matCard.spots[s] == Spot.TRAP) traps++;
                        else if (_matCard.spots[s] >= 0) counts[_matCard.spots[s]]++;
                    }
                    int bestK = -1, bestN = 0;
                    for (int k = 0; k < counts.Length; k++)
                        if (counts[k] >= bestN && counts[k] > 0) { bestN = counts[k]; bestK = k; }
                    if (bestK >= 0 && bestN >= c.matchNeed)
                        status = $"MATCHED {MAT_SYM_NAMES[bestK]}!";
                    else if (bestK >= 0)
                        status = $"{bestN}x {MAT_SYM_NAMES[bestK]} — {c.matchNeed - bestN} more";
                    if (traps > 0) status += $"   ·   traps −{traps * c.trapPenalty:N0}";
                }
                _matStatus.text = status;
                bool anyMatch = status.StartsWith("MATCHED");
                _matBailLbl.text = anyMatch ? "CASH OUT" : "BAIL";
                _matBailBtn.SetActive(true);
                _matNextBtn.SetActive(false);
                _matDoneBtn.SetActive(false);
            }
            else
            {
                _matStatus.text = "";
                _matBailBtn.SetActive(false);
                int owned = _desk.state.cardsOwned[_matCard.cardIdx];
                _matNextBtn.SetActive(owned > 0);
                if (owned > 0) _matNextLbl.text = $"NEXT ({owned})";
                _matDoneBtn.SetActive(true);
            }
        }

        void DeskTearEnvelope()
        {
            int rolls = _desk.state.rollsPending;
            if (rolls <= 0) { Sfx.Play("error"); return; }
            long gross = _desk.TearEnvelope();
            _dkResult = $"Paycheck: {rolls} strides → +{gross:N0} coins";
            _dkPopT = 0.35f;
            Sfx.Play("coin");
            SpawnCoinBurst(_deskPanel.transform, new Vector2(-270f, 470f), Mathf.Min(BURST_POOL, 8 + rolls / 4));
        }

        void UpdateDesk(GamexGame g)
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            TickShake(_dkDesk, DK_DESK_POS, ref _dkShakeT, dt);
            TickPop(_dkResultLine.rectTransform, ref _dkPopT, dt);
            if (_matActive) UpdateDeskMat(dt);

            var d = _desk.state;
            long coins = _desk.host.coins;
            long shown = TickCountUp(ref _dkCoinsShown, coins, dt);
            _dkCoins.text  = $"{shown:N0} COINS";
            _dkCoins.color = coins < 0 ? new Color(1f, 0.4f, 0.35f) : TextWhite;
            _dkLoanLine.text = d.loanOwed > 0 ? $"loan shark is owed {d.loanOwed:N0}" : "";

            // earnings bar toward the next locked card
            long next = -1; string nextName = null;
            foreach (var c in DeskGame.CATALOG)
                if (d.earnedThisRun < c.unlockAt) { next = c.unlockAt; nextName = c.name; break; }
            if (next > 0)
            {
                float pct = Mathf.Clamp01(d.earnedThisRun / (float)next);
                _dkBarFill.sizeDelta = new Vector2((DK_BAR_W - 6f) * pct, 24f);
                _dkBarLabel.text = $"{nextName} unlocks at {next:N0} earned  ·  {d.earnedThisRun:N0}";
            }
            else
            {
                _dkBarFill.sizeDelta = new Vector2(DK_BAR_W - 6f, 24f);
                _dkBarLabel.text = $"catalog complete  ·  {d.earnedThisRun:N0} earned";
            }

            // Unlock celebration — golden desk-burst when the bar crosses a
            // card's threshold (the Scritchy "Unlocked!" moment).
            if (_dkPrevEarned >= 0 && d.earnedThisRun > _dkPrevEarned)
                foreach (var c in DeskGame.CATALOG)
                    if (_dkPrevEarned < c.unlockAt && d.earnedThisRun >= c.unlockAt)
                    {
                        _dkResult = $"★ UNLOCKED: {c.name} ★";
                        _dkPopT = 0.4f;
                        Sfx.Play("milestone");
                        SpawnCoinBurst(_deskPanel.transform, DK_DESK_POS, 14);
                    }
            _dkPrevEarned = d.earnedThisRun;

            // envelope + steps
            bool hasPay = d.rollsPending > 0;
            _dkEnvelopeBtn.SetActive(hasPay);
            if (hasPay) _dkEnvelopeLabel.text = $"PAYCHECK ×{d.rollsPending}";
            _dkStepsLine.text = hasPay
                ? $"next stride: {d.stepAccumulator}/{DeskGame.STEPS_PER_ROLL} steps"
                : $"walk {DeskGame.STEPS_PER_ROLL - d.stepAccumulator} more steps for a stride";

            int pile = 0;
            for (int i = 0; i < DeskGame.CATALOG.Length; i++) pile += d.cardsOwned[i];
            _dkPileLine.text = pile > 0 ? $"the pile: {pile} unscratched card{(pile == 1 ? "" : "s")}" : "the desk is clear";

            // R2-4 desk objects
            _robotBtn.SetActive(_desk.RobotOwned && pile > 0);
            _phoneBtn.SetActive(_desk.LoanAvailable);
            _prestigeBtnLbl.text = $"PRESTIGE · {d.prestigePoints:0.#} PP";
            if (_prestigePanel.activeSelf) UpdateDeskPrestige();

            _dkResultLine.text = _dkResult ?? "";

            // catalog rows
            for (int i = 0; i < _dkCardRows.Length; i++)
            {
                var c = DeskGame.CATALOG[i];
                var row = _dkCardRows[i];
                bool unlocked = _desk.Unlocked(i);
                if (!unlocked)
                {
                    row.name.text = c.name;
                    row.name.color = TextDim;
                    row.info.text = $"unlocks at {c.unlockAt:N0} earned";
                    row.buy.gameObject.SetActive(false);
                    row.play.gameObject.SetActive(false);
                    continue;
                }
                long price = _desk.CostOf(i);   // Haggler-aware
                row.name.text = $"{c.name}   Lv {d.cardLevel[i]}";
                row.name.color = TextWhite;
                row.info.text = $"cost {price:N0}   ·   owned {d.cardsOwned[i]}";
                row.buy.gameObject.SetActive(true);
                row.buy.interactable = _desk.CanBuy(i);
                row.buyLbl.text = price >= 1000 ? $"{price / 1000}k" : price.ToString();
                row.play.gameObject.SetActive(d.cardsOwned[i] > 0);
            }

            // upgrade rows
            for (int i = 0; i < 3; i++)
            {
                var kind = i == 0 ? DeskGame.Upgrade.Luck : i == 1 ? DeskGame.Upgrade.Size : DeskGame.Upgrade.Coin;
                var row = _dkUpRows[i];
                int lvl = _desk.UpgradeLevel(kind);
                bool capped = lvl >= _desk.UpgradeCap(kind);
                row.name.text = (i == 0 ? "Scratch Luck" : i == 1 ? "Scratch Size" : "Lucky Coin") + $"   Lv {lvl}";
                row.buyLbl.text = capped ? "MAX" : _desk.UpgradeCost(kind).ToString("N0");
                row.buy.interactable = !capped && coins >= _desk.UpgradeCost(kind);
            }
        }

        // ============================================================
        // Shared juice (migrated from the retired casino rooms)
        // ============================================================
        long TickCountUp(ref float shown, long target, float dt)
        {
            if (shown < 0f && target >= 0) shown = target;
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

        static void TickShake(GameObject go, Vector2 basePos, ref float t, float dt)
        {
            var rt = (RectTransform)go.transform;
            if (t <= 0f) return;
            t -= dt;
            float amp = Mathf.Lerp(0f, 14f, Mathf.Clamp01(t / 0.6f));
            rt.anchoredPosition = basePos + new Vector2(
                UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp, amp));
            if (t <= 0f) rt.anchoredPosition = basePos;
        }

        void EnsureBurstPool()
        {
            if (_burstCoins != null) return;
            _coinFrames = new Sprite[6];
            bool any = false;
            for (int i = 0; i < 6; i++)
            {
                _coinFrames[i] = Make.Casino("coin_gold_" + (i + 1));
                any |= _coinFrames[i] != null;
            }
            if (!any) _coinFrames = null;

            _burstCoins = new Image[BURST_POOL];
            _burstVel   = new Vector2[BURST_POOL];
            _burstSpin  = new float[BURST_POOL];
            for (int i = 0; i < BURST_POOL; i++)
            {
                var go = _coinFrames != null
                    ? MkSpriteIcon("BurstCoin" + i, _deskPanel.transform,
                        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 53f), _coinFrames[0], Color.white)
                    : MkSpriteIcon("BurstCoin" + i, _deskPanel.transform,
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
                    UnityEngine.Random.Range(-130f, 130f), UnityEngine.Random.Range(-40f, 40f));
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
