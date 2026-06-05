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
        public static Sprite Silhouette()      => UI("silhouette");

        // Portrait dispatch:
        //   race == Unset (Lv 1-19, before transformation) -> skeleton growth (M3a)
        //   race != Unset (Lv 20+)                          -> race-specific hero (M3d:
        //     muscular body with race-tinted skin + race head from LPC).
        public static Sprite Portrait(Gender gender, Curse curse, Race race, int stage)
        {
            if (race == Race.Unset)
                return Char($"skel_stage{Mathf.Clamp(stage + 1, 1, 4)}");

            string r = race == Race.Orc ? "orc" : "human";
            string g = gender == Gender.Female ? "female" : "male";
            return Char($"{r}_{g}");
        }

        // Preview sprite for the curse-select cards: shows the gendered cursed body
        // (teen for Weakness, pregnant for Gluttony) so the player can tell the two
        // curses apart. Bypasses the skeleton dispatch in Portrait().
        public static Sprite CursePreview(Curse curse)
        {
            string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
            return Char($"male_{c}_stage1");
        }
    }
}
