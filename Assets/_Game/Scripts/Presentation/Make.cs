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

        public static Sprite UI(string name)   => Resources.Load<Sprite>("UI/" + name);
        public static Sprite Char(string name) => Resources.Load<Sprite>("Char/" + name);

        // Cursed-stage portrait. Lv 1-20 (stages 0-3) render as the gender-neutral
        // skeleton-growing-into-humanoid arc; Lv 21+ (stages 4-5) render as the chosen
        // form (M3d will diversify these by race; for now they fall back to the
        // gender+curse humanoid sprites baked in M2a).
        public static Sprite Portrait(Gender gender, Curse curse, int stage)
        {
            if (stage < 4)
                return Char($"skel_stage{Mathf.Clamp(stage + 1, 1, 4)}");

            string g = gender == Gender.Female ? "female" : "male";
            string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
            return Char($"{g}_{c}_stage{stage + 1}");   // stage 4 -> _stage5, stage 5 -> _stage6
        }
    }
}
