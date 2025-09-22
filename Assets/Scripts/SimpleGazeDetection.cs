using UnityEngine;

using System.Collections;

/// <summary>
/// Simple gaze detection using MobileNetV2 ONNX model from yakhyo/gaze-estimation repository
/// This script detects gaze direction (yaw and pitch) from webcam input
/// </summary>
public class SimpleGazeDetection : MonoBehaviour
{
    [Header("ONNX Model")]
    [SerializeField] private Unity.InferenceEngine.ModelAsset gazeModel; // MobileNetV2 ONNX model
    
    [Header("Webcam Settings")]
    [SerializeField] private int webcamIndex = 0;
    [SerializeField] private int targetWidth = 224;
    [SerializeField] private int targetHeight = 224;
    [SerializeField] private bool useExistingWebcam = true; // Try to use existing WebCamTexture first
    
    [Header("Gaze Results")]
    [SerializeField] private float yaw;   // Left/Right gaze (-180 to +180 degrees)
    [SerializeField] private float pitch; // Up/Down gaze (-90 to +90 degrees)
    [SerializeField] private bool isModelLoaded = false;
    
    [Header("UI Display")]
    [SerializeField] private TMPro.TextMeshProUGUI gazeText;
    [SerializeField] private TMPro.TextMeshProUGUI statusText;
    
    // Private variables
    private WebCamTexture webCamTexture;
    private Unity.InferenceEngine.Worker worker;
    private Unity.InferenceEngine.Model runtimeModel;
    private bool isProcessing = false;
    private bool isWebcamInitialized = false;
    
    // Constants for ImageNet normalization
    private static readonly Vector3 mean = new Vector3(0.485f, 0.456f, 0.406f);
    private static readonly Vector3 std = new Vector3(0.229f, 0.224f, 0.225f);
    
    void Start()
    {
        StartCoroutine(Initialize());
    }
    
    void Update()
    {
        // Check for A button press to toggle processing
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            ToggleProcessing();
        }
        
        // Process webcam frame if enabled
        if (isProcessing && isModelLoaded && isWebcamInitialized && webCamTexture != null && webCamTexture.isPlaying)
        {
            ProcessFrame();
        }
    }
    
    private IEnumerator Initialize()
    {
        if (statusText != null)
            statusText.text = "Initializing...";
        
        Debug.Log("SimpleGazeDetection: Starting initialization...");
        
        // Initialize webcam first
        yield return StartCoroutine(InitializeWebcam());
        
        // Only proceed if webcam is working
        if (!isWebcamInitialized)
        {
            if (statusText != null)
                statusText.text = "Failed to initialize webcam. Check console for details.";
            yield break;
        }
        
        // Load ONNX model
        yield return StartCoroutine(LoadModel());
        
        if (statusText != null)
            statusText.text = "Ready! Press A to start/stop gaze detection";
        
        isModelLoaded = true;
        Debug.Log("SimpleGazeDetection: Initialization complete!");
    }
    
    private IEnumerator InitializeWebcam()
    {
        Debug.Log("SimpleGazeDetection: Initializing webcam...");
        
        // First, try to use existing WebCamTexture if available
        if (useExistingWebcam)
        {
            // Look for any existing WebCamTexture in the scene
            var existingWebcams = FindObjectsOfType<WebCamTexture>();
            if (existingWebcams.Length > 0)
            {
                webCamTexture = existingWebcams[0];
                Debug.Log($"SimpleGazeDetection: Using existing WebCamTexture: {webCamTexture.width}x{webCamTexture.height}");
                
                if (webCamTexture.isPlaying)
                {
                    isWebcamInitialized = true;
                    yield break;
                }
            }
        }
        
        // Check if webcam devices are available
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("SimpleGazeDetection: No webcam devices found!");
            if (statusText != null)
                statusText.text = "Error: No webcam found";
            yield break;
        }
        
        Debug.Log($"SimpleGazeDetection: Found {WebCamTexture.devices.Length} webcam devices:");
        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            Debug.Log($"  Device {i}: {WebCamTexture.devices[i].name}");
        }
        
        // Try to create webcam texture
        string deviceName = WebCamTexture.devices[webcamIndex].name;
        Debug.Log($"SimpleGazeDetection: Attempting to use device: {deviceName}");
        
        try
        {
            webCamTexture = new WebCamTexture(deviceName, targetWidth, targetHeight, 30);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleGazeDetection: Error creating webcam texture: {e.Message}");
            if (statusText != null)
                statusText.text = $"Webcam error: {e.Message}";
            yield break;
        }
        
        // Start the webcam
        webCamTexture.Play();
        
        // Wait for webcam to start
        float startTime = Time.time;
        while (!webCamTexture.isPlaying && Time.time - startTime < 5f)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        if (!webCamTexture.isPlaying)
        {
            Debug.LogError("SimpleGazeDetection: Failed to start webcam after 5 seconds!");
            if (statusText != null)
                statusText.text = "Error: Webcam failed to start";
            yield break;
        }
        
        // Wait a bit more for the webcam to stabilize
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"SimpleGazeDetection: Webcam initialized successfully: {webCamTexture.width}x{webCamTexture.height}");
        isWebcamInitialized = true;
        
        if (statusText != null)
            statusText.text = "Webcam ready!";
    }
    
    private IEnumerator LoadModel()
    {
        if (gazeModel == null)
        {
            Debug.LogError("SimpleGazeDetection: Gaze model asset is not assigned!");
            if (statusText != null)
                statusText.text = "Error: No model assigned";
            yield break;
        }
        
        Debug.Log("SimpleGazeDetection: Loading ONNX model...");
        
        try
        {
            // Load and compile the model
            Unity.InferenceEngine.Model model = Unity.InferenceEngine.ModelLoader.Load(gazeModel);
            Unity.InferenceEngine.FunctionalGraph graph = new Unity.InferenceEngine.FunctionalGraph();
            Unity.InferenceEngine.FunctionalTensor[] inputs = graph.AddInputs(model);
            Unity.InferenceEngine.FunctionalTensor[] outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
            runtimeModel = graph.Compile(outputs);
            
            // Create worker
            worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
            
            Debug.Log("SimpleGazeDetection: Gaze model loaded successfully!");
            if (statusText != null)
                statusText.text = "Model loaded! Press A to start gaze detection";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleGazeDetection: Failed to load model: {e.Message}");
            if (statusText != null)
                statusText.text = $"Error: Model load failed - {e.Message}";
            yield break;
        }
    }
    
    private void ToggleProcessing()
    {
        if (!isWebcamInitialized || !isModelLoaded)
        {
            Debug.LogWarning("SimpleGazeDetection: Cannot toggle processing - not fully initialized");
            return;
        }
        
        isProcessing = !isProcessing;
        
        if (isProcessing)
        {
            Debug.Log("SimpleGazeDetection: Gaze detection started");
            if (statusText != null)
                statusText.text = "Gaze detection: ON - Look at the camera!";
        }
        else
        {
            Debug.Log("SimpleGazeDetection: Gaze detection stopped");
            if (statusText != null)
                statusText.text = "Gaze detection: OFF - Press A to start";
        }
    }
    
    private void ProcessFrame()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying || worker == null || !isWebcamInitialized)
        {
            Debug.LogWarning("SimpleGazeDetection: Cannot process frame - webcam or worker not ready");
            return;
        }
        
        try
        {
            // Create a temporary texture for processing
            Texture2D tempTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            
            // Copy webcam frame to temp texture
            Graphics.CopyTexture(webCamTexture, tempTexture);
            
            // Process the texture
            ProcessTexture(tempTexture);
            
            // Clean up
            DestroyImmediate(tempTexture);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SimpleGazeDetection: Error processing frame: {e.Message}");
        }
    }
    
    private void ProcessTexture(Texture2D texture)
    {
        // Get pixels from texture
        Color32[] pixels = texture.GetPixels32();
        
        // Prepare input tensor data (NCHW format: [1, 3, H, W])
        int N = 1, C = 3, H = targetHeight, W = targetWidth;
        float[] inputData = new float[N * C * H * W];
        
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
            
            // Update gaze values (assuming output is [yaw, pitch])
            if (outputData.Length >= 2)
            {
                yaw = outputData[0];
                pitch = outputData[1];
                
                // Update UI
                if (gazeText != null)
                {
                    gazeText.text = $"Gaze: Yaw: {yaw:F1}°, Pitch: {pitch:F1}°";
                }
                
                Debug.Log($"SimpleGazeDetection: Gaze detected - Yaw: {yaw:F2}, Pitch: {pitch:F2}");
            }
        }
    }
    
    private void OnDestroy()
    {
        // Clean up resources
        if (webCamTexture != null && !useExistingWebcam)
        {
            webCamTexture.Stop();
            DestroyImmediate(webCamTexture);
        }
        
        if (worker != null)
        {
            worker.Dispose();
        }
    }
    
    // Public methods for external access
    public Vector2 GetGazeDirection()
    {
        return new Vector2(yaw, pitch);
    }
    
    public bool IsLookingAtCamera()
    {
        // Simple heuristic: if gaze is roughly centered
        return Mathf.Abs(yaw) < 30f && Mathf.Abs(pitch) < 20f;
    }
    
    public void SetWebcamIndex(int index)
    {
        webcamIndex = index;
        if (webCamTexture != null && !useExistingWebcam)
        {
            webCamTexture.Stop();
            DestroyImmediate(webCamTexture);
        }
        isWebcamInitialized = false;
        StartCoroutine(InitializeWebcam());
    }
    
    // Debug method to check webcam status
    public void DebugWebcamStatus()
    {
        Debug.Log($"SimpleGazeDetection Debug Info:");
        Debug.Log($"  Webcam devices found: {WebCamTexture.devices.Length}");
        Debug.Log($"  Current webcam: {(webCamTexture != null ? webCamTexture.name : "null")}");
        Debug.Log($"  Webcam playing: {(webCamTexture != null ? webCamTexture.isPlaying.ToString() : "N/A")}");
        Debug.Log($"  Webcam dimensions: {(webCamTexture != null ? $"{webCamTexture.width}x{webCamTexture.height}" : "N/A")}");
        Debug.Log($"  Is webcam initialized: {isWebcamInitialized}");
        Debug.Log($"  Is model loaded: {isModelLoaded}");
        Debug.Log($"  Is processing: {isProcessing}");
    }
}
