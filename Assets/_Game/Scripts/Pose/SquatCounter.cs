using UnityEngine;

namespace Gamex.Pose
{
    // Squat counter — uses the interior angle at the knee (between hip->knee
    // and ankle->knee vectors, both emanating from the knee).
    //   Standing:  hip, knee, ankle roughly colinear  -> ~180°
    //   Squatting: thigh ~horizontal, shin vertical   -> ~ 90°
    //
    // State machine: Down = squatting, Up = standing. Rep on Down -> Up.
    public class SquatCounter
    {
        public enum State { Unknown, Up, Down }
        public State CurrentState { get; private set; } = State.Unknown;
        public float LastAngle { get; private set; } = float.NaN;
        public float RawLastAngle { get; private set; } = float.NaN;

        const float UP_THRESHOLD   = 150f;
        const float DOWN_THRESHOLD = 140f;
        const float MIN_SCORE      = 0.2f;
        const float SMOOTH_ALPHA   = 0.35f;

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float raw = AvgKneeAngle(kps);
            RawLastAngle = raw;
            if (float.IsNaN(raw)) return false;

            LastAngle = float.IsNaN(LastAngle) ? raw : LastAngle * (1f - SMOOTH_ALPHA) + raw * SMOOTH_ALPHA;
            float a = LastAngle;

            if (a < DOWN_THRESHOLD)
            {
                if (CurrentState != State.Down) CurrentState = State.Down;
            }
            else if (a > UP_THRESHOLD)
            {
                bool completedRep = CurrentState == State.Down;
                CurrentState = State.Up;
                return completedRep;
            }
            return false;
        }

        public void Reset()
        {
            CurrentState = State.Unknown;
            LastAngle = float.NaN;
            RawLastAngle = float.NaN;
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
            float l = KneeAngle(kps[11], kps[13], kps[15]);   // left hip/knee/ankle
            float r = KneeAngle(kps[12], kps[14], kps[16]);
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
