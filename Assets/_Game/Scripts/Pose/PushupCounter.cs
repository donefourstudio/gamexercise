using UnityEngine;

namespace Gamex.Pose
{
    // Dynamic-range pushup counter — calibrates to the player's actual ROM.
    //
    // We stop comparing angles against fixed thresholds (145° / 150° etc.) and
    // instead watch for *direction reversals* of the smoothed elbow angle:
    //   While extending (Up): track the running maximum. When the smoothed
    //     angle drops MIN_SWING below that max, we've started a descent -> Down.
    //   While bending (Down): track the running minimum. When the smoothed
    //     angle rises MIN_SWING above that min, the rep is complete -> Up + REP.
    //
    // This auto-adapts to whatever range the user can achieve — a shallow
    // 20° pushup counts the same as a full 90° one, as long as the motion
    // is consistent. The trade-off is that "twitchy" motion below MIN_SWING
    // never counts (deliberate: filters jitter), and the first half-cycle
    // is consumed bootstrapping the extremes (one "rep" sacrificed at start).
    public class PushupCounter
    {
        public enum State { Unknown, Up, Down }
        public State CurrentState { get; private set; } = State.Unknown;
        public float LastAngle    { get; private set; } = float.NaN;      // smoothed
        public float RawLastAngle { get; private set; } = float.NaN;
        public float RunningMin   { get; private set; } = float.PositiveInfinity;
        public float RunningMax   { get; private set; } = float.NegativeInfinity;

        const float MIN_SWING    = 15f;
        const float BOOTSTRAP    = 22f;          // need ~1.5x MIN_SWING of motion before locking in
        const float MIN_SCORE    = 0.2f;
        const float SMOOTH_ALPHA = 0.35f;

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float raw = AvgElbowAngle(kps);
            RawLastAngle = raw;
            if (float.IsNaN(raw)) return false;

            LastAngle = float.IsNaN(LastAngle) ? raw : LastAngle * (1f - SMOOTH_ALPHA) + raw * SMOOTH_ALPHA;
            float a = LastAngle;

            if (CurrentState == State.Unknown)
            {
                if (a < RunningMin) RunningMin = a;
                if (a > RunningMax) RunningMax = a;
                if (RunningMax - RunningMin >= BOOTSTRAP)
                {
                    float mid = (RunningMin + RunningMax) * 0.5f;
                    CurrentState = a > mid ? State.Up : State.Down;
                }
                return false;
            }

            if (CurrentState == State.Up)
            {
                if (a > RunningMax) RunningMax = a;
                if (RunningMax - a > MIN_SWING)
                {
                    CurrentState = State.Down;
                    RunningMin = a;
                }
            }
            else // Down
            {
                if (a < RunningMin) RunningMin = a;
                if (a - RunningMin > MIN_SWING)
                {
                    CurrentState = State.Up;
                    RunningMax = a;
                    return true;          // REP
                }
            }
            return false;
        }

        public void Reset()
        {
            CurrentState = State.Unknown;
            LastAngle = float.NaN;
            RawLastAngle = float.NaN;
            RunningMin = float.PositiveInfinity;
            RunningMax = float.NegativeInfinity;
        }

        static float ElbowAngle(PoseDetector.Keypoint s, PoseDetector.Keypoint e, PoseDetector.Keypoint w)
        {
            if (s.score < MIN_SCORE || e.score < MIN_SCORE || w.score < MIN_SCORE) return float.NaN;
            var sToE = new Vector2(e.x - s.x, e.y - s.y);
            var eToW = new Vector2(w.x - e.x, w.y - e.y);
            if (sToE.sqrMagnitude < 1e-6f || eToW.sqrMagnitude < 1e-6f) return float.NaN;
            float dot = Mathf.Clamp(Vector2.Dot(sToE.normalized, eToW.normalized), -1f, 1f);
            return 180f - Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        static float AvgElbowAngle(PoseDetector.Keypoint[] kps)
        {
            // BlazePose indices: 11/12 shoulders, 13/14 elbows, 15/16 wrists.
            float l = ElbowAngle(kps[11], kps[13], kps[15]);
            float r = ElbowAngle(kps[12], kps[14], kps[16]);
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
