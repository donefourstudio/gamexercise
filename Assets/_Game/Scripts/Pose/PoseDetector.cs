using UnityEngine;
using Unity.InferenceEngine;

namespace Gamex.Pose
{
    // Single-person pose detector wrapping the MoveNet Lightning ONNX model.
    // Resizes any incoming texture to 192x192, packs it into an int32 tensor
    // (MoveNet's native input format), runs inference on the CPU backend
    // (M4b — GPUCompute later if perf needs it), and parses the output into
    // 17 (x, y, score) keypoints in normalized [0,1] coords.
    //
    // Output keypoint order (MoveNet standard):
    //   0:nose         1:l_eye       2:r_eye
    //   3:l_ear        4:r_ear
    //   5:l_shoulder   6:r_shoulder
    //   7:l_elbow      8:r_elbow
    //   9:l_wrist     10:r_wrist
    //  11:l_hip       12:r_hip
    //  13:l_knee      14:r_knee
    //  15:l_ankle     16:r_ankle
    public class PoseDetector : System.IDisposable
    {
        public struct Keypoint { public float x, y, score; }

        public const int KEYPOINT_COUNT = 17;
        public const int INPUT_W = 192;
        public const int INPUT_H = 192;

        public Keypoint[] Keypoints { get; } = new Keypoint[KEYPOINT_COUNT];
        public bool IsReady => _worker != null;

        Worker _worker;
        RenderTexture _rt;
        Texture2D _readTex;
        Tensor<int> _input;

        public PoseDetector()
        {
            var asset = Resources.Load<ModelAsset>("Models/movenet_lightning");
            if (asset == null)
            {
                Debug.LogError("[Pose] MoveNet model not found at Resources/Models/movenet_lightning");
                return;
            }
            var model = ModelLoader.Load(asset);
            _worker  = new Worker(model, BackendType.CPU);
            _rt      = new RenderTexture(INPUT_W, INPUT_H, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            _readTex = new Texture2D(INPUT_W, INPUT_H, TextureFormat.RGB24, false);
        }

        // Runs one inference pass against `source`. Synchronous (~30-60ms on CPU for
        // MoveNet Lightning). Caller throttles via a per-frame timer; this class
        // does not throttle itself.
        public void Detect(Texture source)
        {
            if (_worker == null || source == null) return;

            // Resize the camera frame to 192x192 by blitting onto the RT.
            Graphics.Blit(source, _rt);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _readTex.ReadPixels(new Rect(0, 0, INPUT_W, INPUT_H), 0, 0);
            _readTex.Apply();
            RenderTexture.active = prev;

            // Pack RGB pixels into an int32 [1, H, W, 3] tensor (MoveNet's native input).
            var px = _readTex.GetPixels32();
            var data = new int[INPUT_H * INPUT_W * 3];
            for (int i = 0; i < px.Length; i++)
            {
                data[i * 3 + 0] = px[i].r;
                data[i * 3 + 1] = px[i].g;
                data[i * 3 + 2] = px[i].b;
            }

            _input?.Dispose();
            _input = new Tensor<int>(new TensorShape(1, INPUT_H, INPUT_W, 3), data);

            try
            {
                _worker.Schedule(_input);
                var output = _worker.PeekOutput() as Tensor<float>;
                if (output == null) return;

                // MoveNet output: [1, 1, 17, 3] => (y, x, score) per keypoint, normalized 0..1.
                var arr = output.DownloadToArray();
                for (int i = 0; i < KEYPOINT_COUNT; i++)
                {
                    Keypoints[i].y     = arr[i * 3 + 0];
                    Keypoints[i].x     = arr[i * 3 + 1];
                    Keypoints[i].score = arr[i * 3 + 2];
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Pose] inference failed: " + e.Message);
            }
        }

        // Highest score across all keypoints — useful as a single-number "is the user visible?" gate.
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
