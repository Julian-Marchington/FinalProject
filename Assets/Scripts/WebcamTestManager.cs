using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple webcam manager for testing gaze detection on PC
/// </summary>
public class WebcamTestManager : MonoBehaviour
{
    [Header("Webcam Settings")]
    public string deviceName = ""; // Leave empty for default
    public int width = 640;
    public int height = 480;
    public int fps = 30;
    
    [Header("UI Display")]
    public RawImage webcamDisplay; // Optional: to see the camera feed
    
    private WebCamTexture webCamTexture;
    private bool isInitialized = false;

    [Header("Editor Webcam (for PC testing)")]
    public WebcamTestManager webcamTestManager;

    
    void Start()
    {
        InitializeWebcam();
    }
    
    void InitializeWebcam()
    {
        Debug.Log("WebcamTestManager: Initializing webcam...");
        
        // Get available webcams
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"WebcamTestManager: Found {devices.Length} webcam devices:");
        foreach (WebCamDevice device in devices)
        {
            Debug.Log($"  - {device.name}");
        }
        
        // Use specified device or first available
        if (string.IsNullOrEmpty(deviceName) && devices.Length > 0)
        {
            deviceName = devices[0].name;
            Debug.Log($"WebcamTestManager: Using default device: {deviceName}");
        }
        
        if (string.IsNullOrEmpty(deviceName))
        {
            Debug.LogError("WebcamTestManager: No webcam device available!");
            return;
        }
        
        // Create webcam texture
        webCamTexture = new WebCamTexture(deviceName, width, height, fps);
        webCamTexture.Play();
        
        // Display webcam feed if UI element provided
        if (webcamDisplay != null)
        {
            webcamDisplay.texture = webCamTexture;
        }
        
        isInitialized = true;
        Debug.Log($"WebcamTestManager: Webcam initialized successfully on device: {deviceName}");
    }
    
    /// <summary>
    /// Get the webcam texture (compatible with WebCamTextureManager interface)
    /// </summary>
    public WebCamTexture WebCamTexture => webCamTexture;
    
    /// <summary>
    /// Check if webcam is playing
    /// </summary>
    public bool IsPlaying => webCamTexture != null && webCamTexture.isPlaying;
    
    /// <summary>
    /// Check if webcam is initialized
    /// </summary>
    public bool IsInitialized => isInitialized;
    
    void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            webCamTexture = null;
        }
    }
}
