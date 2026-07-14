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
        // Home
        // ============================================================
        void BuildHome(Transform root)
        {
            _homePanel = MkFullPanel("HomePanel", root);

            // top HUD
            _homeLevel = MkText("Level", _homePanel.transform, new Vector2(0f, 1f), new Vector2(50f, -60f - SAFE_AREA_TOP_INSET),
                new Vector2(400f, 60f), FS_BIG, TextAnchor.UpperLeft, AccentGold);
            // Coin LEFT of the number with a 10px gap. Number rect occupies
            // x=300..500 (right edge 40 left of panel edge); coin rect at
            // x=210..290 sits to its left with the gap. Coin pos.y nudged
            // down 8px to compensate for Cubic 11's top-heavy glyph metrics
            // — without it the coin reads as floating above the digits even
            // though the rect centres match mathematically.
            _homeCoinIcon = MkSpriteIcon("CoinIcon", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -54f - SAFE_AREA_TOP_INSET),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _homeCoins = MkText("Coins", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -60f - SAFE_AREA_TOP_INSET),
                new Vector2(200f, 60f), FS_BIG, TextAnchor.MiddleRight, AccentGold);

            // Coin gain floater — "+N" pops in just below the counter and rises
            // into it while fading, so quest rewards / streak bonuses have a
            // visible source. Aggregates rapid bursts so a multi-quest payout
            // doesn't spawn overlapping floaters.
            _homeCoinFloater = MkText("CoinFloat", _homePanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, COIN_FLOAT_HOME_START_Y), new Vector2(200f, 50f),
                FS_TITLE, TextAnchor.MiddleRight, AccentGold);
            _homeCoinFloater.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Mirror — centered, holds the player's CURRENT reflection. The body
            // shape morphs from cursed -> hero as stage advances. No more
            // separate "small cursed avatar + big hero in mirror" split.
            // (Left of mirror is reserved for the M2b-3 daily ritual: candles + crown.)
            var frame = MkSpritePanel("Mirror", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 260f), new Vector2(520f, 660f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var inner = MkSpritePanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(460f, 600f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            _mirrorSelf = BuildAvatar(inner.transform, Vector2.zero, 1.8f, Gender.Male, stage: 0);

            // Pet slot — small chibi tucked at lower-right corner of the mirror
            // inner panel, hidden until the player buys + applies a pet skin.
            _homePet = MkSpriteIcon("Pet", inner.transform, new Vector2(1f, 0f),
                new Vector2(-30f, 50f), new Vector2(100f, 100f),
                (Sprite)null, Color.white).GetComponent<Image>();
            _homePet.raycastTarget = false;
            _homePet.gameObject.SetActive(false);
            // Tap the mirror to open Inventory (paper-doll + storage grid).
            // Sits over the entire inner mirror — the avatar/candles/crown are siblings
            // of the frame, not inner, so the button's raycast catches the whole
            // reflection area without blocking the candle/crown decorations.
            var mirrorTap = inner.AddComponent<Button>();
            mirrorTap.targetGraphic = inner.GetComponent<Image>();
            mirrorTap.transition    = Selectable.Transition.None;
            mirrorTap.onClick.AddListener(() => _onGoInventory?.Invoke());

            // Stage-up flash overlay (above the avatar inside the mirror).
            _stageUpFlash = MkPanel("StageUpFlash", inner.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(460f, 600f), new Color(1f, 1f, 1f, 0f)).GetComponent<Image>();
            _stageUpFlash.raycastTarget = false;

            // Milestone dialogue — fades in just under the mirror after a stage transition.
            _milestoneText = MkText("Milestone", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -110f), new Vector2(1000f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
            _milestoneText.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Level-up toast — same slot as the milestone text, but mutually
            // exclusive: stage-transition levels (10/15/20/30) fire the milestone
            // quote instead so we never stack two banners. Slightly bigger font
            // makes the regular level-up feel rewarding without being intrusive.
            _levelUpToast = MkText("LevelUpToast", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -110f), new Vector2(1000f, 60f), FS_TITLE, TextAnchor.MiddleCenter, AccentGold);
            _levelUpToast.text  = "LEVEL UP!";
            _levelUpToast.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Daily ritual icons:
            //   4 candles bracket the mirror frame (2 left, 2 right).
            //   1 crown hovers above the character; lands on the head when
            //   the day's maintenance is met. Refresh swaps sprites + crown y.
            var candleAt = new[] {
                new Vector2(-330f,  80f),  // bottom left
                new Vector2(-330f, 440f),  // top left
                new Vector2( 330f,  80f),  // bottom right
                new Vector2( 330f, 440f),  // top right
            };
            for (int i = 0; i < 4; i++)
                _candleImgs[i] = MkSpriteIcon("Candle_" + i, _homePanel.transform,
                    new Vector2(0.5f, 0.5f), candleAt[i], new Vector2(64f, 96f),
                    "candle_unlit", Color.white).GetComponent<Image>();

            _crownImg = MkSpriteIcon("Crown", _homePanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, CROWN_Y_FLOATING), new Vector2(120f, 80f),
                "crown_grey", Color.white).GetComponent<Image>();

            // streak + maintenance + XP bar (under mirror, top-down)
            _homeStreak = MkText("Streak", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -180f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
            _homeProgress = MkText("Progress", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -240f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            // Green ✓ sits just right of the text when the 5k daily goal is met.
            // Position recalculated each Refresh from preferredWidth so it tracks
            // strings of different lengths (e.g. "Today 5000 steps" vs "Today 12500 steps").
            var checkGO = new GameObject("ProgressCheck", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkGO.transform.SetParent(_homePanel.transform, false);
            _homeProgressCheck = checkGO.GetComponent<Image>();
            _homeProgressCheck.sprite = GetCheckmarkSprite();
            _homeProgressCheck.raycastTarget = false;
            var checkRT = _homeProgressCheck.rectTransform;
            checkRT.anchorMin = checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(42f, 42f);
            checkRT.anchoredPosition = new Vector2(0f, -240f);
            _homeProgressCheck.enabled = false;

            // XP bar — solid-color rects, fill anchored to parent's left edge.
            // (9-slice sprites stretched to 36px tall produced compression artifacts.)
            var xpBg = MkPanel("XpBg", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -310f), new Vector2(700f, 32f), new Color(0.10f, 0.12f, 0.18f, 1f));
            var fill = MkPanel("XpFill", xpBg.transform, new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(700f, 28f), AccentGold);
            _xpBar = fill.GetComponent<Image>();
            var xrt = _xpBar.rectTransform;
            xrt.pivot = new Vector2(0f, 0.5f);
            xrt.anchorMin = xrt.anchorMax = new Vector2(0f, 0.5f);
            xrt.anchoredPosition = Vector2.zero;   // pivot sits AT the anchor (parent's left-center)

            // Transformation hint — small dim text below the XP bar so the
            // player knows their next visual milestone (Lv 10/15/20) without
            // having to discover it. Stops players quitting before the
            // skeleton -> flesh visual landmark at Lv 10.
            _homeNextHint = MkText("NextHint", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -365f), new Vector2(900f, 56f),
                FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            // bottom buttons — Quests opens the daily-task list, Shop is cosmetics.
            // Whole group lifted ~80px above the prior baseline so Settings can sit
            // beneath Shop at a comfortable size instead of being squashed. There's
            // still ~140px of clearance between Quests' top edge (y=440) and the
            // "Next form change" hint (text bottom at y=580 from screen bottom).
            MkButton("Quests", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 360f),
                new Vector2(800f, 160f), "Quests", () => _onGoQuests?.Invoke());
            // The Casino door (docs/casino-mvp-plan.md) — flag-gated at
            // build time (RemoteConfig cache, read once per session), so
            // flag-off ships a Home screen identical to today's. When on,
            // Shop + Casino split the middle row.
            if (RemoteConfig.CasinoEnabled)
            {
                MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(-215f, 200f),
                    new Vector2(400f, 100f), "Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
                MkButton("Casino", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(215f, 200f),
                    new Vector2(400f, 100f), "CASINO", () => _onDeskNav((int)AppPhase.Desk));
            }
            else
            {
                MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 200f),
                    new Vector2(420f, 100f), "Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
            }

            // Settings — sits below Shop with ~20px breathing. Visual hierarchy
            // reads Quests > Shop > Settings via decreasing width (800 / 420 / 380).
            MkButton("SettingsBtn", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 95f),
                new Vector2(380f, 70f), "Settings", () => _onGoSettings?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Quests — daily-task list + lifetime totals (M5b).
        // Replaces the old Training screen entirely. Each quest is a row
        // showing label + status (✓ / progress fraction). Refresh updates
        // the rows from g.state every frame.
        // ============================================================
        static readonly (Quest quest, string label, int goal, bool runMinutes, int reward)[] QUEST_SPEC =
        {
            (Quest.Walk1000,  "Walk 1,000 steps",  1000,  false, GamexGame.Q_REWARD_WALK_1000),
            (Quest.Walk5000,  "Walk 5,000 steps",  5000,  false, GamexGame.Q_REWARD_WALK_5000),
            (Quest.Walk10000, "Walk 10,000 steps", 10000, false, GamexGame.Q_REWARD_WALK_10000),
            (Quest.Run15Min,  "Run 15 minutes",    15,    true,  GamexGame.Q_REWARD_RUN_15),
            (Quest.Run30Min,  "Run 30 minutes",    30,    true,  GamexGame.Q_REWARD_RUN_30),
        };

        void BuildQuests(Transform root)
        {
            _trainPanel = MkFullPanel("QuestsPanel", root);

            MkText("Title", _trainPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f - SAFE_AREA_TOP_INSET),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Daily Quests";

            // In-between sizing: rows 150 tall (vs original 130 / first-pass
            // 180) + FS_TITLE label (vs original FS_LABEL 33 / first-pass
            // FS_BIG 77). Trophy icon for completion stays.
            //
            // Width 820 (was 950 then 880) — on tall narrow phones like the
            // iPhone 17 Pro Max (aspect 0.462 vs our 0.5625 reference),
            // the visible canvas width in design units shrinks to ~885.
            // 950 overflowed badly; 880 was right at the edge AND the
            // wooden panel's 9-slice tile pattern adds a few px of visual
            // overhang past the declared rect, so 880 still showed bleed.
            // 820 gives a real ~32 design-unit margin on each side.
            const float rowH = 150f, rowGap = 18f, rowW = 820f;
            float startY = 620f;
            for (int i = 0; i < QUEST_SPEC.Length; i++)
            {
                float y = startY - i * (rowH + rowGap);
                var row = MkSpritePanel("Q_" + i, _trainPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(rowW, rowH), "panel", new Color(0.95f, 0.86f, 0.66f, 1f));

                _questRowLabels[i] = MkText("Label", row.transform, new Vector2(0f, 0.5f),
                    new Vector2(50f, 0f), new Vector2(rowW - 240f, rowH - 20f),
                    FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));

                // Reward chip ("+N" over "tickets") wrapped so the whole
                // chip can toggle off when the quest completes (trophy
                // takes the same right slot). Quests pay casino TICKETS
                // since the unified wallet — text chip until ticket art
                // exists.
                var chip = MkPanel("Chip", row.transform, new Vector2(1f, 0.5f),
                    new Vector2(-120f, 0f), new Vector2(190f, 100f),
                    new Color(0f, 0f, 0f, 0f));
                chip.GetComponent<Image>().raycastTarget = false;
                int reward = QUEST_SPEC[i].reward;
                MkText("Reward", chip.transform, new Vector2(0f, 0.5f),
                    new Vector2(0f, 16f), new Vector2(190f, 56f),
                    FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                    .text = $"+{reward}";
                MkText("RewardUnit", chip.transform, new Vector2(0f, 0.5f),
                    new Vector2(0f, -26f), new Vector2(190f, 36f),
                    FS_BODY, TextAnchor.MiddleLeft, new Color(0.38f, 0.24f, 0.12f))
                    .text = "tickets";
                _questChipRoots[i] = chip;

                _questCheckmarks[i] = MkSpriteIcon("Tick", row.transform, new Vector2(1f, 0.5f),
                    new Vector2(-80f, 0f), new Vector2(96f, 96f),
                    "trophy", Color.white).GetComponent<Image>();
                _questCheckmarks[i].gameObject.SetActive(false);
            }

            // Knight Set + totals lifted up to remove the big empty band
            // beneath the daily-quest rows. Streak got pulled — already
            // shown on the Home mirror page, no need to duplicate.
            _questsKnightRow = MkSpritePanel("Q_Knight", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(950f, 150f), "panel", new Color(0.85f, 0.78f, 1f, 1f));
            _questsKnight = MkText("KnightLabel", _questsKnightRow.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 30f), new Vector2(920f, 60f), FS_LABEL, TextAnchor.MiddleCenter, new Color(0.18f, 0.08f, 0.20f));
            // Progress bar — solid rects, fill pivots on the left so width
            // alone drives the visual fraction (same trick as the XP bar).
            _questsKnightBarBg = MkPanel("KnightBarBg", _questsKnightRow.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -30f), new Vector2(840f, 28f), new Color(0.20f, 0.12f, 0.22f, 1f));
            var knightFill = MkPanel("KnightBarFill", _questsKnightBarBg.transform, new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(840f, 24f), AccentGold);
            _questsKnightBarFill = knightFill.GetComponent<Image>();
            var kfRT = _questsKnightBarFill.rectTransform;
            kfRT.pivot = new Vector2(0f, 0.5f);
            kfRT.anchorMin = kfRT.anchorMax = new Vector2(0f, 0.5f);
            kfRT.anchoredPosition = Vector2.zero;
            _questsKnightRow.SetActive(false);

            _questsTotalSteps = MkText("TotalSteps", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -370f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _questsTotalRun   = MkText("TotalRun",   _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -430f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _questsStreak     = null;   // dropped; Home shows it

            MkButton("Back", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Shop
        // ============================================================
        // Phase 5a — source-tag -> displayed section header. Strings show up in
        // the shop dividers; Cyberpunk + Pets keep their literal names per
        // Jackson, the others get medieval-fantasy themed labels.
        static readonly Dictionary<string, string> SectionDisplayNames = new()
        {
            { "champion", "Champions" },
            { "legend",   "Legends" },
            { "cyberpunk","Cyberpunk" },
            { "pet",      "Pets" },
        };
        // Order matters — sections render top-to-bottom in this sequence.
        // "pet" section dropped per Jackson — pets stay in code (state +
        // bake) but aren't sold or shown anywhere right now.
        static readonly string[] SectionOrder = { "champion", "legend", "cyberpunk" };

        void BuildShop(Transform root)
        {
            _shopPanel = MkFullPanel("ShopPanel", root);

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f - SAFE_AREA_TOP_INSET),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Shop";
            _shopCoinIcon = MkSpriteIcon("CoinIcon", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -84f - SAFE_AREA_TOP_INSET),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _shopCoins = MkText("Coins", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -90f - SAFE_AREA_TOP_INSET),
                new Vector2(200f, 60f), FS_BIG, TextAnchor.MiddleRight, AccentGold);
            _shopCoinFloater = MkText("CoinFloat", _shopPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, COIN_FLOAT_SHOP_START_Y), new Vector2(200f, 50f),
                FS_TITLE, TextAnchor.MiddleRight, AccentGold);
            _shopCoinFloater.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Phase 5c — content lives inside a ScrollRect so an arbitrary
            // number of sections + cards can stack without overflowing the
            // back button. Viewport stretches from below the title to above
            // the back button; content is a RectTransform whose height we
            // adjust at the end to match the cumulative card stack.
            var contentRT = MkScrollView("ShopScroll", _shopPanel.transform,
                topInset: 200f + SAFE_AREA_TOP_INSET, bottomInset: 250f);

            // Layout convention: `y` is the TOP edge of the next element to
            // place (anchor + pivot = (0.5, 1) for both header and cards, so
            // pos.y maps directly to top edge). First section starts at y=-30
            // for a small breath under the Shop title.
            //
            // Spacing was inverted before this rewrite — the "Legends" header
            // sat 14px below the last Champions card and 125px above its own
            // first card, so headers visually grouped with the wrong section.
            // Now header→first card is tight (35px) and previous section→
            // next header is roomy (140px), so each header reads as the
            // intro to the section beneath it.
            float y = -30f;
            const float CARD_W = 880f, CARD_H_SET = 280f, CARD_H_SKIN = 280f, CARD_GAP = 24f;
            const float HEADER_H = 80f, HEADER_TO_CARD_GAP = 35f, SECTION_TOP_GAP = 140f;

            bool firstSection = true;
            foreach (var source in SectionOrder)
            {
                // Launch gating (polish round 7) — items with availableAtUnix
                // in the future stay out of the shop until a content drop
                // flips the timestamp. Live-ops dial via Catalogs.IsLive.
                var sectionSets  = new List<SetDef>();
                foreach (var s in GamexGame.SetCatalog)
                    if (s.source == source && Catalogs.IsLive(s.availableAtUnix)) sectionSets.Add(s);
                var sectionSkins = new List<SkinDef>();
                foreach (var s in GamexGame.SkinCatalog)
                    if (s.source == source && Catalogs.IsLive(s.availableAtUnix)) sectionSkins.Add(s);
                if (sectionSets.Count == 0 && sectionSkins.Count == 0) continue;

                if (!firstSection) y -= SECTION_TOP_GAP;
                firstSection = false;

                string headerText = SectionDisplayNames.TryGetValue(source, out var h) ? h : source;
                // Cubic 11's em-dash glyph sits at x-height, not text-centre,
                // so "— Champions —" rendered with the dashes detached above
                // the word. Strip the dashes and bump to FS_TITLE so the
                // header carries the same weight as the card names below it.
                var hdr = MkText("SectionHeader_" + source, contentRT, new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(800f, HEADER_H),
                    FS_TITLE, TextAnchor.MiddleCenter, AccentGold);
                hdr.text = headerText;
                y -= HEADER_H + HEADER_TO_CARD_GAP;

                // Set cards inside this section (multi-piece purchasable bundles).
                foreach (var set in sectionSets)
                {
                    var card = MkSpritePanel("SetCard_" + set.id, contentRT,
                        new Vector2(0.5f, 1f), new Vector2(0f, y),
                        new Vector2(CARD_W, CARD_H_SET),
                        "panel", new Color(0.95f, 0.86f, 0.66f, 1f));
                    var cardBtn = card.AddComponent<Button>();
                    cardBtn.targetGraphic = card.GetComponent<Image>();
                    cardBtn.transition = Selectable.Transition.ColorTint;
                    var cb = cardBtn.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.78f, 1f); cardBtn.colors = cb;
                    string capSetId = set.id;
                    var setBounce = card.AddComponent<PressBounce>();
                    cardBtn.onClick.AddListener(() => { Sfx.Play("tap"); setBounce.Trigger(); _onGoSetDetail?.Invoke(capSetId); });

                    MkSpriteIcon("Preview", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(20f, 0f), new Vector2(140f, 140f),
                        Make.SetPreview(set.id), Color.white);

                    MkText("Name", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, 55f), new Vector2(665f, 70f),
                        FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                        .text = set.displayName;

                    var priceLabel = MkText("Price", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -10f), new Vector2(665f, 50f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));
                    priceLabel.text = $"{set.BundlePrice} gold";

                    // Sets are atomic — no per-piece purchase — so the sub
                    // line no longer references piece count. Just hints at
                    // the tap-to-view interaction.
                    MkText("Sub", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -70f), new Vector2(665f, 56f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.35f, 0.22f, 0.10f))
                        .text = "tap to view";

                    _shopSetCards.Add((set.id, card, priceLabel, cardBtn));
                    y -= CARD_H_SET + CARD_GAP;
                }

                // Skin cards mirror set cards now: preview + name + state +
                // "tap to view" sub, whole card opens the SkinDetail page.
                // Inline Buy/Apply/Remove button moved to that detail page.
                foreach (var skin in sectionSkins)
                {
                    var card = MkSpritePanel("SkinCard_" + skin.id, contentRT,
                        new Vector2(0.5f, 1f), new Vector2(0f, y),
                        new Vector2(CARD_W, CARD_H_SKIN),
                        "panel", new Color(0.92f, 0.84f, 0.62f, 1f));
                    var cardBtn = card.AddComponent<Button>();
                    cardBtn.targetGraphic = card.GetComponent<Image>();
                    cardBtn.transition = Selectable.Transition.ColorTint;
                    var cb = cardBtn.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.78f, 1f); cardBtn.colors = cb;
                    string capSkinId = skin.id;
                    var skinBounce = card.AddComponent<PressBounce>();
                    cardBtn.onClick.AddListener(() => { Sfx.Play("tap"); skinBounce.Trigger(); _onGoSkinDetail?.Invoke(capSkinId); });

                    MkSpriteIcon("Preview", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(20f, 0f), new Vector2(140f, 140f),
                        Make.Skin(skin.id), Color.white);

                    MkText("Name", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, 55f), new Vector2(665f, 70f),
                        FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                        .text = skin.displayName;
                    var stateLabel = MkText("State", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -10f), new Vector2(665f, 50f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));

                    MkText("Sub", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -70f), new Vector2(665f, 56f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.35f, 0.22f, 0.10f))
                        .text = "tap to view";

                    _shopSkinCards.Add((skin.id, card, stateLabel));
                    y -= CARD_H_SKIN + CARD_GAP;
                }
            }

            // Final content height = how far we scrolled down + bottom padding.
            contentRT.sizeDelta = new Vector2(0f, -y + 40f);

            MkButton("Back", _shopPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // Build a vertical ScrollRect anchored to fill the parent except for
        // top/bottom insets (which leave room for the title + back button).
        // Returns the Content RectTransform — caller adds children to it
        // using anchor (0.5, 1) so y=0 is the top edge.
        static RectTransform MkScrollView(string name, Transform parent, float topInset, float bottomInset)
        {
            var rootGO = new GameObject(name);
            rootGO.transform.SetParent(parent, false);
            var rt = rootGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(40f, bottomInset);
            rt.offsetMax = new Vector2(-40f, -topInset);

            var sr = rootGO.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;

            // Viewport — same rect as the scroll root, with Mask so children
            // outside the viewport bounds are clipped. The Image is required
            // for the Mask component to clip — Mask uses the Image's rect.
            var viewGO = new GameObject("Viewport");
            viewGO.transform.SetParent(rootGO.transform, false);
            var vrt = viewGO.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            var vbg = viewGO.AddComponent<Image>();
            vbg.color = new Color(1f, 1f, 1f, 0.01f);   // near-transparent — Mask needs SOMETHING
            var mask = viewGO.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            // Content — explicit-width centered top anchor (avoids the stretch
            // anchor confusion where children's anchoredPosition gets offset
            // by the stretch math).
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewGO.transform, false);
            var crt = contentGO.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(1000f, 1000f);

            sr.viewport = vrt;
            sr.content  = crt;
            return crt;
        }

        // ============================================================
        // Set detail (Phase 3c) — full preview header + per-piece rows
        // + bundle-buy CTA at bottom. Built once for every set in the
        // catalog (one panel containing per-set row groups, toggled by
        // activeSetId at refresh time).
        // ============================================================
        void BuildSetDetail(Transform root)
        {
            _setDetailPanel = MkFullPanel("SetDetailPanel", root);

            // Title left-aligned, coins right — Jackson caught the previous
            // centred title overlapping the coin counter with long names like
            // "Dark Horned Knight". Left anchor puts the title squarely on
            // the left and gives the coin readout the right half clean.
            _setDetailTitle = MkText("Title", _setDetailPanel.transform, new Vector2(0f, 1f),
                new Vector2(40f, -80f - SAFE_AREA_TOP_INSET), new Vector2(620f, 80f),
                FS_TITLE, TextAnchor.UpperLeft, AccentGold);
            _setDetailCoinIcon = MkSpriteIcon("CoinIcon", _setDetailPanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -84f - SAFE_AREA_TOP_INSET),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _setDetailCoins = MkText("Coins", _setDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, -90f - SAFE_AREA_TOP_INSET), new Vector2(200f, 60f),
                FS_BIG, TextAnchor.MiddleRight, AccentGold);
            _setDetailCoinFloater = MkText("CoinFloat", _setDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, COIN_FLOAT_SHOP_START_Y), new Vector2(200f, 50f),
                FS_TITLE, TextAnchor.MiddleRight, AccentGold);
            _setDetailCoinFloater.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Header preview frame — matched to SkinDetail's 820x820 / 760x760
            // sprite so the two detail pages feel like the same screen with
            // different content. Champions get the same room their Legend
            // counterparts do; player can see the full-gear bake at scale.
            var previewFrame = MkSpritePanel("PreviewFrame", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 320f), new Vector2(820f, 820f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            previewFrame.GetComponent<Image>().raycastTarget = false;
            _setDetailPreview = MkSpriteIcon("Preview", previewFrame.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f),
                (Sprite)null, Color.white).GetComponent<Image>();

            // Champions are now atomic outfits — one Buy Set CTA, no per-piece
            // purchase rows. Jackson's call after seeing the SetDetail in Play:
            // "Champions 还是成套成套卖吧，这样子太乱了". Set detail page
            // therefore just shows preview + bundle price + Buy Set / Owned.
            _setDetailBundleLabel = MkText("BundleLabel", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(900f, 60f),
                FS_LABEL, TextAnchor.MiddleCenter, AccentGold);

            var bundleGO = MkButton("BundleBuy", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -370f), new Vector2(640f, 130f),
                "Buy Set", () =>
                {
                    if (_currentSetId != null) _onBuySet?.Invoke(_currentSetId);
                });
            _setDetailBundleBtn = bundleGO.GetComponent<Button>();

            MkButton("Back", _setDetailPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(420f, 100f),
                "Back to Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // Tracks which set's rows are currently visible inside _setDetailPanel.
        // Refresh sets this from g.activeSetId at SetDetail entry.
        string _currentSetId;

        // ============================================================
        // Skin detail — preview + name + price/state + Buy/Apply/Remove.
        // Single shared panel for every Legend / Cyberpunk skin; the
        // active skin id is read from g.activeSkinId in UpdateSkinDetail.
        // ============================================================
        void BuildSkinDetail(Transform root)
        {
            _skinDetailPanel = MkFullPanel("SkinDetailPanel", root);

            _skinDetailTitle = MkText("Title", _skinDetailPanel.transform, new Vector2(0f, 1f),
                new Vector2(40f, -80f - SAFE_AREA_TOP_INSET), new Vector2(620f, 80f),
                FS_TITLE, TextAnchor.UpperLeft, AccentGold);
            MkSpriteIcon("CoinIcon", _skinDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-250f, -84f - SAFE_AREA_TOP_INSET), new Vector2(80f, 80f),
                "coin", Color.white);
            _skinDetailCoins = MkText("Coins", _skinDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, -90f - SAFE_AREA_TOP_INSET), new Vector2(200f, 60f),
                FS_BIG, TextAnchor.MiddleRight, AccentGold);

            // Big preview frame — Skin detail has fewer UI elements than
            // SetDetail (no per-piece grid), so the room goes to the preview.
            // Frame is 820x820, sprite 760x760 — nearly fills the panel
            // width (1080) with side padding, so the legend art reads at
            // glance. Center is offset +320 above canvas mid to leave room
            // for state label + action below.
            var previewFrame = MkSpritePanel("PreviewFrame", _skinDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 320f), new Vector2(820f, 820f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            previewFrame.GetComponent<Image>().raycastTarget = false;
            _skinDetailPreview = MkSpriteIcon("Preview", previewFrame.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 760f),
                (Sprite)null, Color.white).GetComponent<Image>();

            _skinDetailStateLabel = MkText("StateLabel", _skinDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(900f, 60f),
                FS_LABEL, TextAnchor.MiddleCenter, AccentGold);

            var actionGO = MkButton("Action", _skinDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -370f), new Vector2(640f, 130f),
                "Buy", () =>
                {
                    if (!string.IsNullOrEmpty(_currentSkinId)) _onSkinAction?.Invoke(_currentSkinId);
                });
            _skinDetailActionBtn = actionGO.GetComponent<Button>();
            _skinDetailActionLabel = actionGO.GetComponentInChildren<TMP_Text>();

            MkButton("Back", _skinDetailPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(420f, 100f),
                "Back to Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // Tracks which skin's detail page is currently being viewed.
        string _currentSkinId;

        // ============================================================
        // Inventory (M5f) — paper-doll on top, storage grid on bottom.
        // Reached by tapping the home mirror; equipping is one-shot
        // (tap an item in the grid -> swaps into its slot, evicting the
        // previous occupant) and unequipping is tapping the slot icon
        // on the paper-doll.
        // ============================================================
        void BuildInventory(Transform root)
        {
            _inventoryPanel = MkFullPanel("InventoryPanel", root);

            // Top header — title + gold (mirrors home/shop styling)
            MkText("Title", _inventoryPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f - SAFE_AREA_TOP_INSET),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Inventory";

            // Paper-doll — large avatar inside a framed panel, anchored to upper third.
            var dollFrame = MkSpritePanel("DollFrame", _inventoryPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 440f), new Vector2(500f, 600f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var dollInner = MkSpritePanel("DollInner", dollFrame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440f, 540f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            dollInner.GetComponent<Image>().raycastTarget = false;
            _inventoryAvatar = BuildAvatar(dollInner.transform, Vector2.zero, 1.6f,
                Gender.Male, stage: 0);

            // Pet slot — same convention as the Home mirror.
            _inventoryPet = MkSpriteIcon("Pet", dollInner.transform, new Vector2(1f, 0f),
                new Vector2(-20f, 40f), new Vector2(80f, 80f),
                (Sprite)null, Color.white).GetComponent<Image>();
            _inventoryPet.raycastTarget = false;
            _inventoryPet.gameObject.SetActive(false);

            // (Slot icons row removed. Champions are sold + equipped atomically
            // as outfits now, so the player has no per-slot decisions to make
            // and the row was just visual noise. Pets section is also gone for
            // launch — Jackson called it "鸡肋".)

            // Single outfits grid inside a ScrollRect. Each cell shows the
            // full-character preview for one owned outfit; tap to apply.
            // No slot row, no per-source split, no pets — Jackson's simplified
            // model. Header explains the tap behaviour so the player isn't
            // staring at a wall of portraits without context.
            var contentRT = MkScrollView("InvScroll", _inventoryPanel.transform,
                topInset: 900f, bottomInset: 250f);

            _invInventoryHeader = MkText("OutfitsHdr", contentRT,
                new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(800f, 50f),
                FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _invInventoryHeader.text = "Outfits  ·  tap to wear";

            const int COLS = 3, CELL = 220, GAP = 18;
            float gridLeft = -(COLS * (CELL + GAP) - GAP) / 2f + CELL / 2f;
            float gridTop  = -90f - CELL / 2f;
            for (int i = 0; i < INV_GRID_CAPACITY; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                float cx = gridLeft + col * (CELL + GAP);
                float cy = gridTop  - row * (CELL + GAP);

                var cell = MkSpritePanel("InvCell_" + i, contentRT,
                    new Vector2(0.5f, 1f), new Vector2(cx, cy), new Vector2(CELL, CELL),
                    "panel_light", new Color(0.22f, 0.24f, 0.32f, 1f));
                var icon = MkSpriteIcon("Icon", cell.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(CELL - 24f, CELL - 24f), (Sprite)null, Color.white)
                    .GetComponent<Image>();
                var badge = MkText("Active", cell.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 6f), new Vector2(CELL, 44f),
                    FS_LABEL, TextAnchor.LowerCenter, AccentGold);
                badge.text = "Active";
                badge.gameObject.SetActive(false);

                int capIdx = i;
                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = cell.GetComponent<Image>();
                btn.transition    = Selectable.Transition.ColorTint;
                var cb = btn.colors;
                cb.highlightedColor = new Color(0.32f, 0.34f, 0.44f, 1f);
                btn.colors = cb;
                var cellBounce = cell.AddComponent<PressBounce>();
                btn.onClick.AddListener(() =>
                {
                    var id = _invGridIds[capIdx];
                    if (string.IsNullOrEmpty(id)) return;
                    cellBounce.Trigger();
                    Sfx.Play("tap");
                    _onApplyOutfit?.Invoke(id);
                });

                cell.SetActive(false);
                _invGridRoots[i]  = cell;
                _invGridIcons[i]  = icon;
                _invGridBadges[i] = badge.gameObject;
            }
            int gridRows = (INV_GRID_CAPACITY + COLS - 1) / COLS;
            float bottomY = gridTop - (gridRows - 1) * (CELL + GAP) - CELL / 2f - 40f;
            contentRT.sizeDelta = new Vector2(1000f, -bottomY);

            MkButton("Back", _inventoryPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // Settings panel — audio mute toggles, HealthKit status + open-iOS-
        // Settings shortcut, two-tap Reset Progress. Dynamic state (labels)
        // is refreshed in UpdateSettings; this method just builds the
        // skeleton once at boot.
        void BuildSettings(Transform root)
        {
            _settingsPanel = MkFullPanel("SettingsPanel", root);

            MkText("Title", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f - SAFE_AREA_TOP_INSET),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Settings";

            // Section: Audio
            MkText("AudioHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "Audio";

            _settingsSfxLabel = MkButtonWithLabel("SfxRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(880f, 90f),
                "Sound effects: ON", () => _onToggleSfx?.Invoke());

            _settingsBgmLabel = MkButtonWithLabel("BgmRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -450f), new Vector2(880f, 90f),
                "Music: OFF", () => _onToggleBgm?.Invoke());

            // Section: HealthKit (iOS surfaces status; non-iOS shows "Unavailable" + non-clickable)
            MkText("HKHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -580f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "HealthKit";

            _settingsHKLabel = MkButtonWithLabel("HKRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -680f), new Vector2(880f, 90f),
                "Reconnect HealthKit", () => _hkReconnectModal.SetActive(true));

            // Section: Data
            MkText("DataHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -810f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "Data";

            _settingsResetLabel = MkButtonWithLabel("ResetRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -910f), new Vector2(880f, 90f),
                "Reset progress", HandleResetTap);

            // Section: Legal — privacy policy URL is mandated by Apple for
            // any app reading HealthKit data and must be reachable both
            // from the App Store listing and from within the app itself.
            MkText("LegalHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1040f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.UpperCenter, AccentGold).text = "Legal";

            MkButtonWithLabel("PrivacyPolicyRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -1140f), new Vector2(880f, 90f),
                "Privacy Policy", OpenPrivacyPolicy);

            MkButton("Back", _settingsPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");

            BuildHKReconnectModal(_settingsPanel.transform);
        }

        // Sits on top of the Settings panel (last child = highest sort
        // order), hidden by default, shown by the Reconnect HealthKit
        // row's click handler. Apple removed the deep link path to
        // "Settings → Privacy & Security → Health → <app>" in iOS 17+,
        // so we explain the manual nav steps in-app and let the user
        // navigate themselves. Continue clears the cached auth so the
        // gate re-shows when they return; Cancel just dismisses.
        void BuildHKReconnectModal(Transform parent)
        {
            _hkReconnectModal = new GameObject("HKReconnectModal");
            _hkReconnectModal.transform.SetParent(parent, false);
            var rt = _hkReconnectModal.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            // Dim backdrop covers the whole panel so the Settings UI
            // behind reads as inactive while the modal is up.
            var backdrop = _hkReconnectModal.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.78f);
            backdrop.raycastTarget = true;   // swallows taps so backdrop clicks don't leak through

            // Centred content panel.
            var content = MkSpritePanel("Content", _hkReconnectModal.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 1080f),
                "panel", new Color(0.95f, 0.86f, 0.66f, 1f));

            MkText("Title", content.transform, new Vector2(0.5f, 1f), new Vector2(0f, -60f),
                new Vector2(760f, 80f), FS_TITLE, TextAnchor.UpperCenter,
                new Color(0.18f, 0.10f, 0.05f)).text = "Reconnect HealthKit";

            // Step-by-step body. enableWordWrapping is required because the
            // string is long and the rect is narrow — same trap MkText fell
            // into for the HK gate body earlier.
            var body = MkText("Body", content.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -180f), new Vector2(760f, 640f),
                FS_LABEL, TextAnchor.UpperLeft, new Color(0.18f, 0.10f, 0.05f));
            body.enableWordWrapping = true;
            body.text =
                "Apple doesn't let apps jump straight to the HealthKit toggle, so please navigate manually:\n\n" +
                "1. Open the iPhone Settings app\n" +
                "2. Tap Privacy & Security\n" +
                "3. Tap Health\n" +
                "4. Tap Gamexercise\n" +
                "5. Turn on Step Count and Workouts\n" +
                "6. Return to this app\n\n" +
                "Tap Continue and we'll reset the in-app HealthKit status so the connect screen re-appears when you come back.";

            MkButton("Continue", content.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 240f), new Vector2(640f, 130f),
                "Continue", () =>
                {
                    HealthKitBridge.ResetAuthCache();
                    _hkReconnectModal.SetActive(false);
                });

            MkButton("Cancel", content.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(420f, 100f),
                "Cancel", () => _hkReconnectModal.SetActive(false),
                "btn_grey", "btn_grey_down");

            _hkReconnectModal.SetActive(false);
        }

        // HealthKit hard-gate panel. Sits behind a single CTA whose label
        // depends on the current HK auth state:
        //   NotDetermined -> "Connect HealthKit" (triggers iOS modal)
        //   Denied        -> "Open iOS Settings" (deep-link to system Settings)
        //   Not available -> button hidden; copy says device unsupported
        // Refresh() updates the body + button label every tick so the panel
        // reacts to state transitions (e.g. user accepts in iOS modal and
        // immediately advances to Home, or denies and the button text flips).
        void BuildHealthKitGate(Transform root)
        {
            _hkGatePanel = MkFullPanel("HealthKitGatePanel", root);

            MkText("Title", _hkGatePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -180f),
                new Vector2(900f, 90f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Connect HealthKit";

            _hkGateBody = MkText("Body", _hkGatePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f),
                new Vector2(880f, 480f), FS_LABEL, TextAnchor.UpperCenter, TextDim);
            // MkTextTMP defaults enableWordWrapping=false (suits single-line
            // labels like quest names + counters). Long narrative bodies like
            // this one need wrapping or they overflow horizontally — tester
            // hit exactly this issue, screenshot showed the body running off
            // the right edge of the screen.
            _hkGateBody.enableWordWrapping = true;
            _hkGateBody.text = "Gamexercise tracks your real-world steps via HealthKit. Your character only grows when you walk — without HealthKit there's nothing to power the game.";

            _hkGateActionBtn = MkButton("ConnectBtn", _hkGatePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f),
                new Vector2(640f, 140f), "Connect HealthKit", () => _onConnectHealthKit?.Invoke());
            _hkGateActionLabel = _hkGateActionBtn.transform.Find("Label")?.GetComponent<TMP_Text>();
        }

        // Privacy policy URL — published from docs/privacy-policy.html via
        // GitHub Pages (commit a6bf9f6 + deploy 2026-06-09). The same URL
        // is what gets entered into App Store Connect's App Privacy section
        // at submission time.
        const string PRIVACY_POLICY_URL = "https://donefourstudio.github.io/gamexercise/privacy-policy.html";
        static void OpenPrivacyPolicy()
        {
            Application.OpenURL(PRIVACY_POLICY_URL);
        }

        // Helper — clickable text row styled as a grey button. Returns the
        // Text component so the caller can update the label later (e.g.
        // "Sound effects: ON" -> "Sound effects: OFF" after a toggle).
        TMP_Text MkButtonWithLabel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                   string label, Action onClick)
        {
            var go = MkButton(name, parent, anchor, pos, size, label, onClick, "btn_grey", "btn_grey_down");
            // The label is added as a child by MkButton (now TMP_Text under the
            // SDF migration); fetch it back so UpdateSettings can mutate the
            // string without re-creating the button.
            var labelT = go.transform.Find("Label")?.GetComponent<TMP_Text>();
            return labelT;
        }

        // First tap arms the confirm window; second tap within RESET_CONFIRM_WINDOW
        // seconds calls onResetProgress. Window timer is shown via the row label
        // ("Tap again to confirm (3s)"); UpdateSettings repaints it every frame.
        void HandleResetTap()
        {
            float now = Time.unscaledTime;
            if (now < _resetArmedUntil) { _resetArmedUntil = 0f; _onResetProgress?.Invoke(); }
            else                          _resetArmedUntil = now + RESET_CONFIRM_WINDOW;
        }

        // Fallback iOS Settings deep link — used by the HealthKit gate's
        // Denied-state retry path (rarely hit in practice; our cached
        // auth resolution means the gate normally sees only NotDetermined
        // or Authorized). Lands on Gamexercise's iOS Settings page; user
        // would need to navigate Settings root → Privacy & Security →
        // Health → Gamexercise from there, but at least the entry point
        // is consistent. The Settings panel's "Reconnect HealthKit"
        // button no longer uses this — it shows an in-app instruction
        // modal instead (see BuildHKReconnectModal).
        public static void OpenHealthKitSettings()
        {
#if UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#endif
        }

        // ============================================================
        // First-run tutorial coach-marks. Four dim rects form a cutout
        // around the active step's target; the caption + Next button live
        // on top. AdvanceTutorial bumps the step or finishes (persists
        // tutorialDone). Sits at the very top of the canvas hierarchy so
        // it covers every other UI panel when active.
        // ============================================================
        void BuildTutorialOverlay(Transform root)
        {
            _tutorialOverlay = MkFullPanel("TutorialOverlay", root);
            // The full panel from MkFullPanel adds a transparent Image already;
            // keep it but make it non-blocking — the 4 dim quadrants do the
            // actual raycast-blocking around the spotlight.
            var bg = _tutorialOverlay.GetComponent<Image>();
            if (bg != null) bg.raycastTarget = false;

            var dimColor = new Color(0f, 0f, 0f, 0.92f);
            _tutorialDimTop    = MkPanel("DimT", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimColor).GetComponent<Image>();
            _tutorialDimBottom = MkPanel("DimB", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimColor).GetComponent<Image>();
            _tutorialDimLeft   = MkPanel("DimL", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimColor).GetComponent<Image>();
            _tutorialDimRight  = MkPanel("DimR", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, dimColor).GetComponent<Image>();

            // Solid card backing under the caption + button. Sits between
            // the dim panels and the text so partially-dimmed Home labels
            // (streak, today-steps, etc.) don't bleed into the caption.
            _tutorialCaptionBg = MkSpritePanel("CaptionBg", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880f, 360f), "panel_light", new Color(0.10f, 0.08f, 0.05f, 1f))
                .GetComponent<Image>();

            _tutorialCaption = MkText("Caption", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 200f), FS_TITLE, TextAnchor.MiddleCenter, AccentGold);
            _tutorialCaption.text = "";

            // Next button sits below the caption inside the same group so it
            // moves with the caption block per-step. Last-step relabels to
            // "Got it" inside UpdateTutorial.
            var btnGO = MkButton("Next", _tutorialOverlay.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(280f, 110f), "Next", AdvanceTutorial);
            _tutorialNextLabel = btnGO.GetComponentInChildren<TMP_Text>();

            _tutorialOverlay.SetActive(false);
        }

        void StartTutorial()
        {
            _tutorialStep = 0;
            _tutorialOverlay.SetActive(true);
            UpdateTutorial();
        }

        void AdvanceTutorial()
        {
            _tutorialStep++;
            if (_tutorialStep >= TUTORIAL_STEPS.Length)
            {
                // Last step dismissed — Refresh will pick up the flag, set
                // state.tutorialDone, and persist it via g.onSave.
                _pendingTutorialFinish = true;
                _tutorialStep = -1;
                _tutorialOverlay.SetActive(false);
            }
            else
            {
                UpdateTutorial();
            }
        }

        bool _pendingTutorialFinish;

        void UpdateTutorial()
        {
            if (_tutorialStep < 0 || _tutorialStep >= TUTORIAL_STEPS.Length) return;
            var s = TUTORIAL_STEPS[_tutorialStep];

            // Canvas half-extents (matches CanvasScaler.referenceResolution 1080x1920).
            const float HW = 540f, HH = 960f;

            float tl = s.targetCenter.x - s.targetSize.x / 2f;  // target left
            float tr = s.targetCenter.x + s.targetSize.x / 2f;  // target right
            float tb = s.targetCenter.y - s.targetSize.y / 2f;  // target bottom
            float tt = s.targetCenter.y + s.targetSize.y / 2f;  // target top

            // Top dim: full-width band above the target.
            SetRect(_tutorialDimTop.rectTransform,
                new Vector2(0f, (tt + HH) / 2f), new Vector2(HW * 2f, HH - tt));
            // Bottom dim: full-width band below the target.
            SetRect(_tutorialDimBottom.rectTransform,
                new Vector2(0f, (tb - HH) / 2f), new Vector2(HW * 2f, tb + HH));
            // Left + right dims: only span the vertical strip of the target.
            float midY = (tb + tt) / 2f;
            float midH = tt - tb;
            SetRect(_tutorialDimLeft.rectTransform,
                new Vector2((tl - HW) / 2f, midY), new Vector2(tl + HW, midH));
            SetRect(_tutorialDimRight.rectTransform,
                new Vector2((tr + HW) / 2f, midY), new Vector2(HW - tr, midH));

            _tutorialCaption.text = s.caption;
            _tutorialCaption.rectTransform.anchoredPosition = s.captionCenter;

            // Next button sits just below the caption text.
            var btnRT = _tutorialNextLabel.transform.parent.GetComponent<RectTransform>();
            btnRT.anchoredPosition = new Vector2(s.captionCenter.x, s.captionCenter.y - 130f);
            _tutorialNextLabel.text = (_tutorialStep == TUTORIAL_STEPS.Length - 1) ? "Got it" : "Next";

            // Card backing wraps caption (top) + Next button (bottom) with padding.
            // Caption height 200 (top edge at +100), Next height 110 centered
            // at -130 (bottom edge at -185). Pad 35 above caption, 25 below Next.
            SetRect(_tutorialCaptionBg.rectTransform,
                new Vector2(s.captionCenter.x, s.captionCenter.y - 42.5f),
                new Vector2(900f, 320f));
        }

        static void SetRect(RectTransform rt, Vector2 center, Vector2 size)
        {
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
        }

        // Per-frame position + alpha update for one coin floater Text. Driven
        // from the shared _coinFloatT timer so Home / Shop / SetDetail floaters
        // all rise + fade in lockstep — only the active panel's actually shows.
        static void TickCoinFloater(TMP_Text floater, float t01, float alpha, float startY, float endY)
        {
            if (floater == null) return;
            float y = Mathf.Lerp(startY, endY, t01);
            var rt = floater.rectTransform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
            var c = floater.color; c.a = alpha; floater.color = c;
        }

        // Build one horizontal row of capacity SK_ROW_CAPACITY cells + a "—
        // <label> —" header. Each cell starts hidden; UpdateInventory wires
        // the cell to a concrete skin/pet id at refresh time and shows it.
        void BuildOwnedRow(string name, Transform parent, float yTop,
                           GameObject[] roots, Image[] icons,
                           GameObject[] activeMarks, string[] idsArray,
                           string label)
        {
            // yTop = y where the section starts (header top edge). Header sits
            // here; cell row starts 50px below.
            MkText(name + "Header", parent, new Vector2(0.5f, 1f),
                new Vector2(0f, yTop), new Vector2(400f, 40f),
                FS_LABEL, TextAnchor.MiddleCenter, AccentGold).text = $"— {label} —";

            const int CELL = 100, GAP = 12;
            float rowLeft = -((SK_ROW_CAPACITY - 1) * (CELL + GAP)) / 2f - CELL / 2f;
            float cellY   = yTop - 50f - CELL / 2f;  // 50px gap below header to cell centre
            for (int i = 0; i < SK_ROW_CAPACITY; i++)
            {
                float x = rowLeft + i * (CELL + GAP);
                var cell = MkSpritePanel(name + "_Cell_" + i, parent,
                    new Vector2(0.5f, 1f), new Vector2(x, cellY), new Vector2(CELL, CELL),
                    "panel_light", new Color(0.22f, 0.24f, 0.32f, 1f));
                var icon = MkSpriteIcon("Icon", cell.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(CELL - 12f, CELL - 12f), (Sprite)null, Color.white).GetComponent<Image>();
                var mark = MkText("Active", cell.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 4f), new Vector2(CELL, 24f),
                    FS_BODY, TextAnchor.LowerCenter, AccentGold);
                mark.text = "Active";
                mark.gameObject.SetActive(false);

                int capIdx = i;
                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = cell.GetComponent<Image>();
                btn.transition    = Selectable.Transition.ColorTint;
                var cb = btn.colors;
                cb.highlightedColor = new Color(0.32f, 0.34f, 0.44f, 1f);
                btn.colors = cb;
                btn.onClick.AddListener(() =>
                {
                    var id = idsArray[capIdx];
                    if (!string.IsNullOrEmpty(id)) _onSkinAction?.Invoke(id);
                });

                cell.SetActive(false);
                roots[i] = cell;
                icons[i] = icon;
                activeMarks[i] = mark.gameObject;
            }
        }

        bool IsOwned(string id) => _ownedSnapshot.Contains(id);

    }
}
