using UnityEngine;

using TMPro;
using PassthroughCameraSamples;
using System.Collections;

/// <summary>
/// VR-ready gaze detection using ONNX models with Quest passthrough camera
/// This script provides real-time gaze detection in VR using the Quest's camera feed
/// </summary>
public class VRGazeDetection : MonoBehaviour
{
    [Header("Camera Sources")]
    [SerializeField] private WebCamTextureManager webCamTextureManager;  // Quest passthrough camera
    [SerializeField] private WebcamTestManager webcamTestManager;        // PC testing fallback
    
    [Header("ONNX Model")]
    [SerializeField] private Unity.InferenceEngine.ModelAsset gazeModel;                      // Your ONNX gaze model
    [SerializeField] private Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.GPUCompute;
    [SerializeField] private int inputSize = 448;                       // Model input size
    
    [Header("Gaze Detection Settings")]
    [SerializeField] private float updateInterval = 0.1f;               // Seconds between inferences
    [SerializeField] private float yawDeadband = 0.3f;                 // Radians for "looking at you" detection
    [SerializeField] private float pitchDeadband = 0.6f;                // Radians for "looking at you" detection
    [Range(0f, 1f)] [SerializeField] private float smoothing = 0.7f;   // Exponential moving average smoothing
    
    [Header("VR UI Display")]
    [SerializeField] private TextMeshProUGUI statusText;                // "Looking at you" / "Not looking"
    [SerializeField] private TextMeshProUGUI debugText;                 // Yaw/pitch values
    [SerializeField] private GameObject gazeIndicator;                  // Visual indicator in VR space
    
    [Header("Debug & Testing")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool useTestImage = false;                 // Use test image instead of camera
    [SerializeField] private Texture2D testImage;                       // Test image for debugging
    [SerializeField] private HeadAttachedDebugger debugger;              // Head-attached debugger for Quest
    
    // Private variables
    private Unity.InferenceEngine.Worker worker;
    private Unity.InferenceEngine.Model runtimeModel;
    private bool isModelLoaded = false;
    private bool isProcessing = false;
    private float timer;
    private Vector2 ema;                                               // Smoothed yaw/pitch
    private bool emaInitialized = false;
    private Texture2D resizeBuffer;
    
    // Gaze results
    private float currentYaw;
    private float currentPitch;
    private bool isLookingAtUser;
    
    void Start()
    {
        if (gazeModel == null)
        {
            Debug.LogError("[VRGazeDetection] Missing gaze model! Please assign an ONNX model asset.");
            enabled = false;
            return;
        }
        
        StartCoroutine(Initialize());
    }
    
    void Update()
    {
        // Handle VR input for testing
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            ToggleProcessing();
        }
        
        // Process camera frames at specified interval
        timer += Time.deltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            ProcessGazeDetection();
        }
        
        // Update UI
        UpdateUI();
        
        // Update visual indicator
        UpdateGazeIndicator();
    }
    
    private IEnumerator Initialize()
    {
        if (statusText != null)
            statusText.text = "Initializing VR Gaze Detection...";
        
        if (debugger != null)
            debugger.SetStatus("Starting initialization...");
        
        Debug.Log("[VRGazeDetection] Starting initialization...");
        
        // Load and compile the ONNX model
        yield return StartCoroutine(LoadModel());
        
        if (statusText != null)
            statusText.text = "Ready! Press A to start/stop gaze detection";
        
        if (debugger != null)
            debugger.SetStatus("Ready! Press A to start/stop");
        
        Debug.Log("[VRGazeDetection] Initialization complete!");
    }
    
    private IEnumerator LoadModel()
    {
        Debug.Log("[VRGazeDetection] Loading ONNX model...");
        
        try
        {
            if (debugger != null)
                debugger.SetStatus("Loading ONNX model...");
            
            // Load and compile the model
            Unity.InferenceEngine.Model model = Unity.InferenceEngine.ModelLoader.Load(gazeModel);
            Unity.InferenceEngine.FunctionalGraph graph = new Unity.InferenceEngine.FunctionalGraph();
            Unity.InferenceEngine.FunctionalTensor[] inputs = graph.AddInputs(model);
            Unity.InferenceEngine.FunctionalTensor[] outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
            runtimeModel = graph.Compile(outputs);
            
            // Create worker
            worker = new Unity.InferenceEngine.Worker(runtimeModel, backend);
            
            isModelLoaded = true;
            Debug.Log("[VRGazeDetection] Gaze model loaded successfully!");
            
            if (statusText != null)
                statusText.text = "Model loaded! Press A to start gaze detection";
            
            if (debugger != null)
                debugger.SetStatus("Model loaded successfully!");
        }
        catch (System.Exception e)
        {
            string errorMsg = $"Failed to load model: {e.Message}";
            Debug.LogError($"[VRGazeDetection] {errorMsg}");
            
            if (statusText != null)
                statusText.text = $"Error: Model load failed - {e.Message}";
            
            if (debugger != null)
                debugger.SetError(errorMsg);
            
            yield break;
        }
    }
    
    private void ToggleProcessing()
    {
        if (!isModelLoaded)
        {
            Debug.LogWarning("[VRGazeDetection] Cannot toggle processing - model not loaded");
            return;
        }
        
        isProcessing = !isProcessing;
        
        if (isProcessing)
        {
            Debug.Log("[VRGazeDetection] Gaze detection started");
            if (statusText != null)
                statusText.text = "Gaze detection: ON - Look at the camera!";
        }
        else
        {
            Debug.Log("[VRGazeDetection] Gaze detection stopped");
            if (statusText != null)
                statusText.text = "Gaze detection: OFF - Press A to start";
        }
    }
    
    private void ProcessGazeDetection()
    {
        if (!isProcessing || !isModelLoaded || worker == null)
            return;
        
        // Get camera texture
        WebCamTexture cameraTexture = GetCameraTexture();
        if (cameraTexture == null || !cameraTexture.isPlaying)
            return;
        
        // Use test image if enabled
        if (useTestImage && testImage != null)
        {
            ProcessTexture(testImage);
            return;
        }
        
        // Process camera texture
        if (cameraTexture.didUpdateThisFrame || !Application.isMobilePlatform)
        {
            ProcessCameraTexture(cameraTexture);
        }
    }
    
    private WebCamTexture GetCameraTexture()
    {
        // Try Quest passthrough camera first
        if (webCamTextureManager != null && webCamTextureManager.WebCamTexture != null && webCamTextureManager.WebCamTexture.isPlaying)
        {
            if (debugger != null)
                debugger.SetStatus("Using Quest passthrough camera");
            return webCamTextureManager.WebCamTexture;
        }
        
        // Fallback to PC webcam for testing
        if (webcamTestManager != null && webcamTestManager.WebCamTexture != null && webcamTestManager.WebCamTexture.isPlaying)
        {
            if (debugger != null)
                debugger.SetStatus("Using PC webcam");
            return webcamTestManager.WebCamTexture;
        }
        
        if (debugger != null)
            debugger.SetError("No camera available");
        
        return null;
    }
    
    private void ProcessCameraTexture(WebCamTexture cameraTexture)
    {
        try
        {
            // Create or resize buffer texture
            if (resizeBuffer == null || resizeBuffer.width != inputSize || resizeBuffer.height != inputSize)
            {
                resizeBuffer = new Texture2D(inputSize, inputSize, TextureFormat.RGBA32, false);
            }
            
            // Resize camera texture to model input size
            Graphics.ConvertTexture(cameraTexture, resizeBuffer);
            
            // Process the resized texture
            ProcessTexture(resizeBuffer);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRGazeDetection] Error processing camera texture: {e.Message}");
        }
    }
    
    private void ProcessTexture(Texture2D texture)
    {
        if (worker == null) return;
        
        try
        {
            // Get pixels from texture
            Color32[] pixels = texture.GetPixels32();
            
            // Prepare input tensor data (NCHW format: [1, 3, H, W])
            int N = 1, C = 3, H = inputSize, W = inputSize;
            float[] inputData = new float[N * C * H * W];
            
            // ImageNet normalization constants
            Vector3 mean = new Vector3(0.485f, 0.456f, 0.406f);
            Vector3 std = new Vector3(0.229f, 0.224f, 0.225f);
            
            // Convert pixels to normalized float values
            for (int h = 0; h < H; h++)
            {
                for (int w = 0; w < W; w++)
                {
                    int pixelIndex = h * W + w;
                    Color32 color = pixels[pixelIndex];
                    
                    // Convert to 0-1 range
                    float r = color.r / 255f;
                    float g = color.g / 255f;
                    float b = color.b / 255f;
                    
                    // Apply ImageNet normalization
                    r = (r - mean.x) / std.x;
                    g = (g - mean.y) / std.y;
                    b = (b - mean.z) / std.z;
                    
                    // Store in NCHW format
                    int baseIndex = h * W + w;
                    inputData[0 * H * W + baseIndex] = r; // Red channel
                    inputData[1 * H * W + baseIndex] = g; // Green channel
                    inputData[2 * H * W + baseIndex] = b; // Blue channel
                }
            }
            
            // Create input tensor
            using Unity.InferenceEngine.Tensor<float> inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(N, C, H, W), inputData);
            
            // Run inference
            worker.Schedule(inputTensor);
            
            // Get output
            Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
            if (outputTensor != null)
            {
                float[] outputData = outputTensor.DownloadToArray();
                ProcessModelOutput(outputData);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRGazeDetection] Error processing texture: {e.Message}");
        }
    }
    
    private void ProcessModelOutput(float[] outputData)
    {
        if (outputData == null || outputData.Length == 0)
            return;
        
        float yaw = 0f, pitch = 0f;
        
        if (outputData.Length == 2)
        {
            // Direct [yaw, pitch] output
            yaw = outputData[0];
            pitch = outputData[1];
        }
        else if (outputData.Length == 3)
        {
            // 3D vector output - convert to yaw/pitch
            float x = outputData[0], y = outputData[1], z = outputData[2];
            yaw = Mathf.Atan2(x, z);
            pitch = Mathf.Atan2(-y, Mathf.Sqrt(x * x + z * z));
        }
        else
        {
            Debug.LogWarning($"[VRGazeDetection] Unexpected output size: {outputData.Length}");
            return;
        }
        
        // Apply smoothing using exponential moving average
        if (!emaInitialized)
        {
            ema = new Vector2(yaw, pitch);
            emaInitialized = true;
        }
        else
        {
            float alpha = 1f - Mathf.Clamp01(smoothing);
            ema.x = Mathf.Lerp(ema.x, yaw, alpha);
            ema.y = Mathf.Lerp(ema.y, pitch, alpha);
        }
        
        // Update current values
        currentYaw = ema.x;
        currentPitch = ema.y;
        
        // Determine if user is looking at the camera
        isLookingAtUser = Mathf.Abs(currentYaw) <= yawDeadband && Mathf.Abs(currentPitch) <= pitchDeadband;
        
        if (enableDebugLogging)
        {
            Debug.Log($"[VRGazeDetection] Gaze - Yaw: {currentYaw:F3} rad ({currentYaw * Mathf.Rad2Deg:F1}°), Pitch: {currentPitch:F3} rad ({currentPitch * Mathf.Rad2Deg:F1}°), Looking: {isLookingAtUser}");
        }
    }
    
    private void UpdateUI()
    {
        if (statusText != null)
        {
            if (!isModelLoaded)
                statusText.text = "Initializing...";
            else if (!isProcessing)
                statusText.text = "Press A to start gaze detection";
            else
                statusText.text = isLookingAtUser ? "Looking at you! 👀" : "Not looking at you";
        }
        
        if (debugText != null)
        {
            if (emaInitialized)
            {
                debugText.text = $"Yaw: {currentYaw * Mathf.Rad2Deg:F1}°\nPitch: {currentPitch * Mathf.Rad2Deg:F1}°";
            }
            else
            {
                debugText.text = "Processing...";
            }
        }
    }
    
    private void UpdateGazeIndicator()
    {
        if (gazeIndicator != null && emaInitialized)
        {
            // Update indicator position based on gaze direction
            Vector3 indicatorPosition = gazeIndicator.transform.localPosition;
            
            // Map yaw to X position, pitch to Y position
            float xOffset = Mathf.Clamp(currentYaw * 2f, -1f, 1f); // Scale yaw to reasonable range
            float yOffset = Mathf.Clamp(currentPitch * 1.5f, -1f, 1f); // Scale pitch to reasonable range
            
            indicatorPosition.x = xOffset;
            indicatorPosition.y = yOffset;
            
            gazeIndicator.transform.localPosition = indicatorPosition;
            
            // Change color based on whether user is looking
            Renderer renderer = gazeIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isLookingAtUser ? Color.green : Color.red;
            }
        }
    }
    
    // Public methods for external access
    public Vector2 GetGazeDirection()
    {
        return new Vector2(currentYaw, currentPitch);
    }
    
    public bool IsLookingAtUser()
    {
        return isLookingAtUser;
    }
    
    public float GetYawDegrees()
    {
        return currentYaw * Mathf.Rad2Deg;
    }
    
    public float GetPitchDegrees()
    {
        return currentPitch * Mathf.Rad2Deg;
    }
    
    public void SetUpdateInterval(float interval)
    {
        updateInterval = Mathf.Max(0.05f, interval);
    }
    
    public void SetSmoothing(float smooth)
    {
        smoothing = Mathf.Clamp01(smooth);
    }
    
    private void OnDestroy()
    {
        // Clean up resources
        if (worker != null)
        {
            worker.Dispose();
        }
        
        if (resizeBuffer != null)
        {
            DestroyImmediate(resizeBuffer);
        }
    }
}
