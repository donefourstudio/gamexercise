using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Gamex.Core;
using Gamex.Pose;

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

        // ---- camera preview + pose detection (Training) ----
        RawImage _camPreview;
        WebCamTexture _camTexture;
        Text _camStatus;
        PoseDetector _pose;
        PushupCounter _pushupCounter;
        SitupCounter  _situpCounter;
        SquatCounter  _squatCounter;
        FaceMotionCounter _faceCounter;          // 新主力:基于脸部 Y 震荡的次数统计
        Exercise _currentExercise = Exercise.Pushup;
        readonly GameObject[] _exerciseButtons = new GameObject[3];
        Text _tipText;
        readonly Image[] _poseDots = new Image[PoseDetector.KEYPOINT_COUNT];

        // Adaptive pushup session (model 3-module spec): silhouette overlay,
        // traffic-light tint, calibration FSM, dynamic-threshold counter,
        // procedural audio for all five hook events. Only used when
        // _currentExercise == Pushup.
        AdaptivePushupSystem _adaptive;
        ProcAudio.Clips _audioClips;
        AudioSource _audioSrc;
        Image _silhouetteOverlay;
        Image _trafficLightTint;
        Image _goldFlash;
        Text  _adaptiveStatus;
        float _goldFlashT;
        float _poseT;
        const float POSE_INTERVAL = 0.1f;        // 10 Hz inference
        const float POSE_SCORE_GATE = 0.3f;

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

        // ---- training refs ----
        Text _trainReps, _trainCoinsXp, _trainMaintenance;

        // ---- first mirror refs ----
        Text _firstMirrorLine;

        // ---- shop refs ----
        Transform _shopRowsRoot;
        readonly Dictionary<string, Button> _shopBtns   = new();
        readonly Dictionary<string, Text>   _shopLabels = new();
        Text _shopCoins;
        readonly HashSet<string> _ownedSnapshot = new();

        // ---- callbacks ----
        readonly Action         _onTapAdvanceOpening;
        readonly Action         _onCurseAnimDone;
        readonly Action<int>    _onSelectCurse;
        readonly Action<int,int> _onSelectRaceAndGender;
        readonly Action         _onRaceAnimDone;
        readonly Action         _onFinishFirstMirror;
        readonly Action         _onGoTraining, _onGoShop, _onGoHome, _onFakeRep;
        readonly Action<string> _onBuy, _onToggleEquip;

        public Hud(
            Action onTapAdvanceOpening,
            Action onCurseAnimDone,
            Action<int> onSelectCurse,
            Action<int,int> onSelectRaceAndGender,
            Action onRaceAnimDone,
            Action onFinishFirstMirror,
            Action onGoTraining,
            Action onGoShop,
            Action onGoHome,
            Action onFakeRep,
            Action<string> onBuy,
            Action<string> onToggleEquip)
        {
            _onTapAdvanceOpening   = onTapAdvanceOpening;
            _onCurseAnimDone       = onCurseAnimDone;
            _onSelectCurse         = onSelectCurse;
            _onSelectRaceAndGender = onSelectRaceAndGender;
            _onRaceAnimDone        = onRaceAnimDone;
            _onFinishFirstMirror   = onFinishFirstMirror;
            _onGoTraining          = onGoTraining;
            _onGoShop              = onGoShop;
            _onGoHome              = onGoHome;
            _onFakeRep             = onFakeRep;
            _onBuy                 = onBuy;
            _onToggleEquip         = onToggleEquip;

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
            BuildTraining(root);
            BuildShop(root);
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

            MkRaceCard(_raceSelectPanel.transform, new Vector2(-220f,  220f), "Human", "Male",
                Make.Portrait(Gender.Male, Curse.Unset, Race.Human, 5),
                "The classic hero.",   () => _onSelectRaceAndGender?.Invoke(1, 1));
            MkRaceCard(_raceSelectPanel.transform, new Vector2( 220f,  220f), "Human", "Female",
                Make.Portrait(Gender.Female, Curse.Unset, Race.Human, 5),
                "The classic hero.",   () => _onSelectRaceAndGender?.Invoke(1, 2));
            MkRaceCard(_raceSelectPanel.transform, new Vector2(-220f, -220f), "Orc", "Male",
                Make.Portrait(Gender.Male, Curse.Unset, Race.Orc, 5),
                "Strength and rage.",  () => _onSelectRaceAndGender?.Invoke(2, 1));
            MkRaceCard(_raceSelectPanel.transform, new Vector2( 220f, -220f), "Orc", "Female",
                Make.Portrait(Gender.Female, Curse.Unset, Race.Orc, 5),
                "Strength and rage.",  () => _onSelectRaceAndGender?.Invoke(2, 2));
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
                new Vector2(540f, 130f), "Do a Pushup", () => _onFinishFirstMirror?.Invoke());
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

            // bottom buttons
            MkButton("Train", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 280f),
                new Vector2(800f, 160f), "Start Training", () => _onGoTraining?.Invoke());
            MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "Shop", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Training
        // ============================================================
        void BuildTraining(Transform root)
        {
            _trainPanel = MkFullPanel("TrainPanel", root);

            // Exercise selector row at the top of the screen (above the camera).
            string[] names = { "Pushup", "Situp", "Squat" };
            const float btnW = 220f, gap = 30f;
            float totalW = btnW * 3f + gap * 2f;
            for (int i = 0; i < 3; i++)
            {
                Exercise ex = (Exercise)i;
                float x = -totalW * 0.5f + btnW * 0.5f + i * (btnW + gap);
                _exerciseButtons[i] = MkButton("Ex_" + ex, _trainPanel.transform,
                    new Vector2(0.5f, 1f), new Vector2(x, -60f),
                    new Vector2(btnW, 80f), names[i],
                    () => SelectExercise(ex),
                    "btn_grey", "btn_grey_down");
            }
            UpdateExerciseButtonHighlight();

            // Top half: live camera viewport. Live front-camera feed renders into
            // a RawImage that fills the panel; cartoon face mask sits on top as a
            // privacy overlay.
            var cam = MkSpritePanel("CamViewport", _trainPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -200f), new Vector2(900f, 640f), "panel_light", CamBg);

            var rawGO = new GameObject("CamRaw");
            rawGO.transform.SetParent(cam.transform, false);
            _camPreview = rawGO.AddComponent<RawImage>();
            _camPreview.color = Color.white;
            _camPreview.raycastTarget = false;
            var rrt = _camPreview.rectTransform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(16f, 70f);   // leave room for tip text at the bottom
            rrt.offsetMax = new Vector2(-16f, -60f); // and for status tag at the top

            // Big, prominent status banner across the top of the camera viewport
            // so the user can see live angle + state from across the room.
            _camStatus = MkText("CamTag", cam.transform, new Vector2(0.5f, 1f), new Vector2(0f, -40f),
                new Vector2(880f, 80f), FS_BIG, TextAnchor.UpperCenter, AccentGold);
            _camStatus.text = "Camera off";

            // --- AdaptivePushupSystem visual layer (pushup only) ---------------
            // (1) Full-viewport traffic-light tint — under silhouette, over camera.
            //     Colour driven by AdaptivePushupSystem.BackgroundTintForAlignment.
            _trafficLightTint = MkPanel("TrafficLight", cam.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(900f, 700f), new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            _trafficLightTint.raycastTarget = false;

            // (2) Side-view pushup silhouette overlay — visible during alignment phases.
            _silhouetteOverlay = MkSpriteIcon("Silhouette", cam.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 615f), Make.UI("pushup_silhouette"), Color.white).GetComponent<Image>();
            _silhouetteOverlay.gameObject.SetActive(false);

            // (3) Adaptive status banner — replaces _camStatus content when pushup is active.
            //     Sits below the camera tag.
            _adaptiveStatus = MkText("AdaptiveStatus", cam.transform, new Vector2(0.5f, 0f),
                new Vector2(0f, 90f), new Vector2(880f, 50f), FS_LABEL, TextAnchor.LowerCenter, TextDim);
            _adaptiveStatus.text = "";

            // (4) Full-screen gold flash for rep success — under selector strip.
            _goldFlash = MkPanel("GoldFlash", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(4000f, 4000f), new Color(1f, 0.8f, 0.3f, 0f)).GetComponent<Image>();
            _goldFlash.raycastTarget = false;

            // AudioSource for the five procedural clips.
            _audioSrc = _trainPanel.AddComponent<AudioSource>();
            _audioSrc.playOnAwake = false;
            _audioSrc.spatialBlend = 0f;

            // Per-exercise placement tip at the bottom of the viewport. Swaps when the
            // user changes exercise (SelectExercise). The cartoon face placeholder was
            // removed — the user was confused about whether to face the camera or stand
            // sideways, so the tip text now tells them exactly.
            _tipText = MkText("Tip", cam.transform, new Vector2(0.5f, 0f), new Vector2(0f, 32f),
                new Vector2(860f, 50f), FS_BODY, TextAnchor.LowerCenter, TextDim);
            _tipText.text = TipForExercise(_currentExercise);

            // Center: huge rep counter
            _trainReps = MkText("Reps", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -120f), new Vector2(900f, 220f), FS_HUGE, TextAnchor.MiddleCenter, AccentGold);
            _trainCoinsXp = MkText("CoinsXp", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(900f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _trainMaintenance = MkText("Maint", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -320f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            // Bottom: buttons
            MkButton("FakeRep", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 280f),
                new Vector2(720f, 140f), "Fake a Rep", () => _onFakeRep?.Invoke());
            MkButton("Done", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "Done", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Shop
        // ============================================================
        void BuildShop(Transform root)
        {
            _shopPanel = MkFullPanel("ShopPanel", root);

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "Shop";
            _shopCoins = MkText("Coins", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-50f, -90f),
                new Vector2(400f, 60f), FS_BIG, TextAnchor.UpperRight, AccentGold);

            var listGO = MkPanel("Rows", _shopPanel.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(900f, 1400f), new Color(0f, 0f, 0f, 0f));
            listGO.GetComponent<Image>().raycastTarget = false;
            _shopRowsRoot = listGO.transform;

            var defs = GamexGame.Catalog;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float y = 580f - i * 150f;
                var row = MkSpritePanel("Row_" + def.id, _shopRowsRoot, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(900f, 130f), "panel", new Color(0.95f, 0.86f, 0.66f, 1f));
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = row.GetComponent<Image>();
                btn.transition = Selectable.Transition.ColorTint;
                var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.78f, 1f); btn.colors = cb;
                string capId = def.id;
                btn.onClick.AddListener(() =>
                {
                    if (!IsOwned(capId)) _onBuy?.Invoke(capId);
                    else _onToggleEquip?.Invoke(capId);
                });

                var label = MkText("Label", row.transform, new Vector2(0f, 0.5f), new Vector2(50f, 0f),
                    new Vector2(820f, 110f), FS_LABEL, TextAnchor.MiddleLeft, new Color(0.18f, 0.10f, 0.05f));
                _shopBtns[def.id]   = btn;
                _shopLabels[def.id] = label;
            }

            MkButton("Back", _shopPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
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
            Set(_trainPanel,              g.phase == AppPhase.Training);
            Set(_shopPanel,               g.phase == AppPhase.Shop);
            Set(_raceSelectPanel,         g.phase == AppPhase.RaceSelect);
            Set(_raceAnimPanel,           g.phase == AppPhase.RaceTransformAnim);

            UpdateCamera(g);

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
                            (Race)g.state.race, g.Stage);
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

            if (g.phase == AppPhase.Home || g.phase == AppPhase.Training || g.phase == AppPhase.Shop)
            {
                _homeLevel.text   = $"Lv {g.state.level}";
                _homeCoins.text   = $"{g.state.coins} Gold";
                _homeStreak.text  = $"{g.state.streakDays}-day streak";
                _homeProgress.text = $"Today {g.state.todaySteps} steps";
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    700f * Mathf.Clamp01((float)g.XpInCurrentLevel / Mathf.Max(1, g.XpToNextLevel)), 28f);

                // Mirror is the player at the current stage. race == Unset -> skeleton growth,
                // race != Unset -> race form (post Lv 20 transformation).
                ApplyAvatarLook(_mirrorSelf, safeGender, curse, (Race)g.state.race, g.Stage);
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

            if (g.phase == AppPhase.Training)
            {
                _trainReps.text = g.state.todaySteps.ToString();
                _trainCoinsXp.text = $"{g.state.coins} Gold   ·   Lv {g.state.level}  ({g.XpInCurrentLevel}/{g.XpToNextLevel} XP)";
                _trainMaintenance.text = $"Run today: {g.state.todayRunSeconds / 60} min";
            }

            if (g.phase == AppPhase.Shop)
            {
                _shopCoins.text = $"{g.state.coins} Gold";
                _ownedSnapshot.Clear();
                foreach (var id in g.state.owned) _ownedSnapshot.Add(id);

                foreach (var def in GamexGame.Catalog)
                {
                    if (!_shopLabels.TryGetValue(def.id, out var t)) continue;
                    bool owned = g.IsOwned(def.id);
                    bool equipped = g.IsEquipped(def.id);
                    bool unlocked = g.state.level >= def.minLevel;
                    bool affordable = g.state.coins >= def.price;

                    string status;
                    if (equipped) status = "✓ Equipped (tap to remove)";
                    else if (owned) status = "Owned (tap to equip)";
                    else if (!unlocked) status = $"Requires Lv {def.minLevel}";
                    else if (!affordable) status = $"{def.price} Gold (not enough)";
                    else status = $"{def.price} Gold (tap to buy)";

                    t.text = $"[T{def.tier}] {def.name}    Lv{def.minLevel}\n{status}";
                    _shopBtns[def.id].interactable = equipped || owned || (unlocked && affordable);
                }
            }
        }

        static void Set(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active) go.SetActive(active);
        }

        // ============================================================
        // Camera lifecycle + pose detection — Training only.
        // ============================================================
        void UpdateCamera(GamexGame g)
        {
            bool wantsCam = g.phase == AppPhase.Training;
            if (wantsCam && (_camTexture == null || !_camTexture.isPlaying))
                StartCamera();
            else if (!wantsCam && _camTexture != null && _camTexture.isPlaying)
                StopCamera();

            if (_camPreview != null && _camTexture != null && _camTexture.isPlaying)
            {
                _camPreview.uvRect = new Rect(1f, 0f, -1f, 1f);   // selfie mirror

                // Lazily spin up the pose detector + counters.
                if (_pose == null)         _pose          = new PoseDetector();
                if (_faceCounter == null)  _faceCounter   = new FaceMotionCounter();

                if (_pose.IsReady && _camTexture.didUpdateThisFrame)
                {
                    _poseT += Time.unscaledDeltaTime;
                    if (_poseT >= POSE_INTERVAL)
                    {
                        _poseT = 0f;
                        _pose.Detect(_camTexture);
                        UpdatePoseOverlay();

                        // 全部三个运动都用 FaceMotionCounter 数 reps —— BlazePose 对脸的
                        // 检测远比对身体关节的检测稳定,不管做什么动作脸都会规律上下,
                        // 比追踪肘/胯角度更可靠。
                        bool repDone = _faceCounter.Update(_pose.Keypoints);
                        if (repDone) _onFakeRep?.Invoke();
                        float angle = float.NaN;
                        string stateLabel = _faceCounter.CurrentState.ToString();

                        if (_camStatus != null)
                        {
                            // FaceMotionCounter-aware status banner. Three things matter
                            // to the user: (1) is the face detected, (2) is the system
                            // calibrating, (3) what state are we in + current rep count.
                            if (float.IsNaN(_faceCounter.SmoothedY))
                            {
                                _camStatus.text = "Show your face";
                                _camStatus.color = new Color(1f, 0.55f, 0.45f, 1f);
                            }
                            else if (_faceCounter.CurrentState == FaceMotionCounter.State.Unknown)
                            {
                                _camStatus.text = "Start moving — calibrating...";
                                _camStatus.color = TextDim;
                            }
                            else
                            {
                                bool isHigh = _faceCounter.CurrentState == FaceMotionCounter.State.High;
                                _camStatus.color = isHigh
                                    ? new Color(0.55f, 1f, 0.55f, 1f)
                                    : new Color(1f, 0.70f, 0.35f, 1f);
                                _camStatus.text = $"{_faceCounter.Reps} reps · {(isHigh ? "UP" : "DOWN")}";
                            }
                        }
                    }
                }
            }
            else
            {
                if (_camPreview != null && _camPreview.texture != null) _camPreview.texture = null;
                for (int i = 0; i < _poseDots.Length; i++)
                    if (_poseDots[i] != null) _poseDots[i].gameObject.SetActive(false);
            }
        }

        void UpdatePoseOverlay()
        {
            if (_pose == null || _camPreview == null) return;
            var rect = _camPreview.rectTransform.rect;

            for (int i = 0; i < PoseDetector.KEYPOINT_COUNT; i++)
            {
                if (_poseDots[i] == null)
                {
                    var go = new GameObject("Pose_" + i);
                    go.transform.SetParent(_camPreview.transform, false);
                    var img = go.AddComponent<Image>();
                    img.color = new Color(0.45f, 1f, 0.50f, 0.85f);
                    img.raycastTarget = false;
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);  // bottom-left of preview
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(14f, 14f);
                    _poseDots[i] = img;
                }

                var kp = _pose.Keypoints[i];
                if (kp.score >= POSE_SCORE_GATE)
                {
                    // MoveNet: y down, x right. UI: y up. We also mirrored the feed.
                    float ux = (1f - kp.x) * rect.width;
                    float uy = (1f - kp.y) * rect.height;
                    _poseDots[i].rectTransform.anchoredPosition = new Vector2(ux, uy);
                    if (!_poseDots[i].gameObject.activeSelf) _poseDots[i].gameObject.SetActive(true);
                }
                else if (_poseDots[i].gameObject.activeSelf)
                {
                    _poseDots[i].gameObject.SetActive(false);
                }
            }
        }

        void StartCamera()
        {
            if (_camPreview == null) return;
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                if (_camStatus != null) _camStatus.text = "No camera available";
                return;
            }
            string deviceName = devices[0].name;
            foreach (var d in devices) { if (d.isFrontFacing) { deviceName = d.name; break; } }
            try
            {
                _camTexture = new WebCamTexture(deviceName, 640, 480, 30);
                _camTexture.Play();
                _camPreview.texture = _camTexture;
            }
            catch (System.Exception e)
            {
                if (_camStatus != null) _camStatus.text = "Camera error";
                Debug.LogWarning("[Gamex] camera start failed: " + e.Message);
            }
        }

        void StopCamera()
        {
            if (_camTexture != null)
            {
                try { _camTexture.Stop(); } catch { /* fine */ }
            }
        }

        void SelectExercise(Exercise ex)
        {
            _currentExercise = ex;
            _pushupCounter?.Reset();
            _situpCounter?.Reset();
            _squatCounter?.Reset();
            _adaptive?.Reset();
            _faceCounter?.Reset();
            UpdateExerciseButtonHighlight();
            if (_tipText != null) _tipText.text = TipForExercise(ex);
        }

        // ============================================================
        // Adaptive system setup + per-frame UI
        // ============================================================
        void SetupAdaptive()
        {
            _audioClips = ProcAudio.BuildAll();
            _adaptive = new AdaptivePushupSystem();

            // Zones in 0..1 sprite-relative coords (from PushupSilhouetteZones.txt).
            // These match the silhouette PNG — adjust together if the art changes.
            _adaptive.SilhouetteZones[11] = new Rect(0.178f, 0.320f, 0.095f, 0.127f);  // shoulder
            _adaptive.SilhouetteZones[13] = new Rect(0.178f, 0.487f, 0.095f, 0.127f);  // elbow
            _adaptive.SilhouetteZones[15] = new Rect(0.178f, 0.703f, 0.095f, 0.127f);  // wrist
            _adaptive.SilhouetteZones[23] = new Rect(0.515f, 0.337f, 0.095f, 0.127f);  // hip
            _adaptive.SilhouetteZones[27] = new Rect(0.828f, 0.420f, 0.095f, 0.127f);  // ankle

            _adaptive.OnAlignmentTierChanged = _ =>
                _trafficLightTint.color = _adaptive.BackgroundTintForAlignment();
            _adaptive.OnAlignmentSuccess        = () => _audioSrc.PlayOneShot(_audioClips.alignmentDing);
            _adaptive.OnCalibrationPromptStart  = () => _audioSrc.PlayOneShot(_audioClips.calibrationPrompt);
            _adaptive.OnCalibrationComplete     = () => _audioSrc.PlayOneShot(_audioClips.readyGo);
            _adaptive.OnRepSuccess              = () =>
            {
                _audioSrc.PlayOneShot(_audioClips.repSuccess);
                _goldFlashT = 0.4f;
                _onFakeRep?.Invoke();          // commits the rep into GamexGame
            };
            _adaptive.OnRepError                = () => _audioSrc.PlayOneShot(_audioClips.repError);
        }

        void UpdateAdaptiveUI()
        {
            if (_adaptive == null) return;

            bool isPushup = _currentExercise == Exercise.Pushup;
            bool alignmentPhase = isPushup && (
                _adaptive.State == AdaptivePushupSystem.SessionState.WaitingForAlignment ||
                _adaptive.State == AdaptivePushupSystem.SessionState.AlignmentCountdown);

            if (_silhouetteOverlay != null) _silhouetteOverlay.gameObject.SetActive(alignmentPhase);
            if (_trafficLightTint != null)
            {
                if (alignmentPhase) _trafficLightTint.color = _adaptive.BackgroundTintForAlignment();
                else                _trafficLightTint.color = new Color(0f, 0f, 0f, 0f);
            }

            if (_adaptiveStatus != null)
            {
                if (!isPushup) { _adaptiveStatus.text = ""; }
                else _adaptiveStatus.text = _adaptive.State switch
                {
                    AdaptivePushupSystem.SessionState.WaitingForAlignment => "Get into position — match the silhouette",
                    AdaptivePushupSystem.SessionState.AlignmentCountdown  => "Hold steady...",
                    AdaptivePushupSystem.SessionState.CalibrationPrompt   => "Do one standard pushup — slowly",
                    AdaptivePushupSystem.SessionState.Calibrating         => $"Calibrating... {SafeAngle(_adaptive.CurrentAngle)}°",
                    AdaptivePushupSystem.SessionState.ReadyToCount        => "Ready! Go!",
                    AdaptivePushupSystem.SessionState.Counting            => $"Range {SafeAngle(_adaptive.MinAngle)}-{SafeAngle(_adaptive.MaxAngle)}° · {_adaptive.Reps} reps",
                    _ => ""
                };
            }

            // Gold-flash decay
            if (_goldFlashT > 0f)
            {
                _goldFlashT -= Time.unscaledDeltaTime;
                _goldFlash.color = new Color(1f, 0.8f, 0.3f, Mathf.Max(0f, _goldFlashT) * 0.8f);
                if (_goldFlashT <= 0f) _goldFlash.color = new Color(1f, 0.8f, 0.3f, 0f);
            }
        }

        static string SafeAngle(float a) => float.IsNaN(a) ? "—" : ((int)a).ToString();

        static string TipForExercise(Exercise ex)
        {
            switch (ex)
            {
                case Exercise.Pushup: return "Tip: phone on the floor beside you · side view of your arm";
                case Exercise.Situp:  return "Tip: lie down with phone beside you · side view of your body";
                case Exercise.Squat:  return "Tip: phone 2 m away at hip height · full body in frame";
            }
            return "";
        }

        // BlazePose keypoint indexing. Identify which pair is missing for the
        // active exercise so the user knows what to put in frame.
        static string WhatsMissing(Exercise ex, PoseDetector.Keypoint[] k)
        {
            const float G = 0.2f;
            bool L(int a, int b) => k[a].score < G && k[b].score < G;
            switch (ex)
            {
                case Exercise.Pushup:
                    if (L(11, 12)) return "shoulders";
                    if (L(13, 14)) return "elbows";
                    if (L(15, 16)) return "wrists";
                    break;
                case Exercise.Situp:
                    if (L(11, 12)) return "shoulders";
                    if (L(23, 24)) return "hips";
                    if (L(25, 26)) return "knees";
                    break;
                case Exercise.Squat:
                    if (L(23, 24)) return "hips";
                    if (L(25, 26)) return "knees";
                    if (L(27, 28)) return "ankles";
                    break;
            }
            return null;
        }

        void UpdateExerciseButtonHighlight()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_exerciseButtons[i] == null) continue;
                bool selected = (Exercise)i == _currentExercise;
                var img = _exerciseButtons[i].GetComponent<Image>();
                if (img != null)
                    img.color = selected ? new Color(1f, 0.85f, 0.42f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);
            }
        }

        // ============================================================
        // Avatar — LPC portrait sprite + (future) equipment overlays
        // ============================================================
        public class AvatarSprite
        {
            public GameObject root;
            public Image portrait;

            public void SetAlpha(float a)
            {
                if (portrait == null) return;
                var c = portrait.color;
                portrait.color = new Color(c.r, c.g, c.b, a);
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

            var goImg = new GameObject("Portrait");
            goImg.transform.SetParent(root.transform, false);
            var img = goImg.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            var avatar = new AvatarSprite { root = root, portrait = img };
            ApplyAvatarLook(avatar, gender, curse, race, stage);
            return avatar;
        }

        void ApplyAvatarLook(AvatarSprite avatar, Gender gender, Curse curse, Race race, int stage)
        {
            if (avatar == null || avatar.portrait == null) return;
            avatar.portrait.sprite = Make.Portrait(
                gender == Gender.Unset ? Gender.Male : gender, curse, race, stage);
            // preserve current alpha (mirror fade etc), reset RGB to white so sprite shows true colors
            float a = avatar.portrait.color.a;
            avatar.portrait.color = new Color(1f, 1f, 1f, a);
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
