using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using Gamex.Core;
using Gamex.Platform;

namespace Gamex.Game
{
    public partial class Hud
    {
        // ============================================================
        // UI factories
        // ============================================================
        // MkText now produces TMP_Text (SDF) instead of UI.Text (bitmap). Every
        // caller that types its receiver as TMP_Text (or var) auto-inherits
        // pixel-crisp rendering at non-integer canvas scale. The signature
        // keeps TextAnchor for backwards compatibility so 57+ callsites don't
        // need touching — we translate to TMP's TextAlignmentOptions internally.
        static TMP_Text MkText(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                               int fontSize, TextAnchor align, Color color)
            => MkTextTMP(name, parent, anchor, pos, size, fontSize, ToTMPAlign(align), color);

        static TextAlignmentOptions ToTMPAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
                default:                      return TextAlignmentOptions.Center;
            }
        }

        // Runtime-generated SDF font asset from Cubic_11. Rasterised at
        // 8x the native 11px design size into a 1024x1024 SDFAA atlas, then
        // re-used across every TMP text in the session. SDFAA means the GPU
        // shader samples a signed-distance field instead of the raw glyph
        // bitmap, so the same font asset renders pixel-crisp at any output
        // scale — the dodge for "App Store screenshot at 1.447x canvas
        // scale shows bilinear gray edges" without redesigning the canvas.
        static TMP_FontAsset _cubicSDFCached;
        static TMP_FontAsset GetCubicSDF()
        {
            if (_cubicSDFCached != null) return _cubicSDFCached;
            var ttf = Resources.Load<Font>("Fonts/Cubic_11");
            if (ttf == null)
            {
                Debug.LogWarning("[Hud] Cubic_11.ttf missing from Resources/Fonts — falling back to TMP default");
                return null;
            }
            _cubicSDFCached = TMP_FontAsset.CreateFontAsset(
                ttf,
                samplingPointSize: 88,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);
            _cubicSDFCached.name = "Cubic_11_SDF_Runtime";
            return _cubicSDFCached;
        }

        // Pixel-art green ✓ generated at startup. Cubic 11 doesn't ship a
        // U+2713 glyph and the SDF atlas can only bake what's in the font,
        // so we paint a 14x14 mark directly into a Texture2D and wrap it
        // as a Sprite for UI Image use.
        static Sprite _checkmarkSpriteCached;
        static Sprite GetCheckmarkSprite()
        {
            if (_checkmarkSpriteCached != null) return _checkmarkSpriteCached;
            const int W = 14, H = 14;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            var c = new Color32(123, 202, 63, 255); // pixel-tasteful spring green
            // ✓ traced as two diagonals: short up-right from (2,5)->(5,2), long up-right from (5,2)->(12,9)
            (int x, int y)[] mark = {
                (2,5),(3,5),(2,4),(3,4),
                (3,4),(4,3),(3,3),(4,2),
                (4,2),(5,1),(5,2),
                (6,3),(7,4),(8,5),(9,6),(10,7),(11,8),(12,9),
                (6,2),(7,3),(8,4),(9,5),(10,6),(11,7),
            };
            for (int i = 0; i < W * H; i++) px[i] = new Color32(0,0,0,0);
            foreach (var (x, y) in mark)
                if ((uint)x < W && (uint)y < H) px[y * W + x] = c;
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _checkmarkSpriteCached = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
            _checkmarkSpriteCached.name = "CheckmarkRuntime";
            return _checkmarkSpriteCached;
        }

        // TMP's underlay (drop-shadow) shader feature. Off by default — to
        // enable we have to (a) flip the UNDERLAY_ON keyword on the font
        // material instance and (b) push the offset/dilate/softness props.
        // Hard 0 softness keeps the shadow pixel-edged so it doesn't clash
        // with the rest of the pixel-art aesthetic. Accessing fontMaterial
        // implicitly instances a per-component material, so each text gets
        // its own underlay without polluting the shared SDF asset's mat.
        static void ApplyUnderlay(TMP_Text text, float dilate, float offsetX, float offsetY)
        {
            var mat = text.fontMaterial;
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 1f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, offsetX);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offsetY);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
        }

        // TMP analog of MkText. Same anchor/size/fontSize contract, but the
        // returned component is a TextMeshProUGUI and the alignment enum is
        // TMP's own (not TextAnchor). Only used on the Title screen so far —
        // first piece of the bitmap-font-to-SDF migration.
        static TMP_Text MkTextTMP(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size,
                                  int fontSize, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            var sdf = GetCubicSDF();
            if (sdf != null) t.font = sdf;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = false;
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
                                   string spriteName = "btn_brown", string pressedSpriteName = "btn_brown_down",
                                   string sfx = "tap")
        {
            var go = MkSpritePanel(name, parent, anchor, pos, size, spriteName, Color.white);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.transition = Selectable.Transition.SpriteSwap;
            var ss = btn.spriteState;
            ss.pressedSprite = Make.UI(pressedSpriteName);
            ss.highlightedSprite = Make.UI(spriteName);
            btn.spriteState = ss;
            string clickSfx = sfx;
            btn.onClick.AddListener(() => { Sfx.Play(clickSfx); onClick?.Invoke(); });

            var t = MkText("Label", go.transform, new Vector2(0.5f, 0.5f), Vector2.zero,
                size, FS_BTN, TextAnchor.MiddleCenter, new Color(0.20f, 0.12f, 0.05f));
            t.text = label;
            return go;
        }
    }
}
