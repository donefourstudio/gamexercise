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
        // Fate Cards (M_fate Phase 2) — the three MVP screens:
        //   FateHome     — gold / cards / step->card progress + nav
        //   FateScratch  — the Three Fates card, tap-to-reveal + Scratch All
        //   FateUpgrades — per-run (gold) + permanent (AP) upgrades
        //
        // Unlike the old game's callback routing, these screens hold the
        // FateGame core directly (_fate, injected via the Hud ctor): the
        // scratch result must flow back into the reveal animation
        // synchronously, and every button is a 1:1 core call anyway.
        // Navigation still goes through _onFateNav so GameRunner stays the
        // owner of the phase field. See docs/fatecards-mvp-plan.md.
        //
        // Reveal model: FateGame pre-rolls the outcome the moment a card is
        // dealt (ScratchCard()). The three cells are pure theatre — a win
        // shows three matching symbols of the rolled tier; a bust shows a
        // near-miss (a pair + one odd symbol, occasionally a heartbreaking
        // pair of Suns). Because gold is credited at deal time, the on-
        // screen gold counter subtracts the pending card's payout until the
        // player has flipped all three cells (FateDisplayGold).
        // ============================================================

        GameObject _fatePanel, _fateScratchPanel, _fateUpgradesPanel;   // Home / Scratch / Upgrades roots

        // ---- Home ----
        TMP_Text _fateHomeGold, _fateHomeAp, _fateHomeCards, _fateHomeSteps,
                 _fateHomeBarLabel, _fateHomeAscendHint;
        RectTransform _fateHomeBarFill;
        const float FATE_BAR_W = 700f;

        // ---- Scratch ----
        TMP_Text _fateScratchGold, _fateScratchCardsLeft, _fateResultLine, _fateEmptyLabel;
        readonly GameObject[] _fateCells = new GameObject[3];
        readonly TMP_Text[]   _fateCellTexts = new TMP_Text[3];
        readonly bool[]       _fateCellFlipped = new bool[3];
        readonly FateTier[]   _fateCellSymbols = new FateTier[3];
        FateScratchResult _fatePending;
        bool   _fateHasPending;
        string _fateSummary;                 // scratch-all batch summary, shown until the next deal
        GameObject _fateNextBtn, _fateScratchAllBtn;
        TMP_Text   _fateScratchAllLabel;

        // ---- Upgrades ----
        TMP_Text _fateUpWallet;
        (TMP_Text lvl, TMP_Text cost, Button btn) _fateRowFortune, _fateRowFavor, _fateRowEndur,
                                                  _fateRowMidas, _fateRowBFate, _fateRowMara;

        // ============================================================
        // Home
        // ============================================================
        void BuildFateHome(Transform root)
        {
            _fatePanel = MkFullPanel("FatePanel", root);

            MkText("Title", _fatePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - SAFE_AREA_TOP_INSET), new Vector2(900f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "FATE CARDS";

            _fateHomeGold = MkText("Gold", _fatePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -260f - SAFE_AREA_TOP_INSET), new Vector2(900f, 70f),
                FS_BTN, TextAnchor.UpperCenter, TextWhite);
            _fateHomeAp = MkText("Ap", _fatePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -340f - SAFE_AREA_TOP_INSET), new Vector2(900f, 50f),
                FS_LABEL, TextAnchor.UpperCenter, TextDim);

            // Card stock — the big center readout.
            _fateHomeCards = MkText("Cards", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 190f), new Vector2(900f, 170f), FS_HUGE, TextAnchor.MiddleCenter, TextWhite);
            MkText("CardsLabel", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "FATE CARDS";

            // Step -> card progress bar.
            var barBg = MkPanel("BarBg", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -60f), new Vector2(FATE_BAR_W, 36f), new Color(0.10f, 0.09f, 0.16f, 1f));
            var fill = MkPanel("Fill", barBg.transform, new Vector2(0f, 0.5f),
                new Vector2(4f, 0f), new Vector2(0f, 28f), AccentGold);
            _fateHomeBarFill = fill.GetComponent<RectTransform>();
            _fateHomeBarLabel = MkText("BarLabel", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -120f), new Vector2(900f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            _fateHomeSteps = MkText("Steps", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -180f), new Vector2(900f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            // Foreshadow only — the Ascend flow itself lands in Phase 3.
            _fateHomeAscendHint = MkText("AscendHint", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(960f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
            _fateHomeAscendHint.text = "The Seal trembles — Ascension nears...";

            MkButton("ScratchBtn", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -450f), new Vector2(640f, 150f), "SCRATCH",
                () => _onFateNav((int)AppPhase.FateScratch));
            MkButton("UpgradesBtn", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -630f), new Vector2(640f, 130f), "UPGRADES",
                () => _onFateNav((int)AppPhase.FateUpgrades));

            if (Application.isEditor)
                MkText("DebugHint", _fatePanel.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 40f), new Vector2(960f, 40f), FS_BODY, TextAnchor.LowerCenter, TextDim)
                    .text = "(Editor: T = +1000 steps)";
        }

        void UpdateFateHome(GamexGame g)
        {
            var f = _fate.state;
            _fateHomeGold.text  = $"{f.gold:N0} GOLD";
            _fateHomeAp.text    = $"Ascension Points: {f.ascensionPoints:0.#}";
            _fateHomeCards.text = f.cardsBanked.ToString("N0");
            int spc = _fate.StepsPerCard;
            float pct = Mathf.Clamp01(f.stepAccumulator / (float)spc);
            _fateHomeBarFill.sizeDelta = new Vector2((FATE_BAR_W - 8f) * pct, 28f);
            _fateHomeBarLabel.text = $"next card: {f.stepAccumulator}/{spc} steps";
            _fateHomeSteps.text = $"Today: {g.state.todaySteps:N0} steps";
            _fateHomeAscendHint.gameObject.SetActive(_fate.AscensionEligible);
        }

        // ============================================================
        // Scratch — the Three Fates card
        // ============================================================
        void BuildFateScratch(Transform root)
        {
            _fateScratchPanel = MkFullPanel("FateScratch", root);

            _fateScratchGold = MkText("Gold", _fateScratchPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperLeft, TextWhite);
            _fateScratchCardsLeft = MkText("CardsLeft", _fateScratchPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -140f - SAFE_AREA_TOP_INSET), new Vector2(500f, 60f),
                FS_LABEL, TextAnchor.UpperRight, TextWhite);

            // The card face: a framed panel holding the 3 reveal cells.
            var card = MkSpritePanel("Card", _fateScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(960f, 560f), "panel", PanelTint);
            card.GetComponent<Image>().raycastTarget = false;
            MkText("CardTitle", card.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "· THREE FATES ·";
            MkText("CardRule", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 28f),
                new Vector2(900f, 44f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "match all 3 to win";

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
                btn.onClick.AddListener(() => FateFlipCell(idx));
                _fateCells[i] = cell;
                _fateCellTexts[i] = MkText("Sym", cell.transform, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(260f, 320f), FS_BTN, TextAnchor.MiddleCenter, TextDim);
            }

            // Shares the card slot; shown when there's nothing to scratch.
            _fateEmptyLabel = MkText("Empty", _fateScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -160f), new Vector2(1000f, 100f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            _fateResultLine = MkText("Result", _fateScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -100f), new Vector2(1040f, 70f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            _fateNextBtn = MkButton("Next", _fateScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -280f), new Vector2(540f, 130f), "NEXT CARD",
                () => { _fateSummary = null; FateDealNext(); });

            _fateScratchAllBtn = MkButton("ScratchAll", _fateScratchPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -450f), new Vector2(540f, 110f), "SCRATCH ALL",
                FateScratchAll);
            _fateScratchAllLabel = _fateScratchAllBtn.GetComponentInChildren<TMP_Text>();

            MkButton("Back", _fateScratchPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onFateNav((int)AppPhase.FateHome), sfx: "back");
        }

        // Phase-entry hook (called from Refresh's _lastPhase block): clear a
        // finished card / stale summary and auto-deal so the screen is
        // always ready to scratch the moment it appears.
        void OnEnterFateScratch()
        {
            if (!_fateHasPending || FateAllRevealed)
            {
                _fateSummary = null;
                FateDealNext();
            }
        }

        bool FateAllRevealed => _fateCellFlipped[0] && _fateCellFlipped[1] && _fateCellFlipped[2];

        // Gold as shown on screen: the pending card's payout is already in
        // state (credited at deal time) but hasn't "happened" visually until
        // every cell is flipped, so hold it back from the counter.
        long FateDisplayGold => _fate.state.gold
            - (_fateHasPending && !FateAllRevealed ? _fatePending.gold : 0);

        void FateDealNext()
        {
            if (!_fate.CanScratch) { _fateHasPending = false; return; }
            _fatePending = _fate.ScratchCard();
            _fateHasPending = true;
            for (int i = 0; i < 3; i++) _fateCellFlipped[i] = false;
            if (_fatePending.tier == FateTier.Bust) RollBustSymbols();
            else for (int i = 0; i < 3; i++) _fateCellSymbols[i] = _fatePending.tier;
        }

        // Near-miss theatre for busts: a pair + one odd symbol. 15% of busts
        // tease a pair of Suns (the agonising almost-jackpot). Presentation-
        // only randomness — the payout was already rolled in FateGame.
        void RollBustSymbols()
        {
            FateTier pair = UnityEngine.Random.value < 0.15f
                ? FateTier.Sun
                : (FateTier)UnityEngine.Random.Range((int)FateTier.Moon, (int)FateTier.Crown + 1);
            FateTier odd;
            do { odd = (FateTier)UnityEngine.Random.Range((int)FateTier.Moon, (int)FateTier.Sun + 1); }
            while (odd == pair);
            int oddSlot = UnityEngine.Random.Range(0, 3);
            for (int i = 0; i < 3; i++) _fateCellSymbols[i] = i == oddSlot ? odd : pair;
        }

        void FateFlipCell(int i)
        {
            if (!_fateHasPending || _fateCellFlipped[i]) return;
            _fateCellFlipped[i] = true;
            Sfx.Play("tap");
            if (FateAllRevealed)
            {
                if (_fatePending.jackpot)                    Sfx.Play("milestone");
                else if (_fatePending.tier != FateTier.Bust) Sfx.Play("coin");
            }
        }

        // Fix #2 base feature: batch-rip the whole backlog. Folds in the
        // on-screen card if its reveal wasn't finished, then drains the bank
        // and reports one aggregate line.
        void FateScratchAll()
        {
            long gold = 0; int count = 0, jackpots = 0;
            if (_fateHasPending && !FateAllRevealed)
            {
                for (int i = 0; i < 3; i++) _fateCellFlipped[i] = true;
                gold += _fatePending.gold; count++;
                if (_fatePending.jackpot) jackpots++;
            }
            while (_fate.CanScratch)
            {
                var r = _fate.ScratchCard();
                gold += r.gold; count++;
                if (r.jackpot) jackpots++;
            }
            if (count == 0) return;
            _fateSummary = jackpots > 0
                ? $"Scratched {count}: +{gold:N0} gold — {jackpots} JACKPOT{(jackpots > 1 ? "S" : "")}!"
                : $"Scratched {count}: +{gold:N0} gold";
            Sfx.Play(jackpots > 0 ? "milestone" : "coin");
        }

        static string FateSymbolLabel(FateTier t)
        {
            switch (t)
            {
                case FateTier.Sun:   return "SUN";
                case FateTier.Crown: return "CROWN";
                case FateTier.Coin:  return "COIN";
                default:             return "MOON";
            }
        }

        static Color FateTierColor(FateTier t)
        {
            switch (t)
            {
                case FateTier.Sun:   return new Color(1f,    0.84f, 0.30f);
                case FateTier.Crown: return new Color(0.85f, 0.55f, 1f);
                case FateTier.Coin:  return new Color(0.95f, 0.70f, 0.35f);
                case FateTier.Moon:  return new Color(0.70f, 0.80f, 1f);
                default:             return new Color(0.55f, 0.50f, 0.45f);
            }
        }

        static string FateResultText(FateScratchResult r)
        {
            switch (r.tier)
            {
                case FateTier.Sun:   return $"FORTUNE SMILES!  +{r.gold:N0} GOLD";
                case FateTier.Crown: return $"A royal omen!  +{r.gold:N0} gold";
                case FateTier.Coin:  return $"Fortune favors you.  +{r.gold:N0} gold";
                case FateTier.Moon:  return $"A modest blessing.  +{r.gold:N0} gold";
                default:             return $"The Curse holds...  +{r.gold:N0} dust";
            }
        }

        void UpdateFateScratch()
        {
            _fateScratchGold.text      = $"Gold: {FateDisplayGold:N0}";
            _fateScratchCardsLeft.text = $"Cards: {_fate.state.cardsBanked:N0}";

            for (int i = 0; i < 3; i++)
            {
                _fateCells[i].SetActive(_fateHasPending);
                if (!_fateHasPending) continue;
                bool up = _fateCellFlipped[i];
                _fateCellTexts[i].text  = up ? FateSymbolLabel(_fateCellSymbols[i]) : "?";
                _fateCellTexts[i].color = up ? FateTierColor(_fateCellSymbols[i]) : TextDim;
            }

            if (_fateSummary != null)                    _fateResultLine.text = _fateSummary;
            else if (_fateHasPending && FateAllRevealed) _fateResultLine.text = FateResultText(_fatePending);
            else                                         _fateResultLine.text = "";

            _fateNextBtn.SetActive((!_fateHasPending || FateAllRevealed) && _fate.CanScratch);

            bool canRip = _fate.state.cardsBanked > 0 || (_fateHasPending && !FateAllRevealed);
            _fateScratchAllBtn.SetActive(canRip);
            if (canRip)
            {
                int ripCount = _fate.state.cardsBanked + (_fateHasPending && !FateAllRevealed ? 1 : 0);
                _fateScratchAllLabel.text = $"SCRATCH ALL ({ripCount:N0})";
            }

            bool dry = !_fate.CanScratch && (!_fateHasPending || FateAllRevealed);
            _fateEmptyLabel.gameObject.SetActive(dry);
            if (dry)
                _fateEmptyLabel.text = _fateHasPending
                    ? $"Out of Fate Cards — walk {_fate.StepsPerCard - _fate.state.stepAccumulator} more steps for the next one."
                    : $"Out of Fate Cards.\nWalk {_fate.StepsPerCard - _fate.state.stepAccumulator} more steps for the next one.";
        }

        // ============================================================
        // Upgrades
        // ============================================================
        void BuildFateUpgrades(Transform root)
        {
            _fateUpgradesPanel = MkFullPanel("FateUpgrades", root);

            MkText("Title", _fateUpgradesPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -140f - SAFE_AREA_TOP_INSET), new Vector2(900f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "UPGRADES";
            _fateUpWallet = MkText("Wallet", _fateUpgradesPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -250f - SAFE_AREA_TOP_INSET), new Vector2(960f, 56f),
                FS_LABEL, TextAnchor.UpperCenter, TextWhite);

            // ---- per-run (gold; reset on Ascension) ----
            _fateRowFortune = MkFateUpgradeRow(_fateUpgradesPanel.transform, 400f,
                "Fortune", "+8% gold from every card / level",
                () => Sfx.Play(_fate.TryBuyRunUpgrade(FateGame.RunUpgrade.Fortune) ? "purchase" : "error"));
            _fateRowFavor = MkFateUpgradeRow(_fateUpgradesPanel.transform, 230f,
                "Fate's Favor", "+0.3% jackpot odds / level",
                () => Sfx.Play(_fate.TryBuyRunUpgrade(FateGame.RunUpgrade.Favor) ? "purchase" : "error"));
            _fateRowEndur = MkFateUpgradeRow(_fateUpgradesPanel.transform, 60f,
                "Endurance", "-8 steps per card / level",
                () => Sfx.Play(_fate.TryBuyRunUpgrade(FateGame.RunUpgrade.Endurance) ? "purchase" : "error"));

            MkText("PermHeader", _fateUpgradesPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -70f), new Vector2(960f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim)
                .text = "— PERMANENT · Ascension Points —";

            // ---- permanent (AP; survive Ascension) ----
            _fateRowMidas = MkFateUpgradeRow(_fateUpgradesPanel.transform, -200f,
                "Midas Touch", "+5% gold from every card / level",
                () => Sfx.Play(_fate.TryBuyPermUpgrade(FateGame.PermUpgrade.Midas) ? "purchase" : "error"));
            _fateRowBFate = MkFateUpgradeRow(_fateUpgradesPanel.transform, -370f,
                "Blessed Fate", "+0.3% jackpot odds / level",
                () => Sfx.Play(_fate.TryBuyPermUpgrade(FateGame.PermUpgrade.BlessedFate) ? "purchase" : "error"));
            _fateRowMara = MkFateUpgradeRow(_fateUpgradesPanel.transform, -540f,
                "Marathoner", "running earns +10% cards / level",
                () => Sfx.Play(_fate.TryBuyPermUpgrade(FateGame.PermUpgrade.Marathoner) ? "purchase" : "error"));

            MkButton("Back", _fateUpgradesPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 50f), new Vector2(300f, 100f), "BACK",
                () => _onFateNav((int)AppPhase.FateHome), sfx: "back");
        }

        (TMP_Text lvl, TMP_Text cost, Button btn) MkFateUpgradeRow(
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

        void UpdateFateUpgrades()
        {
            var f = _fate.state;
            _fateUpWallet.text = $"Gold: {f.gold:N0}    ·    AP: {f.ascensionPoints:0.#}";

            UpdateFateRow(_fateRowFortune, f.runFortune,
                _fate.RunUpgradeCost(FateGame.RunUpgrade.Fortune) + "g",
                f.gold >= _fate.RunUpgradeCost(FateGame.RunUpgrade.Fortune), false);
            UpdateFateRow(_fateRowFavor, f.runFavor,
                _fate.RunUpgradeCost(FateGame.RunUpgrade.Favor) + "g",
                f.gold >= _fate.RunUpgradeCost(FateGame.RunUpgrade.Favor), false);
            UpdateFateRow(_fateRowEndur, f.runEndurance,
                _fate.RunUpgradeCost(FateGame.RunUpgrade.Endurance) + "g",
                f.gold >= _fate.RunUpgradeCost(FateGame.RunUpgrade.Endurance),
                f.runEndurance >= FateGame.ENDUR_MAX_LEVEL);

            UpdateFateRow(_fateRowMidas, f.permMidas,
                _fate.PermUpgradeCost(FateGame.PermUpgrade.Midas) + " AP",
                f.ascensionPoints >= _fate.PermUpgradeCost(FateGame.PermUpgrade.Midas), false);
            UpdateFateRow(_fateRowBFate, f.permBlessedFate,
                _fate.PermUpgradeCost(FateGame.PermUpgrade.BlessedFate) + " AP",
                f.ascensionPoints >= _fate.PermUpgradeCost(FateGame.PermUpgrade.BlessedFate), false);
            UpdateFateRow(_fateRowMara, f.permMarathoner,
                _fate.PermUpgradeCost(FateGame.PermUpgrade.Marathoner) + " AP",
                f.ascensionPoints >= _fate.PermUpgradeCost(FateGame.PermUpgrade.Marathoner), false);
        }

        static void UpdateFateRow((TMP_Text lvl, TMP_Text cost, Button btn) row,
                                  int level, string costLabel, bool affordable, bool capped)
        {
            row.lvl.text  = "Lv " + level;
            row.cost.text = capped ? "MAX" : costLabel;
            row.btn.interactable = !capped && affordable;
        }
    }
}
