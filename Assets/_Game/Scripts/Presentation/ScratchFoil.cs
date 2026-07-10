using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gamex.Game
{
    // True drag-to-scratch foil (P3 — docs/casino-mvp-plan.md). A RawImage
    // holding a runtime Texture2D of silver "foil"; pointer presses/drags
    // punch transparent holes into it, and once REVEAL_AT of the pixels are
    // gone the whole foil clears and onRevealed fires. Attached to a child
    // stretched over each ticket cell; the symbol text sits UNDER it, so
    // scratching physically uncovers the result.
    //
    // Unity's drag events stay with the object the press started on, so
    // each cell takes its own stroke(s) — deliberate: real scratch tickets
    // are scratched panel by panel, and the per-cell ritual is the point.
    public class ScratchFoil : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        const int   W = 96, H = 118;    // small texture: cheap SetPixels/Apply per drag frame
        const float BRUSH_R   = 15f;    // brush radius in texture px
        const float REVEAL_AT = 0.55f;  // cleared fraction that pops the cell
        const float SFX_EVERY = 0.12f;  // scratch-tick cadence while actively erasing

        public Action onRevealed;
        // Base material color (set before Init). Default = silver ticket
        // foil; Gold Rush overrides with dirt brown, Mega with gold.
        public Color32 foilColor = new Color32(178, 178, 188, 255);

        RawImage      _img;
        Texture2D     _tex;
        Color32[]     _px;
        bool[]        _holes;
        int           _cleared;
        bool          _revealed;
        float         _lastSfx;
        RectTransform _rt;

        public void Init()
        {
            _rt  = GetComponent<RectTransform>();
            _img = GetComponent<RawImage>();
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
            };
            _px    = new Color32[W * H];
            _holes = new bool[W * H];
            _img.texture = _tex;
            ResetFoil();
        }

        // Fresh foil for a newly dealt ticket: the base color with
        // per-pixel noise so it reads as scratchable material instead of
        // a flat panel.
        public void ResetFoil()
        {
            var rng = new System.Random(GetInstanceID() ^ Environment.TickCount);
            for (int i = 0; i < _px.Length; i++)
            {
                int n = rng.Next(-12, 13);
                _px[i] = new Color32(
                    (byte)Mathf.Clamp(foilColor.r + n, 0, 255),
                    (byte)Mathf.Clamp(foilColor.g + n, 0, 255),
                    (byte)Mathf.Clamp(foilColor.b + n, 0, 255), 255);
                _holes[i] = false;
            }
            _cleared  = 0;
            _revealed = false;
            _tex.SetPixels32(_px);
            _tex.Apply(false);
            gameObject.SetActive(true);
        }

        public void OnPointerDown(PointerEventData e) => Erase(e);
        public void OnDrag(PointerEventData e)        => Erase(e);

        void Erase(PointerEventData e)
        {
            if (_revealed) return;
            // ScreenSpaceOverlay canvas -> pressEventCamera is null; the
            // utility handles that case.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rt, e.position, e.pressEventCamera, out var lp)) return;

            var rect = _rt.rect;
            int cx = Mathf.RoundToInt((lp.x - rect.xMin) / rect.width  * W);
            int cy = Mathf.RoundToInt((lp.y - rect.yMin) / rect.height * H);

            int newly = 0;
            int r = Mathf.CeilToInt(BRUSH_R);
            for (int y = cy - r; y <= cy + r; y++)
            {
                if ((uint)y >= H) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((uint)x >= W) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > BRUSH_R * BRUSH_R) continue;
                    int i = y * W + x;
                    if (_holes[i]) continue;
                    _holes[i] = true;
                    _px[i].a = 0;
                    newly++;
                }
            }
            if (newly == 0) return;

            _cleared += newly;
            _tex.SetPixels32(_px);
            _tex.Apply(false);

            if (Time.unscaledTime - _lastSfx > SFX_EVERY)
            {
                _lastSfx = Time.unscaledTime;
                Sfx.Play("tap", 0.35f);   // stand-in scratch tick until a real scratch clip exists
            }

            if (_cleared >= REVEAL_AT * _px.Length) Reveal();
        }

        void Reveal()
        {
            _revealed = true;
            gameObject.SetActive(false);   // remaining foil clears in one satisfying pop
            onRevealed?.Invoke();
        }
    }
}
