using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Head-attached debug system for VR Gaze Detection
/// Attaches debug text to the user's head/camera so it's always visible
/// </summary>
public class HeadAttachedDebugger : MonoBehaviour
{
    [Header("Debug Display")]
    [SerializeField] private TextMeshPro debugText;
    [SerializeField] private Vector3 offsetFromHead = new Vector3(0, 0.1f, 0.3f);
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float textScale = 0.05f;
    
    [Header("Components to Debug")]
    [SerializeField] private VRGazeDetection gazeDetection;
    [SerializeField] private VRGazeDetectionUI gazeUI;
    
    [Header("Debug Info")]
    [SerializeField] private string currentStatus = "Not initialized";
    [SerializeField] private string errorMessage = "";
    [SerializeField] private bool hasModel = false;
    [SerializeField] private bool hasCamera = false;
    [SerializeField] private bool isProcessing = false;
    
    private Transform headTransform;
    private float updateTimer = 0f;
    private const float UPDATE_INTERVAL = 0.5f; // Update debug info every 0.5 seconds
    
    void Start()
    {
        FindHeadTransform();
        CreateDebugDisplay();
        StartCoroutine(InitialDebugCheck());
    }
    
    void Update()
    {
        // Update debug text position to follow head
        UpdateDebugPosition();
        
        // Update debug info at intervals
        updateTimer += Time.deltaTime;
        if (updateTimer >= UPDATE_INTERVAL)
        {
            updateTimer = 0f;
            UpdateDebugInfo();
        }
    }
    
    private void FindHeadTransform()
    {
        // Try to find the main camera/head transform
        if (Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }
        else
        {
            // Fallback: look for OVR camera rig
            var ovrCameraRig = FindObjectOfType<OVRCameraRig>();
            if (ovrCameraRig != null)
            {
                headTransform = ovrCameraRig.centerEyeAnchor;
            }
            else
            {
                // Last resort: look for any camera
                var camera = FindObjectOfType<Camera>();
                if (camera != null)
                {
                    headTransform = camera.transform;
                }
            }
        }
        
        if (headTransform == null)
        {
            Debug.LogError("[HeadAttachedDebugger] Could not find head/camera transform!");
        }
        else
        {
            Debug.Log($"[HeadAttachedDebugger] Found head transform: {headTransform.name}");
        }
    }
    
    private void CreateDebugDisplay()
    {
        if (debugText == null)
        {
            // Create debug text object
            GameObject debugObj = new GameObject("HeadDebugText");
            debugObj.transform.SetParent(transform);
            
            debugText = debugObj.AddComponent<TextMeshPro>();
            debugText.text = "Debug: Initializing...";
            debugText.fontSize = textScale;
            debugText.color = Color.yellow;
            debugText.alignment = TextAlignmentOptions.Left;
            debugText.enableWordWrapping = true;
            
            Debug.Log("[HeadAttachedDebugger] Head-attached debug display created");
        }
    }
    
    private void UpdateDebugPosition()
    {
        if (headTransform != null && debugText != null)
        {
            // Position debug text relative to head
            Vector3 headPosition = headTransform.position;
            Vector3 headForward = headTransform.forward;
            Vector3 headUp = headTransform.up;
            Vector3 headRight = headTransform.right;
            
            // Calculate position offset from head
            Vector3 targetPosition = headPosition + 
                                   headForward * offsetFromHead.z + 
                                   headUp * offsetFromHead.y + 
                                   headRight * offsetFromHead.x;
            
            debugText.transform.position = targetPosition;
            
            // Make text face the user
            debugText.transform.LookAt(headPosition);
            debugText.transform.Rotate(0, 180, 0); // Flip to face user
        }
    }
    
    private IEnumerator InitialDebugCheck()
    {
        yield return new WaitForSeconds(1f); // Wait for components to initialize
        
        // Check for components
        if (gazeDetection == null)
            gazeDetection = FindObjectOfType<VRGazeDetection>();
        if (gazeUI == null)
            gazeUI = FindObjectOfType<VRGazeDetectionUI>();
        
        // Check model assignment
        CheckModelAssignment();
        
        // Check camera setup
        CheckCameraSetup();
        
        UpdateDebugDisplay();
        
        Debug.Log("[HeadAttachedDebugger] Initial debug check complete");
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
    
    // Adjust offset from head
    public void SetOffset(Vector3 offset)
    {
        offsetFromHead = offset;
    }
    
    // Adjust text scale
    public void SetTextScale(float scale)
    {
        textScale = scale;
        if (debugText != null)
        {
            debugText.fontSize = scale;
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
        Debug.Log($"[HeadAttachedDebugger] Model check: {hasModel}, Status: {currentStatus}");
    }
    
    [ContextMenu("Test Camera Setup")]
    public void TestCameraSetup()
    {
        CheckCameraSetup();
        UpdateDebugDisplay();
        Debug.Log($"[HeadAttachedDebugger] Camera check: {hasCamera}, Error: {errorMessage}");
    }
    
    [ContextMenu("Move Closer to Head")]
    public void MoveCloserToHead()
    {
        offsetFromHead = new Vector3(0, 0.05f, 0.2f);
        Debug.Log("[HeadAttachedDebugger] Moved debug text closer to head");
    }
    
    [ContextMenu("Move Further from Head")]
    public void MoveFurtherFromHead()
    {
        offsetFromHead = new Vector3(0, 0.15f, 0.4f);
        Debug.Log("[HeadAttachedDebugger] Moved debug text further from head");
    }
}
