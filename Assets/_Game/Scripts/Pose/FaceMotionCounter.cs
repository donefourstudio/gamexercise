using UnityEngine;

namespace Gamex.Pose
{
    // 基于人脸 Y 轴震荡的次数统计 —— 替代之前的肘/胯/膝关节角度法。
    //
    // 理论:
    //   BlazePose 在"识别人脸"上的鲁棒性远远高于"识别全身关节"。
    //   不管你做的是俯卧撑、仰卧、还是深蹲,只要相机能看到你的脸,
    //   你的脸就会规律性地上下震荡。我们追踪 face Y(归一化 0..1),
    //   找到峰谷反转点,数 reps。
    //
    // 优点:
    //   * 不依赖关节遮挡(脸是最不容易遮挡的部分)
    //   * 不关心 2D 角度歧义(只要 y 动了就算)
    //   * 不关心相机角度,只要脸在画面里
    //   * 同一套逻辑覆盖三个运动
    //
    // 缺点:
    //   * 完全静止做"假动作"(只动手不动脸)会漏数
    //   * 但这种情况罕见,真实运动头部一定会动
    public class FaceMotionCounter
    {
        public enum State { Unknown, High, Low }     // High = 脸在画面上方(y 小), Low = 脸在画面下方(y 大)
        public State CurrentState { get; private set; } = State.Unknown;
        public int Reps { get; private set; }

        public float CurrentY  { get; private set; } = float.NaN;       // 当帧脸 Y(平均)
        public float SmoothedY { get; private set; } = float.NaN;       // EWMA 后
        public float RunningMin { get; private set; } = float.PositiveInfinity;  // y 最小值(最高位)
        public float RunningMax { get; private set; } = float.NegativeInfinity;  // y 最大值(最低位)
        public float Confidence { get; private set; }                   // 脸部关键点平均置信度

        // 配置
        const float MIN_SCORE      = 0.30f;
        const float SMOOTH_ALPHA   = 0.40f;
        const float MIN_SWING      = 0.06f;   // 至少 6% 画面高度的反转幅度才算一次反转
        const float BOOTSTRAP      = 0.10f;   // 启动期需要看到 10% 高度的运动才锁状态

        // 计数策略:
        //   1) 启动期(Unknown):跟踪 min/max,直到看到足够幅度运动 → 进入 High 或 Low
        //   2) High 状态:跟踪新的 min(更高的位置)。如果 y 比 min 大了 MIN_SWING → Low 状态
        //   3) Low  状态:跟踪新的 max(更低的位置)。如果 y 比 max 小了 MIN_SWING → High 状态,REP+1
        //
        //   只在 Low → High 转换时计数,确保 1 个完整 high-low-high 周期只算一次。
        //   不管起始位置是 High 还是 Low,玩家做完一个完整动作总会触发一次 Low → High。

        public bool Update(PoseDetector.Keypoint[] kps)
        {
            float y = AvgFaceY(kps);
            CurrentY = y;
            if (float.IsNaN(y)) return false;

            SmoothedY = float.IsNaN(SmoothedY)
                ? y
                : SmoothedY * (1f - SMOOTH_ALPHA) + y * SMOOTH_ALPHA;
            float sy = SmoothedY;

            if (CurrentState == State.Unknown)
            {
                if (sy < RunningMin) RunningMin = sy;
                if (sy > RunningMax) RunningMax = sy;
                if (RunningMax - RunningMin >= BOOTSTRAP)
                {
                    float mid = (RunningMin + RunningMax) * 0.5f;
                    CurrentState = sy < mid ? State.High : State.Low;
                }
                return false;
            }

            if (CurrentState == State.High)
            {
                if (sy < RunningMin) RunningMin = sy;
                if (sy - RunningMin > MIN_SWING)
                {
                    CurrentState = State.Low;
                    RunningMax = sy;
                }
            }
            else // Low
            {
                if (sy > RunningMax) RunningMax = sy;
                if (RunningMax - sy > MIN_SWING)
                {
                    CurrentState = State.High;
                    RunningMin = sy;
                    Reps++;
                    return true;          // REP!
                }
            }
            return false;
        }

        public void Reset()
        {
            CurrentState = State.Unknown;
            Reps = 0;
            CurrentY = SmoothedY = float.NaN;
            RunningMin = float.PositiveInfinity;
            RunningMax = float.NegativeInfinity;
            Confidence = 0f;
        }

        // 平均所有可见的人脸关键点(BlazePose 索引 0..10:鼻/眼/耳/嘴)的 Y 坐标。
        // 这样脸局部遮挡(比如下压时鼻子被胳膊挡了)依然能取到信号。
        float AvgFaceY(PoseDetector.Keypoint[] kps)
        {
            float sumY = 0f, sumScore = 0f;
            int n = 0;
            for (int i = 0; i <= 10; i++)
            {
                if (kps[i].score >= MIN_SCORE)
                {
                    sumY += kps[i].y;
                    sumScore += kps[i].score;
                    n++;
                }
            }
            if (n == 0) { Confidence = 0f; return float.NaN; }
            Confidence = sumScore / n;
            return sumY / n;
        }
    }
}
