using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    // Resource access helpers — Cubic 11 Chinese pixel font + LPC sprite shortcuts.
    public static class Make
    {
        static Font _font;
        public static Font Font()
        {
            if (_font != null) return _font;
            _font = Resources.Load<Font>("Fonts/Cubic_11");
            if (_font == null)
                _font = UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[] { "PingFang SC", "Heiti SC", "Hiragino Sans GB", "Arial Unicode MS", "Arial" }, 32);
            return _font;
        }

        public static Sprite UI(string name)        => Resources.Load<Sprite>("UI/" + name);
        public static Sprite Char(string name)      => Resources.Load<Sprite>("Char/" + name);
        public static Sprite Equipment(string id)   => Resources.Load<Sprite>("Equip/" + id);
        // Phase 4 — full-body skin sprite (Resources/Skins/<id>.png). Returned by
        // Portrait() instead of the race-form sprite when state.activeSkin is set.
        public static Sprite Skin(string id)        => string.IsNullOrEmpty(id) ? null : Resources.Load<Sprite>("Skins/" + id);
        // Full-gear bake of a shop set's source prefab — used by the shop set
        // card + set detail header so the player sees the whole loadout at a
        // glance before drilling into the per-piece list. Knight Set falls
        // back to the chest piece sprite when the composite hasn't been
        // baked yet (BakeKnightSetPreviewBatch in SPUMBaker writes it).
        public static Sprite SetPreview(string id)
        {
            var s = Resources.Load<Sprite>("Sets/" + id);
            if (s != null) return s;
            if (id == "knight_silver_set") return Resources.Load<Sprite>("Equip/knight_chest");
            return null;
        }
        // Icon variant — tight-bbox cropped sprite for inventory cells / paper-doll
        // slot icons. Falls back to the 256x256 overlay if the icon hasn't been
        // baked yet (e.g. during a partial asset import).
        public static Sprite EquipmentIcon(string id)
        {
            var s = Resources.Load<Sprite>("Equip/" + id + "_icon");
            return s != null ? s : Equipment(id);
        }
        public static Sprite Silhouette()           => UI("silhouette");

        // Portrait dispatch (M5c rework):
        //   race == Unset (Lv 1-19) -> per-curse stage sprite:
        //     Weakness -> weakness_stageN (skeleton -> human flesh)
        //     Gluttony -> gluttony_stageN (zombie -> human skin tone)
        //   race != Unset (Lv 20+)  -> {race}_{gender} race form. Race.Human was
        //     dropped per Jackson: starting roster is Orc + Elf only.
        public static Sprite Portrait(Gender gender, Curse curse, Race race, int stage, string activeSkin = null)
        {
            // Phase 4 — an applied skin overrides everything else. The skin's
            // baked-in art carries the body + clothing + weapon in one image, so
            // we skip race/curse/stage logic entirely. If the lookup fails (skin
            // asset missing) we fall back to the normal race-form pipeline below.
            if (!string.IsNullOrEmpty(activeSkin))
            {
                var s = Skin(activeSkin);
                if (s != null) return s;
            }
            if (race == Race.Unset)
            {
                string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
                return Char($"{c}_stage{Mathf.Clamp(stage + 1, 1, 4)}");
            }
            string r = race == Race.Orc ? "orc" : "elf";
            string g = gender == Gender.Female ? "female" : "male";
            return Char($"{r}_{g}");
        }

        // Preview sprite for the curse-select cards. Stage-1 of either arc is
        // already gender-neutral (no human heads), so a single lookup is enough.
        public static Sprite CursePreview(Curse curse)
        {
            string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
            return Char($"{c}_stage1");
        }
    }
}
