using UnityEngine;

namespace Gamex.Pose
{
    // Dynamic-range situp counter. Same shape as PushupCounter but the
    // direction is inverted: lying flat is the HIGH-angle (Down) state,
    // sitting up is the LOW-angle (Up) state, so a rep completes when
    // the smoothed angle DROPS MIN_SWING below the running max
    // (i.e. user just crunched up).
    public class SitupCounter
    {
        public enum State { Unknown, Down, Up }   // Down = lying, Up = sitting
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
            float raw = AvgHipAngle(kps);
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
                    CurrentState = a > mid ? State.Down : State.Up;
                }
                return false;
            }

            if (CurrentState == State.Down)   // lying — tracking max angle
            {
                if (a > RunningMax) RunningMax = a;
                if (RunningMax - a > MIN_SWING)
                {
                    CurrentState = State.Up;
                    RunningMin = a;
                    return true;          // REP — just crunched up
                }
            }
            else // Up — sitting, tracking min angle
            {
                if (a < RunningMin) RunningMin = a;
                if (a - RunningMin > MIN_SWING)
                {
                    CurrentState = State.Down;
                    RunningMax = a;
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

        static float HipAngle(PoseDetector.Keypoint s, PoseDetector.Keypoint h, PoseDetector.Keypoint k)
        {
            if (s.score < MIN_SCORE || h.score < MIN_SCORE || k.score < MIN_SCORE) return float.NaN;
            var hToS = new Vector2(s.x - h.x, s.y - h.y);
            var hToK = new Vector2(k.x - h.x, k.y - h.y);
            if (hToS.sqrMagnitude < 1e-6f || hToK.sqrMagnitude < 1e-6f) return float.NaN;
            float dot = Mathf.Clamp(Vector2.Dot(hToS.normalized, hToK.normalized), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        static float AvgHipAngle(PoseDetector.Keypoint[] kps)
        {
            // BlazePose: 11/12 shoulders, 23/24 hips, 25/26 knees.
            float l = HipAngle(kps[11], kps[23], kps[25]);
            float r = HipAngle(kps[12], kps[24], kps[26]);
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
