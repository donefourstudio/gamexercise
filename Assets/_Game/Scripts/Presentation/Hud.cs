using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Gamex.Core;

namespace Gamex.Game
{
    // Single-canvas HUD with six panels (gender / curse / firstMirror / home / training / shop).
    // M2a rebuild: solid-color rectangles replaced with Kenney pixel UI 9-slice (Ancient theme)
    // and LPC character portraits driven by Make.Portrait(gender, curse, stage). All text is
    // rendered with Cubic 11 (Chinese pixel font). Refresh() repaints every frame from
    // GamexGame state.
    public class Hud
    {
        // ---- palette ----
        static readonly Color BgDark     = new Color(0.06f, 0.05f, 0.10f);
        static readonly Color AccentGold = new Color(1f,    0.84f, 0.42f);
        static readonly Color TextDim    = new Color(0.78f, 0.72f, 0.62f);
        static readonly Color TextWhite  = new Color(0.96f, 0.93f, 0.86f);
        static readonly Color CamBg      = new Color(0.07f, 0.09f, 0.12f);
        static readonly Color PanelTint  = new Color(1f,    1f,    1f,   1f);

        // Cubic 11 is native 11px — keep font sizes in integer multiples to stay crisp.
        const int FS_BODY    = 22;
        const int FS_LABEL   = 33;
        const int FS_BTN     = 44;
        const int FS_TITLE   = 55;
        const int FS_BIG     = 77;
        const int FS_HUGE    = 154;

        // ---- panels ----
        GameObject _openingIntroPanel, _openingHeroShownPanel, _openingCurseLoomsPanel, _openingAmnesiaPanel;
        GameObject _curseAnimPanel;
        GameObject _raceSelectPanel, _raceAnimPanel;
        GameObject _cursePanel, _firstMirrorPanel, _homePanel, _trainPanel, _shopPanel;
        GameObject _inventoryPanel;
        // _genderPanel removed — gender chosen at Lv 20 RaceSelect (Q2=C)

        // ---- avatars ----
        AvatarSprite _mirrorSelf;            // the player's reflection on Home — current cursed -> hero arc
        AvatarSprite _firstMirrorSelf;        // cursed self in the first-mirror beat
        AvatarSprite _openingHeroAvatar;     // muscular hero shown during OpeningHeroShown
        AvatarSprite _curseAnimAvatar;       // hero -> cursed in the curse cinematic
        AvatarSprite _curseMaleA, _curseFemaleA, _curseMaleB, _curseFemaleB;
        Image _raceAnimSilhouette;       // shown for the first half of the cinematic
        AvatarSprite _raceAnimAvatar;    // race form, shown for the second half
        Text _raceAnimText;
        float _raceAnimT;
        const float RACE_ANIM_SWAP_AT = 0.7f;

        // (Pose detection scaffolding deleted in M5b — rep counting was abandoned in
        // favour of step-based progression. The Quests panel below replaces the old
        // Training viewport entirely.)

        // ---- daily ritual icons (Home) ----
        Image[] _candleImgs = new Image[4];   // 4 candles bracketing the mirror
        Image _crownImg;                       // hovers above the mirror character
        // crown y when floating (maintenance not yet met) vs landed on the head (met)
        const float CROWN_Y_FLOATING = 660f;
        const float CROWN_Y_LANDED   = 540f;

        // ---- mirror polish (breathing + stage-up flash + milestone dialogue) ----
        Image _stageUpFlash;          // white overlay inside the mirror, lerps up + back
        Text  _milestoneText;         // 4s line below the mirror after a stage transition
        int   _prevStage  = -1;       // -1 = uninitialised, set on first Home Refresh
        int   _prevLevel  = 1;
        float _stageUpT;              // > 0 while flash + scale-pulse is playing
        float _milestoneT;            // > 0 while milestone line is visible

        const float BREATH_AMP        = 0.015f;
        const float BREATH_FREQ       = 1.8f;
        const float STAGEUP_DURATION  = 0.6f;
        const float STAGEUP_SCALE_AMP = 0.08f;
        const float STAGEUP_FLASH_A   = 0.5f;
        const float MILESTONE_DURATION = 4f;
        const float MILESTONE_FADE_OUT = 1f;

        // 6 lines surface in order at the 6 stage transitions (Lv 6 / 11 / 16 / 21 / 26 / 30).
        static readonly string[] MILESTONE_LINES = new[]
        {
            "\"...I feel a flicker of strength.\"",
            "\"My body remembers the old training.\"",
            "\"The curse's fog is lifting.\"",
            "\"...I'm starting to remember who I am.\"",
            "\"Almost there. Just a little further.\"",
            "\"...I'm back.\"",
        };

        // ---- curse anim state ----
        Image _curseAnimDim;
        float _curseAnimT;
        AppPhase _lastPhase = AppPhase.Boot;
        const float CURSE_ANIM_DURATION = 1.5f;
        const float CURSE_ANIM_SWAP_AT  = 0.7f;

        // ---- home refs ----
        Text _homeLevel, _homeCoins, _homeProgress, _homeStreak;
        Image _xpBar;

        // ---- quests refs (M5b/d) ----
        Text _questsStreak, _questsTotalSteps, _questsTotalRun, _questsKnight;
        GameObject _questsKnightRow;
        readonly Image[] _questCheckmarks = new Image[(int)Quest.Count];
        readonly Text[]  _questRowLabels  = new Text[(int)Quest.Count];

        // ---- first mirror refs ----
        Text _firstMirrorLine;

        // ---- skin animation (Phase 5e3) ----
        // Tracks the currently-applied skin so frame state resets when the
        // player swaps skins. _animFrame walks 0 .. skin.frameCount-1 over
        // skin.frameSeconds intervals, swapping Image.sprite each step.
        string _animLastSkin;
        int    _animFrame;
        float  _animTimer;

        // ---- pet rendering (polish round 3) ----
        // Pet sits at the bottom-right of the mirror / paper-doll, hidden
        // until state.activePet is set. Each phase has its own Image so the
        // pet appears wherever the avatar is currently visible.
        Image  _homePet, _inventoryPet;
        string _petAnimLast;
        int    _petAnimFrame;
        float  _petAnimTimer;

        // ---- shop refs ----
        Text _shopCoins;
        // Per-set card refs — `priceLabel` flips to "Owned" once every piece
        // is in state.owned, `bundleBtn` disables atomically with affordability.
        readonly List<(string setId, GameObject root, Text priceLabel, Button cardBtn)> _shopSetCards = new();
        // Per-skin card refs (Phase 4b) — `actionLabel`/`actionBtn` flip between
        // Buy / Apply / Remove based on owned + active state.
        readonly List<(string skinId, GameObject root, Text stateLabel, Text actionLabel, Button actionBtn)> _shopSkinCards = new();
        readonly HashSet<string> _ownedSnapshot = new();

        // ---- set detail (Phase 3c) refs ----
        GameObject _setDetailPanel;
        Image _setDetailPreview;
        Text  _setDetailTitle, _setDetailCoins, _setDetailBundleLabel;
        Button _setDetailBundleBtn;
        // Per-piece row: each row shows icon + name + per-piece price + buy/owned.
        // Built lazily on first entry into a set's detail page; cached so re-entry
        // doesn't rebuild.
        readonly Dictionary<string, GameObject> _setDetailRowsRoot = new();
        readonly Dictionary<string, (Text label, Button btn)> _setDetailPieceUI = new();

        // ---- inventory refs ----
        AvatarSprite _inventoryAvatar;
        readonly Image[] _invSlotIcons = new Image[6];     // per-slot occupant sprite (item or null)
        readonly Image[] _invSlotBgs   = new Image[6];     // per-slot background (greyed if empty)
        readonly Text[]  _invSlotLabels = new Text[6];     // slot-name label, shown only when slot is empty
        readonly string[] _invSlotEquippedIds = new string[6]; // tracks current occupant id for tap-to-unequip
        // Bottom storage grid — one cell per Catalog + KnightSet entry. Cells the player
        // doesn't own are hidden via SetActive(false), the others light up + show sprite.
        // equippedBadge is a "✓" text shown only while the item is in state.equipped.
        readonly List<(string id, GameObject root, Image icon, GameObject equippedBadge)> _invItems = new();
        Text _invInventoryHeader;

        // ---- callbacks ----
        readonly Action         _onTapAdvanceOpening;
        readonly Action         _onCurseAnimDone;
        readonly Action<int>    _onSelectCurse;
        readonly Action<int,int> _onSelectRaceAndGender;
        readonly Action         _onRaceAnimDone;
        readonly Action         _onFinishFirstMirror;
        readonly Action         _onGoQuests, _onGoShop, _onGoHome, _onGoInventory, _onFakeRep;
        readonly Action<string> _onBuy, _onToggleEquip, _onGoSetDetail, _onBuySet, _onSkinAction;

        public Hud(
            Action onTapAdvanceOpening,
            Action onCurseAnimDone,
            Action<int> onSelectCurse,
            Action<int,int> onSelectRaceAndGender,
            Action onRaceAnimDone,
            Action onFinishFirstMirror,
            Action onGoQuests,
            Action onGoShop,
            Action onGoHome,
            Action onGoInventory,
            Action onFakeRep,
            Action<string> onBuy,
            Action<string> onToggleEquip,
            Action<string> onGoSetDetail,
            Action<string> onBuySet,
            Action<string> onSkinAction)
        {
            _onTapAdvanceOpening   = onTapAdvanceOpening;
            _onCurseAnimDone       = onCurseAnimDone;
            _onSelectCurse         = onSelectCurse;
            _onSelectRaceAndGender = onSelectRaceAndGender;
            _onRaceAnimDone        = onRaceAnimDone;
            _onFinishFirstMirror   = onFinishFirstMirror;
            _onGoQuests            = onGoQuests;
            _onGoShop              = onGoShop;
            _onGoHome              = onGoHome;
            _onGoInventory         = onGoInventory;
            _onFakeRep             = onFakeRep;
            _onBuy                 = onBuy;
            _onToggleEquip         = onToggleEquip;
            _onGoSetDetail         = onGoSetDetail;
            _onBuySet              = onBuySet;
            _onSkinAction          = onSkinAction;

            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var canvasGO = new GameObject("HUD");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight  = 1f;   // portrait-only: match height, letterbox sides in landscape preview
            canvasGO.AddComponent<GraphicRaycaster>();
            var root = canvasGO.transform;

            BuildBackground(root);
            BuildOpeningIntro(root);
            BuildOpeningHeroShown(root);
            BuildOpeningCurseLooms(root);
            BuildCurseAnim(root);
            BuildOpeningAmnesia(root);
            BuildCurseSelect(root);
            BuildFirstMirror(root);
            BuildHome(root);
            BuildQuests(root);
            BuildShop(root);
            BuildSetDetail(root);
            BuildInventory(root);
            BuildRaceSelect(root);
            BuildRaceTransformAnim(root);
        }

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
            MkRaceCard(_raceSelectPanel.transform, new Vector2(-220f, -220f), "Orc", "Male",
                Make.Portrait(Gender.Male, Curse.Unset, Race.Orc, 5),
                "Strength and rage.",    () => _onSelectRaceAndGender?.Invoke(2, 1));
            MkRaceCard(_raceSelectPanel.transform, new Vector2( 220f, -220f), "Orc", "Female",
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

            MkText("Body", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(960f, 400f), FS_TITLE, align, AccentGold).text = text;
            MkText("Hint", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
            return go;
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

            MkSpriteIcon("HeroSilhouette", _openingHeroShownPanel.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 640f),
                "silhouette", Color.white);

            MkText("Hint", _openingHeroShownPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
        }

        void BuildOpeningCurseLooms(Transform root)
        {
            _openingCurseLoomsPanel = BuildOpeningTextPanel(
                "OpeningCurseLooms", root, "...until the curse fell upon you.", TextAnchor.MiddleCenter);
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
        // Curse select
        // ============================================================
        void BuildCurseSelect(Transform root)
        {
            _cursePanel = MkFullPanel("CursePanel", root);

            MkText("Title", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(900f, 90f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Which curse befell you?";
            MkText("Sub", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -340f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, TextDim)
                .text = "The curse takes two forms.";

            MkCurseOption(_cursePanel.transform, new Vector2(-260f, 60f),
                "Weakness", "Your strength is fading", Curse.Weakness, () => _onSelectCurse(1),
                out _curseMaleA, out _curseFemaleA);
            MkCurseOption(_cursePanel.transform, new Vector2( 260f, 60f),
                "Gluttony", "Your body grows heavy", Curse.Gluttony, () => _onSelectCurse(2),
                out _curseMaleB, out _curseFemaleB);
        }

        void MkCurseOption(Transform parent, Vector2 pos, string title, string sub, Curse curse, Action onClick,
                           out AvatarSprite male, out AvatarSprite female)
        {
            var card = MkSpritePanel("CurseOpt", parent, new Vector2(0.5f, 0.5f), pos,
                new Vector2(420f, 760f), "panel", PanelTint);
            var btn = card.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.7f, 1f); btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            // Preview the cursed body shape (teen vs pregnant) — NOT the post-transformation
            // skeleton, so the player can actually see what they're picking between.
            MkSpriteIcon("Preview", card.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 110f),
                new Vector2(300f, 300f), Make.CursePreview(curse), Color.white);

            male = female = null;   // M3a removed gender from curse-select avatar; visuals are gender-neutral here

            MkText("Title", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 130f),
                new Vector2(360f, 60f), FS_BIG, TextAnchor.MiddleCenter, AccentGold).text = title;
            MkText("Sub", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(360f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim).text = sub;
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

        // ============================================================
        // Home
        // ============================================================
        void BuildHome(Transform root)
        {
            _homePanel = MkFullPanel("HomePanel", root);

            // top HUD
            _homeLevel = MkText("Level", _homePanel.transform, new Vector2(0f, 1f), new Vector2(50f, -60f),
                new Vector2(400f, 60f), FS_BIG, TextAnchor.UpperLeft, AccentGold);
            _homeCoins = MkText("Coins", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-50f, -60f),
                new Vector2(400f, 60f), FS_BIG, TextAnchor.UpperRight, AccentGold);

            // Mirror — centered, holds the player's CURRENT reflection. The body
            // shape morphs from cursed -> hero as stage advances. No more
            // separate "small cursed avatar + big hero in mirror" split.
            // (Left of mirror is reserved for the M2b-3 daily ritual: candles + crown.)
            var frame = MkSpritePanel("Mirror", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 260f), new Vector2(520f, 660f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var inner = MkSpritePanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(460f, 600f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            _mirrorSelf = BuildAvatar(inner.transform, Vector2.zero, 1.8f, Gender.Male, Curse.Weakness, stage: 0);

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

            // bottom buttons — Quests opens the daily-task list, Shop is cosmetics
            MkButton("Quests", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 280f),
                new Vector2(800f, 160f), "Quests", () => _onGoQuests?.Invoke());
            MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Quests — daily-task list + lifetime totals (M5b).
        // Replaces the old Training screen entirely. Each quest is a row
        // showing label + status (✓ / progress fraction). Refresh updates
        // the rows from g.state every frame.
        // ============================================================
        static readonly (Quest quest, string label, int goal, bool runMinutes)[] QUEST_SPEC =
        {
            (Quest.Walk1000,  "Walk 1,000 steps",  1000,  false),
            (Quest.Walk5000,  "Walk 5,000 steps",  5000,  false),
            (Quest.Walk10000, "Walk 10,000 steps", 10000, false),
            (Quest.Run15Min,  "Run 15 minutes",    15,    true),
            (Quest.Run30Min,  "Run 30 minutes",    30,    true),
        };

        void BuildQuests(Transform root)
        {
            _trainPanel = MkFullPanel("QuestsPanel", root);

            MkText("Title", _trainPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Daily Quests";

            // 5 quest rows, stacked. Each is a tan-tinted panel with label left, status right.
            const float rowH = 130f, rowGap = 16f, rowW = 920f;
            float startY = 660f;
            for (int i = 0; i < QUEST_SPEC.Length; i++)
            {
                float y = startY - i * (rowH + rowGap);
                var row = MkSpritePanel("Q_" + i, _trainPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(rowW, rowH), "panel", new Color(0.95f, 0.86f, 0.66f, 1f));

                _questRowLabels[i] = MkText("Label", row.transform, new Vector2(0f, 0.5f),
                    new Vector2(50f, 0f), new Vector2(rowW - 220f, rowH - 20f),
                    FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));

                // ✓ checkmark / gold reward chip on the right
                _questCheckmarks[i] = MkSpriteIcon("Tick", row.transform, new Vector2(1f, 0.5f),
                    new Vector2(-70f, 0f), new Vector2(72f, 72f),
                    "panel_light", new Color(1f, 0.84f, 0.42f, 1f)).GetComponent<Image>();
                _questCheckmarks[i].gameObject.SetActive(false);
            }

            // Special row: Knight Set chain progress. Hidden until Lv 20 unlocks it.
            // Sits just below the daily quests.
            _questsKnightRow = MkSpritePanel("Q_Knight", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -60f), new Vector2(920f, 110f), "panel", new Color(0.85f, 0.78f, 1f, 1f));
            _questsKnight = MkText("KnightLabel", _questsKnightRow.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880f, 90f), FS_LABEL, TextAnchor.MiddleCenter, new Color(0.18f, 0.08f, 0.20f));
            _questsKnightRow.SetActive(false);

            // Totals — lifetime steps + running time + streak — under the special row.
            _questsTotalSteps = MkText("TotalSteps", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -200f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _questsTotalRun   = MkText("TotalRun",   _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _questsStreak     = MkText("Streak",     _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -320f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, AccentGold);

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
        static readonly string[] SectionOrder = { "champion", "legend", "cyberpunk", "pet" };

        void BuildShop(Transform root)
        {
            _shopPanel = MkFullPanel("ShopPanel", root);

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Shop";
            _shopCoins = MkText("Coins", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-50f, -90f),
                new Vector2(400f, 60f), FS_BIG, TextAnchor.UpperRight, AccentGold);

            // Phase 5c — content lives inside a ScrollRect so an arbitrary
            // number of sections + cards can stack without overflowing the
            // back button. Viewport stretches from below the title to above
            // the back button; content is a RectTransform whose height we
            // adjust at the end to match the cumulative card stack.
            var contentRT = MkScrollView("ShopScroll", _shopPanel.transform,
                topInset: 200f, bottomInset: 250f);

            float y = -30f;   // cursor in content-space (top is y=0, growing negative downward)
            const float CARD_W = 880f, CARD_H_SET = 280f, CARD_H_SKIN = 110f, CARD_GAP = 24f;
            // Jackson's read: "Champions" header sat too far above its first
            // card, "Legends" header sat too close under the last Champion
            // card. Tightened header->card and widened section->section to
            // emphasise the boundary at section ends.
            const float SECTION_GAP = 130f, HEADER_GAP = 35f;

            foreach (var source in SectionOrder)
            {
                var sectionSets  = new List<SetDef>();
                foreach (var s in GamexGame.SetCatalog)  if (s.source == source) sectionSets.Add(s);
                var sectionSkins = new List<SkinDef>();
                foreach (var s in GamexGame.SkinCatalog) if (s.source == source) sectionSkins.Add(s);
                if (sectionSets.Count == 0 && sectionSkins.Count == 0) continue;

                string headerText = SectionDisplayNames.TryGetValue(source, out var h) ? h : source;
                var hdr = MkText("SectionHeader_" + source, contentRT, new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(800f, 50f),
                    FS_LABEL, TextAnchor.MiddleCenter, AccentGold);
                hdr.text = $"— {headerText} —";
                y -= HEADER_GAP;

                // Set cards inside this section (multi-piece purchasable bundles).
                foreach (var set in sectionSets)
                {
                    var card = MkSpritePanel("SetCard_" + set.id, contentRT,
                        new Vector2(0.5f, 1f), new Vector2(0f, y - CARD_H_SET / 2f),
                        new Vector2(CARD_W, CARD_H_SET),
                        "panel", new Color(0.95f, 0.86f, 0.66f, 1f));
                    var cardBtn = card.AddComponent<Button>();
                    cardBtn.targetGraphic = card.GetComponent<Image>();
                    cardBtn.transition = Selectable.Transition.ColorTint;
                    var cb = cardBtn.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.78f, 1f); cardBtn.colors = cb;
                    string capSetId = set.id;
                    cardBtn.onClick.AddListener(() => _onGoSetDetail?.Invoke(capSetId));

                    // anchor (0, 0.5) -> pivot (0, 0.5) means pos.x is the rect's
                    // LEFT edge, not its centre. Earlier layouts had preview at
                    // pos (95, ...) size (180, ...) which placed the right edge
                    // at x=275 — overrunning the text column at x=195. With
                    // pos.x=20 size 140, the preview's right edge sits at x=160
                    // and the text column starts at 195 — 35 px clear gap.
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
                    priceLabel.text = $"{set.BundlePrice} gold (set, 20% off)";

                    MkText("Sub", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -65f), new Vector2(665f, 40f),
                        FS_BODY, TextAnchor.MiddleLeft, new Color(0.35f, 0.22f, 0.10f))
                        .text = $"tap to view {set.pieces.Length} pieces";

                    _shopSetCards.Add((set.id, card, priceLabel, cardBtn));
                    y -= CARD_H_SET + CARD_GAP;
                }

                // Skin cards (full-body, single sprite, Buy/Apply/Remove toggle).
                foreach (var skin in sectionSkins)
                {
                    var card = MkSpritePanel("SkinCard_" + skin.id, contentRT,
                        new Vector2(0.5f, 1f), new Vector2(0f, y - CARD_H_SKIN / 2f),
                        new Vector2(CARD_W, CARD_H_SKIN),
                        "panel", new Color(0.92f, 0.84f, 0.62f, 1f));
                    card.GetComponent<Image>().raycastTarget = false;

                    MkSpriteIcon("Preview", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(65f, 0f), new Vector2(95f, 95f),
                        Make.Skin(skin.id), Color.white);

                    MkText("Name", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(140f, 18f), new Vector2(420f, 45f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                        .text = skin.displayName;
                    var stateLabel = MkText("State", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(140f, -25f), new Vector2(420f, 35f),
                        FS_BODY, TextAnchor.MiddleLeft, new Color(0.40f, 0.25f, 0.10f));

                    string capSkinId = skin.id;
                    var actionGO = MkButton("Action_" + skin.id, card.transform,
                        new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(150f, 75f),
                        "Buy", () => _onSkinAction?.Invoke(capSkinId), "btn_grey", "btn_grey_down");
                    _shopSkinCards.Add((skin.id, card, stateLabel,
                        actionGO.GetComponentInChildren<Text>(),
                        actionGO.GetComponent<Button>()));
                    y -= CARD_H_SKIN + CARD_GAP;
                }

                y -= SECTION_GAP;
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

            _setDetailTitle = MkText("Title", _setDetailPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -80f), new Vector2(800f, 80f),
                FS_TITLE, TextAnchor.UpperCenter, AccentGold);
            _setDetailCoins = MkText("Coins", _setDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -90f), new Vector2(400f, 60f),
                FS_BIG, TextAnchor.UpperRight, AccentGold);

            // Header preview frame — big square showing the full-gear bake.
            var previewFrame = MkSpritePanel("PreviewFrame", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 540f), new Vector2(420f, 420f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            previewFrame.GetComponent<Image>().raycastTarget = false;
            _setDetailPreview = MkSpriteIcon("Preview", previewFrame.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 380f),
                (Sprite)null, Color.white).GetComponent<Image>();

            // Per-set row groups, one Rows GameObject per set; visibility flips
            // in UpdateSetDetail. Each row: icon + name + per-piece price + Buy.
            foreach (var set in GamexGame.SetCatalog)
            {
                var rowsRoot = MkPanel("Rows_" + set.id, _setDetailPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
                    new Vector2(900f, 700f), new Color(0f, 0f, 0f, 0f));
                rowsRoot.GetComponent<Image>().raycastTarget = false;
                rowsRoot.SetActive(false);
                _setDetailRowsRoot[set.id] = rowsRoot;

                for (int i = 0; i < set.pieces.Length; i++)
                {
                    var p = set.pieces[i];
                    float y = 130f - i * 130f;
                    var row = MkSpritePanel("Row_" + p.id, rowsRoot.transform,
                        new Vector2(0.5f, 0.5f), new Vector2(0f, y),
                        new Vector2(880f, 115f), "panel", new Color(0.95f, 0.86f, 0.66f, 1f));
                    row.GetComponent<Image>().raycastTarget = false;

                    // Icon on the left.
                    MkSpriteIcon("Icon", row.transform, new Vector2(0f, 0.5f),
                        new Vector2(70f, 0f), new Vector2(95f, 95f),
                        Make.EquipmentIcon(p.id), Color.white);

                    // Name + price stacked in the middle.
                    MkText("Name", row.transform, new Vector2(0f, 0.5f),
                        new Vector2(150f, 20f), new Vector2(500f, 50f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                        .text = p.name;
                    var priceText = MkText("Price", row.transform, new Vector2(0f, 0.5f),
                        new Vector2(150f, -30f), new Vector2(500f, 40f),
                        FS_BODY, TextAnchor.MiddleLeft, new Color(0.40f, 0.25f, 0.10f));
                    priceText.text = $"{p.price} gold";

                    // Buy button on the right — flips to "Owned" once purchased.
                    var btn = MkButton("Buy_" + p.id, row.transform,
                        new Vector2(1f, 0.5f), new Vector2(-90f, 0f),
                        new Vector2(150f, 80f), "Buy",
                        () => { if (!IsOwned(p.id)) _onBuy?.Invoke(p.id); }, "btn_grey", "btn_grey_down");
                    _setDetailPieceUI[p.id] = (btn.GetComponentInChildren<Text>(), btn.GetComponent<Button>());
                }
            }

            // Bundle CTA at bottom of detail page.
            _setDetailBundleLabel = MkText("BundleLabel", _setDetailPanel.transform,
                new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(900f, 50f),
                FS_LABEL, TextAnchor.MiddleCenter, AccentGold);

            var bundleGO = MkButton("BundleBuy", _setDetailPanel.transform,
                new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(640f, 110f),
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
            MkText("Title", _inventoryPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Inventory";

            // Paper-doll — large avatar inside a framed panel, anchored to upper third.
            var dollFrame = MkSpritePanel("DollFrame", _inventoryPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 440f), new Vector2(500f, 600f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var dollInner = MkSpritePanel("DollInner", dollFrame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440f, 540f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            dollInner.GetComponent<Image>().raycastTarget = false;
            _inventoryAvatar = BuildAvatar(dollInner.transform, Vector2.zero, 1.6f,
                Gender.Male, Curse.Weakness, stage: 0);

            // Pet slot — same convention as the Home mirror.
            _inventoryPet = MkSpriteIcon("Pet", dollInner.transform, new Vector2(1f, 0f),
                new Vector2(-20f, 40f), new Vector2(80f, 80f),
                (Sprite)null, Color.white).GetComponent<Image>();
            _inventoryPet.raycastTarget = false;
            _inventoryPet.gameObject.SetActive(false);

            // Six slot icons in a row underneath the paper-doll. Order matches
            // GamexGame.AllSlots: Head, Chest, Wrists, Weapon, Legs, Feet.
            // Tap an occupied slot -> unequip.
            string[] slotShortLabels = { "Head", "Chest", "Wrists", "Weapon", "Legs", "Feet" };
            for (int i = 0; i < 6; i++)
            {
                float x = -325f + i * 130f;
                var slotBg = MkSpritePanel("Slot_" + slotShortLabels[i], _inventoryPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(x, 90f), new Vector2(110f, 110f),
                    "panel_light", new Color(0.22f, 0.24f, 0.32f, 1f));
                _invSlotBgs[i] = slotBg.GetComponent<Image>();
                // Slot-name label — dimmed text shown when empty, hidden when item present.
                _invSlotLabels[i] = MkText("Label", slotBg.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(100f, 100f), FS_BODY, TextAnchor.MiddleCenter, TextDim);
                _invSlotLabels[i].text = slotShortLabels[i];
                // Item-occupant icon (sits centered, hidden until equipped).
                var iconGO = MkSpriteIcon("Icon", slotBg.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(100f, 100f), (Sprite)null, Color.white);
                _invSlotIcons[i] = iconGO.GetComponent<Image>();
                iconGO.SetActive(false);
                // Tap to unequip whatever's in this slot — looked up at click time from
                // _invSlotEquippedIds, which UpdateInventory keeps in sync with state.equipped.
                int capIdx = i;
                var btn = slotBg.AddComponent<Button>();
                btn.targetGraphic = _invSlotBgs[i];
                btn.transition    = Selectable.Transition.ColorTint;
                var cb = btn.colors;
                cb.highlightedColor = new Color(0.32f, 0.34f, 0.44f, 1f);
                btn.colors = cb;
                btn.onClick.AddListener(() =>
                {
                    var equipped = _invSlotEquippedIds[capIdx];
                    if (equipped != null) _onToggleEquip?.Invoke(equipped);
                });
            }

            // "Storage" header
            _invInventoryHeader = MkText("StorageHdr", _inventoryPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -10f),
                new Vector2(800f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            _invInventoryHeader.text = "Storage  ·  tap to equip";

            // Bottom storage grid — 4 columns, up to 4 rows, one cell per item.
            // Catalog items first (sword/armor tiers), then knight pieces. All cells
            // pre-built; UpdateInventory shows/hides based on owned set.
            var allIds = new List<string>();
            foreach (var p in GamexGame.KnightSet) allIds.Add(p.id);
            foreach (var set in GamexGame.SetCatalog)
                foreach (var p in set.pieces) allIds.Add(p.id);

            const int COLS = 4, CELL = 130, GAP = 14;
            float gridLeft = -(COLS * CELL + (COLS - 1) * GAP) / 2f + CELL / 2f;
            float gridTop  = -100f;   // top edge of the grid
            for (int i = 0; i < allIds.Count; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                float x = gridLeft + col * (CELL + GAP);
                float y = gridTop  - row * (CELL + GAP);

                var cell = MkSpritePanel("Cell_" + allIds[i], _inventoryPanel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(CELL, CELL),
                    "panel_light", new Color(0.22f, 0.24f, 0.32f, 1f));
                var icon = MkSpriteIcon("Icon", cell.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(CELL - 16f, CELL - 16f), Make.EquipmentIcon(allIds[i]), Color.white)
                    .GetComponent<Image>();
                // Small "✓" badge in the corner for currently-equipped items.
                var badge = MkText("Eq", cell.transform, new Vector2(1f, 1f),
                    new Vector2(-10f, -10f), new Vector2(40f, 40f),
                    FS_BODY, TextAnchor.UpperRight, AccentGold);
                badge.text = "✓";
                var badgeGo = badge.gameObject;
                badgeGo.SetActive(false);

                string capId = allIds[i];
                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = cell.GetComponent<Image>();
                btn.transition    = Selectable.Transition.ColorTint;
                var cb = btn.colors;
                cb.highlightedColor = new Color(0.32f, 0.34f, 0.44f, 1f);
                btn.colors = cb;
                btn.onClick.AddListener(() => _onToggleEquip?.Invoke(capId));

                _invItems.Add((capId, cell, icon, badgeGo));
            }

            MkButton("Back", _inventoryPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        bool IsOwned(string id) => _ownedSnapshot.Contains(id);

        // ============================================================
        // Refresh
        // ============================================================
        public void Refresh(GamexGame g)
        {
            // Phase entry — reset per-phase animation state.
            if (g.phase != _lastPhase)
            {
                if (g.phase == AppPhase.CurseAnim)         _curseAnimT = 0f;
                if (g.phase == AppPhase.RaceTransformAnim) _raceAnimT  = 0f;
                _lastPhase = g.phase;
            }

            Set(_openingIntroPanel,       g.phase == AppPhase.OpeningIntro);
            Set(_openingHeroShownPanel,   g.phase == AppPhase.OpeningHeroShown);
            Set(_openingCurseLoomsPanel,  g.phase == AppPhase.OpeningCurseLooms);
            Set(_curseAnimPanel,          g.phase == AppPhase.CurseAnim);
            Set(_openingAmnesiaPanel,     g.phase == AppPhase.OpeningAmnesia);
            Set(_cursePanel,              g.phase == AppPhase.CurseSelect);
            Set(_firstMirrorPanel,        g.phase == AppPhase.FirstMirror);
            Set(_homePanel,               g.phase == AppPhase.Home);
            Set(_trainPanel,              g.phase == AppPhase.Quests);
            Set(_shopPanel,               g.phase == AppPhase.Shop);
            Set(_setDetailPanel,          g.phase == AppPhase.SetDetail);
            Set(_inventoryPanel,          g.phase == AppPhase.Inventory);
            Set(_raceSelectPanel,         g.phase == AppPhase.RaceSelect);
            Set(_raceAnimPanel,           g.phase == AppPhase.RaceTransformAnim);

            if (g.phase == AppPhase.RaceTransformAnim)
            {
                _raceAnimT += Time.unscaledDeltaTime;
                bool swapped = _raceAnimT >= RACE_ANIM_SWAP_AT;
                if (_raceAnimSilhouette != null) _raceAnimSilhouette.gameObject.SetActive(!swapped);
                if (_raceAnimAvatar != null)
                {
                    _raceAnimAvatar.root.SetActive(swapped);
                    if (swapped)
                        ApplyAvatarLook(_raceAnimAvatar,
                            (Gender)g.state.gender, (Curse)g.state.curse,
                            (Race)g.state.race, g.Stage, g.state.equipped);
                }
                if (_raceAnimT >= RACE_ANIM_DURATION) _onRaceAnimDone?.Invoke();
            }

            var gender = (Gender)g.state.gender;
            var curse  = (Curse)g.state.curse;
            var safeGender = gender == Gender.Unset ? Gender.Male : gender;

            // Curse animation: shake hero + dim background, swap sprite at SWAP_AT,
            // auto-advance when duration elapses.
            if (g.phase == AppPhase.CurseAnim)
            {
                _curseAnimT += Time.unscaledDeltaTime;
                bool swapped = _curseAnimT >= CURSE_ANIM_SWAP_AT;

                // shake amplitude: ramp up 2 -> 14 px to swap, then decay 14 -> 0 px after
                float shakeAmp = swapped
                    ? Mathf.Lerp(14f, 0f, (_curseAnimT - CURSE_ANIM_SWAP_AT) / (CURSE_ANIM_DURATION - CURSE_ANIM_SWAP_AT))
                    : Mathf.Lerp(2f, 14f, _curseAnimT / CURSE_ANIM_SWAP_AT);
                float sx = Mathf.Round(UnityEngine.Random.Range(-shakeAmp, shakeAmp));
                float sy = Mathf.Round(UnityEngine.Random.Range(-shakeAmp, shakeAmp));
                ((RectTransform)_curseAnimAvatar.root.transform).anchoredPosition = new Vector2(sx, sy);

                // dim overlay alpha 0 -> 0.7
                var dc = _curseAnimDim.color;
                dc.a = Mathf.Lerp(0f, 0.7f, _curseAnimT / CURSE_ANIM_DURATION);
                _curseAnimDim.color = dc;

                // sprite: hero (stage 5) before swap, cursed (stage 0 of chosen curse) after
                ApplyAvatarLook(_curseAnimAvatar, safeGender,
                                swapped ? (curse == Curse.Unset ? Curse.Weakness : curse) : Curse.Unset,
                                Race.Unset,
                                swapped ? 0 : 5);

                if (_curseAnimT >= CURSE_ANIM_DURATION) _onCurseAnimDone?.Invoke();
            }

            // (Curse-select avatars used to switch by gender; M3a/M3b made the
            // curse preview gender-neutral so no per-frame work is needed.)

            // First mirror reveals the cursed self (not the lost hero).
            if (_firstMirrorSelf != null)
                ApplyAvatarLook(_firstMirrorSelf, safeGender,
                                curse == Curse.Unset ? Curse.Weakness : curse, Race.Unset, stage: 0);

            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop)
            {
                _homeLevel.text   = $"Lv {g.state.level}";
                _homeCoins.text   = $"{g.state.coins} Gold";
                _homeStreak.text  = $"{g.state.streakDays}-day streak";
                _homeProgress.text = $"Today {g.state.todaySteps} steps";
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    700f * Mathf.Clamp01((float)g.XpInCurrentLevel / Mathf.Max(1, g.XpToNextLevel)), 28f);

                // Mirror is the player at the current stage. race == Unset -> skeleton growth,
                // race != Unset -> race form (post Lv 20 transformation).
                ApplyAvatarLook(_mirrorSelf, safeGender, curse, (Race)g.state.race, g.Stage,
                                g.state.equipped, g.state.activeSkin);
                _mirrorSelf.SetAlpha(1f);

                // Stage / level transition detection (Home only). First Refresh after Home
                // appears initialises the tracker without firing a fake milestone.
                if (g.phase == AppPhase.Home)
                {
                    int currentStage = g.Stage;
                    if (_prevStage < 0)
                    {
                        _prevStage = currentStage;
                        _prevLevel = g.state.level;
                    }
                    else
                    {
                        if (currentStage > _prevStage)
                        {
                            _stageUpT = STAGEUP_DURATION;
                            int idx = Mathf.Clamp(currentStage - 1, 0, MILESTONE_LINES.Length - 1);
                            _milestoneText.text = MILESTONE_LINES[idx];
                            _milestoneT = MILESTONE_DURATION;
                        }
                        // hitting max level for the first time fires the 6th line, even
                        // though Stage doesn't change (Lv 26-30 are all stage 5).
                        // Lv 30 milestone fires once when first hit (no level cap now).
                        if (_prevLevel < 30 && g.state.level >= 30)
                        {
                            _stageUpT = STAGEUP_DURATION;
                            _milestoneText.text = MILESTONE_LINES[5];
                            _milestoneT = MILESTONE_DURATION;
                        }
                        _prevStage = currentStage;
                        _prevLevel = g.state.level;
                    }
                }

                // Stage-up flash (scale pulse + white overlay) supersedes breathing.
                if (_stageUpT > 0f)
                {
                    _stageUpT -= Time.unscaledDeltaTime;
                    float t = 1f - Mathf.Clamp01(_stageUpT / STAGEUP_DURATION);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    float scale = 1f + STAGEUP_SCALE_AMP * pulse;
                    _mirrorSelf.root.transform.localScale = new Vector3(scale, scale, 1f);
                    var fc = _stageUpFlash.color;
                    fc.a = STAGEUP_FLASH_A * pulse;
                    _stageUpFlash.color = fc;
                    if (_stageUpT <= 0f) _stageUpFlash.color = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    // Idle breathing — gentle sin-wave scale modulation.
                    float breath = 1f + BREATH_AMP * Mathf.Sin(Time.time * BREATH_FREQ);
                    _mirrorSelf.root.transform.localScale = new Vector3(breath, breath, 1f);
                }

                // Milestone text — visible for MILESTONE_DURATION, fades out over last second.
                if (_milestoneT > 0f)
                {
                    _milestoneT -= Time.unscaledDeltaTime;
                    var tc = _milestoneText.color;
                    tc.a = Mathf.Clamp01(_milestoneT / MILESTONE_FADE_OUT);
                    _milestoneText.color = tc;
                }

                // Daily ritual: candles light + crown turns gold and drops when maintenance met.
                // Ritual = today's daily quest progress. Crown + candles light up once any
                // step-quest has been completed today; M5b will tie this to specific quests.
                bool ritualDone = g.state.todaySteps >= 1000;
                var candleSprite = Make.UI(ritualDone ? "candle_lit" : "candle_unlit");
                foreach (var c in _candleImgs) if (c != null) c.sprite = candleSprite;
                _crownImg.sprite = Make.UI(ritualDone ? "crown_gold" : "crown_grey");
                ((RectTransform)_crownImg.transform).anchoredPosition = new Vector2(0f,
                    ritualDone ? CROWN_Y_LANDED : CROWN_Y_FLOATING);
            }

            if (g.phase == AppPhase.Quests)
            {
                UpdateQuests(g);
            }

            if (g.phase == AppPhase.Shop)
            {
                _shopCoins.text = $"{g.state.coins} Gold";
                _ownedSnapshot.Clear();
                foreach (var id in g.state.owned) _ownedSnapshot.Add(id);

                // Per-card price/owned labels.
                foreach (var card in _shopSetCards)
                {
                    var set = GamexGame.FindSet(card.setId);
                    if (set == null) continue;
                    bool fullyOwned = true;
                    foreach (var p in set.pieces)
                        if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                    if (fullyOwned)
                        card.priceLabel.text = "✓ Complete set owned";
                    else
                        card.priceLabel.text = $"{set.BundlePrice} gold (set, 20% off)";
                }

                // Phase 4b: per-skin Buy / Apply / Remove + locked state.
                foreach (var card in _shopSkinCards)
                {
                    var skin = GamexGame.FindSkin(card.skinId);
                    if (skin == null) continue;
                    bool owned = g.IsSkinOwned(skin.id);
                    bool active = g.IsSkinActive(skin.id);
                    if (!owned)
                    {
                        card.stateLabel.text = $"{skin.price} gold";
                        card.actionLabel.text = "Buy";
                        card.actionBtn.interactable = g.state.coins >= skin.price;
                    }
                    else if (!active)
                    {
                        card.stateLabel.text = "Owned";
                        card.actionLabel.text = "Apply";
                        card.actionBtn.interactable = true;
                    }
                    else
                    {
                        card.stateLabel.text = "Active";
                        card.actionLabel.text = "Remove";
                        card.actionBtn.interactable = true;
                    }
                }
            }

            if (g.phase == AppPhase.SetDetail) UpdateSetDetail(g);
            if (g.phase == AppPhase.Inventory) UpdateInventory(g);

            // Phase 5e3 — animate the active skin. Runs every Refresh; cheap
            // when no animated skin is applied (early return on frameCount<=1).
            TickActiveSkinAnimation(g);
            TickActivePet(g);
        }

        // Pet companion — small sprite drawn beside the avatar. Same per-frame
        // sprite swap as TickActiveSkinAnimation, just driven off the separate
        // state.activePet slot so a character skin + a pet can coexist.
        void TickActivePet(GamexGame g)
        {
            string activePet = g.state.activePet;
            if (activePet != _petAnimLast)
            {
                _petAnimLast   = activePet;
                _petAnimFrame  = 0;
                _petAnimTimer  = 0f;
            }
            Image petImg = null;
            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop || g.phase == AppPhase.SetDetail)
                petImg = _homePet;
            else if (g.phase == AppPhase.Inventory)
                petImg = _inventoryPet;
            if (petImg == null) return;
            if (string.IsNullOrEmpty(activePet))
            {
                if (petImg.gameObject.activeSelf) petImg.gameObject.SetActive(false);
                return;
            }
            var pet = GamexGame.FindSkin(activePet);
            if (pet == null) return;
            if (!petImg.gameObject.activeSelf) petImg.gameObject.SetActive(true);
            if (pet.frameCount > 1)
            {
                _petAnimTimer += Time.unscaledDeltaTime;
                float perFrame = pet.frameSeconds > 0f ? pet.frameSeconds : 0.12f;
                while (_petAnimTimer >= perFrame)
                {
                    _petAnimTimer -= perFrame;
                    _petAnimFrame  = (_petAnimFrame + 1) % pet.frameCount;
                }
                var spr = Resources.Load<Sprite>($"Skins/{activePet}_{_petAnimFrame:D2}");
                if (spr != null) petImg.sprite = spr;
            }
            else
            {
                var spr = Resources.Load<Sprite>($"Skins/{activePet}");
                if (spr != null) petImg.sprite = spr;
            }
        }

        // Advances the active-skin frame timer and swaps the avatar's portrait
        // sprite to the next frame when the per-frame interval elapses. Frame
        // state resets whenever the player applies a different skin (or none).
        void TickActiveSkinAnimation(GamexGame g)
        {
            string activeSkin = g.state.activeSkin;
            if (activeSkin != _animLastSkin)
            {
                _animLastSkin = activeSkin;
                _animFrame    = 0;
                _animTimer    = 0f;
            }
            var skin = GamexGame.FindSkin(activeSkin);
            if (skin == null || skin.frameCount <= 1) return;

            _animTimer += Time.unscaledDeltaTime;
            float perFrame = skin.frameSeconds > 0f ? skin.frameSeconds : 0.12f;
            while (_animTimer >= perFrame)
            {
                _animTimer -= perFrame;
                _animFrame  = (_animFrame + 1) % skin.frameCount;
            }
            // Pick whichever avatar is on screen this phase.
            AvatarSprite active = null;
            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop || g.phase == AppPhase.SetDetail)
                active = _mirrorSelf;
            else if (g.phase == AppPhase.Inventory)
                active = _inventoryAvatar;
            if (active == null || active.portrait == null) return;
            var spr = Resources.Load<Sprite>($"Skins/{activeSkin}_{_animFrame:D2}");
            if (spr != null) active.portrait.sprite = spr;
        }

        // ============================================================
        // Set detail refresh — flip per-set row groups to match
        // g.activeSetId, refresh price/owned labels + bundle CTA state.
        // ============================================================
        void UpdateSetDetail(GamexGame g)
        {
            _currentSetId = g.activeSetId;
            _ownedSnapshot.Clear();
            foreach (var id in g.state.owned) _ownedSnapshot.Add(id);
            _setDetailCoins.text = $"{g.state.coins} Gold";

            // Toggle per-set rows: only the active set's rows visible.
            foreach (var kv in _setDetailRowsRoot)
                if (kv.Value.activeSelf != (kv.Key == _currentSetId))
                    kv.Value.SetActive(kv.Key == _currentSetId);

            var set = GamexGame.FindSet(_currentSetId);
            if (set == null) return;

            _setDetailTitle.text  = set.displayName;
            _setDetailPreview.sprite = Make.SetPreview(set.id);

            bool fullyOwned = true;
            foreach (var p in set.pieces)
            {
                bool owned = g.IsOwned(p.id);
                if (!owned) fullyOwned = false;
                if (_setDetailPieceUI.TryGetValue(p.id, out var ui))
                {
                    if (owned)            { ui.label.text = "Owned"; ui.btn.interactable = false; }
                    else if (g.state.coins < p.price) { ui.label.text = $"{p.price}g"; ui.btn.interactable = false; }
                    else                  { ui.label.text = "Buy";   ui.btn.interactable = true;  }
                }
            }

            int price = set.BundlePrice;
            if (fullyOwned)
            {
                _setDetailBundleLabel.text = "Complete set owned";
                _setDetailBundleBtn.interactable = false;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = "Already Owned";
            }
            else if (g.state.coins < price)
            {
                _setDetailBundleLabel.text = $"Buy whole set: {price} gold (20% off — you need more gold)";
                _setDetailBundleBtn.interactable = false;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = $"Buy Set ({price}g)";
            }
            else
            {
                _setDetailBundleLabel.text = $"Buy whole set: {price} gold (20% off)";
                _setDetailBundleBtn.interactable = true;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = $"Buy Set ({price}g)";
            }
        }

        // ============================================================
        // Inventory panel refresh — repaints the paper-doll avatar, the
        // 6 slot icons, and the storage grid (own / equipped state).
        // ============================================================
        void UpdateInventory(GamexGame g)
        {
            var gender = (Gender)g.state.gender;
            var safeGender = gender == Gender.Unset ? Gender.Male : gender;
            var curse  = (Curse)g.state.curse;
            var race   = (Race)g.state.race;
            ApplyAvatarLook(_inventoryAvatar, safeGender, curse, race, g.Stage, g.state.equipped, g.state.activeSkin);
            _inventoryAvatar.SetAlpha(1f);

            // Paper-doll slot icons — one per AllSlots entry. Show the equipped
            // item's sprite if any, otherwise the slot's name label.
            for (int i = 0; i < GamexGame.AllSlots.Length; i++)
            {
                var slot = GamexGame.AllSlots[i];
                string equippedId = g.EquippedInSlot(slot);
                _invSlotEquippedIds[i] = equippedId;

                if (equippedId != null)
                {
                    var spr = Make.EquipmentIcon(equippedId);
                    if (spr != null)
                    {
                        _invSlotIcons[i].sprite = spr;
                        if (!_invSlotIcons[i].gameObject.activeSelf)
                            _invSlotIcons[i].gameObject.SetActive(true);
                        if (_invSlotLabels[i].gameObject.activeSelf)
                            _invSlotLabels[i].gameObject.SetActive(false);
                    }
                }
                else
                {
                    if (_invSlotIcons[i].gameObject.activeSelf)
                        _invSlotIcons[i].gameObject.SetActive(false);
                    if (!_invSlotLabels[i].gameObject.activeSelf)
                        _invSlotLabels[i].gameObject.SetActive(true);
                }
            }

            // Storage grid — hide cells for items the player doesn't own; show + mark
            // equipped state for the rest.
            foreach (var item in _invItems)
            {
                bool owned    = g.IsOwned(item.id);
                bool equipped = g.IsEquipped(item.id);
                if (item.root.activeSelf != owned) item.root.SetActive(owned);
                if (item.equippedBadge.activeSelf != equipped) item.equippedBadge.SetActive(equipped);
            }
        }

        static void Set(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active) go.SetActive(active);
        }

        // ============================================================
        // Quests panel — per-row checkmark + total counters refresh.
        // ============================================================
        void UpdateQuests(GamexGame g)
        {
            for (int i = 0; i < QUEST_SPEC.Length; i++)
            {
                var spec = QUEST_SPEC[i];
                int progress = spec.runMinutes ? g.state.todayRunSeconds / 60 : g.state.todaySteps;
                bool done = g.state.questDone != null
                            && spec.quest < (Quest)g.state.questDone.Length
                            && g.state.questDone[(int)spec.quest];

                if (_questRowLabels[i] != null)
                {
                    string status = done
                        ? "✓ done"
                        : (spec.runMinutes
                            ? $"{progress} / {spec.goal} min"
                            : $"{progress} / {spec.goal} steps");
                    _questRowLabels[i].text = $"{spec.label}\n{status}   +1 Gold";
                }
                if (_questCheckmarks[i] != null) _questCheckmarks[i].gameObject.SetActive(done);
            }

            if (_questsTotalSteps != null) _questsTotalSteps.text = $"Total steps: {g.state.totalSteps:N0}";
            if (_questsTotalRun != null)
            {
                int totalMin = (int)(g.state.totalRunSeconds / 60);
                _questsTotalRun.text = $"Total running: {totalMin / 60}h {totalMin % 60}m";
            }
            if (_questsStreak != null) _questsStreak.text = $"{g.state.streakDays}-day streak";

            // Knight Set chain row: only visible once Lv 20 is reached. Shows the
            // next piece + day progress, or a celebratory line once all 5 are earned.
            if (_questsKnightRow != null)
            {
                bool show = g.state.level >= GamexGame.KNIGHT_CHAIN_UNLOCK_LEVEL;
                _questsKnightRow.SetActive(show);
                if (show && _questsKnight != null)
                {
                    if (g.state.knightChainStage >= GamexGame.KnightSet.Length)
                    {
                        _questsKnight.text = "Knight Set complete ✓";
                    }
                    else
                    {
                        var piece = GamexGame.KnightSet[g.state.knightChainStage];
                        int days = g.state.knightChainProgress;
                        int needed = GamexGame.KNIGHT_CHAIN_DAYS;
                        _questsKnight.text = $"Knight Set — Next: {piece.name}\n{days}/{needed} days (5k+ steps each)";
                    }
                }
            }
        }

        // ============================================================
        // Avatar — LPC portrait sprite + equipment overlays (M5e)
        // ============================================================
        public class AvatarSprite
        {
            public GameObject root;
            public Image portrait;
            // 6 equipment slots, all stacked on top of the portrait in the same rect.
            // The sprites themselves are 256x256 with content only in the right
            // anatomical region, so layering "just works".
            public Image sword;
            public Image armor;
            public Image helmet;
            public Image leggings;
            public Image gauntlets;
            public Image boots;

            public void SetAlpha(float a)
            {
                Set(portrait, a);
                Set(sword, a); Set(armor, a); Set(helmet, a);
                Set(leggings, a); Set(gauntlets, a); Set(boots, a);
            }
            static void Set(Image img, float a)
            {
                if (img == null) return;
                var c = img.color; img.color = new Color(c.r, c.g, c.b, a);
            }
        }

        AvatarSprite BuildAvatar(Transform parent, Vector2 anchoredPos, float scale,
                                 Gender gender, Curse curse, int stage, Race race = Race.Unset)
        {
            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var rrt = root.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.anchoredPosition = anchoredPos;
            rrt.sizeDelta = new Vector2(256f, 256f) * scale;
            rrt.localScale = Vector3.one;

            Image MkLayer(string name, bool hiddenInitially)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var img = go.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                if (hiddenInitially) go.SetActive(false);   // wholly disabled until SetOverlay activates
                return img;
            }

            var avatar = new AvatarSprite { root = root };
            avatar.portrait  = MkLayer("Portrait",  false);
            avatar.armor     = MkLayer("Armor",     true);   // torso layer (over body)
            avatar.leggings  = MkLayer("Leggings",  true);
            avatar.boots     = MkLayer("Boots",     true);
            avatar.gauntlets = MkLayer("Gauntlets", true);
            avatar.helmet    = MkLayer("Helmet",    true);
            avatar.sword     = MkLayer("Sword",     true);

            ApplyAvatarLook(avatar, gender, curse, race, stage);
            return avatar;
        }

        void ApplyAvatarLook(AvatarSprite avatar, Gender gender, Curse curse, Race race, int stage,
                             List<string> equipped = null, string activeSkin = null)
        {
            if (avatar == null || avatar.portrait == null) return;
            avatar.portrait.sprite = Make.Portrait(
                gender == Gender.Unset ? Gender.Male : gender, curse, race, stage, activeSkin);
            float a = avatar.portrait.color.a;
            avatar.portrait.color = new Color(1f, 1f, 1f, a);
            // Skins are full-body art — their weapons/armor are painted in,
            // so applying equipment overlays on top would clash. Skip the
            // overlay routing entirely when a skin is active.
            bool skinActive = !string.IsNullOrEmpty(activeSkin) && Make.Skin(activeSkin) != null;
            if (skinActive) equipped = null;

            // Equipment overlays: only on race-form characters (Lv 20+). Pre-race
            // skeleton / zombie bodies don't wear armour — and the overlay sprites
            // are positioned for the race-form silhouette anyway. Slot routing
            // is centralised in GamexGame.SlotOf so the Inventory paper-doll
            // and the avatar overlay agree on where each item lives.
            string swordId = null, armorId = null, helmetId = null,
                   leggingsId = null, gauntletsId = null, bootsId = null;
            if (race != Race.Unset && equipped != null)
            {
                foreach (var id in equipped)
                {
                    switch (GamexGame.SlotOf(id))
                    {
                        case GamexGame.EquipSlot.Weapon: swordId     = id; break;
                        case GamexGame.EquipSlot.Chest:  armorId     = id; break;
                        case GamexGame.EquipSlot.Head:   helmetId    = id; break;
                        case GamexGame.EquipSlot.Legs:   leggingsId  = id; break;
                        case GamexGame.EquipSlot.Wrists: gauntletsId = id; break;
                        case GamexGame.EquipSlot.Feet:   bootsId     = id; break;
                    }
                }
            }
            SetOverlay(avatar.sword,     swordId,     a);
            SetOverlay(avatar.armor,     armorId,     a);
            SetOverlay(avatar.helmet,    helmetId,    a);
            SetOverlay(avatar.leggings,  leggingsId,  a);
            SetOverlay(avatar.gauntlets, gauntletsId, a);
            SetOverlay(avatar.boots,     bootsId,     a);
        }

        static void SetOverlay(Image ov, string id, float alpha)
        {
            if (ov == null) return;
            if (id == null)
            {
                if (ov.gameObject.activeSelf) ov.gameObject.SetActive(false);
                return;
            }
            var spr = Make.Equipment(id);
            if (spr == null)
            {
                // Asset not imported yet — keep the overlay disabled so we don't
                // get the default white-quad render.
                if (ov.gameObject.activeSelf) ov.gameObject.SetActive(false);
                return;
            }
            if (!ov.gameObject.activeSelf) ov.gameObject.SetActive(true);
            ov.sprite = spr;
            ov.color  = new Color(1f, 1f, 1f, alpha);
        }

        // ============================================================
        // UI factories
        // ============================================================
        static Text MkText(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                           int fontSize, TextAnchor align, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Make.Font();
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return t;
        }

        static GameObject MkFullPanel(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        static GameObject MkPanel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go;
        }

        static GameObject MkSpritePanel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                        string spriteName, Color tint)
        {
            var go = MkPanel(name, parent, anchor, pos, size, tint);
            var img = go.GetComponent<Image>();
            var spr = Make.UI(spriteName);
            if (spr != null)
            {
                img.sprite = spr;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            return go;
        }

        // Same as MkSpritePanel but renders the sprite as-is (Image.Type.Simple) —
        // for icons and props where 9-slicing would warp the artwork.
        static GameObject MkSpriteIcon(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                       string spriteName, Color tint)
            => MkSpriteIcon(name, parent, anchor, pos, size, Make.UI(spriteName), tint);

        static GameObject MkSpriteIcon(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                       Sprite sprite, Color tint)
        {
            var go = MkPanel(name, parent, anchor, pos, size, tint);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            return go;
        }

        static GameObject MkButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                   string label, Action onClick,
                                   string spriteName = "btn_brown", string pressedSpriteName = "btn_brown_down")
        {
            var go = MkSpritePanel(name, parent, anchor, pos, size, spriteName, Color.white);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.transition = Selectable.Transition.SpriteSwap;
            var ss = btn.spriteState;
            ss.pressedSprite = Make.UI(pressedSpriteName);
            ss.highlightedSprite = Make.UI(spriteName);
            btn.spriteState = ss;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var t = MkText("Label", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                size, FS_BTN, TextAnchor.MiddleCenter, new Color(0.20f, 0.12f, 0.05f));
            t.text = label;
            return go;
        }
    }
}
