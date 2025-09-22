using UnityEngine;
using System.Collections;

/// <summary>
/// Simple webcam debugger to test webcam functionality
/// Use this to verify your webcam is working before using the gaze detection
/// </summary>
public class WebcamDebugger : MonoBehaviour
{
    [Header("Webcam Settings")]
    [SerializeField] private int webcamIndex = 0;
    [SerializeField] private int targetWidth = 640;
    [SerializeField] private int targetHeight = 480;
    
    [Header("Display")]
    [SerializeField] private Renderer targetRenderer; // Assign a material to see the webcam feed
    [SerializeField] private TMPro.TextMeshProUGUI statusText;
    [SerializeField] private bool createVisualDisplay = true; // Automatically create a visual display
    
    private WebCamTexture webCamTexture;
    private bool isInitialized = false;
    private GameObject displayQuad; // Visual display for webcam feed
    
    void Start()
    {
        StartCoroutine(InitializeWebcam());
    }
    
    void Update()
    {
        // Press Space to toggle webcam
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleWebcam();
        }
        
        // Press R to restart webcam
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartWebcam();
        }
        
        // Press D to debug info
        if (Input.GetKeyDown(KeyCode.D))
        {
            DebugWebcamInfo();
        }
        
        // Press V to toggle visual display
        if (Input.GetKeyDown(KeyCode.V))
        {
            ToggleVisualDisplay();
        }
    }
    
    private IEnumerator InitializeWebcam()
    {
        if (statusText != null)
            statusText.text = "Initializing webcam...";
        
        Debug.Log("WebcamDebugger: Starting webcam initialization...");
        
        // Check available devices
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("WebcamDebugger: No webcam devices found!");
            if (statusText != null)
                statusText.text = "Error: No webcam devices found";
            yield break;
        }
        
        Debug.Log($"WebcamDebugger: Found {WebCamTexture.devices.Length} webcam devices:");
        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            Debug.Log($"  Device {i}: {WebCamTexture.devices[i].name}");
        }
        
        // Try to create webcam texture
        string deviceName = WebCamTexture.devices[webcamIndex].name;
        Debug.Log($"WebcamDebugger: Attempting to use device: {deviceName}");
        
        try
        {
            webCamTexture = new WebCamTexture(deviceName, targetWidth, targetHeight, 30);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WebcamDebugger: Error creating webcam texture: {e.Message}");
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
            Debug.LogError("WebcamDebugger: Failed to start webcam after 5 seconds!");
            if (statusText != null)
                statusText.text = "Error: Webcam failed to start";
            yield break;
        }
        
        // Wait a bit more for the webcam to stabilize
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"WebcamDebugger: Webcam initialized successfully: {webCamTexture.width}x{webCamTexture.height}");
        isInitialized = true;
        
        // Create visual display if requested
        if (createVisualDisplay)
        {
            yield return StartCoroutine(CreateVisualDisplay());
        }
        
        // Assign to renderer if available
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = webCamTexture;
            Debug.Log("WebcamDebugger: Assigned webcam texture to renderer");
        }
        
        if (statusText != null)
            statusText.text = $"Webcam ready! {webCamTexture.width}x{webCamTexture.height} - Press Space to toggle, R to restart, V to toggle display";
    }
    
    private IEnumerator CreateVisualDisplay()
    {
        if (displayQuad != null)
        {
            DestroyImmediate(displayQuad);
        }
        
        // Wait a frame to ensure everything is initialized
        yield return null;
        
        // Create a quad to display the webcam feed
        displayQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        displayQuad.name = "WebcamDisplay";
        
        // Don't parent to transform - keep it as a world object
        displayQuad.transform.SetParent(null);
        
        // Find a camera to position relative to
        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            // Try to find any camera in the scene
            targetCamera = FindObjectOfType<Camera>();
            if (targetCamera == null)
            {
                Debug.LogWarning("WebcamDebugger: No camera found, positioning display at origin");
                displayQuad.transform.position = Vector3.forward * 3f;
            }
        }
        
        if (targetCamera != null)
        {
            // Position it in front of the camera with more distance
            displayQuad.transform.position = targetCamera.transform.position + targetCamera.transform.forward * 3f;
            displayQuad.transform.LookAt(targetCamera.transform);
            displayQuad.transform.Rotate(0, 180, 0); // Flip to face camera
            
            Debug.Log($"WebcamDebugger: Positioned display relative to camera: {targetCamera.name} at position {displayQuad.transform.position}");
        }
        else
        {
            // Fallback positioning - make it more visible
            displayQuad.transform.position = Vector3.forward * 3f;
            displayQuad.transform.rotation = Quaternion.identity;
        }
        
        // Make it much larger and more visible
        float aspectRatio = (float)targetWidth / targetHeight;
        float baseScale = 5f; // Much larger base scale
        displayQuad.transform.localScale = new Vector3(aspectRatio * baseScale, baseScale, 1f);
        
        Debug.Log($"WebcamDebugger: Scaled display to {displayQuad.transform.localScale}");
        
        // Create material and assign webcam texture
        Material webcamMaterial = new Material(Shader.Find("Unlit/Texture"));
        if (webcamMaterial == null)
        {
            // Fallback to standard shader if Unlit/Texture not found
            webcamMaterial = new Material(Shader.Find("Standard"));
            Debug.LogWarning("WebcamDebugger: Unlit/Texture shader not found, using Standard shader");
        }
        
        webcamMaterial.mainTexture = webCamTexture;
        
        // Assign material to renderer
        Renderer quadRenderer = displayQuad.GetComponent<Renderer>();
        if (quadRenderer != null)
        {
            quadRenderer.material = webcamMaterial;
            Debug.Log($"WebcamDebugger: Created visual display for webcam feed at {displayQuad.transform.position} with scale {displayQuad.transform.localScale}");
        }
        else
        {
            Debug.LogError("WebcamDebugger: Failed to get renderer from display quad");
        }
    }
    
    private void ToggleVisualDisplay()
    {
        if (displayQuad != null)
        {
            bool isVisible = displayQuad.activeSelf;
            displayQuad.SetActive(!isVisible);
            
            if (statusText != null)
            {
                statusText.text = isVisible ? "Visual display hidden" : "Visual display shown";
            }
            
            Debug.Log($"WebcamDebugger: Visual display toggled - {(isVisible ? "hidden" : "shown")}");
        }
        else if (createVisualDisplay)
        {
            StartCoroutine(CreateVisualDisplay());
            if (statusText != null)
                statusText.text = "Visual display created";
        }
    }
    
    private void ToggleWebcam()
    {
        if (!isInitialized || webCamTexture == null)
        {
            Debug.LogWarning("WebcamDebugger: Cannot toggle - webcam not initialized");
            return;
        }
        
        if (webCamTexture.isPlaying)
        {
            webCamTexture.Pause();
            Debug.Log("WebcamDebugger: Webcam paused");
            if (statusText != null)
                statusText.text = "Webcam paused - Press Space to resume";
        }
        else
        {
            webCamTexture.Play();
            Debug.Log("WebcamDebugger: Webcam resumed");
            if (statusText != null)
                statusText.text = "Webcam resumed - Press Space to pause";
        }
    }
    
    private void RestartWebcam()
    {
        Debug.Log("WebcamDebugger: Restarting webcam...");
        
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            DestroyImmediate(webCamTexture);
        }
        
        if (displayQuad != null)
        {
            DestroyImmediate(displayQuad);
        }
        
        isInitialized = false;
        StartCoroutine(InitializeWebcam());
    }
    
    private void DebugWebcamInfo()
    {
        Debug.Log("=== WebcamDebugger Debug Info ===");
        Debug.Log($"Webcam devices found: {WebCamTexture.devices.Length}");
        
        for (int i = 0; i < WebCamTexture.devices.Length; i++)
        {
            var device = WebCamTexture.devices[i];
            Debug.Log($"  Device {i}: {device.name} (isFrontFacing: {device.isFrontFacing})");
        }
        
        if (webCamTexture != null)
        {
            Debug.Log($"Current webcam: {webCamTexture.name}");
            Debug.Log($"Webcam playing: {webCamTexture.isPlaying}");
            Debug.Log($"Webcam dimensions: {webCamTexture.width}x{webCamTexture.height}");
            Debug.Log($"Webcam requested dimensions: {targetWidth}x{targetHeight}");
        }
        else
        {
            Debug.Log("Current webcam: null");
        }
        
        Debug.Log($"Is initialized: {isInitialized}");
        Debug.Log($"Target renderer: {(targetRenderer != null ? targetRenderer.name : "null")}");
        Debug.Log($"Visual display: {(displayQuad != null ? displayQuad.name : "null")}");
        Debug.Log($"Display active: {(displayQuad != null ? displayQuad.activeSelf.ToString() : "N/A")}");
        
        // Camera info
        Camera mainCam = Camera.main;
        Debug.Log($"Main camera: {(mainCam != null ? mainCam.name : "null")}");
        if (mainCam != null)
        {
            Debug.Log($"Camera position: {mainCam.transform.position}");
            Debug.Log($"Camera forward: {mainCam.transform.forward}");
        }
        
        Debug.Log("================================");
    }
    
    private void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            DestroyImmediate(webCamTexture);
        }
        
        if (displayQuad != null)
        {
            DestroyImmediate(displayQuad);
        }
    }
    
    // Public methods for external access
    public WebCamTexture GetWebcamTexture()
    {
        return webCamTexture;
    }
    
    public bool IsWebcamReady()
    {
        return isInitialized && webCamTexture != null && webCamTexture.isPlaying;
    }
    
    public void SetWebcamIndex(int index)
    {
        webcamIndex = index;
        RestartWebcam();
    }
}
