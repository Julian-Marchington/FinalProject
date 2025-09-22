// PCAGazeDetector.cs — manual calibration + persistence + editor/quest parity
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR;

public class OldDetect : MonoBehaviour
{
    [Header("Models")]
    public Unity.InferenceEngine.ModelAsset faceModel;
    public Unity.InferenceEngine.ModelAsset gazeModel;
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.CPU;

    [Header("Source")]
    public PCAFeedBinder pcaFeed;     // Quest PCA binder (assign in scene)
    public bool useQuestPCA = true;   // true = Quest cameras, false = PC webcam (Editor)

    [Header("Webcam (PC fallback only)")]
    public int requestWidth = 1280, requestHeight = 720, requestFPS = 30;

    [Header("Detection")]
    [Range(0.05f, 0.9f)] public float confThreshold = 0.35f;
    [Range(0.0f, 1.0f)]  public float iouThreshold  = 0.45f;
    [Range(1, 8)]        public int   maxFaces = 3;
    public float minFaceSize = 90f; // px in the 640x640 tensor

    [Header("Gaze Conventions")]
    public bool invertYaw   = true;   // webcams mirrored: true; Quest PCA: overridden to false
    public bool invertPitch = false;

    [Header("Gaze Preprocess")]
    public bool useImagenetNorm = true;
    [SerializeField] bool swapYawPitch = false;  // some ONNX exports are [yaw,pitch]

    [Header("Calibration (manual)")]
    public bool  manualCalibration = true;   // if true, we don’t auto-zero; use buttons/keys
    public bool  persistCalibration = true;
    public string prefsKey = "PCAGazeCalib_v1";
    [Tooltip("These are the stored zero-bias values in degrees.")]
    public float yawBias = 0f, pitchBias = 0f;

    [Header("Gaze gate (looking-at-camera)")]
    public float yawHalfDeg   = 32f;  // loosen slightly for MR
    public float pitchHalfDeg = 24f;

    // Output you can show on controller UI
    public string LatestStatus { get; private set; } = "Faces: 0 | Looking: 0";

    const int MODEL_SIZE = 640;
    const int GAZE_SIZE  = 448; // your gaze model

    private Unity.InferenceEngine.Worker _faceWorker;
    private Unity.InferenceEngine.Worker _gazeWorker;

    private WebCamTexture _pcWebcam;   // editor fallback
    private RenderTexture _rt640;
    private Texture2D _readbackTex;
    private Color32[] _cols640;
    private bool _haveCols;
    private bool _loggedShape = false;

    // edge detection for controller buttons
    bool _prevCalPress, _prevResetPress;

    // last largest-face sample (for manual “calibrate now”)
    bool _haveSample;
    float _lastYaw, _lastPitch;
    float _lastSidePx;

    // Editor helper (optional preview)
    public Texture DebugWebcamTexture => useQuestPCA ? null : (Texture)_pcWebcam;

    void Awake()
    {
        if (persistCalibration) LoadCalib();
    }

    void Start()
    {
        if (!useQuestPCA)
        {
            _pcWebcam = new WebCamTexture(requestWidth, requestHeight, requestFPS);
            _pcWebcam.Play();
        }

        if (faceModel != null)
        {
            var m = Unity.InferenceEngine.ModelLoader.Load(faceModel);
            _faceWorker = new Unity.InferenceEngine.Worker(m, backend);
            Debug.Log("[GAZE] Face model loaded");
        }
        else Debug.LogError("[GAZE] Face model missing");

        if (gazeModel != null)
        {
            var m = Unity.InferenceEngine.ModelLoader.Load(gazeModel);
            _gazeWorker = new Unity.InferenceEngine.Worker(m, backend);
            Debug.Log("[GAZE] Gaze model loaded");
        }
        else Debug.LogError("[GAZE] Gaze model missing");

        _rt640       = new RenderTexture(MODEL_SIZE, MODEL_SIZE, 0, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(MODEL_SIZE, MODEL_SIZE, TextureFormat.RGBA32, false);
    }

    void OnDisable()
    {
        if (persistCalibration) SaveCalib();

        _faceWorker?.Dispose();
        _gazeWorker?.Dispose();
        if (_pcWebcam != null && _pcWebcam.isPlaying) _pcWebcam.Stop();
        if (_rt640 != null) { _rt640.Release(); _rt640 = null; }
        if (_readbackTex != null) Destroy(_readbackTex);
    }

    void Update()
    {
        // --- Source selection ---
        WebCamTexture wct = null;
        Texture srcTex = null;

        if (useQuestPCA)
        {
            if (!pcaFeed)
            {
                Debug.LogWarning("[GAZE] useQuestPCA=true but PCAFeedBinder is not assigned.");
                return;
            }
            wct = pcaFeed.WebCamTexture;
            if (wct == null)
            {
                Debug.LogWarning("[GAZE] Quest PCA WebCamTexture not ready (permission or startup).");
                return;
            }
            srcTex = wct;
        }
        else
        {
            wct = _pcWebcam;
            if (wct == null) return;
            if (!wct.didUpdateThisFrame) return;
            srcTex = wct;
        }

        // --- Readback 640x640 ---
        Graphics.Blit(srcTex, _rt640);

        var prev = RenderTexture.active;
        RenderTexture.active = _rt640;
        _readbackTex.ReadPixels(new Rect(0, 0, MODEL_SIZE, MODEL_SIZE), 0, 0);
        _readbackTex.Apply(false);
        _cols640   = _readbackTex.GetPixels32();
        _haveCols  = true;
        RenderTexture.active = prev;

        // --- Detect faces (top-K) ---
        var faces = DetectFaces(maxFaces);
        if (faces.Count == 0)
        {
            LatestStatus = "Faces: 0 | Looking: 0";
            _haveSample = false;
            return;
        }

        // --- Per face: gaze + looking classification ---
        int lookingCount = 0;
        _haveSample = false;
        _lastSidePx = 0;

        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            float side = Mathf.Min(f.width, f.height);
            if (side < minFaceSize) continue;

            var gaze = RunGazeOnFace(_cols640, f, out float yawDeg, out float pitchDeg);

            // store the largest-face sample for "Calibrate Now"
            if (side > _lastSidePx)
            {
                _lastSidePx = side;
                _lastYaw = gaze.yaw;
                _lastPitch = gaze.pitch;
                _haveSample = true;
            }

            if (IsGazeAtCamera(gaze.yaw, gaze.pitch)) lookingCount++;
        }

        LatestStatus = $"Faces: {faces.Count} | Looking: {lookingCount}";

        // --- Manual calibration hotkeys/buttons ---
        if (manualCalibration)
        {
            if (CalibratePressed() && _haveSample)
            {
                SetCalibration(_lastYaw, _lastPitch, $"(side={_lastSidePx:F0}px)");
            }
            if (ResetPressed())
            {
                ClearCalibration();
            }
        }
    }

    // ---------- Manual calibration API ----------
    public void SetCalibration(float yawZero, float pitchZero, string reason = "")
    {
        yawBias = yawZero;
        pitchBias = pitchZero;
        if (persistCalibration) SaveCalib();
        Debug.Log($"[GAZE] Manual Calibrated: yawBias={yawBias:F1}, pitchBias={pitchBias:F1} {reason}");
    }

    public void ClearCalibration()
    {
        yawBias = 0f;
        pitchBias = 0f;
        if (persistCalibration) SaveCalib();
        Debug.Log("[GAZE] Calibration cleared (bias=0,0).");
    }

    void SaveCalib()
    {
        PlayerPrefs.SetFloat(prefsKey + "_yb", yawBias);
        PlayerPrefs.SetFloat(prefsKey + "_pb", pitchBias);
        PlayerPrefs.Save();
    }

    void LoadCalib()
    {
        if (PlayerPrefs.HasKey(prefsKey + "_yb"))
        {
            yawBias   = PlayerPrefs.GetFloat(prefsKey + "_yb", 0f);
            pitchBias = PlayerPrefs.GetFloat(prefsKey + "_pb", 0f);
            Debug.Log($"[GAZE] Loaded calibration: yawBias={yawBias:F1}, pitchBias={pitchBias:F1}");
        }
    }

    bool CalibratePressed()
    {
        bool pressed = false;

        // Keyboard (Editor)
        if (Input.GetKey(KeyCode.C)) pressed = true;

        // Quest controllers (A or X)
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (right.isValid && right.TryGetFeatureValue(CommonUsages.primaryButton, out bool a)) pressed |= a;
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.primaryButton, out bool x)) pressed |= x;

        bool edge = pressed && !_prevCalPress;
        _prevCalPress = pressed;
        return edge;
    }

    bool ResetPressed()
    {
        bool pressed = false;

        // Keyboard
        if (Input.GetKey(KeyCode.R)) pressed = true;

        // Quest controllers (B or Y)
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (right.isValid && right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b)) pressed |= b;
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool y)) pressed |= y;

        bool edge = pressed && !_prevResetPress;
        _prevResetPress = pressed;
        return edge;
    }

    // ---------- Looking gate ----------
    bool IsGazeAtCamera(float yawDeg, float pitchDeg)
    {
        float yaw   = yawDeg   - yawBias;
        float pitch = pitchDeg - pitchBias;

        float ey = yaw / yawHalfDeg;
        float ep = pitch / pitchHalfDeg;
        return (ey * ey + ep * ep) <= 1f;
    }

    // ---------- Face detection (top-K) ----------
    List<Rect> DetectFaces(int maxK)
    {
        var faces = new List<Rect>();
        if (_faceWorker == null || !_haveCols) return faces;

        using (var input = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, 3, MODEL_SIZE, MODEL_SIZE)))
        {
            int plane = MODEL_SIZE * MODEL_SIZE;
            int idxR = 0 * plane, idxG = 1 * plane, idxB = 2 * plane;

            // vertical flip to match your YOLO-face export
            for (int y = 0; y < MODEL_SIZE; y++)
            {
                int srcY = MODEL_SIZE - 1 - y;
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
            _faceWorker.Schedule(input);
        }

        using (var output = _faceWorker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            if (output == null) return faces;

            float[] data = output.DownloadToArray();

            if (!_loggedShape)
            {
                Debug.Log($"[GAZE] Face output shape: {string.Join("x", output.shape.ToArray())}");
                _loggedShape = true;
            }

            var dets = ParseDetections(data, output.shape, confThreshold);
            var kept = NMS(dets, iouThreshold).OrderByDescending(d => d.score).Take(maxK).ToList();

            foreach (var d in kept)
            {
                var r = d.rect;
                r.y = MODEL_SIZE - r.y - r.height; // unflip

                r.x      = Mathf.Clamp(r.x, 0, MODEL_SIZE - 1);
                r.y      = Mathf.Clamp(r.y, 0, MODEL_SIZE - 1);
                r.width  = Mathf.Clamp(r.width,  1, MODEL_SIZE - r.x);
                r.height = Mathf.Clamp(r.height, 1, MODEL_SIZE - r.y);

                faces.Add(r);
            }
        }
        return faces;
    }

    struct Detection { public Rect rect; public float score; }

    List<Detection> ParseDetections(float[] data, Unity.InferenceEngine.TensorShape shape, float conf)
    {
        var dims = shape.ToArray(); // expected [1,5,8400]
        if (dims.Length < 3) return new List<Detection>();
        int A = dims[2];

        var dets = new List<Detection>(32);
        float Get(int a, int c) => data[c * A + a];

        for (int i = 0; i < A; i++)
        {
            float cx = Get(i, 0), cy = Get(i, 1);
            float w  = Get(i, 2), h  = Get(i, 3);
            float s  = Get(i, 4);
            if (s < conf) continue;

            float x = cx - w * 0.5f;
            float y = cy - h * 0.5f;
            dets.Add(new Detection { rect = new Rect(x, y, w, h), score = s });
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
                if (IoU(a.rect, sorted[j].rect) > iou) sorted.RemoveAt(j);
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

    // Returns bias-applied yaw/pitch for gate, and also supplies raw degrees via out params
    GazeAngles RunGazeOnFace(Color32[] cols640, Rect faceBox, out float yawDegRaw, out float pitchDegRaw)
    {
        yawDegRaw = 0f; pitchDegRaw = 0f;

        // square, margin-padded crop around the face
        float cx = faceBox.x + faceBox.width * 0.5f;
        float cy = faceBox.y + faceBox.height * 0.5f;

        float side = Mathf.Max(faceBox.width, faceBox.height) * 1.40f;
        float x0f = Mathf.Clamp(cx - side * 0.5f, 0, MODEL_SIZE - 1);
        float y0f = Mathf.Clamp(cy - side * 0.5f, 0, MODEL_SIZE - 1);
        float x1f = Mathf.Clamp(cx + side * 0.5f, 0, MODEL_SIZE - 1);
        float y1f = Mathf.Clamp(cy + side * 0.5f, 0, MODEL_SIZE - 1);

        float srcSideX = (x1f - x0f);
        float srcSideY = (y1f - y0f);
        float srcSide  = Mathf.Min(srcSideX, srcSideY);
        x0f = Mathf.Clamp(cx - srcSide * 0.5f, 0, MODEL_SIZE - 1);
        y0f = Mathf.Clamp(cy - srcSide * 0.5f, 0, MODEL_SIZE - 1);
        x1f = Mathf.Clamp(cx + srcSide * 0.5f, 0, MODEL_SIZE - 1);
        y1f = Mathf.Clamp(cy + srcSide * 0.5f, 0, MODEL_SIZE - 1);
        srcSide = Mathf.Min(x1f - x0f, y1f - y0f);

        using (var t = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, 3, GAZE_SIZE, GAZE_SIZE)))
        {
            int plane = GAZE_SIZE * GAZE_SIZE;
            int idxR = 0 * plane, idxG = 1 * plane, idxB = 2 * plane;

            const float mR = 0.485f, mG = 0.456f, mB = 0.406f;
            const float sR = 0.229f, sG = 0.224f, sB = 0.225f;

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
            _gazeWorker.Schedule(t);
        }

        using (var outT = _gazeWorker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            if (outT == null) return new GazeAngles { yaw = 0, pitch = 0 };

            float[] outArr = outT.DownloadToArray();
            if (outArr.Length < 2) return new GazeAngles { yaw = 0, pitch = 0 };

            float a = outArr[0], b = outArr[1];
            float pitchRaw = swapYawPitch ? b : a;
            float yawRaw   = swapYawPitch ? a : b;

            bool rad = Mathf.Abs(yawRaw) < 6.3f && Mathf.Abs(pitchRaw) < 6.3f;
            yawDegRaw   = rad ? yawRaw   * Mathf.Rad2Deg : yawRaw;   // <-- assignment, not redeclare
            pitchDegRaw = rad ? pitchRaw * Mathf.Rad2Deg : pitchRaw; // <-- assignment, not redeclare

            // auto source-aware yaw invert
            bool yawInvertEffective = invertYaw;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (useQuestPCA) yawInvertEffective = false; // Quest PCA not mirrored
#endif
            float yaw   = yawInvertEffective ? -yawDegRaw   :  yawDegRaw;
            float pitch = invertPitch        ? -pitchDegRaw :  pitchDegRaw;

            yaw   = Mathf.Clamp(yaw,   -180f, 180f);
            pitch = Mathf.Clamp(pitch,  -90f,  90f);

            return new GazeAngles { yaw = yaw, pitch = pitch };
        }
    }
}
