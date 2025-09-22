using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class SimpleGazeDetector : MonoBehaviour
{
    [Header("Models")]
    public Unity.InferenceEngine.ModelAsset faceModel;              // YOLO face detection model
    public Unity.InferenceEngine.ModelAsset gazeModel;              // Gaze detection model
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.CPU;

    [Header("UI")]
    public RawImage videoImage;               // Webcam display
    public SimpleGazeVisualizer gazeVisualizer; // Gaze arrow visualizer

    [Header("Webcam")]
    public int requestWidth = 1280;
    public int requestHeight = 720;
    public int requestFPS = 30;

    [Header("Detection")]
    [Range(0.05f, 0.9f)] public float confThreshold = 0.35f;
    [Range(0.0f, 1.0f)] public float iouThreshold = 0.45f;

    [Header("Gaze Conventions")]
    public bool invertYaw = true;     // webcams are usually mirrored → flip yaw
    public bool invertPitch = false;  // up/down as-is

    [Header("Gaze Preprocess")]
    public bool useImagenetNorm = true;  // required for most MobileNetV2 gaze nets

    [Header("Gaze Options")]
    [SerializeField] bool swapYawPitch = false;  // some ONNX exports are [yaw,pitch]
    [SerializeField] KeyCode calibrateKey = KeyCode.C;

    const int MODEL_SIZE = 640;
    const int GAZE_SIZE = 448; // your checkpoint wants 448x448

    private Unity.InferenceEngine.Worker _faceWorker;
    private Unity.InferenceEngine.Worker _gazeWorker;
    private WebCamTexture _webcam;
    private RenderTexture _rt640;
    private Texture2D _readbackTex;
    private Color32[] _cols640;
    private bool _haveCols;
    private bool _loggedShape = false;

    bool _lookingPrev = false;
    float _yawBias = 0f, _pitchBias = 0f;
    bool _didAutoCalib = false;

    int _noFaceFrames = 0;

    void Start()
    {
        // Start webcam
        _webcam = new WebCamTexture(requestWidth, requestHeight, requestFPS);
        _webcam.Play();
        if (videoImage != null) videoImage.texture = _webcam;

        // Load face detection model
        if (faceModel != null)
        {
            var model = Unity.InferenceEngine.ModelLoader.Load(faceModel);
            _faceWorker = new Unity.InferenceEngine.Worker(model, backend);
            Debug.Log("[GAZE] Face detection model loaded successfully");
        }
        else
        {
            Debug.LogError("[GAZE] Face detection model not assigned!");
        }

        // Load gaze detection model
        if (gazeModel != null)
        {
            var model = Unity.InferenceEngine.ModelLoader.Load(gazeModel);
            _gazeWorker = new Unity.InferenceEngine.Worker(model, backend);
            Debug.Log("[GAZE] Gaze detection model loaded successfully");
        }
        else
        {
            Debug.LogError("[GAZE] Gaze detection model not assigned!");
        }

        // Allocate resources
        _rt640 = new RenderTexture(MODEL_SIZE, MODEL_SIZE, 0, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(MODEL_SIZE, MODEL_SIZE, TextureFormat.RGBA32, false);

        // Validate components
        if (gazeVisualizer == null) Debug.LogError("[GAZE] GazeVisualizer not assigned!");
    }

    void OnDisable()
    {
        _faceWorker?.Dispose();
        _gazeWorker?.Dispose();
        if (_webcam != null && _webcam.isPlaying) _webcam.Stop();
        if (_rt640 != null) { _rt640.Release(); _rt640 = null; }
        if (_readbackTex != null) Destroy(_readbackTex);
    }

    void Update()
    {
        if (_webcam == null || !_webcam.didUpdateThisFrame) return;

        try
        {
            // Process webcam frame → square RT 640x640
            Graphics.Blit(_webcam, _rt640);

            var prev = RenderTexture.active;
            RenderTexture.active = _rt640;
            _readbackTex.ReadPixels(new Rect(0, 0, MODEL_SIZE, MODEL_SIZE), 0, 0);
            _readbackTex.Apply(false);
            _cols640 = _readbackTex.GetPixels32();
            _haveCols = true;
            RenderTexture.active = prev;

            // Run face detection
            var faceRect = DetectFace();

            if (!faceRect.HasValue)
            {
                _noFaceFrames++;
                if (_noFaceFrames % 30 == 0)
                    Debug.Log($"[GAZE] No face for {_noFaceFrames} frames.");
                return;
            }
            _noFaceFrames = 0;

            // If face detected, run gaze detection
            if (_gazeWorker != null && _haveCols)
            {
                var gazeAngles = RunGazeOnFace(_cols640, faceRect.Value);

                //gazeVisualizer?.UpdateGazeDirection(faceRect.Value, gazeAngles.yaw, gazeAngles.pitch);

                // manual calibration on key
                if (Input.GetKeyDown(calibrateKey))
                {
                    _yawBias = gazeAngles.yaw;
                    _pitchBias = gazeAngles.pitch;
                    _didAutoCalib = true;
                    Debug.Log($"[GAZE] Calibrated zero: yawBias={_yawBias:F1}, pitchBias={_pitchBias:F1}");
                }

                // auto-calibration once when face is large enough (good frontal sample)
                if (!_didAutoCalib)
                {
                    float side = Mathf.Max(faceRect.Value.width, faceRect.Value.height);
                    if (side > 160f)
                    {
                        _yawBias = gazeAngles.yaw;
                        _pitchBias = gazeAngles.pitch;
                        _didAutoCalib = true;
                        Debug.Log($"[GAZE] Auto-calibrated zero: yawBias={_yawBias:F1}, pitchBias={_pitchBias:F1}");
                    }
                }

                // Check if gaze is directed at the camera
                if (IsGazeAtCamera(gazeAngles.yaw, gazeAngles.pitch))
                    Debug.Log("[GAZE] The person is looking at the camera!");
                else
                    Debug.Log("[GAZE] The person is NOT looking at the camera.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GAZE] Exception in Update: {ex}");
        }
    }

    bool IsGazeAtCamera(float yawDeg, float pitchDeg)
    {
        // 1) Bias-correct (personal zero)
        float yaw = yawDeg - _yawBias;
        float pitch = pitchDeg - _pitchBias;

        // 2) Ellipse thresholds (slightly loose while tuning)
        const float YAW_HALF   = 28f;
        const float PITCH_HALF = 20f;

        float scale = _lookingPrev ? 1.25f : 1.0f;
        float ey = yaw / (YAW_HALF * scale);
        float ep = pitch / (PITCH_HALF * scale);
        bool nowLooking = (ey * ey + ep * ep) <= 1f;

        _lookingPrev = nowLooking;

        Debug.Log($"[GAZE] yaw={yawDeg:F1}°, pitch={pitchDeg:F1}°, biased=({yaw:F1},{pitch:F1}), gate={(nowLooking ? "IN" : "OUT")}, prev={_lookingPrev}");
        return nowLooking;
    }

    Rect? DetectFace()
    {
        if (_faceWorker == null) return null;
        if (!_haveCols || _cols640 == null || _cols640.Length == 0)
        {
            Debug.LogWarning("[GAZE] DetectFace() called without pixels.");
            return null;
        }

        // Create input tensor
        using (var input = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 3, MODEL_SIZE, MODEL_SIZE)))
        {
            int plane = MODEL_SIZE * MODEL_SIZE;
            int idxR = 0 * plane, idxG = 1 * plane, idxB = 2 * plane;

            // IMPORTANT: keep the original vertical flip here
            // (your exported YOLO-face worked with this)
            for (int y = 0; y < MODEL_SIZE; y++)
            {
                int srcY = MODEL_SIZE - 1 - y; // vertical flip
                int rowOff = srcY * MODEL_SIZE;
                int dstRow = y * MODEL_SIZE;

                for (int x = 0; x < MODEL_SIZE; x++)
                {
                    var c = _cols640[rowOff + x];
                    int ofs = dstRow + x;
                    input[ofs + idxR] = c.r / 255f;
                    input[ofs + idxG] = c.g / 255f;
                    input[ofs + idxB] = c.b / 255f;
                }
            }

            // Run inference (and force completion before peek)
            _faceWorker.Schedule(input);
        }

        using (var output = _faceWorker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            if (output == null)
            {
                Debug.LogWarning("[GAZE] Face worker output is null.");
                return null;
            }

            float[] data = output.DownloadToArray();

            if (!_loggedShape)
            {
                Debug.Log($"[GAZE] Face detection output shape: {string.Join("x", output.shape.ToArray())}");
                _loggedShape = true;
            }

            var dets = ParseDetections(data, output.shape, confThreshold);
            var kept = NMS(dets, iouThreshold).OrderByDescending(d => d.score).Take(1).ToList();

            if (kept.Count == 0)
            {
                return null;
            }

            var r = kept[0].rect;

            // UNFLIP rectangle back to readback pixel coords (pair to the input flip)
            r.y = MODEL_SIZE - r.y - r.height;

            // Clamp to bounds
            r.x      = Mathf.Clamp(r.x, 0, MODEL_SIZE - 1);
            r.y      = Mathf.Clamp(r.y, 0, MODEL_SIZE - 1);
            r.width  = Mathf.Clamp(r.width,  1, MODEL_SIZE - r.x);
            r.height = Mathf.Clamp(r.height, 1, MODEL_SIZE - r.y);

            return r;
        }
    }

    struct Detection
    {
        public Rect rect;
        public float score;
    }

    List<Detection> ParseDetections(float[] data, Unity.InferenceEngine.TensorShape shape, float conf)
    {
        var dims = shape.ToArray(); // expected [1,5,8400] for your ONNX
        if (dims.Length < 3)
        {
            Debug.LogWarning($"[GAZE] Unexpected face output dims: {string.Join("x", dims)}");
            return new List<Detection>();
        }

        int C = dims[1]; // 5
        int A = dims[2]; // 8400

        var dets = new List<Detection>(32);
        float Get(int a, int c) => data[c * A + a];

        for (int i = 0; i < A; i++)
        {
            float cx = Get(i, 0);
            float cy = Get(i, 1);
            float w  = Get(i, 2);
            float h  = Get(i, 3);
            float confScore = Get(i, 4);

            if (confScore < conf) continue;

            float x = cx - w * 0.5f;
            float y = cy - h * 0.5f;

            dets.Add(new Detection
            {
                rect = new Rect(x, y, w, h),
                score = confScore
            });
        }

        return dets;
    }

    List<Detection> NMS(List<Detection> dets, float iou)
    {
        var sorted = dets.OrderByDescending(d => d.score).ToList();
        var kept = new List<Detection>();

        while (sorted.Count > 0)
        {
            var a = sorted[0];
            kept.Add(a);
            sorted.RemoveAt(0);

            for (int j = sorted.Count - 1; j >= 0; j--)
                if (IoU(a.rect, sorted[j].rect) > iou)
                    sorted.RemoveAt(j);
        }

        return kept;
    }

    float IoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);
        float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float union = a.width * a.height + b.width * b.height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    struct GazeAngles { public float yaw; public float pitch; }

    GazeAngles RunGazeOnFace(Color32[] cols640, Rect faceBox)
    {
        // --- Square, margin-padded crop around the face (no aspect distortion) ---
        float cx = faceBox.x + faceBox.width * 0.5f;
        float cy = faceBox.y + faceBox.height * 0.5f;

        float side = Mathf.Max(faceBox.width, faceBox.height);
        side *= 1.40f; // 40% margin to include forehead/ears/background
        float x0f = Mathf.Clamp(cx - side * 0.5f, 0, MODEL_SIZE - 1);
        float y0f = Mathf.Clamp(cy - side * 0.5f, 0, MODEL_SIZE - 1);
        float x1f = Mathf.Clamp(cx + side * 0.5f, 0, MODEL_SIZE - 1);
        float y1f = Mathf.Clamp(cy + side * 0.5f, 0, MODEL_SIZE - 1);

        float srcSideX = (x1f - x0f);
        float srcSideY = (y1f - y0f);
        float srcSide = Mathf.Min(srcSideX, srcSideY);
        x0f = Mathf.Clamp(cx - srcSide * 0.5f, 0, MODEL_SIZE - 1);
        y0f = Mathf.Clamp(cy - srcSide * 0.5f, 0, MODEL_SIZE - 1);
        x1f = Mathf.Clamp(cx + srcSide * 0.5f, 0, MODEL_SIZE - 1);
        y1f = Mathf.Clamp(cy + srcSide * 0.5f, 0, MODEL_SIZE - 1);
        srcSide = Mathf.Min(x1f - x0f, y1f - y0f); // final side

        // Create gaze input tensor
        using (var t = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 3, GAZE_SIZE, GAZE_SIZE)))
        {
            int plane = GAZE_SIZE * GAZE_SIZE;
            int idxR = 0 * plane, idxG = 1 * plane, idxB = 2 * plane;

            // ImageNet normalization constants
            const float mR = 0.485f, mG = 0.456f, mB = 0.406f;
            const float sR = 0.229f, sG = 0.224f, sB = 0.225f;

            // Sample from the square region (NO vertical flip)
            for (int gy = 0; gy < GAZE_SIZE; gy++)
            {
                float v = (gy + 0.5f) / GAZE_SIZE;
                float srcYf = y0f + v * srcSide;

                int srcY0 = Mathf.Clamp((int)srcYf, 0, MODEL_SIZE - 1);
                int srcY1i = Mathf.Min(srcY0 + 1, MODEL_SIZE - 1);
                float fy = srcYf - srcY0;

                for (int gx = 0; gx < GAZE_SIZE; gx++)
                {
                    float u = (gx + 0.5f) / GAZE_SIZE;
                    float srcXf = x0f + u * srcSide;

                    int srcX0 = Mathf.Clamp((int)srcXf, 0, MODEL_SIZE - 1);
                    int srcX1 = Mathf.Min(srcX0 + 1, MODEL_SIZE - 1);
                    float fx = srcXf - srcX0;

                    int i00 = srcY0 * MODEL_SIZE + srcX0;
                    int i01 = srcY0 * MODEL_SIZE + srcX1;
                    int i10 = srcY1i * MODEL_SIZE + srcX0;
                    int i11 = srcY1i * MODEL_SIZE + srcX1;

                    Color32 c00 = cols640[i00], c01 = cols640[i01];
                    Color32 c10 = cols640[i10], c11 = cols640[i11];

                    float r = Mathf.Lerp(Mathf.Lerp(c00.r, c01.r, fx), Mathf.Lerp(c10.r, c11.r, fx), fy) / 255f;
                    float g = Mathf.Lerp(Mathf.Lerp(c00.g, c01.g, fx), Mathf.Lerp(c10.g, c11.g, fx), fy) / 255f;
                    float b = Mathf.Lerp(Mathf.Lerp(c00.b, c01.b, fx), Mathf.Lerp(c10.b, c11.b, fx), fy) / 255f;

                    int ofs = gy * GAZE_SIZE + gx;
                    if (useImagenetNorm)
                    {
                        t[ofs + idxR] = (r - mR) / sR;
                        t[ofs + idxG] = (g - mG) / sG;
                        t[ofs + idxB] = (b - mB) / sB;
                    }
                    else
                    {
                        t[ofs + idxR] = r;
                        t[ofs + idxG] = g;
                        t[ofs + idxB] = b;
                    }
                }
            }

            // Run gaze inference
            _gazeWorker.Schedule(t);
        }

        // Get gaze output
        using (var outT = _gazeWorker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            if (outT == null)
            {
                Debug.LogWarning("[GAZE] Gaze worker output is null.");
                return new GazeAngles { yaw = 0, pitch = 0 };
            }

            float[] outArr = outT.DownloadToArray();
            Debug.Log($"[GAZE][RAW] out[0]={outArr[0]:F4}, out[1]={outArr[1]:F4}");

            float yaw = 0f, pitch = 0f;

            if (outArr.Length >= 2)
            {
                // Handle possible order swap
                float a = outArr[0], b = outArr[1];
                float pitchRaw = swapYawPitch ? b : a;
                float yawRaw   = swapYawPitch ? a : b;

                bool looksLikeRadians = Mathf.Abs(yawRaw) < 6.3f && Mathf.Abs(pitchRaw) < 6.3f;
                float yawDegRaw   = looksLikeRadians ? yawRaw   * Mathf.Rad2Deg : yawRaw;
                float pitchDegRaw = looksLikeRadians ? pitchRaw * Mathf.Rad2Deg : pitchRaw;

                // Mirror/sign conventions
                yaw   = invertYaw   ? -yawDegRaw   :  yawDegRaw;
                pitch = invertPitch ? -pitchDegRaw :  pitchDegRaw;

                // Clamp to sane ranges
                yaw   = Mathf.Clamp(yaw,   -180f, 180f);
                pitch = Mathf.Clamp(pitch,  -90f,  90f);

                Debug.Log($"[GAZE] rawDeg yaw={yawDegRaw:F1}, pitch={pitchDegRaw:F1} (crop side={srcSide:F1})");
                Debug.Log($"[GAZE] Yaw: {yaw:F1}°, Pitch: {pitch:F1}°");
            }
            else
            {
                Debug.LogWarning($"[GAZE] Unexpected gaze output length: {outArr.Length}");
            }

            return new GazeAngles { yaw = yaw, pitch = pitch };
        }
    }
}
