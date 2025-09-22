using UnityEngine;
using UnityEditor;

using PassthroughCameraSamples;

/// <summary>
/// Test setup script for VR Gaze Detection
/// Automatically configures the system for testing in the editor and on Quest
/// </summary>
public class VRGazeDetectionTestSetup : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private bool autoSetupOnStart = true;
    [SerializeField] private bool createTestUI = true;
    [SerializeField] private bool enableDebugMode = true;
    
    [Header("Test Assets")]
    [SerializeField] private Unity.InferenceEngine.ModelAsset testGazeModel;
    [SerializeField] private GameObject webCamTextureManagerPrefab;
    
    void Start()
    {
        if (autoSetupOnStart)
        {
            SetupTestEnvironment();
        }
    }
    
    [ContextMenu("Setup Test Environment")]
    public void SetupTestEnvironment()
    {
        Debug.Log("[VRGazeDetectionTestSetup] Setting up test environment...");
        
        // Find or create the main gaze detection object
        GameObject gazeDetectionObject = FindOrCreateGazeDetectionObject();
        
        // Setup VRGazeDetection component
        SetupVRGazeDetection(gazeDetectionObject);
        
        // Setup VR UI if requested
        if (createTestUI)
        {
            SetupVRUI(gazeDetectionObject);
        }
        
        // Setup camera manager
        SetupCameraManager();
        
        Debug.Log("[VRGazeDetectionTestSetup] Test environment setup complete!");
    }
    
    private GameObject FindOrCreateGazeDetectionObject()
    {
        // Look for existing GazeDetection object
        GameObject existingObject = GameObject.Find("GazeDetection");
        if (existingObject != null)
        {
            Debug.Log("[VRGazeDetectionTestSetup] Found existing GazeDetection object");
            return existingObject;
        }
        
        // Create new object if none exists
        GameObject newObject = new GameObject("GazeDetection");
        newObject.transform.position = new Vector3(0, 1.6f, 2f); // Position in front of user
        
        Debug.Log("[VRGazeDetectionTestSetup] Created new GazeDetection object");
        return newObject;
    }
    
    private void SetupVRGazeDetection(GameObject targetObject)
    {
        // Remove old components
        var oldGazeDetection = targetObject.GetComponent<NewGazeDetection>();
        if (oldGazeDetection != null)
        {
            Debug.Log("[VRGazeDetectionTestSetup] Removing old NewGazeDetection component");
            DestroyImmediate(oldGazeDetection);
        }
        
        // Add VRGazeDetection component
        var vrGazeDetection = targetObject.GetComponent<VRGazeDetection>();
        if (vrGazeDetection == null)
        {
            vrGazeDetection = targetObject.AddComponent<VRGazeDetection>();
            Debug.Log("[VRGazeDetectionTestSetup] Added VRGazeDetection component");
        }
        
        // Configure the component
        if (testGazeModel != null)
        {
            // Use reflection to set the private field
            var gazeModelField = typeof(VRGazeDetection).GetField("gazeModel", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (gazeModelField != null)
            {
                gazeModelField.SetValue(vrGazeDetection, testGazeModel);
                Debug.Log("[VRGazeDetectionTestSetup] Assigned test gaze model");
            }
        }
        
        // Set debug mode
        var debugField = typeof(VRGazeDetection).GetField("enableDebugLogging", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (debugField != null)
        {
            debugField.SetValue(vrGazeDetection, enableDebugMode);
        }
        
        // Set test image mode for editor testing
        var testImageField = typeof(VRGazeDetection).GetField("useTestImage", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (testImageField != null)
        {
            testImageField.SetValue(vrGazeDetection, !Application.isMobilePlatform);
        }
    }
    
    private void SetupVRUI(GameObject targetObject)
    {
        // Add VR UI manager
        var vrUI = targetObject.GetComponent<VRGazeDetectionUI>();
        if (vrUI == null)
        {
            vrUI = targetObject.AddComponent<VRGazeDetectionUI>();
            Debug.Log("[VRGazeDetectionTestSetup] Added VRGazeDetectionUI component");
        }
        
        // Configure UI positioning for testing
        var statusOffsetField = typeof(VRGazeDetectionUI).GetField("statusTextOffset", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var debugOffsetField = typeof(VRGazeDetectionUI).GetField("debugTextOffset", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var indicatorOffsetField = typeof(VRGazeDetectionUI).GetField("gazeIndicatorOffset", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (statusOffsetField != null) statusOffsetField.SetValue(vrUI, new Vector3(0, 0.3f, 0.5f));
        if (debugOffsetField != null) debugOffsetField.SetValue(vrUI, new Vector3(0, 0.1f, 0.5f));
        if (indicatorOffsetField != null) indicatorOffsetField.SetValue(vrUI, new Vector3(0, 0, 0.3f));
    }
    
    private void SetupCameraManager()
    {
        // Look for existing WebCamTextureManager
        var existingManager = FindObjectOfType<WebCamTextureManager>();
        if (existingManager != null)
        {
            Debug.Log("[VRGazeDetectionTestSetup] Found existing WebCamTextureManager");
            return;
        }
        
        // Create camera manager if none exists
        if (webCamTextureManagerPrefab != null)
        {
            GameObject cameraManager = Instantiate(webCamTextureManagerPrefab);
            cameraManager.name = "WebCamTextureManager";
            Debug.Log("[VRGazeDetectionTestSetup] Created WebCamTextureManager from prefab");
        }
        else
        {
            Debug.LogWarning("[VRGazeDetectionTestSetup] No WebCamTextureManagerPrefab assigned - camera may not work");
        }
    }
    
    [ContextMenu("Test Gaze Detection")]
    public void TestGazeDetection()
    {
        var gazeDetection = FindObjectOfType<VRGazeDetection>();
        if (gazeDetection != null)
        {
            Debug.Log("[VRGazeDetectionTestSetup] Testing gaze detection...");
            
            // Test public methods
            Vector2 gazeDir = gazeDetection.GetGazeDirection();
            bool isLooking = gazeDetection.IsLookingAtUser();
            
            Debug.Log($"[VRGazeDetectionTestSetup] Current gaze: {gazeDir}, Looking: {isLooking}");
        }
        else
        {
            Debug.LogError("[VRGazeDetectionTestSetup] No VRGazeDetection component found!");
        }
    }
    
    [ContextMenu("Reset to Default")]
    public void ResetToDefault()
    {
        Debug.Log("[VRGazeDetectionTestSetup] Resetting to default configuration...");
        
        // Reset VRGazeDetection settings
        var gazeDetection = FindObjectOfType<VRGazeDetection>();
        if (gazeDetection != null)
        {
            gazeDetection.SetUpdateInterval(0.1f);
            gazeDetection.SetSmoothing(0.7f);
        }
        
        // Reset UI positioning
        var vrUI = FindObjectOfType<VRGazeDetectionUI>();
        if (vrUI != null)
        {
            vrUI.SetUITransparency(1.0f);
            vrUI.ShowUI(true);
        }
        
        Debug.Log("[VRGazeDetectionTestSetup] Reset complete");
    }
    
    [ContextMenu("Validate Setup")]
    public void ValidateSetup()
    {
        Debug.Log("[VRGazeDetectionTestSetup] Validating setup...");
        
        bool isValid = true;
        
        // Check VRGazeDetection component
        var gazeDetection = FindObjectOfType<VRGazeDetection>();
        if (gazeDetection == null)
        {
            Debug.LogError("[VRGazeDetectionTestSetup] Missing VRGazeDetection component!");
            isValid = false;
        }
        else
        {
            Debug.Log("[VRGazeDetectionTestSetup] ✓ VRGazeDetection component found");
        }
        
        // Check VR UI component
        var vrUI = FindObjectOfType<VRGazeDetectionUI>();
        if (vrUI == null)
        {
            Debug.LogError("[VRGazeDetectionTestSetup] Missing VRGazeDetectionUI component!");
            isValid = false;
        }
        else
        {
            Debug.Log("[VRGazeDetectionTestSetup] ✓ VRGazeDetectionUI component found");
        }
        
        // Check camera manager
        var cameraManager = FindObjectOfType<WebCamTextureManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning("[VRGazeDetectionTestSetup] Missing WebCamTextureManager - camera may not work");
        }
        else
        {
            Debug.Log("[VRGazeDetectionTestSetup] ✓ WebCamTextureManager found");
        }
        
        // Check ONNX model
        if (testGazeModel == null)
        {
            Debug.LogWarning("[VRGazeDetectionTestSetup] No test gaze model assigned");
        }
        else
        {
            Debug.Log("[VRGazeDetectionTestSetup] ✓ Test gaze model assigned");
        }
        
        if (isValid)
        {
            Debug.Log("[VRGazeDetectionTestSetup] ✓ Setup validation passed!");
        }
        else
        {
            Debug.LogError("[VRGazeDetectionTestSetup] ✗ Setup validation failed!");
        }
    }
    
    void OnValidate()
    {
        // Auto-setup when values change in editor
        if (Application.isPlaying && autoSetupOnStart)
        {
            SetupTestEnvironment();
        }
    }
}
