using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Visual debug system for VR Gaze Detection on Quest
/// Shows debug information directly in VR space since console logs aren't visible
/// </summary>
public class VRGazeDetectionDebugger : MonoBehaviour
{
    [Header("Debug Display")]
    [SerializeField] private TextMeshPro debugText;
    [SerializeField] private Vector3 debugTextOffset = new Vector3(0, -0.2f, 0.5f);
    [SerializeField] private bool showDebugInfo = true;
    
    [Header("Components to Debug")]
    [SerializeField] private VRGazeDetection gazeDetection;
    [SerializeField] private VRGazeDetectionUI gazeUI;
    
    [Header("Debug Info")]
    [SerializeField] private string currentStatus = "Not initialized";
    [SerializeField] private string errorMessage = "";
    [SerializeField] private bool hasModel = false;
    [SerializeField] private bool hasCamera = false;
    [SerializeField] private bool isProcessing = false;
    
    private float updateTimer = 0f;
    private const float UPDATE_INTERVAL = 1f; // Update debug info every second
    
    void Start()
    {
        CreateDebugDisplay();
        StartCoroutine(InitialDebugCheck());
    }
    
    void Update()
    {
        updateTimer += Time.deltaTime;
        if (updateTimer >= UPDATE_INTERVAL)
        {
            updateTimer = 0f;
            UpdateDebugInfo();
        }
    }
    
    private void CreateDebugDisplay()
    {
        if (debugText == null)
        {
            // Create debug text object
            GameObject debugObj = new GameObject("DebugText");
            debugObj.transform.SetParent(transform);
            debugObj.transform.localPosition = debugTextOffset;
            
            debugText = debugObj.AddComponent<TextMeshPro>();
            debugText.text = "Debug: Initializing...";
            debugText.fontSize = 0.06f;
            debugText.color = Color.yellow;
            debugText.alignment = TextAlignmentOptions.Left;
            
            Debug.Log("[VRGazeDetectionDebugger] Debug display created");
        }
    }
    
    private IEnumerator InitialDebugCheck()
    {
        yield return new WaitForSeconds(2f); // Wait for components to initialize
        
        // Check for components
        if (gazeDetection == null)
            gazeDetection = GetComponent<VRGazeDetection>();
        if (gazeUI == null)
            gazeUI = GetComponent<VRGazeDetectionUI>();
        
        // Check model assignment
        CheckModelAssignment();
        
        // Check camera setup
        CheckCameraSetup();
        
        UpdateDebugDisplay();
        
        Debug.Log("[VRGazeDetectionDebugger] Initial debug check complete");
    }
    
    private void CheckModelAssignment()
    {
        if (gazeDetection != null)
        {
            // Use reflection to check if model is assigned
            var modelField = typeof(VRGazeDetection).GetField("gazeModel", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (modelField != null)
            {
                var model = modelField.GetValue(gazeDetection);
                hasModel = (model != null);
                currentStatus = hasModel ? "Model assigned" : "No model assigned";
            }
        }
        else
        {
            hasModel = false;
            currentStatus = "VRGazeDetection component missing";
        }
    }
    
    private void CheckCameraSetup()
    {
        // Check for WebCamTextureManager
        var cameraManager = FindObjectOfType<PassthroughCameraSamples.WebCamTextureManager>();
        hasCamera = (cameraManager != null && cameraManager.WebCamTexture != null);
        
        if (!hasCamera)
        {
            errorMessage = "No camera manager found";
        }
        else if (cameraManager.WebCamTexture != null)
        {
            errorMessage = $"Camera: {cameraManager.WebCamTexture.width}x{cameraManager.WebCamTexture.height}";
        }
    }
    
    private void UpdateDebugInfo()
    {
        if (gazeDetection != null)
        {
            // Check processing status
            var processingField = typeof(VRGazeDetection).GetField("isProcessing", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (processingField != null)
            {
                isProcessing = (bool)processingField.GetValue(gazeDetection);
            }
            
            // Check model loaded status
            var modelLoadedField = typeof(VRGazeDetection).GetField("isModelLoaded", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (modelLoadedField != null)
            {
                bool modelLoaded = (bool)modelLoadedField.GetValue(gazeDetection);
                if (modelLoaded && currentStatus == "Model assigned")
                {
                    currentStatus = "Model loaded successfully";
                }
            }
        }
        
        UpdateDebugDisplay();
    }
    
    private void UpdateDebugDisplay()
    {
        if (debugText == null || !showDebugInfo) return;
        
        string debugInfo = $"=== VR Gaze Detection Debug ===\n";
        debugInfo += $"Status: {currentStatus}\n";
        debugInfo += $"Model: {(hasModel ? "✓" : "✗")}\n";
        debugInfo += $"Camera: {(hasCamera ? "✓" : "✗")}\n";
        debugInfo += $"Processing: {(isProcessing ? "ON" : "OFF")}\n";
        
        if (!string.IsNullOrEmpty(errorMessage))
        {
            debugInfo += $"Error: {errorMessage}\n";
        }
        
        debugInfo += $"\nPress A to start/stop\n";
        debugInfo += $"Time: {Time.time:F1}s";
        
        debugText.text = debugInfo;
        
        // Change color based on status
        if (!hasModel || !hasCamera)
        {
            debugText.color = Color.red;
        }
        else if (isProcessing)
        {
            debugText.color = Color.green;
        }
        else
        {
            debugText.color = Color.yellow;
        }
    }
    
    // Public methods for external debugging
    public void SetStatus(string status)
    {
        currentStatus = status;
        UpdateDebugDisplay();
    }
    
    public void SetError(string error)
    {
        errorMessage = error;
        UpdateDebugDisplay();
    }
    
    public void ShowDebug(bool show)
    {
        showDebugInfo = show;
        if (debugText != null)
        {
            debugText.gameObject.SetActive(show);
        }
    }
    
    // Context menu for testing
    [ContextMenu("Force Debug Update")]
    public void ForceDebugUpdate()
    {
        CheckModelAssignment();
        CheckCameraSetup();
        UpdateDebugInfo();
        UpdateDebugDisplay();
    }
    
    [ContextMenu("Test Model Assignment")]
    public void TestModelAssignment()
    {
        CheckModelAssignment();
        UpdateDebugDisplay();
        Debug.Log($"[VRGazeDetectionDebugger] Model check: {hasModel}, Status: {currentStatus}");
    }
    
    [ContextMenu("Test Camera Setup")]
    public void TestCameraSetup()
    {
        CheckCameraSetup();
        UpdateDebugDisplay();
        Debug.Log($"[VRGazeDetectionDebugger] Camera check: {hasCamera}, Error: {errorMessage}");
    }
}

