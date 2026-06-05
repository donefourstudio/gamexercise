using UnityEngine;

namespace Gamex.Pose
{
    // Orientation-invariant pushup counter driven by 2D elbow angle.
    // Doesn't care which way the camera is pointing — only the interior
    // angle at the elbow matters: ~90° at the bottom of a pushup, ~180°
    // when arms are extended at the top.
    //
    // State machine (hysteresis prevents flicker at the thresholds):
    //   Unknown -> arm extended (>160°) -> Up   (no rep)
    //   Up      -> arm bent     (<110°) -> Down (no rep)
    //   Down    -> arm extended (>160°) -> Up   (+1 REP)
    //
    // Uses whichever elbow is visible (averages if both are). Returns
    // NaN when neither elbow has confident shoulder+elbow+wrist data.
    public class PushupCounter
    {
        public enum State { Unknown, Up, Down }
        public State CurrentState { get; private set; } = State.Unknown;
        public float LastAngle { get; private set; } = float.NaN;

        const float DOWN_THRESHOLD = 110f;
        const float UP_THRESHOLD   = 160f;
        const float MIN_SCORE      = 0.3f;

        // Returns true on the frame a full rep (Down -> Up cycle) completes.
        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float angle = AvgElbowAngle(kps);
            LastAngle = angle;
            if (float.IsNaN(angle)) return false;

            if (angle < DOWN_THRESHOLD)
            {
                if (CurrentState != State.Down) CurrentState = State.Down;
            }
            else if (angle > UP_THRESHOLD)
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

        // Interior angle at elbow (180° = arm straight, 90° = elbow at right angle).
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
            float l = ElbowAngle(kps[5], kps[7], kps[9]);    // left
            float r = ElbowAngle(kps[6], kps[8], kps[10]);   // right
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }
    }
}
