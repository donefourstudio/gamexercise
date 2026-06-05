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
        GameObject _genderPanel, _cursePanel, _firstMirrorPanel, _homePanel, _trainPanel, _shopPanel;

        // ---- avatars ----
        AvatarSprite _homeAvatar, _mirrorHero, _firstMirrorHero;
        AvatarSprite _curseMaleA, _curseFemaleA, _curseMaleB, _curseFemaleB;
        AvatarSprite _genderHeroMale, _genderHeroFemale;

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
        readonly Action<int>    _onSelectGender;
        readonly Action<int>    _onSelectCurse;
        readonly Action         _onFinishFirstMirror;
        readonly Action         _onGoTraining, _onGoShop, _onGoHome, _onFakeRep;
        readonly Action<string> _onBuy, _onToggleEquip;

        public Hud(
            Action<int> onSelectGender,
            Action<int> onSelectCurse,
            Action onFinishFirstMirror,
            Action onGoTraining,
            Action onGoShop,
            Action onGoHome,
            Action onFakeRep,
            Action<string> onBuy,
            Action<string> onToggleEquip)
        {
            _onSelectGender      = onSelectGender;
            _onSelectCurse       = onSelectCurse;
            _onFinishFirstMirror = onFinishFirstMirror;
            _onGoTraining        = onGoTraining;
            _onGoShop            = onGoShop;
            _onGoHome            = onGoHome;
            _onFakeRep           = onFakeRep;
            _onBuy               = onBuy;
            _onToggleEquip       = onToggleEquip;

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
            BuildGenderSelect(root);
            BuildCurseSelect(root);
            BuildFirstMirror(root);
            BuildHome(root);
            BuildTraining(root);
            BuildShop(root);
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
        // Gender select
        // ============================================================
        void BuildGenderSelect(Transform root)
        {
            _genderPanel = MkFullPanel("GenderPanel", root);

            MkText("Title", _genderPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(900f, 90f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "选择你的性别";
            MkText("Sub", _genderPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -340f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, TextDim)
                .text = "你曾是这个时代最强的勇士……";

            _genderHeroMale   = MkGenderOption(_genderPanel.transform, new Vector2(-260f, 80f), "男", Gender.Male,   () => _onSelectGender(1));
            _genderHeroFemale = MkGenderOption(_genderPanel.transform, new Vector2( 260f, 80f), "女", Gender.Female, () => _onSelectGender(2));
        }

        AvatarSprite MkGenderOption(Transform parent, Vector2 pos, string label, Gender gender, Action onClick)
        {
            var card = MkSpritePanel("Option_" + label, parent, new Vector2(0.5f, 0.5f), pos,
                new Vector2(420f, 720f), "panel", PanelTint);
            var btn = card.AddComponent<Button>();
            btn.targetGraphic = card.GetComponent<Image>();
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.7f, 1f); btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var avatar = BuildAvatar(card.transform, new Vector2(0f, 60f), 1.4f, gender, Curse.Unset, stage: 5);

            MkText("Label", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 80f),
                new Vector2(360f, 80f), FS_BIG, TextAnchor.MiddleCenter, AccentGold).text = label;
            return avatar;
        }

        // ============================================================
        // Curse select
        // ============================================================
        void BuildCurseSelect(Transform root)
        {
            _cursePanel = MkFullPanel("CursePanel", root);

            MkText("Title", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -240f),
                new Vector2(900f, 90f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "你中了哪种诅咒？";
            MkText("Sub", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -340f),
                new Vector2(900f, 60f), FS_LABEL, TextAnchor.UpperCenter, TextDim)
                .text = "……直到诅咒降临。";

            MkCurseOption(_cursePanel.transform, new Vector2(-260f, 60f),
                "虚弱诅咒", "你的力量正在消散", Curse.Weakness, () => _onSelectCurse(1),
                out _curseMaleA, out _curseFemaleA);
            MkCurseOption(_cursePanel.transform, new Vector2( 260f, 60f),
                "贪食诅咒", "你的身躯沉重不堪", Curse.Gluttony, () => _onSelectCurse(2),
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

            male   = BuildAvatar(card.transform, new Vector2(0f, 90f), 1.4f, Gender.Male,   curse, stage: 0);
            female = BuildAvatar(card.transform, new Vector2(0f, 90f), 1.4f, Gender.Female, curse, stage: 0);
            female.root.SetActive(false);

            MkText("Title", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 130f),
                new Vector2(360f, 60f), FS_BIG, TextAnchor.MiddleCenter, AccentGold).text = title;
            MkText("Sub", card.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(360f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim).text = sub;
        }

        // ============================================================
        // First mirror
        // ============================================================
        void BuildFirstMirror(Transform root)
        {
            _firstMirrorPanel = MkFullPanel("FirstMirror", root);

            var frame = MkSpritePanel("MirrorFrame", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 180f), new Vector2(540f, 720f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var inner = MkSpritePanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(480f, 660f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            _firstMirrorHero = BuildAvatar(inner.transform, new Vector2(0f, 0f), 2.0f,
                Gender.Male, Curse.Unset, stage: 5);

            _firstMirrorLine = MkText("Line", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -380f), new Vector2(1000f, 80f), FS_BIG, TextAnchor.MiddleCenter, AccentGold);
            _firstMirrorLine.text = "「……终于，你想起来了。」";

            MkButton("Begin", _firstMirrorPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 180f),
                new Vector2(540f, 130f), "做一个俯卧撑", () => _onFinishFirstMirror?.Invoke());
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

            // mirror (centered upper)
            var frame = MkSpritePanel("Mirror", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(80f, 260f), new Vector2(440f, 580f), "panel", new Color(0.95f, 0.78f, 0.42f, 1f));
            var inner = MkSpritePanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(380f, 520f), "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
            _mirrorHero = BuildAvatar(inner.transform, Vector2.zero, 1.6f, Gender.Male, Curse.Unset, stage: 5);

            // current cursed avatar (left of mirror)
            _homeAvatar = BuildAvatar(_homePanel.transform, new Vector2(-340f, 240f), 1.0f,
                Gender.Male, Curse.Weakness, stage: 0);

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
                new Vector2(800f, 160f), "开始训练", () => _onGoTraining?.Invoke());
            MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "商店", () => _onGoShop?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Training
        // ============================================================
        void BuildTraining(Transform root)
        {
            _trainPanel = MkFullPanel("TrainPanel", root);

            // Top half: camera viewport with cartoon face overlay
            var cam = MkSpritePanel("CamViewport", _trainPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -120f), new Vector2(900f, 700f), "panel_light", CamBg);
            MkText("CamTag", cam.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f),
                new Vector2(800f, 50f), FS_LABEL, TextAnchor.UpperCenter, TextDim).text = "摄像头预览（占位）";

            // cartoon face mask — orange disc with smiley
            var mask = MkSpritePanel("Mask", cam.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(220f, 220f), "panel", new Color(1f, 0.78f, 0.45f, 1f));
            MkText("Face", mask.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(220f, 220f), 88, TextAnchor.MiddleCenter, new Color(0.2f, 0.1f, 0f)).text = "(•‿•)";

            // Center: huge rep counter
            _trainReps = MkText("Reps", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -120f), new Vector2(900f, 220f), FS_HUGE, TextAnchor.MiddleCenter, AccentGold);
            _trainCoinsXp = MkText("CoinsXp", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -260f), new Vector2(900f, 60f), FS_LABEL, TextAnchor.MiddleCenter, TextWhite);
            _trainMaintenance = MkText("Maint", _trainPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -320f), new Vector2(900f, 50f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);

            // Bottom: buttons
            MkButton("FakeRep", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 280f),
                new Vector2(720f, 140f), "假装做一个", () => _onFakeRep?.Invoke());
            MkButton("Done", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(420f, 100f), "训练结束", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        // ============================================================
        // Shop
        // ============================================================
        void BuildShop(Transform root)
        {
            _shopPanel = MkFullPanel("ShopPanel", root);

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), FS_TITLE, TextAnchor.UpperCenter, AccentGold).text = "商店";
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
                new Vector2(420f, 100f), "返回", () => _onGoHome?.Invoke(), "btn_grey", "btn_grey_down");
        }

        bool IsOwned(string id) => _ownedSnapshot.Contains(id);

        // ============================================================
        // Refresh
        // ============================================================
        public void Refresh(GamexGame g)
        {
            Set(_genderPanel,      g.phase == AppPhase.GenderSelect);
            Set(_cursePanel,       g.phase == AppPhase.CurseSelect);
            Set(_firstMirrorPanel, g.phase == AppPhase.FirstMirror);
            Set(_homePanel,        g.phase == AppPhase.Home);
            Set(_trainPanel,       g.phase == AppPhase.Training);
            Set(_shopPanel,        g.phase == AppPhase.Shop);

            var gender = (Gender)g.state.gender;
            var curse  = (Curse)g.state.curse;

            // curse select: show the avatar that matches chosen gender
            if (_curseMaleA != null)
            {
                _curseMaleA.root.SetActive(gender != Gender.Female);
                _curseFemaleA.root.SetActive(gender == Gender.Female);
                _curseMaleB.root.SetActive(gender != Gender.Female);
                _curseFemaleB.root.SetActive(gender == Gender.Female);
            }

            if (_firstMirrorHero != null)
                ApplyAvatarLook(_firstMirrorHero, gender == Gender.Unset ? Gender.Male : gender, Curse.Unset, stage: 5);

            if (g.phase == AppPhase.Home || g.phase == AppPhase.Training || g.phase == AppPhase.Shop)
            {
                _homeLevel.text   = $"Lv {g.state.level}";
                _homeCoins.text   = $"金币 {g.state.coins}";
                _homeStreak.text  = $"连续 {g.state.streakDays} 天 · 错过 {g.state.missedDays}";
                _homeProgress.text = $"今日 {g.state.repsToday} / {g.MaintenanceToday} （保持量）";
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    700f * Mathf.Clamp01((float)g.state.xp / Mathf.Max(1, g.XpPerLevel)), 28f);

                int stage = g.Stage;
                ApplyAvatarLook(_homeAvatar, gender, curse, stage);
                ApplyAvatarLook(_mirrorHero, gender, Curse.Unset, stage: 5);
                float p = (g.state.level - 1) / 29f;
                _mirrorHero.SetAlpha(0.30f + 0.70f * p);
            }

            if (g.phase == AppPhase.Training)
            {
                _trainReps.text = g.state.repsToday.ToString();
                _trainCoinsXp.text = $"+{g.state.coins} 金币   ·   Lv {g.state.level}  ({g.state.xp}/{g.XpPerLevel} XP)";
                _trainMaintenance.text = $"今日保持量 {g.MaintenanceToday} 个";
            }

            if (g.phase == AppPhase.Shop)
            {
                _shopCoins.text = $"金币 {g.state.coins}";
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
                    if (equipped) status = "✓ 已穿戴（点击卸下）";
                    else if (owned) status = "已拥有（点击穿戴）";
                    else if (!unlocked) status = $"需要 Lv {def.minLevel}";
                    else if (!affordable) status = $"{def.price} 金币（不足）";
                    else status = $"{def.price} 金币（点击购买）";

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
                                 Gender gender, Curse curse, int stage)
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
            ApplyAvatarLook(avatar, gender, curse, stage);
            return avatar;
        }

        void ApplyAvatarLook(AvatarSprite avatar, Gender gender, Curse curse, int stage)
        {
            if (avatar == null || avatar.portrait == null) return;
            avatar.portrait.sprite = Make.Portrait(
                gender == Gender.Unset ? Gender.Male : gender, curse, stage);
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
