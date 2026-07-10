using UnityEditor;
using UnityEngine;
using Gamex.Platform;

namespace Gamex.EditorTools
{
    // Dev toggle for the Casino section (docs/casino-mvp-plan.md). Writes
    // the same PlayerPrefs key RemoteConfig caches its fetched flag into,
    // so Editor play mode follows this checkbox without a network flip or
    // the GAMEX_CASINO define. Takes effect on the next Play — the Hud
    // reads the flag once, at construction.
    public static class CasinoFlagMenu
    {
        const string MENU = "Gamex/Casino (cached flag)";

        [MenuItem(MENU)]
        static void Toggle()
        {
            bool next = PlayerPrefs.GetInt(RemoteConfig.CACHE_KEY_CASINO, 0) == 0;
            PlayerPrefs.SetInt(RemoteConfig.CACHE_KEY_CASINO, next ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("[CasinoFlagMenu] casinoEnabled (cached) -> " + next);
        }

        [MenuItem(MENU, true)]
        static bool Validate()
        {
            Menu.SetChecked(MENU, PlayerPrefs.GetInt(RemoteConfig.CACHE_KEY_CASINO, 0) != 0);
            return true;
        }
    }
}
