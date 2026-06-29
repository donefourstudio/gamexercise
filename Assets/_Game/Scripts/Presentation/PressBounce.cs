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
    // Shop-card press feedback. Attached to each shop card root; Trigger() runs
    // a quick "push down + bounce back" scale tween so taps feel committed.
    // Unscaled time keeps the bounce snappy even if a future menu pauses gameplay.
    public class PressBounce : UnityEngine.MonoBehaviour
    {
        const float DURATION  = 0.22f;
        const float SCALE_AMP = 0.07f;   // peak shrink amount
        const float SHRINK_PCT = 0.30f;  // first 30% of duration shrinks, rest expands back
        float _t;

        public void Trigger() { _t = DURATION; }

        void Update()
        {
            if (_t <= 0f) return;
            _t -= UnityEngine.Time.unscaledDeltaTime;
            float t01 = 1f - UnityEngine.Mathf.Clamp01(_t / DURATION);
            float s = t01 < SHRINK_PCT
                ? UnityEngine.Mathf.Lerp(1f, 1f - SCALE_AMP, t01 / SHRINK_PCT)
                : UnityEngine.Mathf.Lerp(1f - SCALE_AMP, 1f, (t01 - SHRINK_PCT) / (1f - SHRINK_PCT));
            transform.localScale = new UnityEngine.Vector3(s, s, 1f);
            if (_t <= 0f) transform.localScale = UnityEngine.Vector3.one;
        }
    }
}
