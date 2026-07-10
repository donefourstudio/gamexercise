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
        // Fate Cards (M_fate Phase 0) — placeholder shell proving the
        // flag-gated boot path end to end: RemoteConfig flag ->
        // GameRunner branch -> AppPhase.FateHome -> this panel. The real
        // screens (Home / Scratch / Upgrades / Ascension) replace it in
        // Phase 2. See docs/fatecards-mvp-plan.md.
        // ============================================================
        GameObject _fatePanel;
        TMP_Text _fateGold, _fateCards, _fateSteps;

        void BuildFateShell(Transform root)
        {
            _fatePanel = MkFullPanel("FatePanel", root);

            MkText("Title", _fatePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -240f - SAFE_AREA_TOP_INSET), new Vector2(900f, 90f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "FATE CARDS";
            MkText("Sub", _fatePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -350f - SAFE_AREA_TOP_INSET), new Vector2(960f, 60f),
                FS_LABEL, TextAnchor.UpperCenter, TextDim)
                .text = "The Curse stole your fortune. Walk to win it back.";

            _fateGold = MkText("Gold", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 120f), new Vector2(900f, 70f), FS_BTN, TextAnchor.MiddleCenter, TextWhite);
            _fateCards = MkText("Cards", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 20f), new Vector2(900f, 70f), FS_BTN, TextAnchor.MiddleCenter, TextWhite);
            _fateSteps = MkText("Steps", _fatePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -80f), new Vector2(900f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            MkText("Hint", _fatePanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 140f), new Vector2(960f, 60f), FS_LABEL, TextAnchor.LowerCenter, TextDim)
                .text = "Phase 0 scaffold — scratch screens arrive in Phase 2.";
        }

        void UpdateFateShell(GamexGame g)
        {
            var f = g.state.fate;
            _fateGold.text  = $"Gold: {(f != null ? f.gold : 0)}";
            _fateCards.text = $"Fate Cards: {(f != null ? f.cardsBanked : 0)}";
            _fateSteps.text = $"Today: {g.state.todaySteps:N0} steps";
        }
    }
}
