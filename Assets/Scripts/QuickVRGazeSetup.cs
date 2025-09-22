using UnityEngine;


/// <summary>
/// Quick setup script for VR Gaze Detection
/// Automatically configures everything you need for testing
/// </summary>
public class QuickVRGazeSetup : MonoBehaviour
{
    [Header("Required Assets")]
    [SerializeField] private Unity.InferenceEngine.ModelAsset gazeModel;                      // Your ONNX gaze model
    [SerializeField] private GameObject webCamTextureManagerPrefab;      // WebCamTextureManager prefab
    
    [Header("Auto Setup")]
    [SerializeField] private bool setupOnStart = true;
    [SerializeField] private bool createHeadDebugger = true;
    
    void Start()
    {
        if (setupOnStart)
        {
            SetupVRGazeDetection();
        }
    }
    
    [ContextMenu("Setup VR Gaze Detection")]
    public void SetupVRGazeDetection()
    {
        Debug.Log("[QuickVRGazeSetup] Setting up VR Gaze Detection...");
        
        // Find or create the main gaze detection object
        GameObject gazeDetectionObject = FindOrCreateGazeDetectionObject();
        
        // Setup VRGazeDetection component
        SetupVRGazeDetectionComponent(gazeDetectionObject);
        
        // Setup VR UI
        SetupVRUI(gazeDetectionObject);
        
        // Setup head-attached debugger
        if (createHeadDebugger)
        {
            SetupHeadDebugger(gazeDetectionObject);
        }
        
        // Setup camera manager
        SetupCameraManager();
        
        Debug.Log("[QuickVRGazeSetup] Setup complete! Build and test on Quest.");
    }
    
    private GameObject FindOrCreateGazeDetectionObject()
    {
        // Look for existing GazeDetection object
        GameObject existingObject = GameObject.Find("GazeDetection");
        if (existingObject != null)
        {
            Debug.Log("[QuickVRGazeSetup] Found existing GazeDetection object");
            return existingObject;
        }
        
        // Create new object if none exists
        GameObject newObject = new GameObject("GazeDetection");
        newObject.transform.position = new Vector3(0, 1.6f, 2f); // Position in front of user
        
        Debug.Log("[QuickVRGazeSetup] Created new GazeDetection object");
        return newObject;
    }
    
    private void SetupVRGazeDetectionComponent(GameObject targetObject)
    {
        // Remove old components
        var oldGazeDetection = targetObject.GetComponent<NewGazeDetection>();
        if (oldGazeDetection != null)
        {
            Debug.Log("[QuickVRGazeSetup] Removing old NewGazeDetection component");
            DestroyImmediate(oldGazeDetection);
        }
        
        // Add VRGazeDetection component
        var vrGazeDetection = targetObject.GetComponent<VRGazeDetection>();
        if (vrGazeDetection == null)
        {
            vrGazeDetection = targetObject.AddComponent<VRGazeDetection>();
            Debug.Log("[QuickVRGazeSetup] Added VRGazeDetection component");
        }
        
        // Configure the component
        if (gazeModel != null)
        {
            // Use reflection to set the private field
            var gazeModelField = typeof(VRGazeDetection).GetField("gazeModel", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (gazeModelField != null)
            {
                gazeModelField.SetValue(vrGazeDetection, gazeModel);
                Debug.Log("[QuickVRGazeSetup] Assigned gaze model");
            }
        }
        else
        {
            Debug.LogError("[QuickVRGazeSetup] No gaze model assigned! Please assign an ONNX file.");
        }
    }
    
    private void SetupVRUI(GameObject targetObject)
    {
        // Add VR UI manager
        var vrUI = targetObject.GetComponent<VRGazeDetectionUI>();
        if (vrUI == null)
        {
            vrUI = targetObject.AddComponent<VRGazeDetectionUI>();
            Debug.Log("[QuickVRGazeSetup] Added VRGazeDetectionUI component");
        }
    }
    
    private void SetupHeadDebugger(GameObject targetObject)
    {
        // Add head-attached debugger
        var headDebugger = targetObject.GetComponent<HeadAttachedDebugger>();
        if (headDebugger == null)
        {
            headDebugger = targetObject.AddComponent<HeadAttachedDebugger>();
            Debug.Log("[QuickVRGazeSetup] Added HeadAttachedDebugger component");
        }
        
        // Connect debugger to VRGazeDetection
        var vrGazeDetection = targetObject.GetComponent<VRGazeDetection>();
        if (vrGazeDetection != null)
        {
            var debuggerField = typeof(VRGazeDetection).GetField("debugger", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (debuggerField != null)
            {
                debuggerField.SetValue(vrGazeDetection, headDebugger);
                Debug.Log("[QuickVRGazeSetup] Connected debugger to VRGazeDetection");
            }
        }
    }
    
    private void SetupCameraManager()
    {
        // Look for existing WebCamTextureManager
        var existingManager = FindObjectOfType<PassthroughCameraSamples.WebCamTextureManager>();
        if (existingManager != null)
        {
            Debug.Log("[QuickVRGazeSetup] Found existing WebCamTextureManager");
            return;
        }
        
        // Create camera manager if none exists
        if (webCamTextureManagerPrefab != null)
        {
            GameObject cameraManager = Instantiate(webCamTextureManagerPrefab);
            cameraManager.name = "WebCamTextureManager";
            Debug.Log("[QuickVRGazeSetup] Created WebCamTextureManager from prefab");
        }
        else
        {
            Debug.LogWarning("[QuickVRGazeSetup] No WebCamTextureManagerPrefab assigned - camera may not work");
        }
    }
    
    [ContextMenu("Test Setup")]
    public void TestSetup()
    {
        Debug.Log("[QuickVRGazeSetup] Testing setup...");
        
        var gazeDetection = FindObjectOfType<VRGazeDetection>();
        var vrUI = FindObjectOfType<VRGazeDetectionUI>();
        var headDebugger = FindObjectOfType<HeadAttachedDebugger>();
        var cameraManager = FindObjectOfType<PassthroughCameraSamples.WebCamTextureManager>();
        
        Debug.Log($"VRGazeDetection: {(gazeDetection != null ? "✓" : "✗")}");
        Debug.Log($"VRGazeDetectionUI: {(vrUI != null ? "✓" : "✗")}");
        Debug.Log($"HeadAttachedDebugger: {(headDebugger != null ? "✓" : "✗")}");
        Debug.Log($"WebCamTextureManager: {(cameraManager != null ? "✓" : "✗")}");
        Debug.Log($"Gaze Model: {(gazeModel != null ? "✓" : "✗")}");
        
        if (gazeDetection != null && vrUI != null && headDebugger != null && cameraManager != null && gazeModel != null)
        {
            Debug.Log("[QuickVRGazeSetup] ✓ All components are ready! Build and test on Quest.");
        }
        else
        {
            Debug.LogError("[QuickVRGazeSetup] ✗ Some components are missing. Check the setup.");
        }
    }
    
    [ContextMenu("Clean Up Old Components")]
    public void CleanUpOldComponents()
    {
        Debug.Log("[QuickVRGazeSetup] Cleaning up old components...");
        
        // Remove old debugger if it exists
        var oldDebugger = FindObjectOfType<VRGazeDetectionDebugger>();
        if (oldDebugger != null)
        {
            Debug.Log("[QuickVRGazeSetup] Removing old VRGazeDetectionDebugger");
            DestroyImmediate(oldDebugger);
        }
        
        // Remove old gaze detection components
        var oldGazeDetections = FindObjectsOfType<NewGazeDetection>();
        foreach (var old in oldGazeDetections)
        {
            Debug.Log("[QuickVRGazeSetup] Removing old NewGazeDetection component");
            DestroyImmediate(old);
        }
        
        Debug.Log("[QuickVRGazeSetup] Cleanup complete");
    }
}

