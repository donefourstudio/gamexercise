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
        public float LastAngle { get; private set; } = float.NaN;          // smoothed
        public float RawLastAngle { get; private set; } = float.NaN;       // unsmoothed (debug)

        // VERY permissive — 5° dead band on smoothed signal. Any noticeable
        // dip + return counts. Trades false positives for sensitivity (Jackson's
        // playtest showed 1/4 detection rate; the smaller the band, the more
        // borderline reps get caught).
        const float DOWN_THRESHOLD = 155f;
        const float UP_THRESHOLD   = 160f;
        const float MIN_SCORE      = 0.2f;
        const float SMOOTH_ALPHA   = 0.35f;

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float raw = AvgElbowAngle(kps);
            RawLastAngle = raw;
            if (float.IsNaN(raw)) return false;

            // EWMA smoothing reduces phantom transitions from per-frame noise.
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
