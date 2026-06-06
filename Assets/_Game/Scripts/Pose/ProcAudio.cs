using UnityEngine;

namespace Gamex.Pose
{
    // 程序化音频生成 —— 五个 AlignmentSystem 反馈事件全部用代码合成 AudioClip,
    // 不依赖任何外部音频资源(免去找/下载/版权问题)。Hud 在启动时调用一次
    // ProcAudio.BuildAll() 拿到所有 clips,挂到一个 AudioSource 上。
    //
    // 所有音频:44.1 kHz 单声道,音量 0.4 防止过响。
    public static class ProcAudio
    {
        const int SAMPLE_RATE = 44100;

        public class Clips
        {
            public AudioClip alignmentDing;     // 模块1 绿灯 "叮!"
            public AudioClip calibrationPrompt; // 模块2 提示音(代替语音)
            public AudioClip readyGo;            // 模块2 标定完成 "Ready-Go"
            public AudioClip repSuccess;         // 模块3 +1 rep 马里奥金币
            public AudioClip repError;           // 模块3 半幅 错误嘟嘟
        }

        public static Clips BuildAll() => new Clips
        {
            alignmentDing     = Ding(880f, 0.30f),
            calibrationPrompt = TwoTone(660f, 880f, 0.18f, 0.18f),
            readyGo           = AscendingTriad(523f, 659f, 784f, 0.50f),   // C5 E5 G5
            repSuccess        = TwoTone(988f, 1319f, 0.10f, 0.18f),         // 马里奥金币 B5→E6
            repError          = Buzz(180f, 0.25f),
        };

        // 单音叮:正弦 + 指数衰减包络
        static AudioClip Ding(float freq, float dur)
        {
            int n = (int)(SAMPLE_RATE * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float env = Mathf.Exp(-t * 6f);
                data[i] = env * Mathf.Sin(2f * Mathf.PI * freq * t) * 0.4f;
            }
            return MakeClip("Ding", data);
        }

        // 两段不同音高拼接:用于马里奥金币 / 提示
        static AudioClip TwoTone(float f1, float f2, float dur1, float dur2)
        {
            int n1 = (int)(SAMPLE_RATE * dur1);
            int n2 = (int)(SAMPLE_RATE * dur2);
            var data = new float[n1 + n2];
            for (int i = 0; i < n1; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float env = Mathf.Exp(-t * 9f);
                data[i] = env * Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.4f;
            }
            for (int i = 0; i < n2; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                float env = Mathf.Exp(-t * 5f);
                data[n1 + i] = env * Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.4f;
            }
            return MakeClip("TwoTone", data);
        }

        // 上行三和弦 —— Ready-Go 用
        static AudioClip AscendingTriad(float f1, float f2, float f3, float dur)
        {
            int n = (int)(SAMPLE_RATE * dur);
            int seg = n / 3;
            var data = new float[n];
            float[] freqs = { f1, f2, f3 };
            for (int s = 0; s < 3; s++)
            {
                for (int i = 0; i < seg; i++)
                {
                    float t = i / (float)SAMPLE_RATE;
                    float env = Mathf.Exp(-t * 4f);
                    data[s * seg + i] = env * Mathf.Sin(2f * Mathf.PI * freqs[s] * t) * 0.4f;
                }
            }
            return MakeClip("Triad", data);
        }

        // 错误嘟嘟:方波 + 低音 + 颤动包络
        static AudioClip Buzz(float freq, float dur)
        {
            int n = (int)(SAMPLE_RATE * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SAMPLE_RATE;
                // 方波(锐利)
                float s = Mathf.Sin(2f * Mathf.PI * freq * t) >= 0 ? 1f : -1f;
                // 5 Hz 颤动让它听起来像电子提示
                float trem = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 5f * t);
                float env = Mathf.Exp(-t * 4f);
                data[i] = env * trem * s * 0.25f;
            }
            return MakeClip("Buzz", data);
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SAMPLE_RATE, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
