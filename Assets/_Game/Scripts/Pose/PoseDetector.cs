using UnityEngine;
using Unity.InferenceEngine;

namespace Gamex.Pose
{
    // Single-person pose detector wrapping the BlazePose Lite ONNX model
    // (OpenCV's mediapipe-pose port — single-stage, no separate detector).
    //
    // Input:  Tensor<float>[1, 256, 256, 3], pixels normalized 0..1
    // Outputs: 5 tensors; we only need "Identity" = [1, 195]
    //          = 39 keypoints * (x, y, z, visibility, presence)
    //          x, y are in 0..256 pixel coords of the input; we normalize 0..1.
    //
    // BlazePose's 33-keypoint indexing (Keypoints[0..32]):
    //   0 nose, 1-6 face details, 7-8 ears, 9-10 mouth
    //   11 l_shoulder  12 r_shoulder
    //   13 l_elbow     14 r_elbow
    //   15 l_wrist     16 r_wrist
    //   17-22 hand details
    //   23 l_hip       24 r_hip
    //   25 l_knee      26 r_knee
    //   27 l_ankle     28 r_ankle
    //   29-32 foot details
    //
    // We swap from MoveNet (17 keypoints, brittle near occlusion) because
    // partial pushup / situp tracking was failing — BlazePose's per-keypoint
    // visibility score + 33 landmarks gives the counters cleaner signals.
    public class PoseDetector : System.IDisposable
    {
        public struct Keypoint { public float x, y, score; }

        public const int KEYPOINT_COUNT = 33;
        public const int INPUT_W = 256;
        public const int INPUT_H = 256;
        const int RAW_KEYPOINT_FIELDS = 5;     // x, y, z, visibility, presence
        const int RAW_KEYPOINT_COUNT  = 39;    // model emits 39, we expose 33

        public Keypoint[] Keypoints { get; } = new Keypoint[KEYPOINT_COUNT];
        public bool IsReady => _worker != null;

        Worker _worker;
        RenderTexture _rt;
        Texture2D _readTex;
        Tensor<float> _input;

        public PoseDetector()
        {
            var asset = Resources.Load<ModelAsset>("Models/blazepose");
            if (asset == null)
            {
                Debug.LogError("[Pose] BlazePose model not found at Resources/Models/blazepose");
                return;
            }
            var model = ModelLoader.Load(asset);
            _worker  = new Worker(model, BackendType.CPU);
            _rt      = new RenderTexture(INPUT_W, INPUT_H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            _readTex = new Texture2D(INPUT_W, INPUT_H, TextureFormat.RGB24, false);
        }

        public void Detect(Texture source)
        {
            if (_worker == null || source == null) return;

            Graphics.Blit(source, _rt);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _readTex.ReadPixels(new Rect(0, 0, INPUT_W, INPUT_H), 0, 0);
            _readTex.Apply();
            RenderTexture.active = prev;

            // Pack RGB into float [0,1] tensor.
            var px = _readTex.GetPixels32();
            var data = new float[INPUT_H * INPUT_W * 3];
            for (int i = 0; i < px.Length; i++)
            {
                data[i * 3 + 0] = px[i].r / 255f;
                data[i * 3 + 1] = px[i].g / 255f;
                data[i * 3 + 2] = px[i].b / 255f;
            }

            _input?.Dispose();
            _input = new Tensor<float>(new TensorShape(1, INPUT_H, INPUT_W, 3), data);

            try
            {
                _worker.Schedule(_input);
                var landmarks = _worker.PeekOutput("Identity") as Tensor<float>;
                if (landmarks == null) return;

                var arr = landmarks.DownloadToArray();
                // arr[i*5 + 0] = x (pixels), +1 = y (pixels), +2 = z, +3 = visibility, +4 = presence
                int n = Mathf.Min(KEYPOINT_COUNT, RAW_KEYPOINT_COUNT);
                for (int i = 0; i < n; i++)
                {
                    Keypoints[i].x = arr[i * RAW_KEYPOINT_FIELDS + 0] / INPUT_W;
                    Keypoints[i].y = arr[i * RAW_KEYPOINT_FIELDS + 1] / INPUT_H;
                    // BlazePose visibility is a logit; squash via sigmoid for a 0..1 confidence.
                    float vis = arr[i * RAW_KEYPOINT_FIELDS + 3];
                    Keypoints[i].score = 1f / (1f + Mathf.Exp(-vis));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Pose] inference failed: " + e.Message);
            }
        }

        public float TopScore()
        {
            float s = 0f;
            for (int i = 0; i < KEYPOINT_COUNT; i++) if (Keypoints[i].score > s) s = Keypoints[i].score;
            return s;
        }

        public void Dispose()
        {
            _input?.Dispose();
            _worker?.Dispose();
            if (_rt != null) _rt.Release();
            _worker = null; _input = null; _rt = null; _readTex = null;
        }
    }
}
