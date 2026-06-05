using UnityEngine;

namespace Gamex.Pose
{
    // Situp counter — uses the interior angle at the hip (the angle between the
    // shoulder->hip vector and the knee->hip vector, both emanating from the hip).
    //   Lying flat:  shoulder, hip, knee are roughly colinear  -> ~180°
    //   Sitting up:  torso vertical, thighs horizontal         -> ~ 90°
    //
    // State machine — rep on Down -> Up transition (with Down here meaning
    // "lying flat" since that's the start of a situp).
    public class SitupCounter
    {
        public enum State { Unknown, Down, Up }   // Down = lying, Up = sitting
        public State CurrentState { get; private set; } = State.Unknown;
        public float LastAngle { get; private set; } = float.NaN;

        // Loosened from 160/100 after first playtest — partial situps don't hit 100°.
        const float DOWN_THRESHOLD = 150f;
        const float UP_THRESHOLD   = 125f;
        const float MIN_SCORE      = 0.2f;

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float angle = AvgHipAngle(kps);
            LastAngle = angle;
            if (float.IsNaN(angle)) return false;

            if (angle > DOWN_THRESHOLD)
            {
                if (CurrentState != State.Down) CurrentState = State.Down;
            }
            else if (angle < UP_THRESHOLD)
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
        }

        // Interior angle at hip, both vectors emanating from the hip.
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
            float l = HipAngle(kps[5], kps[11], kps[13]);    // left shoulder/hip/knee
            float r = HipAngle(kps[6], kps[12], kps[14]);    // right
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
