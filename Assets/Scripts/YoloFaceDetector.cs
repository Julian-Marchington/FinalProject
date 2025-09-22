using UnityEngine;
using UnityEngine.UI;

using System.Collections.Generic;
using System.Linq;

public class YoloFaceDetector : MonoBehaviour
{
    [Header("Model")]
    public Unity.InferenceEngine.ModelAsset onnxModel;              // yolov8n/10n/11n-face.onnx
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.GPUCompute;

    [Header("UI / Canvas")]
    public RawImage videoImage;               // assign your RawImage that shows the webcam
    public RectTransform canvasRect;         // assign the Canvas RectTransform
    public BoxDrawer boxDrawer;              // assign the BoxDrawer component
    public GameObject boxPrefab;             // assign a simple UI Image prefab (outline) with optional Text child
    public RectTransform videoRect;   // assign VideoImage.rectTransform in Inspector


    [Header("Webcam")]
    public int requestWidth = 1280;
    public int requestHeight = 720;
    public int requestFPS = 30;

    [Header("Detection")]
    [Range(0.05f, 0.9f)] public float confThreshold = 0.35f;
    [Range(0.0f, 1.0f)] public float iouThreshold = 0.45f;

    const int MODEL_SIZE = 640;

    private Unity.InferenceEngine.Worker _worker;
    private WebCamTexture _webcam;

    // Resize/readback buffers
    private RenderTexture _rt640;
    private Texture2D _readbackTex;

    private bool _loggedShape = false;

    [Header("Gaze")]
    public Unity.InferenceEngine.ModelAsset gazeOnnx;               // drag mobilenetv2_gaze.onnx (or resnet18_gaze.onnx)
    public Unity.InferenceEngine.BackendType gazeBackend = Unity.InferenceEngine.BackendType.CPU; // start with CPU; switch to GPU later
    [Range(10f, 250f)] public float gazeDrawScale = 80f;    // arrow length in px on the RawImage
    public SimpleGazeVisualizer gazeVisualizer; // Assign the SimpleGazeVisualizer component

    const int GAZE_SIZE = 448;

    private Unity.InferenceEngine.Worker _gazeWorker;

    // Reuse our 640x640 CPU pixel buffer each frame:
    private Color32[] _cols640;  // fill once per frame from _readbackTex.GetPixels32()
    private bool _haveCols;

    public GameObject faceBoxPrefab;
    // Remove the old gaze prefab fields - we don't need them anymore

    void Start()
    {
        // Start webcam
        _webcam = new WebCamTexture(requestWidth, requestHeight, requestFPS);
        _webcam.Play();
        if (videoImage != null) videoImage.texture = _webcam;

        // Load model + create worker
        var model = Unity.InferenceEngine.ModelLoader.Load(onnxModel);
        _worker = new Unity.InferenceEngine.Worker(model, backend);

        // Init drawer
        if (boxDrawer != null && faceBoxPrefab != null)
        {
            boxDrawer.Init(videoRect, faceBoxPrefab);
            Debug.Log($"[GAZE] BoxDrawer initialized with videoRect: {videoRect?.name}");
        }
        else
        {
            Debug.LogError("[GAZE] BoxDrawer or faceBoxPrefab not assigned!");
        }

        // Allocate resize and readback resources
        _rt640 = new RenderTexture(MODEL_SIZE, MODEL_SIZE, 0, RenderTextureFormat.ARGB32);
        _readbackTex = new Texture2D(MODEL_SIZE, MODEL_SIZE, TextureFormat.RGBA32, false);

        // Initialize gaze detection
        if (gazeOnnx != null)
        {
            try
            {
                var gazeModel = Unity.InferenceEngine.ModelLoader.Load(gazeOnnx);
                _gazeWorker = new Unity.InferenceEngine.Worker(gazeModel, gazeBackend);
                Debug.Log($"[GAZE] Gaze model loaded successfully. Backend: {gazeBackend}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GAZE] Failed to load gaze model: {e.Message}");
                _gazeWorker = null;
            }
        }
        else
        {
            Debug.LogWarning("[GAZE] No gaze model assigned. Gaze detection will be disabled.");
        }

        // Validate prefabs
        if (faceBoxPrefab == null) Debug.LogError("[GAZE] FaceBox prefab is not assigned!");
        if (gazeVisualizer == null) Debug.LogError("[GAZE] SimpleGazeVisualizer is not assigned!");
    }

    void OnDisable()
    {
        _worker?.Dispose();
        _gazeWorker?.Dispose();

        if (_webcam != null && _webcam.isPlaying) _webcam.Stop();

        if (_rt640 != null) { _rt640.Release(); _rt640 = null; }
        if (_readbackTex != null) Destroy(_readbackTex);
    }

    void Update()
    {
        if (_webcam == null || !_webcam.didUpdateThisFrame) return;

        // If your webcam looks mirrored, uncomment:
        // videoImage.uvRect = new Rect(1, 0, -1, 1);

        // 1) Resize webcam -> 640x640 on GPU
        Graphics.Blit(_webcam, _rt640);

        // 2) Read pixels back to CPU
        var prev = RenderTexture.active;
        RenderTexture.active = _rt640;
        _readbackTex.ReadPixels(new Rect(0, 0, MODEL_SIZE, MODEL_SIZE), 0, 0);
        _readbackTex.Apply(false);
        
        _cols640 = _readbackTex.GetPixels32();
        _haveCols = true;

        RenderTexture.active = prev;

        // 3) Get pixel buffer

        // 4) Create input tensor [N,C,H,W] = [1,3,640,640], normalized 0..1
        using (var input = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 3, MODEL_SIZE, MODEL_SIZE)))
        {
            // Fill channels (RGB), flip Y so tensor origin is top-left
            int plane = MODEL_SIZE * MODEL_SIZE;
            int idxR = 0 * plane;
            int idxG = 1 * plane;
            int idxB = 2 * plane;

            for (int y = 0; y < MODEL_SIZE; y++)
            {
                int srcY = MODEL_SIZE - 1 - y; // vertical flip
                int rowOff = srcY * MODEL_SIZE;
                int dstRow = y * MODEL_SIZE;

                for (int x = 0; x < MODEL_SIZE; x++)
                {
                    var c = _cols640[rowOff + x];   // <-- use cols (not _pixels)
                    int ofs = dstRow + x;
                    input[ofs + idxR] = c.r / 255f;
                    input[ofs + idxG] = c.g / 255f;
                    input[ofs + idxB] = c.b / 255f;
                }
            }

            // 5) Run model synchronously (2.x)
            _worker.Schedule(input);
        }

        // 6) Get output + download data (blocking) for CPU parsing
        using (var output = _worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            if (output == null) return;

            float[] data = output.DownloadToArray();   // 2.x way to read CPU-side

            if (!_loggedShape)
            {
                Debug.Log($"[YOLO] Output shape = {string.Join("x", output.shape.ToArray())} (data.Length={data.Length})");

                _loggedShape = true;
            }

            var dets = ParseUltralyticsDetections(data, output.shape, confThreshold);

            var kept = NMS(dets, iouThreshold)
             .OrderByDescending(d => d.score)
             .Take(1)   // keep best 1 face
             .ToList();

            if (_gazeWorker != null && _haveCols && kept.Count > 0)
            {
                var g = RunGazeOnFace(_cols640, kept[0].rect);   // yaw,pitch in degrees
                DrawGazeArrow(kept[0].rect, g.yaw, g.pitch);
            }

            DrawDetections(kept);
        }
    }

    struct Detection
    {
        public Rect rect;   // in 640x640 pixel space
        public float score;
        public string label;
    }

    private static float Sig(float x) => 1f / (1f + Mathf.Exp(-x));

    List<Detection> ParseUltralyticsDetections(float[] data, Unity.InferenceEngine.TensorShape shape, float conf)
    {
        var dims = shape.ToArray(); // [1,5,8400]
        int C = dims[1];            // channels = 5
        int A = dims[2];            // anchors = 8400

        var dets = new List<Detection>(32);

        // Access: data is [1,C,A], so index = c*A + a
        float Get(int a, int c) => data[c * A + a];

        // YOLO-face exports are usually normalized 0–1
        for (int i = 0; i < A; i++)
        {
            float cx = Get(i, 0);
            float cy = Get(i, 1);
            float w  = Get(i, 2);
            float h  = Get(i, 3);

            float confScore = Get(i, 4);  // already probability

            if (confScore < conf) continue;

            float x = cx - w * 0.5f;
            float y = cy - h * 0.5f;

            dets.Add(new Detection
            {
                rect = new Rect(x, y, w, h),
                score = confScore,
                label = "face"
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

    void DrawDetections(List<Detection> dets)
    {
        if (boxDrawer == null || videoRect == null) return;

        boxDrawer.Clear();

        // Work directly in the RawImage's local space (bottom-left origin)
        Vector2 vSize = videoRect.rect.size;   // width/height of the RawImage in its own space
        float sx = vSize.x / MODEL_SIZE;
        float sy = vSize.y / MODEL_SIZE;

        if (dets.Count > 0)
        {
            var d0 = dets[0];
            //Debug.Log($"[DBG] videoRect.size={vSize}  modelRect={d0.rect}  firstBoxScaled=({d0.rect.xMin * vSize.x / MODEL_SIZE}, {d0.rect.yMin * vSize.y / MODEL_SIZE})");
        }

        bool flippedX = (videoImage != null && videoImage.uvRect.width < 0);

        foreach (var d in dets)
        {
            float x = d.rect.xMin * sx;
            float yTop = d.rect.yMin * sy;      // top-left Y in video space
            float w = d.rect.width * sx;
            float h = d.rect.height * sy;

            // Convert top-left origin -> bottom-left origin
            float y = (vSize.y - (yTop + h));

            if (flippedX) x = (vSize.x - (x + w));

            var box = new Rect(x, y, w, h);
            boxDrawer.DrawBox(box, d.label, d.score);   // <-- put this back
        }

    }

    struct GazeAngles { public float yaw; public float pitch; } // degrees

    GazeAngles RunGazeOnFace(Color32[] cols640, Rect faceBox)
    {
        // Clamp to image bounds (0..639)
        int x0 = Mathf.Clamp(Mathf.RoundToInt(faceBox.xMin), 0, MODEL_SIZE - 1);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(faceBox.yMin), 0, MODEL_SIZE - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(faceBox.xMax), 0, MODEL_SIZE - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(faceBox.yMax), 0, MODEL_SIZE - 1);
        int w = Mathf.Max(1, x1 - x0);
        int h = Mathf.Max(1, y1 - y0);

        // Debug logging for face region
        Debug.Log($"[GAZE] Face region: x0={x0}, y0={y0}, w={w}, h={h}");

        // Create input tensor [1,3,448,448], RGB, normalized (ImageNet)
        using (var t = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 3, GAZE_SIZE, GAZE_SIZE)))
        {
            int plane = GAZE_SIZE * GAZE_SIZE;
            int idxR = 0 * plane, idxG = 1 * plane, idxB = 2 * plane;

            // ImageNet mean/std
            const float mR = 0.485f, mG = 0.456f, mB = 0.406f;
            const float sR = 0.229f, sG = 0.224f, sB = 0.225f;

            // FIXED: Proper coordinate system handling
            // The faceBox is in 640x640 space, we need to sample from cols640 correctly
            // cols640 has bottom-left origin due to ReadPixels

            for (int gy = 0; gy < GAZE_SIZE; gy++)
            {
                // normalized (0..1) -> face region row (top->bottom in face region)
                float v = (gy + 0.5f) / GAZE_SIZE;
                float srcYf = y0 + v * h;
                int srcY0 = Mathf.Clamp((int)srcYf, 0, MODEL_SIZE - 1);
                int srcY1 = Mathf.Min(srcY0 + 1, MODEL_SIZE - 1);
                float fy = srcYf - srcY0;

                for (int gx = 0; gx < GAZE_SIZE; gx++)
                {
                    float u = (gx + 0.5f) / GAZE_SIZE;
                    float srcXf = x0 + u * w;
                    int srcX0 = Mathf.Clamp((int)srcXf, 0, MODEL_SIZE - 1);
                    int srcX1 = Mathf.Min(srcX0 + 1, MODEL_SIZE - 1);
                    float fx = srcXf - srcX0;

                    // Bilinear sample from cols640
                    // Note: cols640 has bottom-left origin, but we're sampling from the face region
                    int i00 = srcY0 * MODEL_SIZE + srcX0;
                    int i01 = srcY0 * MODEL_SIZE + srcX1;
                    int i10 = srcY1 * MODEL_SIZE + srcX0;
                    int i11 = srcY1 * MODEL_SIZE + srcX1;

                    Color32 c00 = cols640[i00], c01 = cols640[i01];
                    Color32 c10 = cols640[i10], c11 = cols640[i11];

                    float r = Mathf.Lerp(Mathf.Lerp(c00.r, c01.r, fx), Mathf.Lerp(c10.r, c11.r, fx), fy) / 255f;
                    float g = Mathf.Lerp(Mathf.Lerp(c00.g, c01.g, fx), Mathf.Lerp(c10.g, c11.g, fx), fy) / 255f;
                    float b = Mathf.Lerp(Mathf.Lerp(c00.b, c01.b, fx), Mathf.Lerp(c10.b, c11.b, fx), fy) / 255f;

                    int ofs = gy * GAZE_SIZE + gx;
                    t[ofs + idxR] = (r - mR) / sR;
                    t[ofs + idxG] = (g - mG) / sG;
                    t[ofs + idxB] = (b - mB) / sB;
                }
            }

            // Inference
            _gazeWorker.Schedule(t);
        }

        // Read output
        using (var outT = _gazeWorker.PeekOutput() as Unity.InferenceEngine.Tensor<float>)
        {
            float[] outArr = outT.DownloadToArray();

            // Most MobileGaze ONNX exports give 2 values: [yaw, pitch] (often in radians).
            // If you downloaded a classification variant, it could be longer; we'll handle both.
            float yaw, pitch;

            if (outArr.Length >= 2)
            {
                yaw   = outArr[0];
                pitch = outArr[1];

                // FIXED: Better radian detection and conversion
                // Check if values are likely radians (typically -π to π)
                if (Mathf.Abs(yaw) < 3.2f && Mathf.Abs(pitch) < 3.2f)
                {
                    yaw   *= Mathf.Rad2Deg;
                    pitch *= Mathf.Rad2Deg;
                    Debug.Log($"[GAZE] Converted from radians: yaw={yaw:F1}°, pitch={pitch:F1}°");
                }
                else
                {
                    Debug.Log($"[GAZE] Values appear to be in degrees: yaw={yaw:F1}°, pitch={pitch:F1}°");
                }

                // Clamp values to reasonable ranges
                yaw = Mathf.Clamp(yaw, -90f, 90f);
                pitch = Mathf.Clamp(pitch, -45f, 45f);
            }
            else
            {
                // Unexpected shape – log and fall back to zeros
                Debug.LogWarning($"Gaze output unexpected length: {outArr.Length}");
                yaw = pitch = 0f;
            }

            return new GazeAngles { yaw = yaw, pitch = pitch };
        }
    }

    void DrawGazeArrow(Rect faceModelRect, float yawDeg, float pitchDeg)
    {
        if (gazeVisualizer == null) return;
        
        // Simply update the gaze visualizer with the new direction
        //gazeVisualizer.UpdateGazeDirection(faceModelRect, yawDeg, pitchDeg);
    }

    // Test method to validate gaze detection
    public void TestGazeDetection()
    {
        if (_gazeWorker == null)
        {
            Debug.LogError("[GAZE] Gaze worker not initialized!");
            return;
        }

        // Create a test face region (center of the image)
        var testFaceRect = new Rect(320, 320, 100, 100);
        Debug.Log($"[GAZE] Testing with face region: {testFaceRect}");

        if (_haveCols)
        {
            var gazeAngles = RunGazeOnFace(_cols640, testFaceRect);
            Debug.Log($"[GAZE] Test result: yaw={gazeAngles.yaw:F1}°, pitch={gazeAngles.pitch:F1}°");
        }
        else
        {
            Debug.LogWarning("[GAZE] No pixel data available for testing");
        }
    }

    // Public method to manually trigger gaze detection (for testing)
    public void ManualGazeDetection()
    {
        if (_webcam != null && _webcam.isPlaying && _gazeWorker != null)
        {
            // Force a frame update
            Update();
        }
    }
}
