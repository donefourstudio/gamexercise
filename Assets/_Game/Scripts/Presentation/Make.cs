using UnityEngine;

namespace Gamex.Game
{
    // Code-built UI helpers — same pattern as RouJiaMoKing.
    public static class Make
    {
        static Font _font;
        public static Font Font()
        {
            if (_font == null)
                _font = UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[] { "PingFang SC", "Heiti SC", "Hiragino Sans GB", "Arial Unicode MS", "Arial" }, 32);
            return _font;
        }
    }
}
