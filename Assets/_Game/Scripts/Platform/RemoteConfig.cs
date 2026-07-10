using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Gamex.Platform
{
    // Tiny remote-config layer. Fetches a single JSON file from our GitHub
    // Pages site on app launch and applies its values; falls back to a
    // cached PlayerPrefs copy if the network is unavailable. The whole
    // surface is one boolean today (filterManualEntries — anti-cheat for
    // HealthKit faking) so the implementation is deliberately minimal.
    //
    // Why this exists: shipping v1.0 with HealthKit manual-entry filtering
    // ON would block App Review (reviewers can't generate sensor data on
    // their lab phones). Shipping it OFF and never flipping it leaves the
    // anti-cheat door wide open. Remote config lets us ship OFF, then flip
    // ON later by editing the JSON in the donefourstudio.github.io repo —
    // no resubmission needed.
    //
    // Privacy posture: the fetch is a single GET to a static .json file
    // with no query params, no custom headers, no user identifiers. We
    // don't log who fetched what or when. GitHub Pages logs the request
    // IP per its standard policy, which is consistent with the existing
    // privacy policy disclosure that GitHub Pages serves the policy URL.
    public class RemoteConfig : MonoBehaviour
    {
        const string CONFIG_URL = "https://donefourstudio.github.io/gamexercise/config.json";
        const string CACHE_KEY  = "remote_config_filter_manual_v1";
        const int    TIMEOUT_S  = 10;

        // Read by HealthKit auth-cheat filtering. Always false at process
        // start; the cached PlayerPrefs value is applied as soon as
        // EnsureStarted runs, and the live fetch overwrites it once the
        // GET completes.
        public static bool FilterManualEntries { get; private set; }

        // Casino section flag (docs/casino-mvp-plan.md). Gates ONLY the
        // Home-screen CASINO button + its screens — ticket accrual runs
        // regardless (harmless while hidden). Cache-only by design: the Hud
        // reads this once at construction, so a fetched change takes effect
        // on the NEXT launch. The GAMEX_CASINO scripting define forces it
        // on for dev/QA builds; the Editor menu "Gamex > Casino" toggles
        // the same cached key for play-mode testing.
        public const string CACHE_KEY_CASINO = "remote_config_casino_v1";
        public static bool CasinoEnabled
        {
            get
            {
#if GAMEX_CASINO
                return true;
#else
                return PlayerPrefs.GetInt(CACHE_KEY_CASINO, 0) != 0;
#endif
            }
        }

        static RemoteConfig _instance;

        // Idempotent — safe to call from multiple entry points. GameRunner
        // hits it in Start(); subsequent calls are no-ops.
        public static void EnsureStarted()
        {
            if (_instance != null) return;
            var go = new GameObject("RemoteConfig");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<RemoteConfig>();
        }

        void Awake()
        {
            // Apply cached value immediately so the first HealthKit sync
            // of this session uses the last-known-good filter state even
            // if the network fetch is still in flight (or fails).
            FilterManualEntries = PlayerPrefs.GetInt(CACHE_KEY, 0) != 0;
            HealthKitBridge.SetFilterManualEntries(FilterManualEntries);
            StartCoroutine(Fetch());
        }

        IEnumerator Fetch()
        {
            using var req = UnityWebRequest.Get(CONFIG_URL);
            req.timeout = TIMEOUT_S;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                // Network failure / 404 / timeout — keep the cached value.
                // Logged as info, not error, so a transient outage doesn't
                // spam Sentry.
                Debug.Log("[RemoteConfig] fetch failed: " + req.error + " (using cached value)");
                yield break;
            }
            ConfigSchema parsed = null;
            try
            {
                parsed = JsonUtility.FromJson<ConfigSchema>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RemoteConfig] malformed JSON, keeping cached value: " + e.Message);
            }
            if (parsed == null) yield break;

            bool next = parsed.filterManualEntries;
            if (next != FilterManualEntries)
            {
                FilterManualEntries = next;
                PlayerPrefs.SetInt(CACHE_KEY, next ? 1 : 0);
                PlayerPrefs.Save();
                HealthKitBridge.SetFilterManualEntries(next);
                Debug.Log("[RemoteConfig] filterManualEntries -> " + next);
            }

            // Casino flag — cache for the NEXT launch only (see
            // CasinoEnabled above for why there's no live apply).
            bool casino = parsed.casinoEnabled;
            if (casino != (PlayerPrefs.GetInt(CACHE_KEY_CASINO, 0) != 0))
            {
                PlayerPrefs.SetInt(CACHE_KEY_CASINO, casino ? 1 : 0);
                PlayerPrefs.Save();
                Debug.Log("[RemoteConfig] casinoEnabled -> " + casino + " (applies next launch)");
            }
        }

        // Server JSON shape. Add fields here as future flags are needed.
        // JsonUtility ignores unknown fields, so the JSON can grow without
        // breaking older client builds.
        [Serializable]
        class ConfigSchema
        {
            public bool filterManualEntries;
            public bool casinoEnabled;   // shows/hides the Casino section
        }
    }
}
