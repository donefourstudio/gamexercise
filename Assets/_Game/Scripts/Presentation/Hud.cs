using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Gamex.Core;

namespace Gamex.Game
{
    // Single-canvas Hud that hosts every screen. Each phase corresponds to one panel
    // that is shown/hidden in Refresh().
    public class Hud
    {
        // ---- palette ----
        static readonly Color BgDark      = new Color(0.06f, 0.05f, 0.10f);
        static readonly Color BgPanel     = new Color(0.14f, 0.10f, 0.18f, 0.96f);
        static readonly Color AccentGold  = new Color(1f, 0.85f, 0.40f);
        static readonly Color TextDim     = new Color(0.75f, 0.70f, 0.65f);
        static readonly Color CurseSkin   = new Color(0.62f, 0.60f, 0.58f);
        static readonly Color CurseGirth  = new Color(0.78f, 0.58f, 0.42f);
        static readonly Color HeroGold    = new Color(1f, 0.80f, 0.40f);

        // ---- panels ----
        GameObject _genderPanel, _cursePanel, _firstMirrorPanel, _homePanel, _trainPanel, _shopPanel;

        // ---- home refs ----
        AvatarRig _homeAvatar;
        AvatarRig _mirrorHero;
        Text _homeLevel, _homeCoins, _homeProgress, _homeStreak;
        Image _xpBar;

        // ---- training refs ----
        Text _trainReps, _trainCoinsXp, _trainMaintenance;

        // ---- first mirror refs ----
        AvatarRig _firstMirrorHero;
        Text _firstMirrorLine;

        // ---- shop refs ----
        Transform _shopRowsRoot;
        readonly Dictionary<string, Button> _shopBtns = new();
        readonly Dictionary<string, Text> _shopLabels = new();
        Text _shopCoins;

        // ---- callbacks (set in ctor) ----
        readonly Action<int> _onSelectGender;
        readonly Action<int> _onSelectCurse;
        readonly Action _onFinishFirstMirror;
        readonly Action _onGoTraining;
        readonly Action _onGoShop;
        readonly Action _onGoHome;
        readonly Action _onFakeRep;
        readonly Action<string> _onBuy;
        readonly Action<string> _onToggleEquip;

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
            _onSelectGender = onSelectGender;
            _onSelectCurse = onSelectCurse;
            _onFinishFirstMirror = onFinishFirstMirror;
            _onGoTraining = onGoTraining;
            _onGoShop = onGoShop;
            _onGoHome = onGoHome;
            _onFakeRep = onFakeRep;
            _onBuy = onBuy;
            _onToggleEquip = onToggleEquip;

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
            scaler.referenceResolution = new Vector2(1080f, 1920f);   // portrait, phone
            scaler.matchWidthOrHeight = 0.5f;
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

        // ---- background ----

        static void BuildBackground(Transform root)
        {
            var go = MkPanel("BgFill", root, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f), BgDark);
            go.GetComponent<Image>().raycastTarget = false;
        }

        // ============================================================
        // Gender select
        // ============================================================
        void BuildGenderSelect(Transform root)
        {
            _genderPanel = MkFullPanel("GenderPanel", root);

            MkText("Title", _genderPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -200f),
                new Vector2(900f, 100f), 60, TextAnchor.UpperCenter, AccentGold).text = "选择你的性别";
            MkText("Sub", _genderPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -300f),
                new Vector2(900f, 60f), 30, TextAnchor.UpperCenter, TextDim)
                .text = "你曾是这个时代最强的勇士……";

            MkGenderOption(_genderPanel.transform, new Vector2(-220f, 0f), "男", true,  () => _onSelectGender(1));
            MkGenderOption(_genderPanel.transform, new Vector2( 220f, 0f), "女", false, () => _onSelectGender(2));
        }

        void MkGenderOption(Transform parent, Vector2 pos, string label, bool male, Action onClick)
        {
            var go = MkPanel("Option", parent, new Vector2(0.5f, 0.5f), pos,
                new Vector2(380f, 600f), new Color(0.20f, 0.18f, 0.28f, 0.95f));
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            // placeholder portrait — a 'strong hero' silhouette in heroic gold
            BuildAvatar(go.transform, new Vector2(0f, 60f), 1.0f,
                male ? Gender.Male : Gender.Female, Curse.Unset, stage: 5, equippedIds: null);

            MkText("Label", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(300f, 70f), 44, TextAnchor.MiddleCenter, Color.white).text = label;
        }

        // ============================================================
        // Curse select
        // ============================================================
        void BuildCurseSelect(Transform root)
        {
            _cursePanel = MkFullPanel("CursePanel", root);

            MkText("Title", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -200f),
                new Vector2(900f, 100f), 60, TextAnchor.UpperCenter, AccentGold).text = "你中了哪种诅咒？";
            MkText("Sub", _cursePanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -300f),
                new Vector2(900f, 60f), 30, TextAnchor.UpperCenter, TextDim)
                .text = "……直到诅咒降临。";

            MkCurseOption(_cursePanel.transform, new Vector2(-220f, 0f),
                "虚弱诅咒", "你的力量正在消散", Curse.Weakness, () => _onSelectCurse(1));
            MkCurseOption(_cursePanel.transform, new Vector2( 220f, 0f),
                "贪食诅咒", "你的身躯沉重不堪", Curse.Gluttony, () => _onSelectCurse(2));
        }

        // We don't yet know the player's gender when building (curse panel is built once);
        // refresh updates the avatar at runtime to match the chosen gender.
        AvatarRig _curseMaleA, _curseFemaleA, _curseMaleB, _curseFemaleB;

        void MkCurseOption(Transform parent, Vector2 pos, string title, string sub, Curse curse, Action onClick)
        {
            var go = MkPanel("Option", parent, new Vector2(0.5f, 0.5f), pos,
                new Vector2(380f, 600f), new Color(0.20f, 0.18f, 0.28f, 0.95f));
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            // build two avatars (male/female), show the one matching gender at runtime
            var male = BuildAvatar(go.transform, new Vector2(0f, 80f), 1.0f, Gender.Male, curse, stage: 0, equippedIds: null);
            var female = BuildAvatar(go.transform, new Vector2(0f, 80f), 1.0f, Gender.Female, curse, stage: 0, equippedIds: null);
            if (curse == Curse.Weakness)  { _curseMaleA = male; _curseFemaleA = female; }
            else                          { _curseMaleB = male; _curseFemaleB = female; }
            female.root.SetActive(false);

            MkText("Title", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 95f),
                new Vector2(340f, 60f), 38, TextAnchor.MiddleCenter, AccentGold).text = title;
            MkText("Sub", go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 50f),
                new Vector2(340f, 40f), 22, TextAnchor.MiddleCenter, TextDim).text = sub;
        }

        // ============================================================
        // First mirror (one-time post-curse intro)
        // ============================================================
        void BuildFirstMirror(Transform root)
        {
            _firstMirrorPanel = MkFullPanel("FirstMirror", root);

            // mirror frame
            var frame = MkPanel("MirrorFrame", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 150f), new Vector2(440f, 620f), new Color(0.30f, 0.22f, 0.10f, 1f));
            var inner = MkPanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 580f), new Color(0.12f, 0.10f, 0.18f, 1f));
            _firstMirrorHero = BuildAvatar(inner.transform, new Vector2(0f, -20f), 1.0f,
                Gender.Male, Curse.Unset, stage: 5, equippedIds: null);

            _firstMirrorLine = MkText("Line", _firstMirrorPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -300f), new Vector2(900f, 100f), 36, TextAnchor.MiddleCenter, AccentGold);
            _firstMirrorLine.text = "「……终于，你想起来了。」";

            MkButton("Begin", _firstMirrorPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 200f),
                new Vector2(400f, 100f), "做一个俯卧撑", () => _onFinishFirstMirror?.Invoke());
        }

        // ============================================================
        // Home (main menu)
        // ============================================================
        void BuildHome(Transform root)
        {
            _homePanel = MkFullPanel("HomePanel", root);

            // top: coin + level (corner HUD)
            _homeCoins = MkText("Coins", _homePanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -50f),
                new Vector2(400f, 60f), 36, TextAnchor.UpperRight, AccentGold);
            _homeLevel = MkText("Level", _homePanel.transform, new Vector2(0f, 1f), new Vector2(40f, -50f),
                new Vector2(400f, 60f), 36, TextAnchor.UpperLeft, AccentGold);

            // mirror centered
            var frame = MkPanel("Mirror", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(440f, 600f), new Color(0.28f, 0.20f, 0.10f, 1f));
            var inner = MkPanel("MirrorInner", frame.transform, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(400f, 560f), new Color(0.10f, 0.08f, 0.16f, 1f));
            _mirrorHero = BuildAvatar(inner.transform, new Vector2(0f, -10f), 1.0f,
                Gender.Male, Curse.Unset, stage: 5, equippedIds: null);

            // current cursed avatar to the LEFT of the mirror (smaller)
            _homeAvatar = BuildAvatar(_homePanel.transform, new Vector2(-300f, 250f), 0.6f,
                Gender.Male, Curse.Weakness, stage: 0, equippedIds: null);

            // streak + today's progress
            _homeStreak = MkText("Streak", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -120f), new Vector2(800f, 60f), 32, TextAnchor.MiddleCenter, AccentGold);
            _homeProgress = MkText("Progress", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -180f), new Vector2(800f, 60f), 28, TextAnchor.MiddleCenter, TextDim);

            // XP bar
            var bg = MkPanel("XpBg", _homePanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -240f), new Vector2(600f, 20f), new Color(0f, 0f, 0f, 0.5f));
            var fill = MkPanel("XpFill", bg.transform, new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(600f, 20f), AccentGold);
            _xpBar = fill.GetComponent<Image>();
            var rt = _xpBar.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(-300f, 0f);

            // big bottom buttons
            MkButton("Train", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 240f),
                new Vector2(800f, 160f), "开始训练", () => _onGoTraining?.Invoke());
            MkButton("Shop", _homePanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(400f, 90f), "商店", () => _onGoShop?.Invoke());
        }

        // ============================================================
        // Training
        // ============================================================
        void BuildTraining(Transform root)
        {
            _trainPanel = MkFullPanel("TrainPanel", root);

            // camera viewport placeholder
            var cam = MkPanel("CamViewport", _trainPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -700f), new Vector2(900f, 1100f), new Color(0.08f, 0.10f, 0.14f, 1f));
            MkText("CamTag", cam.transform, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                new Vector2(800f, 50f), 28, TextAnchor.UpperCenter, TextDim).text = "摄像头预览（占位）";

            // cartoon face mask placeholder
            var mask = MkPanel("Mask", cam.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 100f), new Vector2(180f, 180f), new Color(1f, 0.78f, 0.45f, 1f));
            MkText("Face", mask.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(180f, 180f), 80, TextAnchor.MiddleCenter, new Color(0.2f, 0.1f, 0f)).text = "(•‿•)";

            // rep counter
            _trainReps = MkText("Reps", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 540f),
                new Vector2(900f, 200f), 160, TextAnchor.MiddleCenter, AccentGold);
            _trainCoinsXp = MkText("CoinsXp", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 430f),
                new Vector2(900f, 60f), 36, TextAnchor.MiddleCenter, Color.white);
            _trainMaintenance = MkText("Maint", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 360f),
                new Vector2(900f, 50f), 26, TextAnchor.MiddleCenter, TextDim);

            // fake-rep button (debug) + done
            MkButton("FakeRep", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 220f),
                new Vector2(700f, 130f), "🤖 假装做一个", () => _onFakeRep?.Invoke());
            MkButton("Done", _trainPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 80f),
                new Vector2(400f, 90f), "训练结束", () => _onGoHome?.Invoke());
        }

        // ============================================================
        // Shop
        // ============================================================
        void BuildShop(Transform root)
        {
            _shopPanel = MkFullPanel("ShopPanel", root);

            MkText("Title", _shopPanel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                new Vector2(800f, 80f), 48, TextAnchor.UpperCenter, AccentGold).text = "商店";
            _shopCoins = MkText("Coins", _shopPanel.transform, new Vector2(1f, 1f), new Vector2(-40f, -80f),
                new Vector2(400f, 60f), 36, TextAnchor.UpperRight, AccentGold);

            var listGO = MkPanel("Rows", _shopPanel.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(900f, 1400f), new Color(0f, 0f, 0f, 0f));
            listGO.GetComponent<Image>().raycastTarget = false;
            _shopRowsRoot = listGO.transform;

            var defs = GamexGame.Catalog;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                float y = 600f - i * 150f;
                var row = MkPanel("Row_" + def.id, _shopRowsRoot, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(900f, 130f), new Color(0.20f, 0.16f, 0.24f, 0.95f));
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = row.GetComponent<Image>();
                string capId = def.id;
                btn.onClick.AddListener(() =>
                {
                    if (!IsOwned(capId)) _onBuy?.Invoke(capId);
                    else _onToggleEquip?.Invoke(capId);
                });

                var label = MkText("Label", row.transform, new Vector2(0f, 0.5f), new Vector2(40f, 0f),
                    new Vector2(820f, 110f), 28, TextAnchor.MiddleLeft, Color.white);
                _shopBtns[def.id] = btn;
                _shopLabels[def.id] = label;
            }

            MkButton("Back", _shopPanel.transform, new Vector2(0.5f, 0f), new Vector2(0f, 80f),
                new Vector2(400f, 90f), "返回", () => _onGoHome?.Invoke());
        }

        // The Hud holds last-seen owned snapshot for the shop button mode toggle.
        readonly HashSet<string> _ownedSnapshot = new();
        bool IsOwned(string id) => _ownedSnapshot.Contains(id);

        // ============================================================
        // Refresh
        // ============================================================
        public void Refresh(GamexGame g)
        {
            // panel visibility from phase
            Set(_genderPanel,      g.phase == AppPhase.GenderSelect);
            Set(_cursePanel,       g.phase == AppPhase.CurseSelect);
            Set(_firstMirrorPanel, g.phase == AppPhase.FirstMirror);
            Set(_homePanel,        g.phase == AppPhase.Home);
            Set(_trainPanel,       g.phase == AppPhase.Training);
            Set(_shopPanel,        g.phase == AppPhase.Shop);

            var gender = (Gender)g.state.gender;
            var curse  = (Curse)g.state.curse;

            // curse panel: pick avatar to show by gender
            if (_curseMaleA != null)
            {
                _curseMaleA.root.SetActive(gender == Gender.Male);
                _curseFemaleA.root.SetActive(gender == Gender.Female);
                _curseMaleB.root.SetActive(gender == Gender.Male);
                _curseFemaleB.root.SetActive(gender == Gender.Female);
            }

            // first-mirror hero: rebuild visuals
            if (_firstMirrorHero != null) ApplyAvatarLook(_firstMirrorHero, gender, Curse.Unset, stage: 5, equipped: null);

            // home
            if (g.phase == AppPhase.Home || g.phase == AppPhase.Training || g.phase == AppPhase.Shop)
            {
                _homeLevel.text = $"Lv {g.state.level}";
                _homeCoins.text = $"金币 {g.state.coins}";
                _homeStreak.text = $"🔥 连续 {g.state.streakDays} 天 · 错过 {g.state.missedDays}";
                _homeProgress.text = $"今日 {g.state.repsToday}/{g.MaintenanceToday} （保持量）";
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    600f * Mathf.Clamp01((float)g.state.xp / Mathf.Max(1, g.XpPerLevel)), 20f);

                int stage = g.Stage;
                ApplyAvatarLook(_homeAvatar, gender, curse, stage, g.state.equipped);
                ApplyAvatarLook(_mirrorHero, gender, Curse.Unset, stage: 5, equipped: g.state.equipped);
                // hero mirror fades up as the player approaches max
                float p = (g.state.level - 1) / 29f;
                var c = _mirrorHero.root.GetComponentInChildren<Image>(true).color;
                _mirrorHero.SetAlpha(0.35f + 0.65f * p);
            }

            // training
            if (g.phase == AppPhase.Training)
            {
                _trainReps.text = g.state.repsToday.ToString();
                _trainCoinsXp.text = $"+{g.state.coins} 金币   ·   Lv {g.state.level}  ({g.state.xp}/{g.XpPerLevel} XP)";
                _trainMaintenance.text = $"今日保持量 {g.MaintenanceToday} 个";
            }

            // shop
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
        // Avatar — composite of UI rectangles
        // ============================================================
        public class AvatarRig
        {
            public GameObject root;
            public Image head, torso, armL, armR, legL, legR, weapon, armor;

            public void SetAlpha(float a)
            {
                foreach (var img in new[] { head, torso, armL, armR, legL, legR, weapon, armor })
                {
                    if (img == null) continue;
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, a);
                }
            }
        }

        AvatarRig BuildAvatar(Transform parent, Vector2 anchoredPos, float scale,
                              Gender gender, Curse curse, int stage, List<string> equippedIds)
        {
            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(220f, 360f) * scale;
            rt.localScale = Vector3.one;

            var rig = new AvatarRig { root = root };
            rig.head  = MkPanelImage(root.transform, new Vector2(0f,  140f * scale), new Vector2(80f, 80f) * scale, Color.white);
            rig.torso = MkPanelImage(root.transform, new Vector2(0f,   30f * scale), new Vector2(110f, 150f) * scale, Color.white);
            rig.armL  = MkPanelImage(root.transform, new Vector2(-80f, 30f * scale), new Vector2(30f, 130f) * scale, Color.white);
            rig.armR  = MkPanelImage(root.transform, new Vector2( 80f, 30f * scale), new Vector2(30f, 130f) * scale, Color.white);
            rig.legL  = MkPanelImage(root.transform, new Vector2(-30f, -100f * scale), new Vector2(40f, 140f) * scale, Color.white);
            rig.legR  = MkPanelImage(root.transform, new Vector2( 30f, -100f * scale), new Vector2(40f, 140f) * scale, Color.white);
            // equipment slots (hidden unless equipped)
            rig.weapon = MkPanelImage(root.transform, new Vector2(90f, 30f * scale), new Vector2(20f, 200f) * scale, new Color(0.55f, 0.4f, 0.18f, 1f));
            rig.weapon.gameObject.SetActive(false);
            rig.armor  = MkPanelImage(root.transform, new Vector2(0f, 30f * scale), new Vector2(140f, 170f) * scale, new Color(0.55f, 0.6f, 0.65f, 0.85f));
            rig.armor.gameObject.SetActive(false);

            ApplyAvatarLook(rig, gender, curse, stage, equippedIds);
            return rig;
        }

        // Re-applies look based on gender / curse / stage / equipped — called every frame.
        void ApplyAvatarLook(AvatarRig rig, Gender gender, Curse curse, int stage, List<string> equipped)
        {
            // base skin tone — slightly warmer for female placeholder, stage drives saturation
            Color skin = new Color(0.95f, 0.78f, 0.62f);
            Color cloth;
            if (curse == Curse.Weakness)      cloth = Color.Lerp(CurseSkin, HeroGold, stage / 5f);
            else if (curse == Curse.Gluttony) cloth = Color.Lerp(CurseGirth, HeroGold, stage / 5f);
            else                              cloth = HeroGold;  // hero / unset curse = full hero

            // proportions vary by curse + stage
            float torsoWide = 1f, armWide = 1f, legWide = 1f;
            if (curse == Curse.Weakness)
            {
                // thin to begin, fills out over stages
                torsoWide = 0.7f + 0.05f * stage;
                armWide   = 0.6f + 0.08f * stage;
                legWide   = 0.7f + 0.06f * stage;
            }
            else if (curse == Curse.Gluttony)
            {
                // wide to begin, slims over stages
                torsoWide = 1.5f - 0.08f * stage;
                armWide   = 1.4f - 0.08f * stage;
                legWide   = 1.4f - 0.08f * stage;
            }

            rig.head.color = skin;
            rig.torso.color = cloth;
            rig.armL.color = cloth;
            rig.armR.color = cloth;
            rig.legL.color = new Color(0.20f, 0.16f, 0.12f);   // dark trousers
            rig.legR.color = new Color(0.20f, 0.16f, 0.12f);

            // scale rectangles by proportions
            Scale(rig.torso, new Vector2(torsoWide, 1f));
            Scale(rig.armL,  new Vector2(armWide, 1f));
            Scale(rig.armR,  new Vector2(armWide, 1f));
            Scale(rig.legL,  new Vector2(legWide, 1f));
            Scale(rig.legR,  new Vector2(legWide, 1f));

            // tiny gender hint — slimmer torso for female placeholder
            if (gender == Gender.Female) Scale(rig.torso, new Vector2(0.9f, 1f));

            // equipment overlays
            bool hasSword = equipped != null && (equipped.Contains("sword_wood") || equipped.Contains("sword_iron")
                                              || equipped.Contains("sword_silver") || equipped.Contains("sword_legend"));
            bool hasArmor = equipped != null && (equipped.Contains("armor_cloth") || equipped.Contains("armor_leather")
                                              || equipped.Contains("armor_silver") || equipped.Contains("armor_legend"));
            rig.weapon.gameObject.SetActive(hasSword);
            rig.armor.gameObject.SetActive(hasArmor);

            if (hasSword)
            {
                if (equipped.Contains("sword_legend"))      rig.weapon.color = new Color(1f, 0.86f, 0.45f);
                else if (equipped.Contains("sword_silver")) rig.weapon.color = new Color(0.85f, 0.88f, 0.95f);
                else if (equipped.Contains("sword_iron"))   rig.weapon.color = new Color(0.55f, 0.58f, 0.62f);
                else                                        rig.weapon.color = new Color(0.55f, 0.40f, 0.18f);
            }
            if (hasArmor)
            {
                if (equipped.Contains("armor_legend"))      rig.armor.color = new Color(1f, 0.86f, 0.45f, 0.9f);
                else if (equipped.Contains("armor_silver")) rig.armor.color = new Color(0.85f, 0.88f, 0.95f, 0.85f);
                else if (equipped.Contains("armor_leather"))rig.armor.color = new Color(0.50f, 0.34f, 0.22f, 0.9f);
                else                                        rig.armor.color = new Color(0.85f, 0.78f, 0.62f, 0.85f);
            }
        }

        static void Scale(Image img, Vector2 factor)
        {
            var ls = img.transform.localScale;
            img.transform.localScale = new Vector3(factor.x, factor.y, 1f);
            // keep persistent base scale on first call only — for MVP we just overwrite
        }

        // ============================================================
        // Tiny UI factory
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return t;
        }

        // A transparent full-screen container that actually stretches with its parent —
        // child anchors (top/bottom/etc.) then correctly reference the canvas edges.
        static GameObject MkFullPanel(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
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

        static Image MkPanelImage(Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return img;
        }

        static GameObject MkButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                   string label, Action onClick)
        {
            var go = MkPanel(name, parent, anchor, pos, size, new Color(0.85f, 0.55f, 0.25f));
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            var t = MkText(name + "Label", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                size, 40, TextAnchor.MiddleCenter, Color.white);
            t.text = label;
            return go;
        }
    }
}
