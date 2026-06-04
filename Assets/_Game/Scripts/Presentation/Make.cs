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

        // Cohort-aware LPC portrait. curse=Unset falls back to Weakness sheet
        // (stage 6 of both curses is visually identical hero anyway).
        public static Sprite Portrait(Gender gender, Curse curse, int stage)
        {
            string g = gender == Gender.Female ? "female" : "male";
            string c = curse == Curse.Gluttony ? "gluttony" : "weakness";
            int s = Mathf.Clamp(stage + 1, 1, 6);   // GameState.Stage is 0-indexed
            return Char($"{g}_{c}_stage{s}");
        }
    }
}
