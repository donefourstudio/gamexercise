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
        // Race select — 2x2 grid of race x gender cards. Fires at Lv 20.
        // M3b uses text-only cards; M3d will add portraits.
        // ============================================================
        void BuildRaceSelect(Transform root)
        {
            _raceSelectPanel = MkFullPanel("RaceSelect", root);

            MkText("Title", _raceSelectPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -180f),
                new Vector2(960f, 90f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Choose your true form.";
            MkText("Sub", _raceSelectPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -290f),
                new Vector2(960f, 60f), FS_LABEL, TextAnchor.UpperCenter, TextDim)
                .text = "\"...this is what I was meant to be.\"";

            MkRaceCard(_raceSelectPanel.transform, new Vector2(-220f,  220f), "Elf", "Male",
                Make.Portrait(Gender.Male, Curse.Unset, Race.Elf, 5),
                "Slender and ancient.",  () => _onSelectRaceAndGender?.Invoke(1, 1));
            MkRaceCard(_raceSelectPanel.transform, new Vector2( 220f,  220f), "Elf", "Female",
                Make.Portrait(Gender.Female, Curse.Unset, Race.Elf, 5),
                "Slender and ancient.",  () => _onSelectRaceAndGender?.Invoke(1, 2));
            MkRaceCard(_raceSelectPanel.transform, new Vector2(-220f, -220f), "Demon", "Male",
                Make.Portrait(Gender.Male, Curse.Unset, Race.Orc, 5),
                "Strength and rage.",    () => _onSelectRaceAndGender?.Invoke(2, 1));
            MkRaceCard(_raceSelectPanel.transform, new Vector2( 220f, -220f), "Demon", "Female",
                Make.Portrait(Gender.Female, Curse.Unset, Race.Orc, 5),
                "Strength and rage.",    () => _onSelectRaceAndGender?.Invoke(2, 2));
        }

        void MkRaceCard(Transform parent, Vector2 pos, string race, string gender, Sprite portrait, string flavor, Action onClick)
        {
            var card = MkSpritePanel("Race_" + race + "_" + gender, parent, new Vector2(0.5f, 0.5f), pos,
                new Vector2(400f, 460f), "panel", PanelTint);
            var btn = card.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.7f, 1f); btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            MkSpriteIcon("Portrait", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f),
                new Vector2(220f, 220f), portrait, Color.white);
            MkText("Race", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -50f),
                new Vector2(360f, 60f), FS_TITLE, TextAnchor.MiddleCenter, AccentGold).text = race;
            MkText("Gender", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f),
                new Vector2(360f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite).text = gender;
            MkText("Flavor", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -180f),
                new Vector2(360f, 60f), FS_BODY, TextAnchor.MiddleCenter, TextDim).text = flavor;
        }

        // ============================================================
        // Race transformation animation — 1.5s. Silhouette in for the
        // first 0.7s, then sprite-swaps to the chosen race form.
        // ============================================================
        const float RACE_ANIM_DURATION = 1.5f;

        void BuildRaceTransformAnim(Transform root)
        {
            _raceAnimPanel = MkFullPanel("RaceAnim", root);
            var img = _raceAnimPanel.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = false;

            _raceAnimSilhouette = MkSpriteIcon("Silhouette", _raceAnimPanel.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 640f),
                "silhouette", Color.white).GetComponent<Image>();

            _raceAnimAvatar = BuildAvatar(_raceAnimPanel.transform, Vector2.zero, 2.4f,
                Gender.Male, Curse.Unset, stage: 5);
            _raceAnimAvatar.root.SetActive(false);

            _raceAnimText = MkText("AnimText", _raceAnimPanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 220f), new Vector2(960f, 80f), FS_BIG, TextAnchor.LowerCenter, AccentGold);
            _raceAnimText.text = "Awakening...";
        }

        // ============================================================
        // Curse animation panel — auto-driven from Refresh().
        // Layout (back to front): full-screen dim overlay -> hero avatar.
        // The dim alpha lerps 0 -> 0.7 over the duration to isolate the
        // hero. Avatar shakes (amplitude peaks at the swap moment), then
        // its sprite swaps from hero to cursed at CURSE_ANIM_SWAP_AT.
        // ============================================================
        void BuildCurseAnim(Transform root)
        {
            _curseAnimPanel = MkFullPanel("CurseAnim", root);

            var dim = MkPanel("Dim", _curseAnimPanel.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(4000f, 4000f), new Color(0f, 0f, 0f, 0f));
            _curseAnimDim = dim.GetComponent<Image>();
            _curseAnimDim.raycastTarget = false;

            _curseAnimAvatar = BuildAvatar(_curseAnimPanel.transform, Vector2.zero, 2.4f,
                Gender.Male, Curse.Unset, stage: 5);
        }

        // ============================================================
        // Opening — 4 narrative beats. Each panel covers the full screen,
        // its background captures taps that advance via TapAdvanceOpening.
        // ============================================================
        GameObject BuildOpeningTextPanel(string name, Transform root, string text, TextAnchor align)
        {
            var go = MkFullPanel(name, root);
            // make the panel's full-screen invisible image catch clicks
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);   // ~transparent but raycast-receivable
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => _onTapAdvanceOpening?.Invoke());

            // FS_BTN (44pt) — Jackson's "...until the Curse of Weakness fell
            // upon you." overflowed the screen at FS_TITLE (55). Shrunk so
            // any reasonable line of body text breathes inside the rect.
            var body = MkText("Body", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(960f, 400f), FS_BTN, align, AccentGold);
            body.text = text;
            // TMP wraps on by default — flip word-wrap on (MkTextTMP defaults
            // wrap off because most labels in this Hud are single-line).
            body.enableWordWrapping = true;
            MkText("Hint", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
            return go;
        }

        // App title / launch screen. Plays once per cold start so the player
        // taps in deliberately instead of being dumped into the opening
        // cinematic the instant the app loads. New players continue to
        // OpeningIntro from here; returning players jump to Home (or
        // RaceSelect mid-run) via GameRunner.DetermineInitialPhase.
        //
        // Composition reuses the in-game "throne room" visual language —
        // a gold crown centred over the wordmark and four lit candles
        // framing the screen — so the launch screen reads as part of the
        // same world the rest of the UI inhabits. Fade-in and CTA pulse
        // are driven from Refresh via _titleT.
        void BuildTitle(Transform root)
        {
            _titlePanel = MkFullPanel("TitlePanel", root);
            // CanvasGroup drives the exit fade-out by multiplying every
            // child element's alpha. Per-element color.a is still used for
            // staggered fade-IN, so the two effects compose without
            // conflict (fade-in alpha * exit alpha).
            _titleCanvasGroup = _titlePanel.AddComponent<CanvasGroup>();

            // Tap-anywhere-to-start: the full-screen panel itself is a
            // Button. The visible "Tap to Start" label below is purely a
            // prompt + still tappable as its own button (in front of this
            // one in z-order so it gets clicks within its 560x130 area;
            // taps anywhere else on the panel land here).
            var panelImg = _titlePanel.GetComponent<Image>();
            panelImg.raycastTarget = true;
            var panelBtn = _titlePanel.AddComponent<Button>();
            panelBtn.targetGraphic = panelImg;
            panelBtn.transition = Selectable.Transition.None;
            panelBtn.onClick.AddListener(() =>
            {
                if (_titleExiting) return;
                Sfx.Play("tap");
                _titleExiting = true;
                _titleExitT = 0f;
                if (_titleStartButton != null) _titleStartButton.interactable = false;
                panelBtn.interactable = false;
            });

            // Background scene — Midjourney-generated pixel-art throne hall
            // (816x1456 native 9:16). Fills the full screen since its
            // aspect ratio matches the 1080x1920 design canvas exactly.
            // The image's own composition has natural dark zones at top
            // (archway shadow above the throne) and bottom (stone stairs)
            // that the wordmark + crown + tagline (top) and Start Game
            // button (bottom) overlay into. Added FIRST so it renders
            // BEHIND all other title elements.
            var bg = MkSpriteIcon("TitleBg", _titlePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(1080f, 1920f), "title_bg", Color.white);
            bg.GetComponent<Image>().raycastTarget = false;
            bg.GetComponent<Image>().preserveAspect = true;

            // Crown halo removed — the Midjourney throne_bg already provides
            // a warm chandelier-lit centre, and stacking my custom glow on
            // top fights its colour balance. Field kept null; UpdateTitle
            // null-checks before breathing.
            _titleCrownHalo = null;

            // Crown + wordmark + tagline + divider are repositioned UP into
            // the dark band ABOVE the throne_bg (which sits centred in the
            // screen, top edge ~420px from top). Putting the gold wordmark
            // on the chandelier glow at image centre washed it out; in the
            // dark band it reads sharp and the throne scene below feels
            // like the world the title is calling the player into.
            // Crown removed — clashed with the MJ throne_bg's own dense
            // pixel architecture; the throne itself IS the crown motif
            // now. Field kept null so any future re-add is a one-liner.
            _titleCrown = null;

            // Wordmark — Gamexercise is the GAME NAME so it carries the
            // most visual weight on the screen. FS_BIG (77px) gives it
            // proper scale, and TWO stacked Outline components in opposite
            // diagonal offsets simulate a bold-weight stroke (Cubic 11 is
            // a pixel font with no native bold variant; stacked outlines
            // are how pixel-art games typically fake weight). Weathered
            // amber tint matches the god-ray + dust-mote palette so the
            // title reads as paint not sticker.
            // Wordmark + tagline use TMP SDF rendering for pixel-crisp text
            // at non-integer canvas scale. To recover the "thick stamped
            // game-logo" look the bitmap-path dual-UI.Outline produced, we
            // layer THREE shader effects on top of the SDF glyph:
            //   1. fontStyle=Bold — thickens the glyph silhouette
            //   2. outlineWidth=0.4 — black ring around each letter
            //   3. UNDERLAY_ON keyword + offset/dilate — drop-shadow underneath
            // Outline gives the symmetric thick-edge look, underlay gives
            // the asymmetric depth that made the original feel like a
            // metal-stamped logo rather than flat decal text.
            var wordmarkAmber = new Color(0.92f, 0.74f, 0.42f, 1f);
            _titleWordmark = MkTextTMP("AppTitle", _titlePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -220f), new Vector2(1000f, 140f), FS_BIG, TextAlignmentOptions.Center, wordmarkAmber);
            _titleWordmark.text = "Gamexercise";
            _titleWordmark.fontStyle = FontStyles.Bold;
            _titleWordmark.outlineColor = new Color(0f, 0f, 0f, 1f);
            _titleWordmark.outlineWidth = 0.4f;
            ApplyUnderlay(_titleWordmark, dilate: 1.0f, offsetX: 0.35f, offsetY: -0.35f);

            // Tagline — warm-stone tone matching the throne_bg's amber walls.
            // Lighter underlay (offset/dilate halved) so the depth effect
            // tracks the smaller glyph size and doesn't overwhelm the label.
            var taglineStone = new Color(0.92f, 0.84f, 0.62f, 1f);
            _titleTagline = MkTextTMP("Tagline", _titlePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -330f), new Vector2(1000f, 80f), FS_LABEL, TextAlignmentOptions.Center, taglineStone);
            _titleTagline.text = "Walk. Train. Reign.";
            _titleTagline.fontStyle = FontStyles.Bold;
            _titleTagline.outlineColor = new Color(0f, 0f, 0f, 1f);
            _titleTagline.outlineWidth = 0.3f;
            ApplyUnderlay(_titleTagline, dilate: 0.6f, offsetX: 0.2f, offsetY: -0.2f);

            // Decorative divider — Kenney fantasy-ui-borders divider_ornate
            // is asymmetric (key-shape on one end, fade on the other), so
            // we render TWO mirrored halves with the key-ends pointing
            // outward and the fades meeting at centre. Closes off the
            // title cluster with a "Mystic Vale"-style rule.
            // Divider removed — the throne_bg's own stone archway provides
            // plenty of ornamentation, and an extra Kenney divider on top
            // looks redundant against the dense pixel architecture.
            _titleDivider = null;

            // Corner candles removed — the throne_bg already has its own
            // ambient torch glow along both side walls + dust motes in the
            // god-ray, so the four flat candle sprites read as extra UI
            // clutter on top of richer painted lighting. _titleCandles
            // stays as a length-4 array of nulls so UpdateTitle's flicker
            // loop null-checks and no-ops.

            // CTA is now a frameless "Tap to Start" floating directly on
            // the lowest stair — no plate, no sprite, just text. The
            // sprite Image alpha is zeroed (raycastTarget stays true so
            // taps still register) and the label is recoloured to a dark
            // black-grey that reads as an unobtrusive prompt without
            // competing with the painted scene's own light values.
            var startBtn = MkButton("StartGame", _titlePanel.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 300f), new Vector2(560f, 130f), "Tap to Start",
                () => { _titleExiting = true; _titleExitT = 0f; if (_titleStartButton != null) _titleStartButton.interactable = false; },
                "btn_grey", "btn_grey_down");
            _titleStartBtn    = startBtn.transform;
            _titleStartButton = startBtn.GetComponent<Button>();
            var startBtnImg = startBtn.GetComponent<Image>();
            if (startBtnImg != null) startBtnImg.color = new Color(1f, 1f, 1f, 0f);   // invisible plate; click still works via raycastTarget
            _titleStartLabel = startBtn.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (_titleStartLabel != null)
            {
                // Lighter warm-grey so the prompt actually reads on dark
                // stairs; alpha is then breathed in UpdateTitle to draw a
                // subtle "tap me" pulse without the previous full-button
                // scale wobble.
                _titleStartLabel.color = new Color(0.78f, 0.78f, 0.80f, 1f);
                var startBtnLabelOutline = _titleStartLabel.gameObject.AddComponent<Outline>();
                startBtnLabelOutline.effectColor    = new Color(0f, 0f, 0f, 0.7f);
                startBtnLabelOutline.effectDistance = new Vector2(2f, -2f);
            }
        }

        void BuildOpeningIntro(Transform root)
        {
            _openingIntroPanel = BuildOpeningTextPanel(
                "OpeningIntro", root, "You were once the strongest\nhero of this age...", TextAnchor.MiddleCenter);
        }

        void BuildOpeningHeroShown(Transform root)
        {
            // Abstract silhouette of the legendary hero — no race / gender revealed yet.
            // Per Q4=B: faded silhouette with a faint halo. Resolves at the Lv 20
            // race transformation.
            _openingHeroShownPanel = MkFullPanel("OpeningHeroShown", root);
            var img = _openingHeroShownPanel.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f);
            img.raycastTarget = true;
            var btn = _openingHeroShownPanel.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => _onTapAdvanceOpening?.Invoke());

            // Show the legendary hero as the dark_knight composite (full color,
            // not a silhouette) — sets up visual continuity with the CurseAnim
            // pre-curse hero and the dark_knight set in the shop.
            MkSpriteIcon("HeroSilhouette", _openingHeroShownPanel.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 640f),
                Make.SetPreview("champ_dark_knight"), Color.white);

            MkText("Hint", _openingHeroShownPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 60f), FS_LABEL, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
        }

        void BuildOpeningCurseLooms(Transform root)
        {
            // After Jackson cut Gluttony the curse arc is single-track —
            // the player is hit by Weakness immediately and the next tap
            // jumps straight to the cinematic, no choice screen.
            _openingCurseLoomsPanel = BuildOpeningTextPanel(
                "OpeningCurseLooms", root, "...until the Curse of Weakness fell upon you.", TextAnchor.MiddleCenter);
        }

        void BuildOpeningAmnesia(Transform root)
        {
            _openingAmnesiaPanel = BuildOpeningTextPanel(
                "OpeningAmnesia", root, "You have forgotten who you are.", TextAnchor.MiddleCenter);
        }

        // ============================================================
        // Background
        // ============================================================
        static void BuildBackground(Transform root)
        {
            var go = MkFullPanel("BgFill", root);
            var img = go.GetComponent<Image>();
            img.color = BgDark;
            img.raycastTarget = false;
        }

        // ============================================================
        // First mirror — reveals the cursed self for the first time.
        // ============================================================
        void BuildFirstMirror(Transform root)
        {
            _firstMirrorPanel = MkFullPanel("FirstMirror", root);

            var frame = MkSpritePanel("MirrorFrame", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 180f), new Vector2(540f, 720f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var inner = MkSpritePanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(480f, 660f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            // Cursed self at stage 0 — refreshed with the chosen gender+curse on entry.
            _firstMirrorSelf = BuildAvatar(inner.transform, new Vector2(0f, 0f), 2.0f,
                Gender.Male, Curse.Weakness, stage: 0);

            _firstMirrorLine = MkText("Line", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -380f), new Vector2(1000f, 80f), FS_BIG, TextAnchor.MiddleCenter, AccentGold);
            _firstMirrorLine.text = "\"...so this is who I am now.\"";

            MkButton("Begin", _firstMirrorPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 180f),
                new Vector2(540f, 130f), "Take the First Step", () => _onFinishFirstMirror?.Invoke());
        }

    }
}
