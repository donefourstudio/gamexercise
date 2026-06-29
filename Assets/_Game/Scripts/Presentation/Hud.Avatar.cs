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
        // Avatar — LPC portrait sprite + equipment overlays (M5e)
        // ============================================================
        public class AvatarSprite
        {
            public GameObject root;
            public Image portrait;
            // 6 equipment slots, all stacked on top of the portrait in the same rect.
            // The sprites themselves are 256x256 with content only in the right
            // anatomical region, so layering "just works".
            public Image sword;
            public Image armor;
            public Image helmet;
            public Image leggings;
            public Image gauntlets;
            public Image boots;

            public void SetAlpha(float a)
            {
                Set(portrait, a);
                Set(sword, a); Set(armor, a); Set(helmet, a);
                Set(leggings, a); Set(gauntlets, a); Set(boots, a);
            }
            static void Set(Image img, float a)
            {
                if (img == null) return;
                var c = img.color; img.color = new Color(c.r, c.g, c.b, a);
            }
        }

        AvatarSprite BuildAvatar(Transform parent, Vector2 anchoredPos, float scale,
                                 Gender gender, Curse curse, int stage, Race race = Race.Unset)
        {
            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var rrt = root.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.anchoredPosition = anchoredPos;
            rrt.sizeDelta = new Vector2(256f, 256f) * scale;
            rrt.localScale = Vector3.one;

            Image MkLayer(string name, bool hiddenInitially)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                var img = go.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                if (hiddenInitially) go.SetActive(false);   // wholly disabled until SetOverlay activates
                return img;
            }

            var avatar = new AvatarSprite { root = root };
            avatar.portrait  = MkLayer("Portrait",  false);
            avatar.armor     = MkLayer("Armor",     true);   // torso layer (over body)
            avatar.leggings  = MkLayer("Leggings",  true);
            avatar.boots     = MkLayer("Boots",     true);
            avatar.gauntlets = MkLayer("Gauntlets", true);
            avatar.helmet    = MkLayer("Helmet",    true);
            avatar.sword     = MkLayer("Sword",     true);

            ApplyAvatarLook(avatar, gender, curse, race, stage);
            return avatar;
        }

        void ApplyAvatarLook(AvatarSprite avatar, Gender gender, Curse curse, Race race, int stage,
                             List<string> equipped = null, string activeSkin = null)
        {
            if (avatar == null || avatar.portrait == null) return;

            // Three render paths, in priority order:
            //   1. Skin active — full-body skin sprite, no overlays
            //   2. Outfit fully equipped — full-body set composite, no overlays
            //   3. Race form base — race-form sprite + per-slot equipment overlays
            //
            // (2) is the regression fix for "outfit overlays leak the underlying
            // race form's hair / skin / accessories". Every shop set and the
            // Knight Set has a pre-baked Sets/<id>.png composite, so when the
            // player has a fully matching set on we render that one sprite and
            // skip the overlay routing entirely — same shape as the skin path.
            bool skinActive = !string.IsNullOrEmpty(activeSkin) && Make.Skin(activeSkin) != null;
            // Outfit/skin composite is allowed in ALL phases (incl. curse stages):
            // if the player owns a full Set they can wear it over the cursed
            // body. The race-form-only gate used to live here; it's gone so
            // skeleton/flesh stages can render the outfit composite directly.
            // Partial equipment (1-2 pieces) still falls through to the
            // race-form path below, which gates overlays on race awakening.
            var activeOutfit = !skinActive ? GamexGame.FindActiveOutfit(equipped) : null;
            Sprite outfitSprite = activeOutfit != null ? Make.SetPreview(activeOutfit.id) : null;

            if (skinActive || outfitSprite != null)
            {
                avatar.portrait.sprite = outfitSprite != null ? outfitSprite : Make.Skin(activeSkin);
                float aOverride = avatar.portrait.color.a;
                avatar.portrait.color = new Color(1f, 1f, 1f, aOverride);
                SetOverlay(avatar.sword,     null, aOverride);
                SetOverlay(avatar.armor,     null, aOverride);
                SetOverlay(avatar.helmet,    null, aOverride);
                SetOverlay(avatar.leggings,  null, aOverride);
                SetOverlay(avatar.gauntlets, null, aOverride);
                SetOverlay(avatar.boots,     null, aOverride);
                return;
            }

            // Race-form path — partial / non-set equipment overlaid on the
            // bare race form. The bald variant kicks in when a Head slot is
            // occupied (helmet on a smooth scalp instead of through hair).
            bool helmetEquipped = false;
            if (race != Race.Unset && equipped != null)
            {
                foreach (var id in equipped)
                    if (GamexGame.SlotOf(id) == GamexGame.EquipSlot.Head) { helmetEquipped = true; break; }
            }
            avatar.portrait.sprite = Make.Portrait(
                gender == Gender.Unset ? Gender.Male : gender, curse, race, stage, activeSkin, helmetEquipped);
            float a = avatar.portrait.color.a;
            avatar.portrait.color = new Color(1f, 1f, 1f, a);

            string swordId = null, armorId = null, helmetId = null,
                   leggingsId = null, gauntletsId = null, bootsId = null;
            if (race != Race.Unset && equipped != null)
            {
                foreach (var id in equipped)
                {
                    switch (GamexGame.SlotOf(id))
                    {
                        case GamexGame.EquipSlot.Weapon: swordId     = id; break;
                        case GamexGame.EquipSlot.Chest:  armorId     = id; break;
                        case GamexGame.EquipSlot.Head:   helmetId    = id; break;
                        case GamexGame.EquipSlot.Legs:   leggingsId  = id; break;
                        case GamexGame.EquipSlot.Wrists: gauntletsId = id; break;
                        case GamexGame.EquipSlot.Feet:   bootsId     = id; break;
                    }
                }
            }
            SetOverlay(avatar.sword,     swordId,     a);
            SetOverlay(avatar.armor,     armorId,     a);
            SetOverlay(avatar.helmet,    helmetId,    a);
            SetOverlay(avatar.leggings,  leggingsId,  a);
            SetOverlay(avatar.gauntlets, gauntletsId, a);
            SetOverlay(avatar.boots,     bootsId,     a);
        }

        static void SetOverlay(Image ov, string id, float alpha)
        {
            if (ov == null) return;
            if (id == null)
            {
                if (ov.gameObject.activeSelf) ov.gameObject.SetActive(false);
                return;
            }
            var spr = Make.Equipment(id);
            if (spr == null)
            {
                // Asset not imported yet — keep the overlay disabled so we don't
                // get the default white-quad render.
                if (ov.gameObject.activeSelf) ov.gameObject.SetActive(false);
                return;
            }
            if (!ov.gameObject.activeSelf) ov.gameObject.SetActive(true);
            ov.sprite = spr;
            ov.color  = new Color(1f, 1f, 1f, alpha);
        }

    }
}
