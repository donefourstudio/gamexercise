using UnityEngine;
#if !UNITY_EDITOR
using Sentry;
using Sentry.Unity;
#endif

namespace Gamex.Platform
{
    // Sentry crash + error reporting init. Runs before any scene loads so
    // the SDK is ready to capture exceptions during Unity boot itself, not
    // just gameplay. Code-only configuration (no SentryOptions.asset in
    // Resources/) keeps the DSN versioned in source and avoids the Editor
    // Tools > Sentry wizard step that the SDK normally walks the dev
    // through; nothing about the wizard's output isn't expressible here.
    //
    // Privacy posture (must agree with docs/privacy-policy.html section
    // 3): Sentry receives only diagnostic data — exception messages,
    // stack traces, device model + OS + locale + app version, plus the
    // last N log lines as breadcrumbs. It does NOT receive HealthKit
    // step data, game save contents, or any user identifier (we don't
    // have accounts, IDFA collection is off, IP is stripped server-side
    // by default in Sentry's options below).
    public static class SentryConfig
    {
        // Sentry project DSN — public identifier, safe to commit. (Real
        // secrets — like Sentry auth tokens for symbol upload — live
        // elsewhere and are NOT in source.)
        const string DSN = "https://a8e99ea26fe65169b5f83d78b728c303@o4511534376550400.ingest.us.sentry.io/4511534395424768";

        // Sentry's RuntimeInitializeOnLoadMethod hook fires before any
        // GameObject Awake; init here so the SDK can catch exceptions
        // from RuntimeInitializeOnLoadMethod handlers in OTHER scripts
        // (e.g. GameRunner.Boot) too.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
#if !UNITY_EDITOR
            // Sentry Unity 4.x renamed the init helper from SentryUnity.Init
            // to SentrySdk.Init (with a SentryUnityOptions builder). The
            // configure-lambda shape is the same. See SentryInitialization.cs
            // in the io.sentry.unity package for the canonical 4.x pattern.
            SentrySdk.Init(options =>
            {
                options.Dsn = DSN;

                // Strip IP address server-side. Sentry can geolocate from
                // IP if SendDefaultPii is true; we keep it false so no
                // location data is ever stored against the event.
                options.SendDefaultPii = false;

                // Disable performance monitoring sampling — we only want
                // crash + error reports for now. If we enable later, this
                // is a 0.0-1.0 fraction of transactions traced.
                options.TracesSampleRate = 0.0f;

                // Tag every event with the build's version + platform so
                // we can filter by app version in the Sentry dashboard.
                options.Release = Application.version;
                options.Environment = Debug.isDebugBuild ? "development" : "production";

                // Auto-session tracking gives Sentry "crash-free sessions"
                // metric out of the box — % of app launches that finish
                // without a crash. Free + useful.
                options.AutoSessionTracking = true;

                // Cap breadcrumb buffer so we don't ship megabytes of log
                // history on a crash. 100 events is the SDK default but
                // we pin it explicitly so it doesn't drift across SDK
                // upgrades.
                options.MaxBreadcrumbs = 100;
            });
#endif
            // In the Editor we no-op — the SDK pollutes the Console with
            // init logs every play-mode entry and we don't ship from
            // Editor anyway. Real-device + TestFlight builds get full
            // capture coverage.
        }
    }
}
