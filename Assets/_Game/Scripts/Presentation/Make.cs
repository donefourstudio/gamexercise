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
        //   race == Unset (Lv 1-19, before transformation)    -> skeleton growth (M3a)
        //   race != Unset (Lv 20+, after Race transformation) -> race form (M2a sprites
        //     for now; M3d will branch on race for distinct Human / Orc art).
        public static Sprite Portrait(Gender gender, Curse curse, Race race, int stage)
        {
            if (race == Race.Unset)
                return Char($"skel_stage{Mathf.Clamp(stage + 1, 1, 4)}");

            string g = gender == Gender.Female ? "female" : "male";
            string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
            // M3b: race chosen but race-specific sprites not yet diverse. Map Lv 20-25 to
            // stage 5 sprite, Lv 26-30 to stage 6 (hero peak).
            int s = stage >= 5 ? 6 : 5;
            return Char($"{g}_{c}_stage{s}");
        }
    }
}
