using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gamex.Pose
{
    // ================================================================
    // 自适应俯卧撑识别系统(纯 C# 状态机)
    // ----------------------------------------------------------------
    // 三个模块串成一个会话(Session):
    //   模块1  双重对齐系统  -> 用虚线框 + 红/黄/绿全屏背景灯指挥玩家入位
    //   模块2  动态标定      -> 玩家做一次标准俯卧撑,系统记下 MinAngle/MaxAngle
    //   模块3  动态阈值 FSM  -> 用百分比判定"有效下压/有效撑起",
    //                          同时识别"半途而废"的错误动作并触发纠错反馈
    //
    // 此类不直接调用 AudioSource 或操作 UI —— 所有反馈通过 Action 回调发出,
    // 由 Hud 那层把回调绑到声音/颜色变化上。这样:
    //   * 单元测试可以不依赖 Unity 主循环;
    //   * 反馈方式以后想换(例如震动 → 闪屏)只改绑定不改逻辑;
    //   * 三个模块的所有状态/阈值/计数对外暴露,UI 想读什么就读什么。
    // ================================================================
    public class AdaptivePushupSystem
    {
        // ============================================================
        //  对外的会话状态(SessionState)
        // ============================================================
        public enum SessionState
        {
            WaitingForAlignment,    // 模块1 —— 玩家还没站位,全屏红光
            AlignmentCountdown,     // 模块1 —— 进入虚线框,绿灯保持 2s
            CalibrationPrompt,      // 模块2 —— 系统提示"请做一次标准俯卧撑"
            Calibrating,            // 模块2 —— 抓 high→low→high 一个周期
            ReadyToCount,           // 模块2 —— 标定完成,放 Ready-Go 音
            Counting,               // 模块3 —— 正式计数
        }

        // ============================================================
        //  对外的对齐档位(AlignmentTier)
        // ============================================================
        public enum AlignmentTier { Red, Yellow, Green }

        // ============================================================
        //  对外可读的实时状态
        // ============================================================
        public SessionState   State        { get; private set; } = SessionState.WaitingForAlignment;
        public AlignmentTier  Alignment    { get; private set; } = AlignmentTier.Red;
        public int            Reps         { get; private set; }

        public float CurrentAngle  { get; private set; } = float.NaN;   // 当帧原始肘角度
        public float SmoothedAngle { get; private set; } = float.NaN;   // EWMA 平滑后角度

        // 模块2 标定结果(模块3 用)
        public float MinAngle      { get; private set; } = float.NaN;   // 最低点(撑起最弯)
        public float MaxAngle      { get; private set; } = float.NaN;   // 最高点(完全撑直)
        public float DownThreshold { get; private set; } = float.NaN;   // 有效下压阈值
        public float UpThreshold   { get; private set; } = float.NaN;   // 有效撑起阈值

        // 模块3 错误检测计数(便于 UI 显示"刚才那个不算")
        public int InvalidRepAttempts { get; private set; }

        // ============================================================
        //  音频 + UI 反馈回调(由 Hud 绑定到 AudioSource 和高光闪烁动画)
        //
        //  说明:每个回调对应一个"事件触发点",Hud 在事件触发瞬间
        //  调用 AudioSource.PlayOneShot(clip) 即可。视觉反馈
        //  (例如金色闪屏 / 红光淡入)也在这里 hook。
        // ============================================================
        public Action<AlignmentTier> OnAlignmentTierChanged;  // 红/黄/绿切换 → 切背景色
        public Action OnAlignmentSuccess;                     // 绿灯达成 →"叮!"提示音
        public Action OnCalibrationPromptStart;               // 切到 CalibrationPrompt →"请做一个最标准的俯卧撑"语音
        public Action OnCalibrationComplete;                  // 标定 OK → "Ready-Go" 音效
        public Action OnRepSuccess;                           // +1 rep → 马里奥金币音 + 全屏金光闪烁
        public Action OnRepError;                             // 半幅动作 → 嘟嘟错误音 + "再低一点"语音
        public Action<SessionState> OnSessionStateChanged;    // 阶段切换 → 可用于刷新 UI

        // ============================================================
        //  外部配置:虚线框区域(0..1 归一化的屏幕坐标)
        //
        //  Key   = BlazePose 关键点索引 (11 肩, 13 肘, 15 腕, 23 胯, 27 踝)
        //  Value = 这个关键点必须落入的矩形区域
        //
        //  Hud 根据虚线框 UI 的实际像素位置 → 归一化后 → 填入这个字典。
        //  如果某个关键点对应的矩形被设置,就会被对齐检测使用。
        // ============================================================
        public readonly Dictionary<int, Rect> SilhouetteZones = new();

        // ============================================================
        //  常量(可调参数)
        // ============================================================
        const float ALIGN_HOLD_TIME       = 2.0f;   // 绿灯保持 2 秒进入标定
        const float READY_GO_DELAY        = 1.0f;   // Ready-Go 音后等 1 秒进入计数
        const float DOWN_PERCENT          = 0.20f;  // 有效下压 = MinAngle + 范围 * 20%
        const float UP_PERCENT            = 0.10f;  // 有效撑起 = MaxAngle - 范围 * 10%
        const float EWMA_ALPHA            = 0.40f;  // EWMA 平滑系数
        const float MIN_KEYPOINT_SCORE    = 0.30f;  // 关键点可信度门槛
        const float ALIGN_GREEN_FRACTION  = 0.80f;  // 80% 关键点入框算"绿"
        const float ALIGN_YELLOW_FRACTION = 0.50f;  // 50% 关键点入框算"黄"
        const float CAL_MIN_RANGE         = 25f;    // 标定时必须摆动 ≥25° 才认账
        const float CAL_RETURN_TOLERANCE  = 5f;     // 回到 high 时与 _calHighSeen 的容差

        // ============================================================
        //  内部状态
        // ============================================================
        AlignmentTier _prevAlignment = AlignmentTier.Red;
        float         _alignHoldT;

        // 标定子状态:必须依次看到"高位 → 低位 → 高位回归"
        float _calHighSeen = float.NegativeInfinity;
        float _calLowSeen  = float.PositiveInfinity;
        bool  _calHighEstablished;   // 看过一段稳定高位
        bool  _calLowReached;        // 从高位往下走过 CAL_MIN_RANGE

        // 计数子 FSM
        enum CountPhase
        {
            AtTop,           // 维持在 Up 区:等待开始下压
            Descending,      // 离开 Up 区,正在下压
        }
        CountPhase _countPhase    = CountPhase.AtTop;
        bool       _wentDeepEnough;   // 本次下压是否进过有效下压区

        // ============================================================
        //  主入口:Hud 每次 BlazePose 推理完后调用一次。
        //  dt = 距上次调用的时间差(s),用来推进 2s/1s 倒计时。
        // ============================================================
        public void Update(PoseDetector.Keypoint[] kps, float dt)
        {
            // ----- 1) 计算并平滑当前肘角度(供所有模块共用) -----
            float raw = ComputeAvgElbowAngle(kps);
            CurrentAngle = raw;
            if (!float.IsNaN(raw))
            {
                SmoothedAngle = float.IsNaN(SmoothedAngle)
                    ? raw
                    : SmoothedAngle * (1f - EWMA_ALPHA) + raw * EWMA_ALPHA;
            }

            // ----- 2) 根据当前会话阶段走对应分支 -----
            switch (State)
            {
                case SessionState.WaitingForAlignment:
                    StepAlignment(kps);
                    // 一旦绿灯达成,播"叮"音并进入倒计时
                    if (Alignment == AlignmentTier.Green)
                    {
                        OnAlignmentSuccess?.Invoke();
                        TransitionTo(SessionState.AlignmentCountdown);
                    }
                    break;

                case SessionState.AlignmentCountdown:
                    StepAlignment(kps);
                    // 倒计时中如果失位 -> 回到等待对齐
                    if (Alignment != AlignmentTier.Green)
                    {
                        TransitionTo(SessionState.WaitingForAlignment);
                        break;
                    }
                    _alignHoldT += dt;
                    if (_alignHoldT >= ALIGN_HOLD_TIME)
                    {
                        // 进入标定提示
                        TransitionTo(SessionState.CalibrationPrompt);
                        OnCalibrationPromptStart?.Invoke();    // 语音"请做一个标准俯卧撑"
                    }
                    break;

                case SessionState.CalibrationPrompt:
                    // 玩家听完提示开始动作 —— 一旦角度开始有变化就进入 Calibrating
                    if (!float.IsNaN(SmoothedAngle))
                    {
                        _calHighSeen = SmoothedAngle;
                        _calLowSeen  = SmoothedAngle;
                        _calHighEstablished = false;
                        _calLowReached = false;
                        TransitionTo(SessionState.Calibrating);
                    }
                    break;

                case SessionState.Calibrating:
                    StepCalibration();
                    break;

                case SessionState.ReadyToCount:
                    // Ready-Go 音播完后等一拍,进入计数
                    _alignHoldT += dt;
                    if (_alignHoldT >= READY_GO_DELAY)
                    {
                        _countPhase = CountPhase.AtTop;
                        _wentDeepEnough = false;
                        TransitionTo(SessionState.Counting);
                    }
                    break;

                case SessionState.Counting:
                    StepCounting();
                    break;
            }
        }

        // ============================================================
        //  模块1 — 虚线框区域碰撞检测 + 红/黄/绿档位切换
        //
        //  评分逻辑:
        //    1) present  = 可信度达标的关键点数(score >= MIN_KEYPOINT_SCORE)
        //    2) inZone   = 既可信、又落入对应虚线框矩形的关键点数
        //    3) 按 inZone / totalZones 的比例分档:
        //         <50%   → Red    (人离得太近 / 没在框内 / 没检测到)
        //         50-80% → Yellow (大致对位但还没卡准)
        //         ≥80%   → Green  (五点齐整,可以开始)
        // ============================================================
        void StepAlignment(PoseDetector.Keypoint[] kps)
        {
            int totalZones = SilhouetteZones.Count;
            if (totalZones == 0) return;            // 还没配置虚线框就不更新

            int present = 0;
            int inZone  = 0;

            foreach (var pair in SilhouetteZones)
            {
                int  idx  = pair.Key;
                Rect zone = pair.Value;
                if (idx < 0 || idx >= kps.Length) continue;

                var kp = kps[idx];
                if (kp.score < MIN_KEYPOINT_SCORE) continue;
                present++;

                // BlazePose 输出的 (x, y) 已经是 0..1 归一化坐标 → 直接 Contains
                if (zone.Contains(new Vector2(kp.x, kp.y))) inZone++;
            }

            float frac = totalZones == 0 ? 0f : (float)inZone / totalZones;

            AlignmentTier tier;
            if (present < totalZones * 0.5f)          tier = AlignmentTier.Red;
            else if (frac < ALIGN_YELLOW_FRACTION)    tier = AlignmentTier.Red;
            else if (frac < ALIGN_GREEN_FRACTION)     tier = AlignmentTier.Yellow;
            else                                       tier = AlignmentTier.Green;

            if (tier != _prevAlignment)
            {
                Alignment       = tier;
                _prevAlignment  = tier;
                OnAlignmentTierChanged?.Invoke(tier);   // → Hud 切全屏背景色
            }
        }

        // ============================================================
        //  模块2 — 在标定阶段抓住一个完整 high→low→high 周期
        //
        //  捕获策略:
        //    a) 先观察一段时间(玩家应当处于撑直姿势),把 _calHighSeen 当成顶部基准
        //    b) 玩家开始下压 → 跟踪 _calLowSeen
        //    c) 当 (max - min) 达到 CAL_MIN_RANGE 且 _calLowReached = true
        //       → 标定可信
        //    d) 玩家回到 (_calHighSeen ± CAL_RETURN_TOLERANCE) → 完成
        // ============================================================
        void StepCalibration()
        {
            if (float.IsNaN(SmoothedAngle)) return;
            float a = SmoothedAngle;

            // 持续更新两个极值
            if (a > _calHighSeen) _calHighSeen = a;
            if (a < _calLowSeen)  _calLowSeen  = a;

            float range = _calHighSeen - _calLowSeen;

            // 玩家高位站稳:近 0.3s 角度变化 <2° 视为高位稳定。
            // 简化版:看到一个 >150° 的值就算高位起始;Hud 那一侧可在切阶段时已经引导玩家撑直。
            if (!_calHighEstablished && _calHighSeen > 140f) _calHighEstablished = true;

            // 已经从高位向下走了一段 → 进入"看回升"判定
            if (_calHighEstablished && range >= CAL_MIN_RANGE) _calLowReached = true;

            // 回升到接近顶峰 → 完成
            if (_calLowReached && a >= _calHighSeen - CAL_RETURN_TOLERANCE)
            {
                MinAngle      = _calLowSeen;
                MaxAngle      = _calHighSeen;
                float fullR   = MaxAngle - MinAngle;
                DownThreshold = MinAngle + fullR * DOWN_PERCENT;
                UpThreshold   = MaxAngle - fullR * UP_PERCENT;

                OnCalibrationComplete?.Invoke();          // → "Ready-Go" 音效
                TransitionTo(SessionState.ReadyToCount);
            }
        }

        // ============================================================
        //  模块3 — 计数 FSM + 半幅纠错
        //
        //  状态流:
        //    AtTop:        当前在 Up 区(角度 ≥ UpThreshold)。
        //                  当角度落出 Up 区(< UpThreshold)→ Descending,_wentDeepEnough=false。
        //    Descending:   下压中。
        //                  - 若角度 ≤ DownThreshold,记 _wentDeepEnough = true。
        //                  - 若角度回升到 ≥ UpThreshold(回到 Up 区):
        //                      * 经过有效下压区(_wentDeepEnough) → 完整 rep → OnRepSuccess + Reps++
        //                      * 否则 → 半幅,OnRepError("再低一点"),InvalidRepAttempts++。
        //                    无论哪种,都回到 AtTop 等下一次下压。
        // ============================================================
        void StepCounting()
        {
            if (float.IsNaN(SmoothedAngle)) return;
            float a = SmoothedAngle;

            switch (_countPhase)
            {
                case CountPhase.AtTop:
                    if (a < UpThreshold)
                    {
                        // 离开了 Up 区,认定为下压开始
                        _countPhase     = CountPhase.Descending;
                        _wentDeepEnough = false;
                    }
                    break;

                case CountPhase.Descending:
                    // 实时检查是否进入了"有效下压"区
                    if (a <= DownThreshold) _wentDeepEnough = true;

                    // 回升到 Up 区 → 评估这次下压是否合格
                    if (a >= UpThreshold)
                    {
                        if (_wentDeepEnough)
                        {
                            Reps++;
                            OnRepSuccess?.Invoke();      // 马里奥金币音 + 金光闪烁
                        }
                        else
                        {
                            InvalidRepAttempts++;
                            OnRepError?.Invoke();        // 嘟嘟错误音 + "再低一点"语音
                        }
                        _countPhase = CountPhase.AtTop;
                    }
                    break;
            }
        }

        // ============================================================
        //  通用工具
        // ============================================================
        static float ComputeAvgElbowAngle(PoseDetector.Keypoint[] kps)
        {
            // BlazePose 索引:11/12 = 左/右肩,13/14 = 肘,15/16 = 腕
            float l = ComputeAngle(kps[11], kps[13], kps[15]);
            float r = ComputeAngle(kps[12], kps[14], kps[16]);
            bool lv = !float.IsNaN(l), rv = !float.IsNaN(r);
            if (lv && rv) return (l + r) * 0.5f;
            if (lv) return l;
            if (rv) return r;
            return float.NaN;
        }

        // 计算肩-肘-腕的"伸直度":180° = 手臂完全撑直,90° = 直角弯
        static float ComputeAngle(PoseDetector.Keypoint s, PoseDetector.Keypoint e, PoseDetector.Keypoint w)
        {
            if (s.score < MIN_KEYPOINT_SCORE || e.score < MIN_KEYPOINT_SCORE || w.score < MIN_KEYPOINT_SCORE)
                return float.NaN;
            var sToE = new Vector2(e.x - s.x, e.y - s.y);
            var eToW = new Vector2(w.x - e.x, w.y - e.y);
            if (sToE.sqrMagnitude < 1e-6f || eToW.sqrMagnitude < 1e-6f) return float.NaN;
            float dot = Mathf.Clamp(Vector2.Dot(sToE.normalized, eToW.normalized), -1f, 1f);
            return 180f - Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        // 阶段切换 — 把"清空辅助变量"集中在这里,避免分支里到处忘 reset
        void TransitionTo(SessionState next)
        {
            State = next;
            _alignHoldT = 0f;
            OnSessionStateChanged?.Invoke(next);
        }

        // 整个会话重置:供"重新训练" / Shift+R 调用
        public void Reset()
        {
            State              = SessionState.WaitingForAlignment;
            Alignment          = AlignmentTier.Red;
            _prevAlignment     = AlignmentTier.Red;
            Reps               = 0;
            InvalidRepAttempts = 0;
            CurrentAngle       = float.NaN;
            SmoothedAngle      = float.NaN;
            MinAngle = MaxAngle = DownThreshold = UpThreshold = float.NaN;
            _calHighSeen = float.NegativeInfinity;
            _calLowSeen  = float.PositiveInfinity;
            _calHighEstablished = _calLowReached = false;
            _countPhase = CountPhase.AtTop;
            _wentDeepEnough = false;
            _alignHoldT = 0f;
        }

        // 把当前 AlignmentTier 翻译成全屏背景色(供 Hud 直接用)
        public Color BackgroundTintForAlignment()
        {
            switch (Alignment)
            {
                case AlignmentTier.Green:  return new Color(0.35f, 1f,   0.35f, 0.35f);
                case AlignmentTier.Yellow: return new Color(1f,    0.85f, 0.30f, 0.30f);
                case AlignmentTier.Red:    return new Color(1f,    0.30f, 0.30f, 0.30f);
                default: return Color.clear;
            }
        }
    }
}
