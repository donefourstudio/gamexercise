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
    // Single-canvas HUD with six panels (gender / curse / firstMirror / home / training / shop).
    // M2a rebuild: solid-color rectangles replaced with Kenney pixel UI 9-slice (Ancient theme)
    // and LPC character portraits driven by Make.Portrait(gender, race, stage). All text is
    // rendered with Cubic 11 (Chinese pixel font). Refresh() repaints every frame from
    // GamexGame state.
    public partial class Hud
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

        // Vertical inset applied to every top-anchored title + top-bar
        // element (level number, coin counters, scroll-view top) so the
        // iPhone Dynamic Island doesn't cover the top of the UI on
        // iPhone 14 Pro+ devices. Calibrated to 60px in canvas reference
        // units (1080x1920) which lands at ~90px on the iPhone 17 Pro Max
        // display — enough to clear the island with margin. iPhones without
        // a Dynamic Island just see the title sitting slightly further
        // from the top edge, which is harmless.
        const float SAFE_AREA_TOP_INSET = 60f;

        // ---- panels ----
        GameObject _titlePanel;
        // Title screen polish — fade-in for the wordmark + tagline, gentle
        // pulse on the Start Game CTA, independent candle flicker, and a
        // fade-out transition when the player taps Start Game (only AFTER
        // the fade does the actual phase change fire). _titleT counts up
        // from 0 on every Title entry. Stored Image / Transform / Button
        // refs avoid GameObject.Find in Refresh.
        // _titleWordmark + _titleTagline are TMP_Text (signed distance field
        // rendering) so they stay sharp at the 1.447x non-integer canvas scale
        // iPhone Pro Max forces — bitmap UI.Text would land glyph strokes on
        // sub-pixel boundaries and get bilinear-averaged into soft grays.
        // Color tween code below works for both because Graphic.color is the
        // shared base property.
        TMP_Text _titleWordmark, _titleTagline;
        Image _titleCrown;
        TMP_Text _titleStartLabel; // "Tap to Start" label — alpha breathes via UpdateTitle
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
        GameObject _firstMirrorPanel, _homePanel, _trainPanel, _shopPanel;
        GameObject _inventoryPanel;
        GameObject _settingsPanel;
        TMP_Text _settingsSfxLabel, _settingsBgmLabel, _settingsHKLabel, _settingsResetLabel;
        // In-app modal shown by the Settings "Reconnect HealthKit" button.
        // Apple restricted the deep link to "iOS Settings → Privacy &
        // Security → Health → <app>" in iOS 17+, so we display step-by-
        // step nav instructions instead. The Continue button clears the
        // cached HK auth resolution and dismisses the modal; user then
        // navigates iOS Settings manually. Cancel just dismisses.
        GameObject _hkReconnectModal;
        // HealthKit hard-gate panel — shown whenever a real iOS device is
        // missing HK authorization. _hkGateBody describes the state to the
        // player; _hkGateActionLabel switches between "Connect HealthKit"
        // and "Open iOS Settings" depending on HK.CurrentStatus().
        GameObject _hkGatePanel;
        GameObject _hkGateActionBtn;
        TMP_Text _hkGateBody, _hkGateActionLabel;
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
        Image _raceAnimSilhouette;       // shown for the first half of the cinematic
        AvatarSprite _raceAnimAvatar;    // race form, shown for the second half
        TMP_Text _raceAnimText;
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
        TMP_Text _milestoneText;     // 4s line below the mirror after a stage transition
        TMP_Text _levelUpToast;      // "LEVEL UP!" pop on regular level-ups (non-stage)
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
        TMP_Text _homeCoinFloater, _shopCoinFloater, _setDetailCoinFloater;
        float _coinFloatT;
        long  _coinFloatAmount;   // accumulated +N during an ongoing burst
        const float COIN_FLOAT_DURATION = 1.5f;
        // Home counter sits at y=-60, Shop/SetDetail counters at y=-90.
        // Floater rises ~80px into its respective counter from below.
        const float COIN_FLOAT_HOME_START_Y  = -150f - SAFE_AREA_TOP_INSET;
        const float COIN_FLOAT_HOME_END_Y    = -70f  - SAFE_AREA_TOP_INSET;
        const float COIN_FLOAT_SHOP_START_Y  = -180f - SAFE_AREA_TOP_INSET;
        const float COIN_FLOAT_SHOP_END_Y    = -100f - SAFE_AREA_TOP_INSET;

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
        TMP_Text _tutorialCaption;
        TMP_Text _tutorialNextLabel;
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
        //   Quests btn:   anchor (0.5,0),  pos (0,360), size (800,160) -> y[-600, -440], center y = -520
        //   Shop btn:     anchor (0.5,0),  pos (0,200), size (420,100) -> y[-760, -660], center y = -710
        // Targets are padded ~20px each side so the spotlight reads as a
        // generous outline rather than hugging the element tightly.
        static readonly TutorialStep[] TUTORIAL_STEPS = new[]
        {
            new TutorialStep { targetCenter = new Vector2(0f,  260f), targetSize = new Vector2(560f, 700f),
                               captionCenter = new Vector2(0f, -260f),
                               caption = "Tap your reflection\nto dress up." },
            new TutorialStep { targetCenter = new Vector2(0f, -520f), targetSize = new Vector2(840f, 200f),
                               captionCenter = new Vector2(0f, -200f),
                               caption = "Complete daily quests\nto earn coins." },
            new TutorialStep { targetCenter = new Vector2(0f, -710f), targetSize = new Vector2(480f, 140f),
                               captionCenter = new Vector2(0f, -400f),
                               caption = "Spend coins on\nnew outfits." },
        };

        // ---- home refs ----
        TMP_Text _homeLevel, _homeCoins, _homeProgress, _homeStreak, _homeNextHint;
        // Coin sprite references — repositioned every Refresh to track the
        // gold number's left edge so "0" and "9500" both sit flush against
        // the digits with the same gap.
        Image _homeCoinIcon, _shopCoinIcon, _setDetailCoinIcon;
        // Green pixel-art ✓ next to home progress when daily 5k step goal is met.
        // Cubic 11 has no U+2713, so a runtime-generated sprite replaces the glyph.
        Image _homeProgressCheck;
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
        TMP_Text _questsStreak, _questsTotalSteps, _questsTotalRun, _questsKnight;
        GameObject _questsKnightRow, _questsKnightBarBg;
        Image _questsKnightBarFill;
        readonly Image[] _questCheckmarks = new Image[(int)Quest.Count];
        readonly TMP_Text[] _questRowLabels = new TMP_Text[(int)Quest.Count];
        readonly float[] _questPopT       = new float[(int)Quest.Count]; // > 0 -> trophy scale-pop
        const float TROPHY_POP_DURATION  = 0.45f;
        const float TROPHY_POP_AMP       = 0.45f;   // peak overshoot above 1.0x
        // Coin + reward chip (per quest) — hidden once the quest is done so
        // the trophy can take its spot without overlapping.
        readonly GameObject[] _questChipRoots = new GameObject[(int)Quest.Count];

        // ---- first mirror refs ----
        TMP_Text _firstMirrorLine;

        // ---- skin animation (Phase 5e3) ----
        // Tracks the currently-applied skin so frame state resets when the
        // player swaps skins. _animFrame walks 0 .. skin.frameCount-1 over
        // skin.frameSeconds intervals, swapping Image.sprite each step.
        string _animLastSkin;
        int    _animFrame;
        float  _animTimer;
        // Independent frame state for the skin-detail preview — the player
        // may be viewing a different skin in the detail page than the one
        // currently applied to their avatar.
        string _skinDetailAnimLast;
        int    _skinDetailAnimFrame;
        float  _skinDetailAnimTimer;

        // ---- pet rendering (polish round 3) ----
        // Pet sits at the bottom-right of the mirror / paper-doll, hidden
        // until state.activePet is set. Each phase has its own Image so the
        // pet appears wherever the avatar is currently visible.
        Image  _homePet, _inventoryPet;
        string _petAnimLast;
        int    _petAnimFrame;
        float  _petAnimTimer;

        // ---- shop refs ----
        TMP_Text _shopCoins;
        // Per-set card refs — `priceLabel` flips to "Owned" once every piece
        // is in state.owned, `bundleBtn` disables atomically with affordability.
        readonly List<(string setId, GameObject root, TMP_Text priceLabel, Button cardBtn)> _shopSetCards = new();
        // Per-skin card refs (Phase 4b) — `actionLabel`/`actionBtn` flip between
        // Buy / Apply / Remove based on owned + active state.
        readonly List<(string skinId, GameObject root, TMP_Text stateLabel)> _shopSkinCards = new();
        readonly HashSet<string> _ownedSnapshot = new();

        // ---- set detail (Phase 3c) refs ----
        GameObject _setDetailPanel;
        Image _setDetailPreview;
        TMP_Text _setDetailTitle, _setDetailCoins, _setDetailBundleLabel;
        Button _setDetailBundleBtn;
        // ---- skin detail refs (mirror of set detail for Legend / Cyberpunk) ----
        GameObject _skinDetailPanel;
        Image _skinDetailPreview;
        TMP_Text _skinDetailTitle, _skinDetailCoins, _skinDetailStateLabel, _skinDetailActionLabel;
        Button _skinDetailActionBtn;
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
        TMP_Text _invInventoryHeader;
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
        readonly Action<int,int> _onSelectRaceAndGender;
        readonly Action         _onRaceAnimDone;
        readonly Action         _onFinishFirstMirror;
        readonly Action         _onGoQuests, _onGoShop, _onGoHome, _onGoInventory, _onFakeRep;
        readonly Action<string> _onBuy, _onToggleEquip, _onGoSetDetail, _onBuySet, _onSkinAction, _onApplyOutfit, _onGoSkinDetail;
        readonly Action         _onGoSettings, _onToggleSfx, _onToggleBgm, _onResetProgress;
        readonly Action         _onLeaveTitle;
        readonly Action         _onConnectHealthKit;
        // ---- The Desk ----
        readonly Action<int>    _onDeskNav;   // arg = (int)AppPhase; Desk <-> Home nav
        readonly DeskGame       _desk;        // direct core ref — see Hud.Desk.cs header

        public Hud(
            Action onTapAdvanceOpening,
            Action onCurseAnimDone,
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
            Action<string> onGoSkinDetail,
            Action<string> onApplyOutfit,
            Action onGoSettings,
            Action onToggleSfx,
            Action onToggleBgm,
            Action onResetProgress,
            Action onLeaveTitle,
            Action onConnectHealthKit,
            Action<int> onDeskNav,
            DeskGame desk)
        {
            _onTapAdvanceOpening   = onTapAdvanceOpening;
            _onCurseAnimDone       = onCurseAnimDone;
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
            _onGoSkinDetail        = onGoSkinDetail;
            _onApplyOutfit         = onApplyOutfit;
            _onGoSettings          = onGoSettings;
            _onToggleSfx           = onToggleSfx;
            _onToggleBgm           = onToggleBgm;
            _onResetProgress       = onResetProgress;
            _onLeaveTitle          = onLeaveTitle;
            _onConnectHealthKit    = onConnectHealthKit;
            _onDeskNav             = onDeskNav;
            _desk                  = desk;

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
            BuildFirstMirror(root);
            BuildHome(root);
            BuildQuests(root);
            BuildShop(root);
            BuildSetDetail(root);
            BuildSkinDetail(root);
            BuildInventory(root);
            BuildRaceSelect(root);
            BuildRaceTransformAnim(root);
            BuildSettings(root);
            BuildHealthKitGate(root);
            BuildTutorialOverlay(root);
            BuildDesk(root);   // The Desk (Pivot 3) — flag-gated, inert unless AppPhase.Desk
        }

    }
}
