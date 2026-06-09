using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Gamex.Core;
using Gamex.Platform;

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
        GameObject _titlePanel;
        // Title screen polish — fade-in for the wordmark + tagline, gentle
        // pulse on the Start Game CTA, independent candle flicker, and a
        // fade-out transition when the player taps Start Game (only AFTER
        // the fade does the actual phase change fire). _titleT counts up
        // from 0 on every Title entry. Stored Image / Transform / Button
        // refs avoid GameObject.Find in Refresh.
        Text _titleWordmark, _titleTagline;
        Image _titleCrown;
        Text  _titleStartLabel;     // "Tap to Start" label — alpha breathes via UpdateTitle
        Image _titleCrownHalo;   // Kenney particle-pack glow_round behind the crown; breathes via UpdateTitle
        Image _titleDivider;     // Kenney fantasy-ui-borders divider_ornate under the tagline
        Image[] _titleCandles = new Image[4];
        Transform _titleStartBtn;
        Button _titleStartButton;
        CanvasGroup _titleCanvasGroup;
        float _titleT;
        bool  _titleExiting;
        float _titleExitT;
        const float TITLE_FADEIN_DURATION   = 0.6f;
        const float TITLE_TAGLINE_DELAY     = 0.3f;
        const float TITLE_TAGLINE_DURATION  = 0.4f;
        const float TITLE_PULSE_PERIOD      = 1.8f;
        const float TITLE_PULSE_AMP         = 0.04f;
        const float TITLE_EXIT_DURATION     = 0.4f;
        const float CANDLE_FLICKER_ALPHA_MIN = 0.82f;   // never drops to fully transparent — candles stay readable
        GameObject _openingIntroPanel, _openingHeroShownPanel, _openingCurseLoomsPanel, _openingAmnesiaPanel;
        GameObject _curseAnimPanel;
        GameObject _raceSelectPanel, _raceAnimPanel;
        GameObject _cursePanel, _firstMirrorPanel, _homePanel, _trainPanel, _shopPanel;
        GameObject _inventoryPanel;
        GameObject _settingsPanel;
        Text _settingsSfxLabel, _settingsBgmLabel, _settingsHKLabel, _settingsResetLabel;
        // Two-tap confirm timer for Reset Progress. First tap arms the
        // button (label flips to "Tap again to confirm" + red tint) and
        // sets _resetArmedUntil; second tap within the window actually
        // wipes the save. Stored as unscaled time so it survives pause.
        float _resetArmedUntil;
        const float RESET_CONFIRM_WINDOW = 3f;
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

        // ---- daily ritual feedback ----
        readonly float[] _candleFlashT = new float[4];   // > 0 -> candle scale-pop after lighting
        float _crownLandT;                                // > 0 -> crown scale-pop on transition to fully gold
        int _prevCandlesLit = -1;                         // -1 = uninit (first Refresh just records)
        const int   STEPS_PER_CANDLE       = 1250;        // 4 candles, 5k step goal
        const float CANDLE_FLASH_DURATION  = 0.55f;
        const float CANDLE_FLASH_SCALE_AMP = 0.50f;
        const float CROWN_LAND_DURATION    = 0.80f;
        const float CROWN_LAND_SCALE_AMP   = 0.35f;
        // crown y when floating (maintenance not yet met) vs landed on the head (met)
        const float CROWN_Y_FLOATING = 660f;
        const float CROWN_Y_LANDED   = 540f;

        // ---- mirror polish (breathing + stage-up flash + milestone dialogue) ----
        Image _stageUpFlash;          // white overlay inside the mirror, lerps up + back
        Text  _milestoneText;         // 4s line below the mirror after a stage transition
        Text  _levelUpToast;          // "LEVEL UP!" pop on regular level-ups (non-stage)
        int   _prevStage  = -1;       // -1 = uninitialised, set on first Home Refresh
        int   _prevLevel  = 1;
        // Sound triggers — uninit sentinel = -1 so the first Refresh after a
        // save load doesn't fire a fake "coin earned" / "level up".
        long  _prevCoins  = -1;
        bool[] _prevQuestDone;
        float _stageUpT;              // > 0 while flash + scale-pulse is playing
        float _milestoneT;            // > 0 while milestone line is visible
        float _levelUpT;              // > 0 while LEVEL UP toast + Lv-label pulse is playing

        const float BREATH_AMP        = 0.015f;
        const float BREATH_FREQ       = 1.8f;
        const float STAGEUP_DURATION  = 0.6f;
        const float STAGEUP_SCALE_AMP = 0.08f;
        const float STAGEUP_FLASH_A   = 0.5f;
        const float MILESTONE_DURATION = 4f;
        const float MILESTONE_FADE_OUT = 1f;
        const float LEVELUP_DURATION  = 1.2f;
        const float LEVELUP_PULSE_AMP = 0.22f;

        // ---- coin earn floater (Home + Shop + SetDetail) ----
        Text  _homeCoinFloater, _shopCoinFloater, _setDetailCoinFloater;
        float _coinFloatT;
        long  _coinFloatAmount;   // accumulated +N during an ongoing burst
        const float COIN_FLOAT_DURATION = 1.5f;
        // Home counter sits at y=-60, Shop/SetDetail counters at y=-90.
        // Floater rises ~80px into its respective counter from below.
        const float COIN_FLOAT_HOME_START_Y  = -150f;
        const float COIN_FLOAT_HOME_END_Y    = -70f;
        const float COIN_FLOAT_SHOP_START_Y  = -180f;
        const float COIN_FLOAT_SHOP_END_Y    = -100f;

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

        // ---- first-run tutorial coach-marks ----
        GameObject _tutorialOverlay;
        Image _tutorialDimTop, _tutorialDimBottom, _tutorialDimLeft, _tutorialDimRight;
        Image _tutorialCaptionBg;          // solid panel under caption + Next so it occludes dim Home text
        Text  _tutorialCaption;
        Text  _tutorialNextLabel;
        int   _tutorialStep = -1;          // -1 = not active; 0..N-1 = current step
        // Each step: target rect (canvas-center coords) + caption position + caption text.
        // Last step's button reads "Got it" instead of "Next".
        struct TutorialStep
        {
            public Vector2 targetCenter;
            public Vector2 targetSize;
            public Vector2 captionCenter;
            public string  caption;
        }
        // Target centers / sizes match the actual Home rects:
        //   Mirror frame: anchor (0.5,0.5), pos (0,260), size (520,660) -> y[-70, 590]
        //   Quests btn:   anchor (0.5,0),  pos (0,280), size (800,160) -> y[-680, -520], center y = -600
        //   Shop btn:     anchor (0.5,0),  pos (0,120), size (420,100) -> y[-840, -740], center y = -790
        // Targets are padded ~20px each side so the spotlight reads as a
        // generous outline rather than hugging the element tightly.
        static readonly TutorialStep[] TUTORIAL_STEPS = new[]
        {
            new TutorialStep { targetCenter = new Vector2(0f,  260f), targetSize = new Vector2(560f, 700f),
                               captionCenter = new Vector2(0f, -260f),
                               caption = "Tap your reflection\nto dress up." },
            new TutorialStep { targetCenter = new Vector2(0f, -600f), targetSize = new Vector2(840f, 200f),
                               captionCenter = new Vector2(0f, -280f),
                               caption = "Complete daily quests\nto earn coins." },
            new TutorialStep { targetCenter = new Vector2(0f, -790f), targetSize = new Vector2(480f, 140f),
                               captionCenter = new Vector2(0f, -500f),
                               caption = "Spend coins on\nnew outfits." },
        };

        // ---- home refs ----
        Text _homeLevel, _homeCoins, _homeProgress, _homeStreak, _homeNextHint;
        // Coin sprite references — repositioned every Refresh to track the
        // gold number's left edge so "0" and "9500" both sit flush against
        // the digits with the same gap.
        Image _homeCoinIcon, _shopCoinIcon, _setDetailCoinIcon;
        Image _xpBar;
        // XP bar animation — _xpDisplayPct lags the actual XP fraction and
        // eases toward it; on level-up, the bar fills the rest of the way to
        // 100%, holds briefly, then drops to 0% before resuming the chase.
        float _xpDisplayPct = -1f;            // -1 = uninit (first Refresh snaps)
        int   _xpPrevLevel  = -1;
        enum XpAnim { Normal, LevelUpFill, LevelUpHold, LevelUpDrop }
        XpAnim _xpAnimState = XpAnim.Normal;
        float  _xpAnimT;
        float  _xpAnimStartPct;
        const float XP_SMOOTH_SPEED      = 7f;   // higher = snappier convergence in Normal
        const float XP_LEVELUP_FILL_DUR  = 0.30f;
        const float XP_LEVELUP_HOLD_DUR  = 0.10f;
        const float XP_LEVELUP_DROP_DUR  = 0.20f;

        // ---- quests refs (M5b/d) ----
        Text _questsStreak, _questsTotalSteps, _questsTotalRun, _questsKnight;
        GameObject _questsKnightRow, _questsKnightBarBg;
        Image _questsKnightBarFill;
        readonly Image[] _questCheckmarks = new Image[(int)Quest.Count];
        readonly Text[]  _questRowLabels  = new Text[(int)Quest.Count];
        readonly float[] _questPopT       = new float[(int)Quest.Count]; // > 0 -> trophy scale-pop
        const float TROPHY_POP_DURATION  = 0.45f;
        const float TROPHY_POP_AMP       = 0.45f;   // peak overshoot above 1.0x
        // Coin + reward chip (per quest) — hidden once the quest is done so
        // the trophy can take its spot without overlapping.
        readonly GameObject[] _questChipRoots = new GameObject[(int)Quest.Count];

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
        // Outfit grid (post-pivot). One cell per owned outfit, regardless of
        // source — Champion sets (when fully bought), Legend / Cyberpunk skins
        // owned, and the Knight Set once the chain quest awards all 6 pieces.
        // Tap an outfit -> apply (champion pieces hit state.equipped + clear
        // activeSkin; skins set activeSkin + clear equipped). Active cell
        // shows an "Active" badge so the player sees what's on right now.
        const int INV_GRID_CAPACITY = 24;
        readonly GameObject[] _invGridRoots  = new GameObject[INV_GRID_CAPACITY];
        readonly Image[]      _invGridIcons  = new Image[INV_GRID_CAPACITY];
        readonly GameObject[] _invGridBadges = new GameObject[INV_GRID_CAPACITY];
        readonly string[]     _invGridIds    = new string[INV_GRID_CAPACITY];
        Text _invInventoryHeader;
        // Phase 5 polish 6 — owned skin / pet rows below the storage grid.
        // Cells are pre-built (capacity SK_ROW_CAPACITY each) and re-populated
        // from state.ownedSkins / state.ownedPets in UpdateInventory.
        const int SK_ROW_CAPACITY = 4;
        readonly Image[]    _invSkinIcons   = new Image[SK_ROW_CAPACITY];
        readonly GameObject[] _invSkinRoots = new GameObject[SK_ROW_CAPACITY];
        readonly GameObject[] _invSkinActiveMarks = new GameObject[SK_ROW_CAPACITY];
        readonly string[] _invSkinIds = new string[SK_ROW_CAPACITY];
        readonly Image[]    _invPetIcons    = new Image[SK_ROW_CAPACITY];
        readonly GameObject[] _invPetRoots  = new GameObject[SK_ROW_CAPACITY];
        readonly GameObject[] _invPetActiveMarks = new GameObject[SK_ROW_CAPACITY];
        readonly string[] _invPetIds = new string[SK_ROW_CAPACITY];

        // ---- callbacks ----
        readonly Action         _onTapAdvanceOpening;
        readonly Action         _onCurseAnimDone;
        readonly Action<int>    _onSelectCurse;
        readonly Action<int,int> _onSelectRaceAndGender;
        readonly Action         _onRaceAnimDone;
        readonly Action         _onFinishFirstMirror;
        readonly Action         _onGoQuests, _onGoShop, _onGoHome, _onGoInventory, _onFakeRep;
        readonly Action<string> _onBuy, _onToggleEquip, _onGoSetDetail, _onBuySet, _onSkinAction, _onApplyOutfit;
        readonly Action         _onGoSettings, _onToggleSfx, _onToggleBgm, _onResetProgress;
        readonly Action         _onLeaveTitle;

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
            Action<string> onSkinAction,
            Action<string> onApplyOutfit,
            Action onGoSettings,
            Action onToggleSfx,
            Action onToggleBgm,
            Action onResetProgress,
            Action onLeaveTitle)
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
            _onApplyOutfit         = onApplyOutfit;
            _onGoSettings          = onGoSettings;
            _onToggleSfx           = onToggleSfx;
            _onToggleBgm           = onToggleBgm;
            _onResetProgress       = onResetProgress;
            _onLeaveTitle          = onLeaveTitle;

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
            BuildTitle(root);
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
            BuildSettings(root);
            BuildTutorialOverlay(root);
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
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            MkText("Hint", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
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
            var wordmarkAmber = new Color(0.92f, 0.74f, 0.42f, 1f);
            _titleWordmark = MkText("AppTitle", _titlePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -220f), new Vector2(1000f, 140f), FS_BIG, TextAnchor.MiddleCenter, wordmarkAmber);
            _titleWordmark.text = "Gamexercise";
            // Outline #1 — wider bottom-right offset for primary stroke
            var wordmarkOutlineA = _titleWordmark.gameObject.AddComponent<Outline>();
            wordmarkOutlineA.effectColor    = new Color(0f, 0f, 0f, 0.95f);
            wordmarkOutlineA.effectDistance = new Vector2(4f, -4f);
            // Outline #2 — top-left offset; two combined make the letter
            // shape ~2px thicker in every direction, reading as bold.
            var wordmarkOutlineB = _titleWordmark.gameObject.AddComponent<Outline>();
            wordmarkOutlineB.effectColor    = new Color(0f, 0f, 0f, 0.9f);
            wordmarkOutlineB.effectDistance = new Vector2(-4f, 4f);

            // Tagline gets a more muted warm-stone tone (matches the dim
            // ambient amber on the side walls of the bg) and a slightly
            // smaller body font, pushed below the now-larger wordmark.
            var taglineStone = new Color(0.78f, 0.70f, 0.55f, 1f);
            _titleTagline = MkText("Tagline", _titlePanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -320f), new Vector2(1000f, 60f), FS_BODY, TextAnchor.MiddleCenter, taglineStone);
            _titleTagline.text = "Walk. Train. Reign.";
            var taglineOutline = _titleTagline.gameObject.AddComponent<Outline>();
            taglineOutline.effectColor    = new Color(0f, 0f, 0f, 0.85f);
            taglineOutline.effectDistance = new Vector2(2f, -2f);

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
            _titleStartLabel = startBtn.transform.Find("Label")?.GetComponent<Text>();
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

            MkSpriteIcon("HeroSilhouette", _openingHeroShownPanel.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 640f),
                "silhouette", Color.white);

            MkText("Hint", _openingHeroShownPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.LowerCenter, TextDim).text = "(tap to continue)";
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
            // Coin LEFT of the number with a 10px gap. Number rect occupies
            // x=300..500 (right edge 40 left of panel edge); coin rect at
            // x=210..290 sits to its left with the gap. Coin pos.y nudged
            // down 8px to compensate for Cubic 11's top-heavy glyph metrics
            // — without it the coin reads as floating above the digits even
            // though the rect centres match mathematically.
            _homeCoinIcon = MkSpriteIcon("CoinIcon", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -54f),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _homeCoins = MkText("Coins", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -60f),
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
                new Vector2(0f, -360f), new Vector2(900f, 40f),
                FS_BODY, TextAnchor.MiddleCenter, TextDim);

            // bottom buttons — Quests opens the daily-task list, Shop is cosmetics
            MkButton("Quests", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 280f),
                new Vector2(800f, 160f), "Quests", () => _onGoQuests?.Invoke());
            MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");

            // Settings — small button at the top-right corner, below the coin
            // counter. Intentionally low-key so it doesn't draw attention away
            // from the mirror, but reachable for audio mute / HealthKit re-link
            // / reset progress without burying it behind a long-press.
            MkButton("SettingsBtn", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-160f, -180f),
                new Vector2(280f, 70f), "Settings", () => _onGoSettings?.Invoke(), "btn_grey", "btn_grey_down");
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

            MkText("Title", _trainPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Daily Quests";

            // In-between sizing: rows 150 tall (vs original 130 / first-pass
            // 180) + FS_TITLE label (vs original FS_LABEL 33 / first-pass
            // FS_BIG 77). Trophy icon for completion stays.
            const float rowH = 150f, rowGap = 18f, rowW = 950f;
            float startY = 620f;
            for (int i = 0; i < QUEST_SPEC.Length; i++)
            {
                float y = startY - i * (rowH + rowGap);
                var row = MkSpritePanel("Q_" + i, _trainPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(rowW, rowH), "panel", new Color(0.95f, 0.86f, 0.66f, 1f));

                _questRowLabels[i] = MkText("Label", row.transform, new Vector2(0f, 0.5f),
                    new Vector2(50f, 0f), new Vector2(rowW - 240f, rowH - 20f),
                    FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));

                // Coin + "+N" wrapped so the whole chip can toggle off when
                // the quest completes (trophy takes the same right slot).
                var chip = MkPanel("Chip", row.transform, new Vector2(1f, 0.5f),
                    new Vector2(-120f, 0f), new Vector2(160f, 70f),
                    new Color(0f, 0f, 0f, 0f));
                chip.GetComponent<Image>().raycastTarget = false;
                MkSpriteIcon("Coin", chip.transform, new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(60f, 60f),
                    "coin", Color.white);
                int reward = QUEST_SPEC[i].reward;
                MkText("Reward", chip.transform, new Vector2(0f, 0.5f),
                    new Vector2(70f, 0f), new Vector2(90f, 60f),
                    FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                    .text = $"+{reward}";
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

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Shop";
            _shopCoinIcon = MkSpriteIcon("CoinIcon", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -84f),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _shopCoins = MkText("Coins", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -90f),
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
                topInset: 200f, bottomInset: 250f);

            float y = -30f;   // cursor in content-space (top is y=0, growing negative downward)
            // Phase 5 polish 4 — skin cards bumped to the same 280px height
            // as set cards so Legends + Cyberpunk + Pets render at the same
            // visual weight as Champions. Internal layout mirrors set cards:
            // 140 preview on the left, big name + state/price on the right.
            const float CARD_W = 880f, CARD_H_SET = 280f, CARD_H_SKIN = 280f, CARD_GAP = 24f;
            // Jackson's read: "Champions" header sat too far above its first
            // card, "Legends" header sat too close under the last Champion
            // card. Tightened header->card and widened section->section to
            // emphasise the boundary at section ends.
            const float SECTION_GAP = 130f, HEADER_GAP = 35f;

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
                    var setBounce = card.AddComponent<PressBounce>();
                    cardBtn.onClick.AddListener(() => { Sfx.Play("tap"); setBounce.Trigger(); _onGoSetDetail?.Invoke(capSetId); });

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
                // Same overall shape as set cards now — preview on the left,
                // name + state stacked in the middle, action button on the right.
                foreach (var skin in sectionSkins)
                {
                    var card = MkSpritePanel("SkinCard_" + skin.id, contentRT,
                        new Vector2(0.5f, 1f), new Vector2(0f, y - CARD_H_SKIN / 2f),
                        new Vector2(CARD_W, CARD_H_SKIN),
                        "panel", new Color(0.92f, 0.84f, 0.62f, 1f));
                    card.GetComponent<Image>().raycastTarget = false;

                    MkSpriteIcon("Preview", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(20f, 0f), new Vector2(140f, 140f),
                        Make.Skin(skin.id), Color.white);

                    MkText("Name", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, 55f), new Vector2(490f, 70f),
                        FS_TITLE, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f))
                        .text = skin.displayName;
                    var stateLabel = MkText("State", card.transform, new Vector2(0f, 0.5f),
                        new Vector2(195f, -10f), new Vector2(490f, 50f),
                        FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));

                    string capSkinId = skin.id;
                    var actionGO = MkButton("Action_" + skin.id, card.transform,
                        new Vector2(1f, 0.5f), new Vector2(-50f, 0f), new Vector2(220f, 110f),
                        "Buy", () => _onSkinAction?.Invoke(capSkinId), "btn_grey", "btn_grey_down");
                    var skinBounce = card.AddComponent<PressBounce>();
                    actionGO.GetComponent<Button>().onClick.AddListener(() => skinBounce.Trigger());
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

            // Title left-aligned, coins right — Jackson caught the previous
            // centred title overlapping the coin counter with long names like
            // "Dark Horned Knight". Left anchor puts the title squarely on
            // the left and gives the coin readout the right half clean.
            _setDetailTitle = MkText("Title", _setDetailPanel.transform, new Vector2(0f, 1f),
                new Vector2(40f, -80f), new Vector2(620f, 80f),
                FS_TITLE, TextAnchor.UpperLeft, AccentGold);
            _setDetailCoinIcon = MkSpriteIcon("CoinIcon", _setDetailPanel.transform, new Vector2(1f, 1f), new Vector2(-250f, -84f),
                new Vector2(80f, 80f), "coin", Color.white).GetComponent<Image>();
            _setDetailCoins = MkText("Coins", _setDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, -90f), new Vector2(200f, 60f),
                FS_BIG, TextAnchor.MiddleRight, AccentGold);
            _setDetailCoinFloater = MkText("CoinFloat", _setDetailPanel.transform, new Vector2(1f, 1f),
                new Vector2(-40f, COIN_FLOAT_SHOP_START_Y), new Vector2(200f, 50f),
                FS_TITLE, TextAnchor.MiddleRight, AccentGold);
            _setDetailCoinFloater.color = new Color(1f, 0.84f, 0.42f, 0f);

            // Header preview frame — big square showing the full-gear bake.
            var previewFrame = MkSpritePanel("PreviewFrame", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 540f), new Vector2(420f, 420f),
                "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            previewFrame.GetComponent<Image>().raycastTarget = false;
            _setDetailPreview = MkSpriteIcon("Preview", previewFrame.transform,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 380f),
                (Sprite)null, Color.white).GetComponent<Image>();

            // Champions are now atomic outfits — one Buy Set CTA, no per-piece
            // purchase rows. Jackson's call after seeing the SetDetail in Play:
            // "Champions 还是成套成套卖吧，这样子太乱了". Set detail page
            // therefore just shows preview + bundle price + Buy Set / Owned.
            _setDetailBundleLabel = MkText("BundleLabel", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(900f, 60f),
                FS_LABEL, TextAnchor.MiddleCenter, AccentGold);

            var bundleGO = MkButton("BundleBuy", _setDetailPanel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(640f, 130f),
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
                    new Vector2(0f, 6f), new Vector2(CELL, 30f),
                    FS_BODY, TextAnchor.LowerCenter, AccentGold);
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

            MkText("Title", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Settings";

            // Section: Audio
            MkText("AudioHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.UpperCenter, AccentGold).text = "Audio";

            _settingsSfxLabel = MkButtonWithLabel("SfxRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(880f, 90f),
                "Sound effects: ON", () => _onToggleSfx?.Invoke());

            _settingsBgmLabel = MkButtonWithLabel("BgmRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -450f), new Vector2(880f, 90f),
                "Music: OFF", () => _onToggleBgm?.Invoke());

            // Section: HealthKit (iOS surfaces status; non-iOS shows "Unavailable" + non-clickable)
            MkText("HKHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -580f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.UpperCenter, AccentGold).text = "HealthKit";

            _settingsHKLabel = MkButtonWithLabel("HKRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -680f), new Vector2(880f, 90f),
                "HealthKit: Not connected (tap to open Settings)", OpenHealthKitSettings);

            // Section: Data
            MkText("DataHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -810f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.UpperCenter, AccentGold).text = "Data";

            _settingsResetLabel = MkButtonWithLabel("ResetRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -910f), new Vector2(880f, 90f),
                "Reset progress", HandleResetTap);

            // Section: Legal — privacy policy URL is mandated by Apple for
            // any app reading HealthKit data and must be reachable both
            // from the App Store listing and from within the app itself.
            MkText("LegalHdr", _settingsPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -1040f),
                new Vector2(800f, 50f), FS_BODY, TextAnchor.UpperCenter, AccentGold).text = "Legal";

            MkButtonWithLabel("PrivacyPolicyRow", _settingsPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0f, -1140f), new Vector2(880f, 90f),
                "Privacy Policy", OpenPrivacyPolicy);

            MkButton("Back", _settingsPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(420f, 100f), "Back", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
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
        Text MkButtonWithLabel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                               string label, Action onClick)
        {
            var go = MkButton(name, parent, anchor, pos, size, label, onClick, "btn_grey", "btn_grey_down");
            // The label Text is added as a child by MkButton; fetch it back so
            // UpdateSettings can mutate the text without re-creating the button.
            var labelT = go.transform.Find("Label")?.GetComponent<Text>();
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

        // iOS apps can't re-trigger the HealthKit permission modal — once the
        // user has answered (or denied), the OS remembers and silently no-ops
        // subsequent requestAuthorization calls. The supported pattern is to
        // deep-link to the app's own iOS Settings page where Health toggles
        // live under "Health" subpage. UIApplication.openSettingsURLString
        // resolves to "app-settings:" which Application.OpenURL can fire on
        // iOS. No-op on other platforms (Settings panel is iOS-meaningful only).
        static void OpenHealthKitSettings()
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
            _tutorialNextLabel = btnGO.GetComponentInChildren<Text>();

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
        static void TickCoinFloater(Text floater, float t01, float alpha, float startY, float endY)
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
                if (g.phase == AppPhase.Title)
                {
                    _titleT       = 0f;
                    _titleExiting = false;
                    _titleExitT   = 0f;
                    if (_titleCanvasGroup != null)  _titleCanvasGroup.alpha = 1f;
                    if (_titleStartButton != null)  _titleStartButton.interactable = true;
                }
                _lastPhase = g.phase;
            }

            // Background music disabled while Jackson sources a replacement
            // for the placeholder bgm_home.wav (uncomfortable to listen to).
            // The Bgm singleton stays wired and Stop() is called every Refresh
            // so any in-progress loop from a prior build is halted on the next
            // tick. When the new clip lands, swap this for:
            //   bool bgmOn = g.phase == AppPhase.Home || ... ;
            //   if (bgmOn) Bgm.PlayLoop("<new_clip_name>"); else Bgm.Stop();
            Bgm.Stop();

            // First-run tutorial. Process the pending-finish flag BEFORE the
            // trigger check — otherwise the trigger fires again on the same
            // Refresh that should be tearing the overlay down.
            if (_pendingTutorialFinish)
            {
                _pendingTutorialFinish = false;
                g.state.tutorialDone = true;
                g.onSave?.Invoke();
            }
            if (g.phase == AppPhase.Home && g.state.firstMirrorDone
                && !g.state.tutorialDone && _tutorialStep < 0
                && _tutorialOverlay != null && !_tutorialOverlay.activeSelf)
            {
                StartTutorial();
            }

            // Passive coin gain (quest reward / streak bonus) — purchases
            // decrease state.coins so any positive delta is always a gain
            // worth jingling. Skip if uninitialised (just-loaded save).
            if (_prevCoins >= 0 && g.state.coins > _prevCoins)
            {
                long delta = g.state.coins - _prevCoins;
                Sfx.Play("coin");
                // Aggregate inside an active float so a multi-quest burst lands
                // as a single "+N" rather than stacking overlapping floaters.
                _coinFloatAmount = (_coinFloatT > 0f) ? _coinFloatAmount + delta : delta;
                _coinFloatT      = COIN_FLOAT_DURATION;
                string txt = "+" + _coinFloatAmount;
                if (_homeCoinFloater      != null) _homeCoinFloater.text      = txt;
                if (_shopCoinFloater      != null) _shopCoinFloater.text      = txt;
                if (_setDetailCoinFloater != null) _setDetailCoinFloater.text = txt;
            }
            _prevCoins = g.state.coins;

            // Coin floater tick — runs every Refresh regardless of phase so the
            // active screen's "+N" animates wherever the player happens to be
            // when coins land. Each panel has its own floater Text; only the
            // active-phase one renders.
            if (_coinFloatT > 0f)
            {
                _coinFloatT -= Time.unscaledDeltaTime;
                float t01 = 1f - Mathf.Clamp01(_coinFloatT / COIN_FLOAT_DURATION);
                float a   = (t01 < 0.5f) ? 1f : Mathf.Clamp01(1f - (t01 - 0.5f) / 0.5f);
                TickCoinFloater(_homeCoinFloater,      t01, a, COIN_FLOAT_HOME_START_Y, COIN_FLOAT_HOME_END_Y);
                TickCoinFloater(_shopCoinFloater,      t01, a, COIN_FLOAT_SHOP_START_Y, COIN_FLOAT_SHOP_END_Y);
                TickCoinFloater(_setDetailCoinFloater, t01, a, COIN_FLOAT_SHOP_START_Y, COIN_FLOAT_SHOP_END_Y);
                if (_coinFloatT <= 0f) _coinFloatAmount = 0;
            }

            // Quest completion — detect any false -> true flip in questDone.
            if (g.state.questDone != null)
            {
                if (_prevQuestDone == null || _prevQuestDone.Length != g.state.questDone.Length)
                    _prevQuestDone = new bool[g.state.questDone.Length];
                for (int i = 0; i < g.state.questDone.Length; i++)
                {
                    if (g.state.questDone[i] && !_prevQuestDone[i])
                    {
                        Sfx.Play("quest_done");
                        if (i < _questPopT.Length) _questPopT[i] = TROPHY_POP_DURATION;
                    }
                    _prevQuestDone[i] = g.state.questDone[i];
                }
            }

            Set(_titlePanel,              g.phase == AppPhase.Title);
            if (g.phase == AppPhase.Title) UpdateTitle();
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
            Set(_settingsPanel,           g.phase == AppPhase.Settings);
            if (g.phase == AppPhase.Settings) UpdateSettings(g);

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
                _homeCoins.text   = $"{g.state.coins}";
                LayoutCoinNextToText(_homeCoinIcon, _homeCoins, marginRight: 40f, coinYOffset: -54f);
                _homeStreak.text  = $"{g.state.streakDays}-day streak";
                bool dailyGoalMet = g.state.todaySteps >= 5000;
                _homeProgress.text  = dailyGoalMet
                    ? $"Today {g.state.todaySteps} steps  ✓"
                    : $"Today {g.state.todaySteps} steps";
                _homeProgress.color = dailyGoalMet ? AccentGold : TextDim;
                float xpTarget = (float)g.XpInCurrentLevel / Mathf.Max(1, g.XpToNextLevel);
                if (_xpDisplayPct < 0f)
                {
                    _xpDisplayPct = xpTarget;   // first Refresh, snap silently
                    _xpPrevLevel  = g.state.level;
                }
                else if (_xpAnimState == XpAnim.Normal && g.state.level > _xpPrevLevel)
                {
                    // Level-up: enter the fill -> hold -> drop sequence so the
                    // bar finishes the lap before chasing the new XP value.
                    _xpAnimStartPct = _xpDisplayPct;
                    _xpAnimState    = XpAnim.LevelUpFill;
                    _xpAnimT        = XP_LEVELUP_FILL_DUR;
                }
                _xpPrevLevel = g.state.level;

                float dt = Time.unscaledDeltaTime;
                switch (_xpAnimState)
                {
                    case XpAnim.Normal:
                        // Exponential ease — fast initial chase, smooth settle.
                        _xpDisplayPct = Mathf.Lerp(_xpDisplayPct, xpTarget,
                            1f - Mathf.Exp(-XP_SMOOTH_SPEED * dt));
                        break;
                    case XpAnim.LevelUpFill:
                        _xpAnimT -= dt;
                        float tf = 1f - Mathf.Clamp01(_xpAnimT / XP_LEVELUP_FILL_DUR);
                        _xpDisplayPct = Mathf.Lerp(_xpAnimStartPct, 1f, tf);
                        if (_xpAnimT <= 0f)
                        {
                            _xpDisplayPct = 1f;
                            _xpAnimState  = XpAnim.LevelUpHold;
                            _xpAnimT      = XP_LEVELUP_HOLD_DUR;
                        }
                        break;
                    case XpAnim.LevelUpHold:
                        _xpAnimT -= dt;
                        _xpDisplayPct = 1f;
                        if (_xpAnimT <= 0f)
                        {
                            _xpAnimState = XpAnim.LevelUpDrop;
                            _xpAnimT     = XP_LEVELUP_DROP_DUR;
                        }
                        break;
                    case XpAnim.LevelUpDrop:
                        _xpAnimT -= dt;
                        float td = 1f - Mathf.Clamp01(_xpAnimT / XP_LEVELUP_DROP_DUR);
                        _xpDisplayPct = Mathf.Lerp(1f, 0f, td);
                        if (_xpAnimT <= 0f)
                        {
                            _xpDisplayPct = 0f;
                            _xpAnimState  = XpAnim.Normal;
                        }
                        break;
                }
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    700f * Mathf.Clamp01(_xpDisplayPct), 28f);

                // Next-milestone hint — guides the player toward the Lv 10
                // form shift, Lv 15 fuller body, Lv 20 race transformation.
                int next = g.state.level < 10 ? 10
                         : g.state.level < 15 ? 15
                         : g.state.level < 20 ? 20
                         : -1;
                _homeNextHint.text = next > 0
                    ? $"Next form change at Lv {next}"
                    : ((Race)g.state.race != Race.Unset ? "" : "Race awakens at Lv 20");

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
                            Sfx.Play("milestone");
                        }
                        else if (g.state.level > _prevLevel)
                        {
                            Sfx.Play("level_up");
                            _levelUpT = LEVELUP_DURATION;
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

                // Level-up celebration — sine-pulse the Lv label scale + fade the
                // "LEVEL UP!" toast in/hold/out over LEVELUP_DURATION. The pulse
                // pivots from the label's top-left (anchor 0,1) so it grows toward
                // the mirror rather than off-screen.
                if (_levelUpT > 0f)
                {
                    _levelUpT -= Time.unscaledDeltaTime;
                    float t01  = 1f - Mathf.Clamp01(_levelUpT / LEVELUP_DURATION);
                    float bell = Mathf.Sin(t01 * Mathf.PI);
                    float s    = 1f + LEVELUP_PULSE_AMP * bell;
                    _homeLevel.transform.localScale = new Vector3(s, s, 1f);

                    // Toast alpha: fade in over first 15%, hold, fade out over last 35%.
                    float a;
                    if      (t01 < 0.15f) a = t01 / 0.15f;
                    else if (t01 > 0.65f) a = Mathf.Clamp01((1f - t01) / 0.35f);
                    else                  a = 1f;
                    var lc = _levelUpToast.color; lc.a = a; _levelUpToast.color = lc;

                    if (_levelUpT <= 0f)
                    {
                        _homeLevel.transform.localScale = Vector3.one;
                        _levelUpToast.color = new Color(1f, 0.84f, 0.42f, 0f);
                    }
                }


                // Daily ritual: 4 candles light up incrementally at 1.25k / 2.5k /
                // 3.75k / 5k steps. The crown switches to gold + bursts when all
                // four are lit. Each newly-lit candle scale-pops and chimes;
                // the 4th additionally fires the milestone sound + crown burst.
                int candlesLit = Mathf.Clamp(g.state.todaySteps / STEPS_PER_CANDLE, 0, 4);
                var litSprite   = Make.UI("candle_lit");
                var unlitSprite = Make.UI("candle_unlit");
                for (int i = 0; i < _candleImgs.Length; i++)
                {
                    if (_candleImgs[i] == null) continue;
                    _candleImgs[i].sprite = (i < candlesLit) ? litSprite : unlitSprite;
                }
                _crownImg.sprite = Make.UI(candlesLit >= 4 ? "crown_gold" : "crown_grey");

                // Transition detection — first Home Refresh just records the
                // baseline, no celebration fires for a save that loaded
                // mid-day with candles already lit. Decreases (day rollover)
                // also update the tracker so the next mid-day light-up fires.
                if (_prevCandlesLit < 0)
                {
                    _prevCandlesLit = candlesLit;
                }
                else if (candlesLit != _prevCandlesLit)
                {
                    if (candlesLit > _prevCandlesLit)
                    {
                        for (int i = _prevCandlesLit; i < candlesLit && i < _candleFlashT.Length; i++)
                        {
                            _candleFlashT[i] = CANDLE_FLASH_DURATION;
                            Sfx.Play("quest_done");
                        }
                        if (_prevCandlesLit < 4 && candlesLit >= 4)
                        {
                            _crownLandT = CROWN_LAND_DURATION;
                            Sfx.Play("milestone");
                        }
                    }
                    _prevCandlesLit = candlesLit;
                }

                // Per-frame ticks for candle pops + crown burst. Sine-bell scale.
                for (int i = 0; i < _candleImgs.Length; i++)
                {
                    if (_candleImgs[i] == null) continue;
                    if (_candleFlashT[i] > 0f)
                    {
                        _candleFlashT[i] -= Time.unscaledDeltaTime;
                        float t01  = 1f - Mathf.Clamp01(_candleFlashT[i] / CANDLE_FLASH_DURATION);
                        float s    = 1f + CANDLE_FLASH_SCALE_AMP * Mathf.Sin(t01 * Mathf.PI);
                        _candleImgs[i].rectTransform.localScale = new Vector3(s, s, 1f);
                        if (_candleFlashT[i] <= 0f)
                            _candleImgs[i].rectTransform.localScale = Vector3.one;
                    }
                }
                if (_crownLandT > 0f)
                {
                    _crownLandT -= Time.unscaledDeltaTime;
                    float t01 = 1f - Mathf.Clamp01(_crownLandT / CROWN_LAND_DURATION);
                    float s   = 1f + CROWN_LAND_SCALE_AMP * Mathf.Sin(t01 * Mathf.PI);
                    _crownImg.rectTransform.localScale = new Vector3(s, s, 1f);
                    if (_crownLandT <= 0f)
                        _crownImg.rectTransform.localScale = Vector3.one;
                }
            }

            if (g.phase == AppPhase.Quests)
            {
                UpdateQuests(g);
            }

            if (g.phase == AppPhase.Shop)
            {
                _shopCoins.text = $"{g.state.coins}";
                LayoutCoinNextToText(_shopCoinIcon, _shopCoins, marginRight: 40f, coinYOffset: -84f);
                _ownedSnapshot.Clear();
                foreach (var id in g.state.owned) _ownedSnapshot.Add(id);

                // Per-card price/owned labels + gold-glow tint when owned.
                // ColorTint button transition multiplies these so the press
                // highlight still lifts on top of the owned tint.
                var setOwnedTint   = new Color(1.00f, 0.92f, 0.55f, 1f);
                var setDefaultTint = new Color(0.95f, 0.86f, 0.66f, 1f);
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
                    var img = card.root.GetComponent<Image>();
                    if (img != null) img.color = fullyOwned ? setOwnedTint : setDefaultTint;
                }

                // Phase 4b: per-skin Buy / Apply / Remove + locked state.
                // Owned = warm gold tint; Active = brightest gold so the
                // currently-worn skin pops at a glance.
                var skinDefaultTint = new Color(0.92f, 0.84f, 0.62f, 1f);
                var skinOwnedTint   = new Color(0.98f, 0.88f, 0.55f, 1f);
                var skinActiveTint  = new Color(1.00f, 0.92f, 0.45f, 1f);
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
                    var img = card.root.GetComponent<Image>();
                    if (img != null)
                        img.color = active ? skinActiveTint
                                    : owned ? skinOwnedTint
                                            : skinDefaultTint;
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
            _setDetailCoins.text = $"{g.state.coins}";
            LayoutCoinNextToText(_setDetailCoinIcon, _setDetailCoins, marginRight: 40f, coinYOffset: -84f);

            var set = GamexGame.FindSet(_currentSetId);
            if (set == null) return;

            _setDetailTitle.text  = set.displayName;
            _setDetailPreview.sprite = Make.SetPreview(set.id);

            bool fullyOwned = true;
            foreach (var p in set.pieces)
                if (!g.IsOwned(p.id)) { fullyOwned = false; break; }

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

            // Walk owned outfits into the grid: Champion sets first (when all
            // 6 pieces are owned -> one cell), then Skins (each owned Skin =
            // one cell). Active outfit gets the "Active" badge.
            int gridIdx = 0;
            foreach (var set in GamexGame.SetCatalog)
            {
                if (gridIdx >= INV_GRID_CAPACITY) break;
                bool fullyOwned = true;
                foreach (var p in set.pieces)
                    if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                if (!fullyOwned) continue;
                _invGridIds[gridIdx] = set.id;
                var spr = Make.SetPreview(set.id);
                if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                bool active = g.IsOutfitActive(set.id);
                if (_invGridBadges[gridIdx].activeSelf != active)
                    _invGridBadges[gridIdx].SetActive(active);
                if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                gridIdx++;
            }
            // Knight Set sits outside SetCatalog (it's a chain-quest reward,
            // not a shop bundle), so the SetCatalog loop above never sees it.
            // Render it as its own cell once the chain has granted all 6
            // pieces — tap routes through ApplyOutfit("knight_silver_set")
            // because FindSet picks up KnightOutfit as a fallback.
            if (gridIdx < INV_GRID_CAPACITY)
            {
                var knight = GamexGame.KnightOutfit;
                bool fullyOwned = true;
                foreach (var p in knight.pieces)
                    if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                if (fullyOwned)
                {
                    _invGridIds[gridIdx] = knight.id;
                    var spr = Make.SetPreview(knight.id);
                    if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                    bool active = g.IsOutfitActive(knight.id);
                    if (_invGridBadges[gridIdx].activeSelf != active)
                        _invGridBadges[gridIdx].SetActive(active);
                    if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                    gridIdx++;
                }
            }
            // Owned skins (Legends + Cyberpunk; pets are hidden for launch).
            foreach (var id in g.state.ownedSkins)
            {
                if (gridIdx >= INV_GRID_CAPACITY) break;
                _invGridIds[gridIdx] = id;
                var spr = Make.Skin(id);
                if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                bool active = g.IsSkinActive(id);
                if (_invGridBadges[gridIdx].activeSelf != active)
                    _invGridBadges[gridIdx].SetActive(active);
                if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                gridIdx++;
            }
            for (int i = gridIdx; i < INV_GRID_CAPACITY; i++)
            {
                _invGridIds[i] = null;
                if (_invGridRoots[i].activeSelf) _invGridRoots[i].SetActive(false);
            }
        }

        // Title screen tick — drives wordmark / tagline fade-in, gentle
        // continuous pulse on the Start Game button, independent candle
        // flicker, and the post-tap fade-out before the actual phase
        // transition. _titleT is reset to 0 on phase entry so each cold
        // start replays the intro from the beginning.
        void UpdateTitle()
        {
            _titleT += Time.unscaledDeltaTime;

            if (_titleWordmark != null)
            {
                float a = Mathf.Clamp01(_titleT / TITLE_FADEIN_DURATION);
                var c = _titleWordmark.color; c.a = a; _titleWordmark.color = c;
            }
            if (_titleTagline != null)
            {
                // Tagline starts a beat after the wordmark so the eye lands
                // on the title first, then the subtitle resolves.
                float a = Mathf.Clamp01((_titleT - TITLE_TAGLINE_DELAY) / TITLE_TAGLINE_DURATION);
                var c = _titleTagline.color; c.a = a; _titleTagline.color = c;
            }
            // Tap-to-Start gentle alpha pulse — breathes between 0.55 and
            // 1.0 over a 2-second cycle. Subtle enough not to dominate the
            // painted scene, but obvious enough to read as "this is the
            // interactive bit." Alpha-only (no scale) since scaling text
            // on a static painted background looks like a flicker bug;
            // alpha breathing is the standard mobile "tap to start" cue.
            if (_titleStartLabel != null)
            {
                float a = 0.775f + 0.225f * Mathf.Sin(_titleT * (Mathf.PI * 2f / 2.0f));
                var col = _titleStartLabel.color;
                col.a = a;
                _titleStartLabel.color = col;
            }

            // Crown halo breathing — slow ~3.4s cycle on the Kenney glow
            // sprite. Alpha rides between 0.28 and 0.42, scale rides ±2%,
            // both phase-offset from the candle flicker so the throne-
            // room "lights" don't pulse in lockstep.
            if (_titleCrownHalo != null)
            {
                float haloT  = _titleT * (Mathf.PI * 2f / 3.4f);
                float haloA  = 0.35f + 0.07f * Mathf.Sin(haloT);
                float haloS  = 1f    + 0.02f * Mathf.Sin(haloT + 0.6f);
                var col = _titleCrownHalo.color; col.a = haloA; _titleCrownHalo.color = col;
                _titleCrownHalo.rectTransform.localScale = new Vector3(haloS, haloS, 1f);
            }

            // Candle flicker — each lamp gets a hardcoded phase + frequency
            // offset so the four flames flicker out of sync. Two coupled
            // sin waves (one for alpha, one for scale) at slightly different
            // frequencies give an irregular, organic-looking flame instead
            // of a metronomic strobe. Alpha floor is 0.82 so the candle
            // shape stays readable even at the trough.
            for (int i = 0; i < _titleCandles.Length; i++)
            {
                var c = _titleCandles[i];
                if (c == null) continue;
                float phase  = i * 1.37f;                                // arbitrary, mutually-prime-ish
                float aFreq  = 2.1f + i * 0.27f;                         // 2.1..2.91 Hz per lamp
                float sFreq  = 1.6f + i * 0.18f;                         // 1.6..2.14 Hz per lamp
                float aMul   = CANDLE_FLICKER_ALPHA_MIN + (1f - CANDLE_FLICKER_ALPHA_MIN)
                               * (0.5f + 0.5f * Mathf.Sin(_titleT * aFreq + phase));
                float sMul   = 1f + 0.025f * Mathf.Sin(_titleT * sFreq + phase * 1.4f);
                var col = c.color; col.a = aMul; c.color = col;
                c.rectTransform.localScale = new Vector3(sMul, sMul, 1f);
            }

            // Exit transition — Start Game's onClick set _titleExiting and
            // disabled the button. Fade the whole panel via CanvasGroup,
            // then fire _onLeaveTitle once the fade completes so the next
            // phase swap (OpeningIntro / Home / RaceSelect) happens after
            // the player sees the title resolve, not snap-cut away.
            if (_titleExiting)
            {
                _titleExitT += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_titleExitT / TITLE_EXIT_DURATION);
                if (_titleCanvasGroup != null) _titleCanvasGroup.alpha = 1f - t;
                if (t >= 1f)
                {
                    _titleExiting = false;   // arm again for any future re-entry (Reset Progress)
                    _onLeaveTitle?.Invoke();
                }
            }
        }

        // Settings panel refresh — repaints the 4 row labels every frame so
        // toggles + HK status + the reset-confirm countdown reflect live
        // state. Cheap (4 Text mutations) so running on every Refresh is fine.
        void UpdateSettings(GamexGame g)
        {
            if (_settingsSfxLabel != null)
                _settingsSfxLabel.text = "Sound effects: " + (g.state.sfxMuted ? "OFF" : "ON");
            if (_settingsBgmLabel != null)
                _settingsBgmLabel.text = "Music: " + (g.state.bgmMuted ? "OFF (clip pending)" : "ON (clip pending)");

            if (_settingsHKLabel != null)
            {
                string hk;
                if (!HealthKitBridge.IsAvailable()) hk = "Not available on this device";
                else switch (HealthKitBridge.CurrentStatus())
                {
                    case HealthKitBridge.AuthStatus.Authorized: hk = "Connected (tap to open iOS Settings)"; break;
                    case HealthKitBridge.AuthStatus.Denied:     hk = "Denied (tap to open iOS Settings)"; break;
                    default:                                    hk = "Not yet asked"; break;
                }
                _settingsHKLabel.text = hk;
            }

            if (_settingsResetLabel != null)
            {
                float remaining = _resetArmedUntil - Time.unscaledTime;
                if (remaining > 0f)
                    _settingsResetLabel.text = $"Tap again to confirm ({Mathf.CeilToInt(remaining)}s)";
                else
                {
                    _resetArmedUntil = 0f;
                    _settingsResetLabel.text = "Reset progress";
                }
            }
        }

        static void Set(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active) go.SetActive(active);
        }

        // Position the coin sprite immediately to the left of the gold number
        // with a 10px gap. Reading text.preferredWidth after the text is set
        // lets the coin track varying digit counts (e.g. "0" vs "9500").
        // anchor (1,1) pivot (1,1) means pos.x is measured leftward from the
        // panel's right edge: coin's right edge sits at
        //    marginRight + textVisualWidth + gap
        // away from the panel right.
        const float COIN_GAP = 10f;
        static void LayoutCoinNextToText(Image coin, Text text, float marginRight, float coinYOffset)
        {
            if (coin == null || text == null) return;
            float textW = text.preferredWidth;
            var rt = coin.rectTransform;
            rt.anchoredPosition = new Vector2(-(marginRight + textW + COIN_GAP), coinYOffset);
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
                    _questRowLabels[i].text = $"{spec.label}\n{status}";
                }
                if (_questCheckmarks[i] != null)
                {
                    _questCheckmarks[i].gameObject.SetActive(done);
                    // Scale-pop: overshoot to 1+AMP at midpoint, settle to 1.0.
                    // Idle trophies stay at 1.0; the pop only runs for the
                    // freshly-completed one.
                    if (_questPopT[i] > 0f)
                    {
                        _questPopT[i] -= Time.unscaledDeltaTime;
                        float t01  = 1f - Mathf.Clamp01(_questPopT[i] / TROPHY_POP_DURATION);
                        float bell = Mathf.Sin(t01 * Mathf.PI);
                        float s    = 1f + TROPHY_POP_AMP * bell;
                        _questCheckmarks[i].rectTransform.localScale = new Vector3(s, s, 1f);
                        if (_questPopT[i] <= 0f)
                            _questCheckmarks[i].rectTransform.localScale = Vector3.one;
                    }
                }
                // Coin chip vs trophy — mutually exclusive on the right side.
                if (_questChipRoots[i] != null && _questChipRoots[i].activeSelf == done)
                    _questChipRoots[i].SetActive(!done);
            }

            if (_questsTotalSteps != null) _questsTotalSteps.text = $"Total steps: {g.state.totalSteps:N0}";
            if (_questsTotalRun != null)
            {
                int totalMin = (int)(g.state.totalRunSeconds / 60);
                _questsTotalRun.text = $"Total running: {totalMin / 60}h {totalMin % 60}m";
            }
            if (_questsStreak != null) _questsStreak.text = $"{g.state.streakDays}-day streak";

            // Knight Set chain row: only visible once Lv 20 is reached. Shows the
            // next piece + day progress, or a celebratory line once all earned.
            // Bar fills 0..1 across the 10-day chain so the player sees grind progress.
            if (_questsKnightRow != null)
            {
                bool show = g.state.level >= GamexGame.KNIGHT_CHAIN_UNLOCK_LEVEL;
                _questsKnightRow.SetActive(show);
                if (show && _questsKnight != null)
                {
                    bool earned = g.state.knightChainStage >= GamexGame.KnightSet.Length;
                    if (earned)
                    {
                        _questsKnight.text = "Knight Set earned ✓";
                    }
                    else
                    {
                        int days = g.state.knightChainProgress;
                        int needed = GamexGame.KNIGHT_CHAIN_DAYS;
                        _questsKnight.text = $"Knight Set — {days}/{needed} days (5k+ steps each)";
                    }
                    if (_questsKnightBarBg != null) _questsKnightBarBg.SetActive(!earned);
                    if (_questsKnightBarFill != null && !earned)
                    {
                        float pct = Mathf.Clamp01((float)g.state.knightChainProgress / GamexGame.KNIGHT_CHAIN_DAYS);
                        _questsKnightBarFill.rectTransform.sizeDelta = new Vector2(840f * pct, 24f);
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

            // Three render paths, in priority order:
            //   1. Skin active — full-body skin sprite, no overlays
            //   2. Outfit fully equipped — full-body set composite, no overlays
            //   3. Race form base — race-form sprite + per-slot equipment overlays
            //
            // (2) is the regression fix for "outfit overlays leak the underlying
            // race form's hair / skin / accessories". Every shop set and the
            // Knight Set has a pre-baked Sets/<id>.png composite, so when the
            // player has a fully matching set on we render that one sprite and
            // skip the overlay routing entirely — same shape as the skin path.
            bool skinActive = !string.IsNullOrEmpty(activeSkin) && Make.Skin(activeSkin) != null;
            // Outfit/skin composite is allowed in ALL phases (incl. curse stages):
            // if the player owns a full Set they can wear it over the cursed
            // body. The race-form-only gate used to live here; it's gone so
            // skeleton/flesh stages can render the outfit composite directly.
            // Partial equipment (1-2 pieces) still falls through to the
            // race-form path below, which gates overlays on race awakening.
            var activeOutfit = !skinActive ? GamexGame.FindActiveOutfit(equipped) : null;
            Sprite outfitSprite = activeOutfit != null ? Make.SetPreview(activeOutfit.id) : null;

            if (skinActive || outfitSprite != null)
            {
                avatar.portrait.sprite = outfitSprite != null ? outfitSprite : Make.Skin(activeSkin);
                float aOverride = avatar.portrait.color.a;
                avatar.portrait.color = new Color(1f, 1f, 1f, aOverride);
                SetOverlay(avatar.sword,     null, aOverride);
                SetOverlay(avatar.armor,     null, aOverride);
                SetOverlay(avatar.helmet,    null, aOverride);
                SetOverlay(avatar.leggings,  null, aOverride);
                SetOverlay(avatar.gauntlets, null, aOverride);
                SetOverlay(avatar.boots,     null, aOverride);
                return;
            }

            // Race-form path — partial / non-set equipment overlaid on the
            // bare race form. The bald variant kicks in when a Head slot is
            // occupied (helmet on a smooth scalp instead of through hair).
            bool helmetEquipped = false;
            if (race != Race.Unset && equipped != null)
            {
                foreach (var id in equipped)
                    if (GamexGame.SlotOf(id) == GamexGame.EquipSlot.Head) { helmetEquipped = true; break; }
            }
            avatar.portrait.sprite = Make.Portrait(
                gender == Gender.Unset ? Gender.Male : gender, curse, race, stage, activeSkin, helmetEquipped);
            float a = avatar.portrait.color.a;
            avatar.portrait.color = new Color(1f, 1f, 1f, a);

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
                                   string spriteName = "btn_brown", string pressedSpriteName = "btn_brown_down",
                                   string sfx = "tap")
        {
            var go = MkSpritePanel(name, parent, anchor, pos, size, spriteName, Color.white);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.transition = Selectable.Transition.SpriteSwap;
            var ss = btn.spriteState;
            ss.pressedSprite = Make.UI(pressedSpriteName);
            ss.highlightedSprite = Make.UI(spriteName);
            btn.spriteState = ss;
            string clickSfx = sfx;
            btn.onClick.AddListener(() => { Sfx.Play(clickSfx); onClick?.Invoke(); });

            var t = MkText("Label", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                size, FS_BTN, TextAnchor.MiddleCenter, new Color(0.20f, 0.12f, 0.05f));
            t.text = label;
            return go;
        }
    }

    // Shop-card press feedback. Attached to each shop card root; Trigger() runs
    // a quick "push down + bounce back" scale tween so taps feel committed.
    // Unscaled time keeps the bounce snappy even if a future menu pauses gameplay.
    public class PressBounce : UnityEngine.MonoBehaviour
    {
        const float DURATION  = 0.22f;
        const float SCALE_AMP = 0.07f;   // peak shrink amount
        const float SHRINK_PCT = 0.30f;  // first 30% of duration shrinks, rest expands back
        float _t;

        public void Trigger() { _t = DURATION; }

        void Update()
        {
            if (_t <= 0f) return;
            _t -= UnityEngine.Time.unscaledDeltaTime;
            float t01 = 1f - UnityEngine.Mathf.Clamp01(_t / DURATION);
            float s = t01 < SHRINK_PCT
                ? UnityEngine.Mathf.Lerp(1f, 1f - SCALE_AMP, t01 / SHRINK_PCT)
                : UnityEngine.Mathf.Lerp(1f - SCALE_AMP, 1f, (t01 - SHRINK_PCT) / (1f - SHRINK_PCT));
            transform.localScale = new UnityEngine.Vector3(s, s, 1f);
            if (_t <= 0f) transform.localScale = UnityEngine.Vector3.one;
        }
    }
}
