using UnityEngine;

using TMPro;
using PassthroughCameraSamples; // WebCamTextureManager
using System.Collections;

public class RealtimeGazeHUD : MonoBehaviour
{
    [Header("Camera sources")]
    public WebCamTextureManager webcamManager;      // Quest build
    public WebcamTestManager webcamTestManager;     // PC testing

    [Header("ONNX (Sentis)")]
    public Unity.InferenceEngine.ModelAsset gazeModel;                    // e.g., mobileone_s0_gaze.onnx
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.GPUCompute;
    public int inputSize = 448;                    // set to your ONNX input (many use 448)

    [Header("UI")]
    public TextMeshProUGUI statusText;              // "Looking at you" / "Not looking"
    public TextMeshProUGUI debugText;               // "yaw: … pitch: …"

    [Header("Timing & Logic")]
    public float updateInterval = 0.2f;             // seconds between inferences
    public float yawDeadband = 0.30f;               // radians
    public float pitchDeadband = 0.60f;             // radians
    [Range(0f, 1f)] public float smoothing = 0.6f;  // EMA smoothing (higher = smoother)

    private Unity.InferenceEngine.Worker worker;
    private float timer;
    private Texture2D resizeBuffer;
    private bool emaInitialized = false;
    private Vector2 ema;                            // smoothed yaw/pitch

    private bool isProcessing = false;
    private float lastInferenceTime = 0f;
    private float inferenceDelay = 0.1f;            // 100ms min gap

    void Start()
    {
        if (gazeModel == null)
        {
            Debug.LogError("[RealtimeGazeHUD] Missing gazeModel (ModelAsset).");
            enabled = false;
            return;
        }

        // Load & compile the model
        var model = Unity.InferenceEngine.ModelLoader.Load(gazeModel);
        var graph = new Unity.InferenceEngine.FunctionalGraph();
        var inputs = graph.AddInputs(model);
        var outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
        var runtime = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtime, backend);

        if (statusText) statusText.text = "Gaze: —";
        if (debugText)  debugText.text  = "yaw: — pitch: —";
    }

    void Update()
    {
        // Choose whichever camera is available
        WebCamTexture wct = null;
        if (webcamManager != null && webcamManager.WebCamTexture != null && webcamManager.WebCamTexture.isPlaying)
        {
            wct = webcamManager.WebCamTexture;
            Debug.Log("[RealtimeGazeHUD] Using Quest camera");
        }
        else if (webcamTestManager != null && webcamTestManager.WebCamTexture != null && webcamTestManager.WebCamTexture.isPlaying)
        {
            wct = webcamTestManager.WebCamTexture;
            Debug.Log($"[RealtimeGazeHUD] Using PC webcam: {wct.width}x{wct.height}");
        }

        if (wct == null || wct.width <= 16 || wct.height <= 16)
        {
            if (statusText) statusText.text = "No Camera";
            if (debugText)  debugText.text  = "Camera not available";
            Debug.LogWarning("[RealtimeGazeHUD] No valid camera available");
            return;
        }

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        // On device, only run on fresh frames (prevents reusing the same buffer)
        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!wct.didUpdateThisFrame) return;
        #endif

        if (!isProcessing && Time.time - lastInferenceTime >= inferenceDelay)
            StartCoroutine(ProcessGazeDetection(wct));

        UpdateUI();
    }

    private IEnumerator ProcessGazeDetection(WebCamTexture wct)
    {
        isProcessing = true;
        lastInferenceTime = Time.time;

        // 1) Capture & resize to model input
        if (!TryCaptureToSquare(wct, inputSize, out var pixels))
        {
            isProcessing = false;
            yield break;
        }

        // 2) Build NCHW (1,3,H,W) with ImageNet normalization on CPU
        int H = inputSize, W = inputSize;
        float[] data = new float[1 * 3 * H * W];
        var mean = new Vector3(0.485f, 0.456f, 0.406f);
        var std  = new Vector3(0.229f, 0.224f, 0.225f);

        for (int h = 0; h < H; h++)
        {
            int row = h * W;
            for (int x = 0; x < W; x++)
            {
                int p = row + x;
                var c = pixels[p];

                float r = (c.r / 255f - mean.x) / std.x;
                float g = (c.g / 255f - mean.y) / std.y;
                float b = (c.b / 255f - mean.z) / std.z;

                int baseIdx = h * W + x;
                data[0 * H * W + baseIdx] = r; // R
                data[1 * H * W + baseIdx] = g; // G
                data[2 * H * W + baseIdx] = b; // B
            }
        }

        using var input = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 3, H, W), data);

        // 3) Run inference (async) then poll for completion using PeekOutput()
        worker.Schedule(input);

        // Poll up to a short timeout to avoid blocking the main thread forever
        const float maxWait = 0.5f; // 500 ms
        float t0 = Time.time;
        Unity.InferenceEngine.Tensor<float> output = null;

        while (Time.time - t0 < maxWait)
        {
            output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
            if (output != null)
                break;
            yield return null; // wait a frame
        }

        if (output == null)
        {
            Debug.LogWarning("[RealtimeGazeHUD] Inference timeout or null output.");
            isProcessing = false;
            yield break;
        }

        // 4) Read and interpret output
        var arr = output.DownloadToArray(); // could be [yaw,pitch] or [x,y,z] or face landmarks
        Debug.Log($"[RealtimeGazeHUD] Raw model output: [{string.Join(", ", arr)}] (length: {arr.Length})");
        
        float yaw = 0f, pitch = 0f;

        if (arr.Length == 2)
        {
            yaw = arr[0];
            pitch = arr[1];
            Debug.Log($"[RealtimeGazeHUD] Using 2D output - yaw: {yaw:F3}, pitch: {pitch:F3}");
        }
        else if (arr.Length == 3)
        {
            float x = arr[0], y = arr[1], z = arr[2];
            yaw   = Mathf.Atan2(x, z);
            pitch = Mathf.Atan2(-y, Mathf.Sqrt(x * x + z * z));
            Debug.Log($"[RealtimeGazeHUD] Using 3D output - x:{x:F3}, y:{y:F3}, z:{z:F3} -> yaw: {yaw:F3}, pitch: {pitch:F3}");
        }
        else if (arr.Length == 90)
        {
            // Interpret as 30 face landmarks with 3 coordinates each (x, y, z)
            // Calculate gaze from eye region landmarks
            yaw = CalculateGazeFromLandmarks(arr);
            pitch = CalculatePitchFromLandmarks(arr);
            Debug.Log($"[RealtimeGazeHUD] Using face landmarks (90 elements) - calculated yaw: {yaw:F3}, pitch: {pitch:F3}");
        }
        else
        {
            Debug.LogWarning($"[RealtimeGazeHUD] Unexpected output size: {arr.Length}");
        }

        // 5) EMA smoothing
        if (!emaInitialized)
        {
            ema = new Vector2(yaw, pitch);
            emaInitialized = true;
        }
        else
        {
            float alpha = 1f - Mathf.Clamp01(smoothing); // e.g., 0.6 -> alpha 0.4
            ema.x = Mathf.Lerp(ema.x, yaw,   alpha);
            ema.y = Mathf.Lerp(ema.y, pitch, alpha);
        }

        isProcessing = false;
    }

    private void UpdateUI()
    {
        if (emaInitialized)
        {
            bool looking =
                Mathf.Abs(ema.x) <= yawDeadband &&
                Mathf.Abs(ema.y) <= pitchDeadband;

            if (statusText) statusText.text = looking ? "Looking at you" : "Not looking";
            if (debugText)  debugText.text  = $"yaw: {ema.x:F2}  pitch: {ema.y:F2}";
        }
        else
        {
            if (statusText) statusText.text = "Initializing...";
            if (debugText)  debugText.text  = "Processing...";
        }
    }

    private bool TryCaptureToSquare(WebCamTexture wct, int size, out Color32[] pixelsOut)
    {
        pixelsOut = null;

        if (resizeBuffer == null || resizeBuffer.width != size || resizeBuffer.height != size)
            resizeBuffer = new Texture2D(size, size, TextureFormat.RGBA32, false);

        // GPU resize into the square buffer
        Graphics.ConvertTexture(wct, resizeBuffer);

        // Pull to CPU once per tick
        pixelsOut = resizeBuffer.GetPixels32();
        return true;
    }

    private float CalculateGazeFromLandmarks(float[] landmarks)
    {
        // For 90 elements, we have 30 landmarks with 3 coordinates each
        // Assuming standard face landmark ordering, we'll use eye region landmarks
        // This is a simplified approach - you may need to adjust indices based on your specific model
        
        if (landmarks.Length != 90) return 0f;
        
        // Extract eye region landmarks (simplified - adjust indices as needed)
        // Left eye center (approximate)
        int leftEyeIdx = 8; // Adjust this index based on your model
        float leftEyeX = landmarks[leftEyeIdx * 3];
        float leftEyeY = landmarks[leftEyeIdx * 3 + 1];
        float leftEyeZ = landmarks[leftEyeIdx * 3 + 2];
        
        // Right eye center (approximate)
        int rightEyeIdx = 9; // Adjust this index based on your model
        float rightEyeX = landmarks[rightEyeIdx * 3];
        float rightEyeY = landmarks[rightEyeIdx * 3 + 1];
        float rightEyeZ = landmarks[rightEyeIdx * 3 + 2];
        
        // Calculate eye center
        float eyeCenterX = (leftEyeX + rightEyeX) * 0.5f;
        float eyeCenterY = (leftEyeY + rightEyeY) * 0.5f;
        float eyeCenterZ = (leftEyeZ + rightEyeZ) * 0.5f;
        
        // Calculate gaze direction relative to forward (assuming Z is forward)
        // Yaw is left-right rotation (X direction)
        float yaw = Mathf.Atan2(eyeCenterX, eyeCenterZ);
        
        Debug.Log($"[RealtimeGazeHUD] Eye center: ({eyeCenterX:F3}, {eyeCenterY:F3}, {eyeCenterZ:F3}) -> yaw: {yaw:F3}");
        
        return yaw;
    }
    
    private float CalculatePitchFromLandmarks(float[] landmarks)
    {
        // For 90 elements, we have 30 landmarks with 3 coordinates each
        if (landmarks.Length != 90) return 0f;
        
        // Extract eye region landmarks (simplified - adjust indices as needed)
        // Left eye center (approximate)
        int leftEyeIdx = 8; // Adjust this index based on your model
        float leftEyeX = landmarks[leftEyeIdx * 3];
        float leftEyeY = landmarks[leftEyeIdx * 3 + 1];
        float leftEyeZ = landmarks[leftEyeIdx * 3 + 2];
        
        // Right eye center (approximate)
        int rightEyeIdx = 9; // Adjust this index based on your model
        float rightEyeX = landmarks[rightEyeIdx * 3];
        float rightEyeY = landmarks[rightEyeIdx * 3 + 1];
        float rightEyeZ = landmarks[rightEyeIdx * 3 + 2];
        
        // Calculate eye center
        float eyeCenterX = (leftEyeX + rightEyeX) * 0.5f;
        float eyeCenterY = (leftEyeY + rightEyeY) * 0.5f;
        float eyeCenterZ = (leftEyeZ + rightEyeZ) * 0.5f;
        
        // Calculate gaze direction relative to forward (assuming Z is forward)
        // Pitch is up-down rotation (Y direction)
        float pitch = Mathf.Atan2(-eyeCenterY, Mathf.Sqrt(eyeCenterX * eyeCenterX + eyeCenterZ * eyeCenterZ));
        
        Debug.Log($"[RealtimeGazeHUD] Eye center: ({eyeCenterX:F3}, {eyeCenterY:F3}, {eyeCenterZ:F3}) -> pitch: {pitch:F3}");
        
        return pitch;
    }

    void OnDestroy()
    {
        worker?.Dispose();
        if (resizeBuffer) Destroy(resizeBuffer);
    }
}
