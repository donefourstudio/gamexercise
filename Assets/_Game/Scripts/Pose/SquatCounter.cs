using UnityEngine;

namespace Gamex.Pose
{
    // Dynamic-range squat counter — same direction polarity as PushupCounter.
    // Standing is HIGH-angle (Up), squatting is LOW-angle (Down). Rep on the
    // Down -> Up transition (when the smoothed angle rises MIN_SWING above
    // the running min) = user just stood back up.
    public class SquatCounter
    {
        public enum State { Unknown, Up, Down }
        public State CurrentState { get; private set; } = State.Unknown;
        public float LastAngle    { get; private set; } = float.NaN;
        public float RawLastAngle { get; private set; } = float.NaN;
        public float RunningMin   { get; private set; } = float.PositiveInfinity;
        public float RunningMax   { get; private set; } = float.NegativeInfinity;

        const float MIN_SWING    = 20f;
        const float BOOTSTRAP    = 30f;
        const float MIN_SCORE    = 0.2f;
        const float SMOOTH_ALPHA = 0.35f;

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float raw = AvgKneeAngle(kps);
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
            else
            {
                if (a < RunningMin) RunningMin = a;
                if (a - RunningMin > MIN_SWING)
                {
                    CurrentState = State.Up;
                    RunningMax = a;
                    return true;          // REP — stood back up
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

        static float KneeAngle(PoseDetector.Keypoint h, PoseDetector.Keypoint k, PoseDetector.Keypoint a)
        {
            if (h.score < MIN_SCORE || k.score < MIN_SCORE || a.score < MIN_SCORE) return float.NaN;
            var kToH = new Vector2(h.x - k.x, h.y - k.y);
            var kToA = new Vector2(a.x - k.x, a.y - k.y);
            if (kToH.sqrMagnitude < 1e-6f || kToA.sqrMagnitude < 1e-6f) return float.NaN;
            float dot = Mathf.Clamp(Vector2.Dot(kToH.normalized, kToA.normalized), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        static float AvgKneeAngle(PoseDetector.Keypoint[] kps)
        {
            // BlazePose: 23/24 hips, 25/26 knees, 27/28 ankles.
            float l = KneeAngle(kps[23], kps[25], kps[27]);
            float r = KneeAngle(kps[24], kps[26], kps[28]);
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
