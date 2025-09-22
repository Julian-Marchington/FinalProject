using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Helper script to automatically set up the scene for SimpleGazeDetection
/// Run this in the editor to create the necessary UI elements
/// </summary>
public class SimpleGazeSetup : MonoBehaviour
{
    [Header("Setup Options")]
    [SerializeField] private bool autoSetupOnStart = true;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool includeWebcamDebugger = true; // Add webcam debugger for troubleshooting
    
    [Header("UI Prefabs")]
    [SerializeField] private GameObject textPrefab;
    
    private SimpleGazeDetection gazeDetection;
    private WebcamDebugger webcamDebugger;
    
    void Start()
    {
        if (autoSetupOnStart)
        {
            SetupScene();
        }
    }
    
    [ContextMenu("Setup Scene")]
    public void SetupScene()
    {
        Debug.Log("Setting up SimpleGazeDetection scene...");
        
        // Find or create canvas
        if (targetCanvas == null)
        {
            targetCanvas = FindObjectOfType<Canvas>();
            if (targetCanvas == null)
            {
                CreateCanvas();
            }
        }
        
        // Create UI elements first
        CreateUIElements();
        
        // Find or create SimpleGazeDetection component
        gazeDetection = FindObjectOfType<SimpleGazeDetection>();
        if (gazeDetection == null)
        {
            CreateGazeDetection();
        }
        
        // Create webcam debugger if requested
        if (includeWebcamDebugger)
        {
            CreateWebcamDebugger();
        }
        
        // Connect references
        ConnectReferences();
        
        Debug.Log("Scene setup complete!");
    }
    
    private void CreateCanvas()
    {
        GameObject canvasGO = new GameObject("GazeDetectionCanvas");
        targetCanvas = canvasGO.AddComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 100;
        
        // Add CanvasScaler for responsive UI
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        // Add GraphicRaycaster for UI interaction
        canvasGO.AddComponent<GraphicRaycaster>();
        
        Debug.Log("Created new Canvas for gaze detection");
    }
    
    private void CreateGazeDetection()
    {
        GameObject gazeGO = new GameObject("SimpleGazeDetection");
        gazeDetection = gazeGO.AddComponent<SimpleGazeDetection>();
        
        Debug.Log("Created SimpleGazeDetection component");
    }
    
    private void CreateWebcamDebugger()
    {
        GameObject debuggerGO = new GameObject("WebcamDebugger");
        webcamDebugger = debuggerGO.AddComponent<WebcamDebugger>();
        
        Debug.Log("Created WebcamDebugger component");
    }
    
    private void CreateUIElements()
    {
        if (targetCanvas == null) return;
        
        // Create status text
        GameObject statusGO = CreateTextElement("StatusText", "Ready to detect gaze", 24);
        RectTransform statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchoredPosition = new Vector2(0, 200);
        
        // Create gaze text
        GameObject gazeGO = CreateTextElement("GazeText", "Gaze: Yaw: 0.0°, Pitch: 0.0°", 20);
        RectTransform gazeRect = gazeGO.GetComponent<RectTransform>();
        gazeRect.anchoredPosition = new Vector2(0, 150);
        
        // Create instructions text
        GameObject instructionsGO = CreateTextElement("InstructionsText", "Press A button to start/stop gaze detection", 18);
        RectTransform instructionsRect = instructionsGO.GetComponent<RectTransform>();
        instructionsRect.anchoredPosition = new Vector2(0, 100);
        
        // Create webcam debug text
        GameObject webcamDebugGO = CreateTextElement("WebcamDebugText", "Webcam: Initializing...", 16);
        RectTransform webcamDebugRect = webcamDebugGO.GetComponent<RectTransform>();
        webcamDebugRect.anchoredPosition = new Vector2(0, 50);
        
        // Create help text
        GameObject helpGO = CreateTextElement("HelpText", "Press D for debug info, R to restart webcam", 14);
        RectTransform helpRect = helpGO.GetComponent<RectTransform>();
        helpRect.anchoredPosition = new Vector2(0, 0);
        
        Debug.Log("Created UI elements");
    }
    
    private GameObject CreateTextElement(string name, string text, int fontSize)
    {
        GameObject textGO = new GameObject(name);
        textGO.transform.SetParent(targetCanvas.transform, false);
        
        TextMeshProUGUI tmpText = textGO.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = fontSize;
        tmpText.color = Color.white;
        tmpText.alignment = TextAlignmentOptions.Center;
        
        // Set up RectTransform
        RectTransform rect = textGO.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400, 50);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        
        return textGO;
    }
    
    private void ConnectReferences()
    {
        if (gazeDetection == null) return;
        
        // Find UI elements by name
        TextMeshProUGUI statusText = GameObject.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI gazeText = GameObject.Find("GazeText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI webcamDebugText = GameObject.Find("WebcamDebugText")?.GetComponent<TextMeshProUGUI>();
        
        // Use reflection to set private fields (since they're SerializeField)
        var statusField = typeof(SimpleGazeDetection).GetField("statusText", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var gazeField = typeof(SimpleGazeDetection).GetField("gazeText", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (statusField != null && statusText != null)
            statusField.SetValue(gazeDetection, statusText);
        
        if (gazeField != null && gazeText != null)
            gazeField.SetValue(gazeDetection, gazeText);
        
        // Connect webcam debugger if it exists
        if (webcamDebugger != null && webcamDebugText != null)
        {
            var debuggerStatusField = typeof(WebcamDebugger).GetField("statusText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (debuggerStatusField != null)
                debuggerStatusField.SetValue(webcamDebugger, webcamDebugText);
        }
        
        Debug.Log("Connected UI references to SimpleGazeDetection and WebcamDebugger");
    }
    
    [ContextMenu("Clean Up Scene")]
    public void CleanUpScene()
    {
        // Remove UI elements
        GameObject[] uiElements = GameObject.FindGameObjectsWithTag("Untagged");
        foreach (GameObject go in uiElements)
        {
            if (go.name.Contains("StatusText") || go.name.Contains("GazeText") || 
                go.name.Contains("InstructionsText") || go.name.Contains("WebcamDebugText") ||
                go.name.Contains("HelpText") || go.name.Contains("GazeDetectionCanvas"))
            {
                if (Application.isPlaying)
                    Destroy(go);
                else
                    DestroyImmediate(go);
            }
        }
        
        // Remove SimpleGazeDetection if it was created by this script
        if (gazeDetection != null && gazeDetection.gameObject.name == "SimpleGazeDetection")
        {
            if (Application.isPlaying)
                Destroy(gazeDetection.gameObject);
            else
                DestroyImmediate(gazeDetection.gameObject);
        }
        
        // Remove WebcamDebugger if it was created by this script
        if (webcamDebugger != null && webcamDebugger.gameObject.name == "WebcamDebugger")
        {
            if (Application.isPlaying)
                Destroy(webcamDebugger.gameObject);
            else
                DestroyImmediate(webcamDebugger.gameObject);
        }
        
        Debug.Log("Scene cleanup complete");
    }
}
