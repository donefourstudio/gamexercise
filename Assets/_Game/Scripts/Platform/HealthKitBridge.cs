using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Gamex.Platform
{
    // C# wrapper around the iOS HealthKit native plugin (Assets/Plugins/iOS/
    // HealthKitPlugin.mm). The native side completes auth + step queries on
    // a background queue and writes into atomic ints; HealthKitTicker
    // (auto-spawned on first API call) polls those ints once per frame and
    // fires the pending C# callback on the main thread.
    //
    // Non-iOS builds + Editor: every call is a no-op that immediately fires
    // the callback with a "pretend authorized, 0 steps" answer so the
    // Editor debug-key flow (+1000 steps, etc.) keeps working unchanged.
    public static class HealthKitBridge
    {
        public enum AuthStatus { NotDetermined = 0, Denied = 1, Authorized = 2 }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int  _HealthKitIsAvailable();
        [DllImport("__Internal")] static extern int  _HealthKitAuthStatus();
        [DllImport("__Internal")] static extern void _HealthKitRequestAuth();
        [DllImport("__Internal")] static extern int  _HealthKitGetAuthResult();
        [DllImport("__Internal")] static extern void _HealthKitQueryTodaySteps();
        [DllImport("__Internal")] static extern int  _HealthKitGetStepsResult();
#endif

        static GameObject _ticker;
        static Action<AuthStatus> _pendingAuth;
        static Action<int>        _pendingSteps;

        public static bool IsAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return _HealthKitIsAvailable() != 0;
#else
            return false;
#endif
        }

        public static AuthStatus CurrentStatus()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return (AuthStatus)_HealthKitAuthStatus();
#else
            return AuthStatus.NotDetermined;
#endif
        }

        // Triggers the OS HealthKit permission modal (first time only — iOS
        // remembers the answer and subsequent calls return immediately). The
        // callback fires once with the final status. Calling this when a
        // prior auth request is still pending overwrites the pending callback
        // (we only ever issue one at a time from the game's lifecycle hooks).
        public static void RequestAuthorization(Action<AuthStatus> callback)
        {
#if UNITY_IOS && !UNITY_EDITOR
            EnsureTicker();
            _pendingAuth = callback;
            _HealthKitRequestAuth();
#else
            callback?.Invoke(AuthStatus.NotDetermined);
#endif
        }

        // Queries today's step count (since local midnight) as one cumulative
        // sum across all HealthKit sources (phone + Apple Watch). Returns -1
        // via the callback if the query failed; otherwise the int count.
        public static void QueryTodaySteps(Action<int> callback)
        {
#if UNITY_IOS && !UNITY_EDITOR
            EnsureTicker();
            _pendingSteps = callback;
            _HealthKitQueryTodaySteps();
#else
            callback?.Invoke(0);
#endif
        }

        static void EnsureTicker()
        {
            if (_ticker != null) return;
            _ticker = new GameObject("HealthKitTicker");
            UnityEngine.Object.DontDestroyOnLoad(_ticker);
            _ticker.hideFlags = HideFlags.HideAndDontSave;
            _ticker.AddComponent<HealthKitTicker>();
        }

        internal static void Tick()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (_pendingAuth != null)
            {
                int r = _HealthKitGetAuthResult();
                if (r >= 0)
                {
                    var cb = _pendingAuth;
                    _pendingAuth = null;
                    cb?.Invoke((AuthStatus)r);
                }
            }
            if (_pendingSteps != null)
            {
                int r = _HealthKitGetStepsResult();
                if (r >= 0)
                {
                    var cb = _pendingSteps;
                    _pendingSteps = null;
                    cb?.Invoke(r);
                }
            }
#endif
        }
    }

    // Hidden MonoBehaviour that polls native results once per frame. Lives on
    // a DontDestroyOnLoad GameObject so the poll survives scene transitions.
    public class HealthKitTicker : MonoBehaviour
    {
        void Update() { HealthKitBridge.Tick(); }
    }
}
