using UnityEditor;
using UnityEngine;
using Gamex.Platform;

namespace Gamex.EditorTools
{
    // Dev toggle for the Fate Cards boot mode (M_fate Phase 0). Writes the
    // same PlayerPrefs key RemoteConfig caches its fetched flag into, so
    // Editor play mode follows this checkbox without a network flip or the
    // GAMEX_FATE define. Takes effect on the next Play — GameRunner latches
    // the flag once, in Awake.
    public static class FateFlagMenu
    {
        const string MENU = "Gamex/Fate Cards Mode (cached flag)";

        [MenuItem(MENU)]
        static void Toggle()
        {
            bool next = PlayerPrefs.GetInt(RemoteConfig.CACHE_KEY_FATE, 0) == 0;
            PlayerPrefs.SetInt(RemoteConfig.CACHE_KEY_FATE, next ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[FateFlagMenu] fateCardsEnabled (cached) -> " + next);
        }

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            Menu.SetChecked(MENU, PlayerPrefs.GetInt(RemoteConfig.CACHE_KEY_FATE, 0) != 0);
            return true;
        }
    }
}
