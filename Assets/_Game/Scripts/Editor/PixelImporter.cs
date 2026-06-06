#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Gamex.EditorTools
{
    // Auto-applies pixel-art import settings to anything under Resources/UI|Char|Equip|Icons.
    // Filter=Point keeps edges crisp at any scale; no mipmaps / no compression
    // preserves the original pixels. 9-slice border is set for UI panels/buttons.
    public class PixelImporter : AssetPostprocessor
    {
        const float PPU = 16f;
        const int SLICE_BORDER = 8;   // Kenney Ancient theme: ~8px corner border on 48x48 tiles

        void OnPreprocessTexture()
        {
            if (assetImporter is not TextureImporter ti) return;
            string p = assetPath.Replace('\\', '/');
            bool isUI    = p.Contains("/Resources/UI/");
            bool isChar  = p.Contains("/Resources/Char/");
            bool isEquip = p.Contains("/Resources/Equip/");
            bool isIcons = p.Contains("/Resources/Icons/");
            if (!isUI && !isChar && !isEquip && !isIcons) return;

            ti.textureType        = TextureImporterType.Sprite;
            ti.spriteImportMode   = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PPU;
            ti.filterMode         = FilterMode.Point;
            ti.mipmapEnabled      = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaIsTransparency = true;
            ti.wrapMode           = TextureWrapMode.Clamp;

            // Equipment overlays are 256x256 with content only in a small region —
            // force FullRect mesh so the sprite renders at its full canvas size
            // (not cropped to visible bounds, which would offset alignment).
            if (isEquip)
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
            }

            // 9-slice for stretchable panels and buttons
            if (isUI && (System.IO.Path.GetFileName(p).StartsWith("btn_") ||
                         System.IO.Path.GetFileName(p).StartsWith("panel")))
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteBorder = new Vector4(SLICE_BORDER, SLICE_BORDER, SLICE_BORDER, SLICE_BORDER);
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
            }
        }
    }
}
#endif
