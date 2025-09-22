using UnityEngine;
using TMPro;

/// <summary>
/// VR UI Manager for Gaze Detection
/// Creates and manages UI elements in VR space for displaying gaze detection results
/// </summary>
public class VRGazeDetectionUI : MonoBehaviour
{
    [Header("UI Prefabs")]
    [SerializeField] private GameObject statusTextPrefab;
    [SerializeField] private GameObject debugTextPrefab;
    [SerializeField] private GameObject gazeIndicatorPrefab;
    
    [Header("UI Positioning")]
    [SerializeField] private Vector3 statusTextOffset = new Vector3(0, 0.3f, 0.5f);
    [SerializeField] private Vector3 debugTextOffset = new Vector3(0, 0.1f, 0.5f);
    [SerializeField] private Vector3 gazeIndicatorOffset = new Vector3(0, 0, 0.3f);
    
    [Header("UI References")]
    [SerializeField] private TextMeshPro statusText;
    [SerializeField] private TextMeshPro debugText;
    [SerializeField] private GameObject gazeIndicator;
    
    [Header("VR Gaze Detection")]
    [SerializeField] private VRGazeDetection gazeDetection;
    
    void Start()
    {
        CreateVRUI();
        SetupGazeDetection();
    }
    
    private void CreateVRUI()
    {
        // Create status text
        if (statusTextPrefab != null)
        {
            GameObject statusObj = Instantiate(statusTextPrefab, transform);
            statusObj.transform.localPosition = statusTextOffset;
            statusText = statusObj.GetComponent<TextMeshPro>();
            
            if (statusText == null)
            {
                statusText = statusObj.AddComponent<TextMeshPro>();
            }
            
            statusText.text = "Initializing VR Gaze Detection...";
            statusText.fontSize = 0.1f;
            statusText.color = Color.white;
            statusText.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            // Create default status text
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(transform);
            statusObj.transform.localPosition = statusTextOffset;
            
            statusText = statusObj.AddComponent<TextMeshPro>();
            statusText.text = "Initializing VR Gaze Detection...";
            statusText.fontSize = 0.1f;
            statusText.color = Color.white;
            statusText.alignment = TextAlignmentOptions.Center;
        }
        
        // Create debug text
        if (debugTextPrefab != null)
        {
            GameObject debugObj = Instantiate(debugTextPrefab, transform);
            debugObj.transform.localPosition = debugTextOffset;
            debugText = debugObj.GetComponent<TextMeshPro>();
            
            if (debugText == null)
            {
                debugText = debugObj.AddComponent<TextMeshPro>();
            }
            
            debugText.text = "Processing...";
            debugText.fontSize = 0.08f;
            debugText.color = Color.yellow;
            debugText.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            // Create default debug text
            GameObject debugObj = new GameObject("DebugText");
            debugObj.transform.SetParent(transform);
            debugObj.transform.localPosition = debugTextOffset;
            
            debugText = debugObj.AddComponent<TextMeshPro>();
            debugText.text = "Processing...";
            debugText.fontSize = 0.08f;
            debugText.color = Color.yellow;
            debugText.alignment = TextAlignmentOptions.Center;
        }
        
        // Create gaze indicator
        if (gazeIndicatorPrefab != null)
        {
            gazeIndicator = Instantiate(gazeIndicatorPrefab, transform);
            gazeIndicator.transform.localPosition = gazeIndicatorOffset;
        }
        else
        {
            // Create default gaze indicator (sphere)
            gazeIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gazeIndicator.name = "GazeIndicator";
            gazeIndicator.transform.SetParent(transform);
            gazeIndicator.transform.localPosition = gazeIndicatorOffset;
            gazeIndicator.transform.localScale = Vector3.one * 0.05f;
            
            // Set initial color
            Renderer renderer = gazeIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.red;
            }
        }
        
        Debug.Log("[VRGazeDetectionUI] VR UI elements created successfully");
    }
    
    private void SetupGazeDetection()
    {
        // Find or create VRGazeDetection component
        if (gazeDetection == null)
        {
            gazeDetection = GetComponent<VRGazeDetection>();
            if (gazeDetection == null)
            {
                gazeDetection = gameObject.AddComponent<VRGazeDetection>();
            }
        }
        
        // Assign UI references to the gaze detection script
        if (gazeDetection != null)
        {
            // Use reflection to set private fields since they're serialized
            var statusTextField = typeof(VRGazeDetection).GetField("statusText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var debugTextField = typeof(VRGazeDetection).GetField("debugText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var gazeIndicatorField = typeof(VRGazeDetection).GetField("gazeIndicator", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (statusTextField != null) statusTextField.SetValue(gazeDetection, statusText);
            if (debugTextField != null) debugTextField.SetValue(gazeDetection, debugText);
            if (gazeIndicatorField != null) gazeIndicatorField.SetValue(gazeDetection, gazeIndicator);
            
            Debug.Log("[VRGazeDetectionUI] Gaze detection setup complete");
        }
        else
        {
            Debug.LogError("[VRGazeDetectionUI] Failed to setup gaze detection component");
        }
    }
    
    // Public methods for external access
    public void SetStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }
    
    public void SetDebugText(string text)
    {
        if (debugText != null)
        {
            debugText.text = text;
        }
    }
    
    public void UpdateGazeIndicator(Vector2 gazeDirection, bool isLooking)
    {
        if (gazeIndicator != null)
        {
            // Update indicator position based on gaze direction
            Vector3 indicatorPosition = gazeIndicator.transform.localPosition;
            
            // Map yaw to X position, pitch to Y position
            float xOffset = Mathf.Clamp(gazeDirection.x * 2f, -1f, 1f);
            float yOffset = Mathf.Clamp(gazeDirection.y * 1.5f, -1f, 1f);
            
            indicatorPosition.x = gazeIndicatorOffset.x + xOffset * 0.2f;
            indicatorPosition.y = gazeIndicatorOffset.y + yOffset * 0.2f;
            
            gazeIndicator.transform.localPosition = indicatorPosition;
            
            // Change color based on whether user is looking
            Renderer renderer = gazeIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isLooking ? Color.green : Color.red;
            }
        }
    }
    
    public void ShowUI(bool show)
    {
        if (statusText != null) statusText.gameObject.SetActive(show);
        if (debugText != null) debugText.gameObject.SetActive(show);
        if (gazeIndicator != null) gazeIndicator.SetActive(show);
    }
    
    public void SetUITransparency(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        
        if (statusText != null)
        {
            Color color = statusText.color;
            color.a = alpha;
            statusText.color = color;
        }
        
        if (debugText != null)
        {
            Color color = debugText.color;
            color.a = alpha;
            debugText.color = color;
        }
        
        if (gazeIndicator != null)
        {
            Renderer renderer = gazeIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
            }
        }
    }
}

