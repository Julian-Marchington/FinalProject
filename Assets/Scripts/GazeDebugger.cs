using UnityEngine;
using UnityEngine.UI;

public class GazeDebugger : MonoBehaviour
{
    [Header("Debug Controls")]
    public YoloFaceDetector faceDetector;
    public Button testGazeButton;
    public Button manualGazeButton;
    public Text debugText;

    void Start()
    {
        if (faceDetector == null)
            faceDetector = FindObjectOfType<YoloFaceDetector>();

        if (testGazeButton != null)
            testGazeButton.onClick.AddListener(TestGazeDetection);

        if (manualGazeButton != null)
            manualGazeButton.onClick.AddListener(ManualGazeDetection);

        UpdateDebugInfo();
    }

    void Update()
    {
        // Update debug info every frame
        if (Time.frameCount % 30 == 0) // Every 30 frames
        {
            UpdateDebugInfo();
        }
    }

    void UpdateDebugInfo()
    {
        if (debugText == null) return;

        if (faceDetector == null)
        {
            debugText.text = "Gaze Debug Info:\nFace Detector: Not Found";
            return;
        }

        // Check if gaze detection is working
        bool gazeModelLoaded = faceDetector.gazeOnnx != null;
        bool gazeVisualizerAssigned = faceDetector.gazeVisualizer != null;

        string status = "Gaze Debug Info:\n";
        status += $"Face Detector: Found\n";
        status += $"Gaze Model: {(gazeModelLoaded ? "Loaded" : "Not Loaded")}\n";
        status += $"Gaze Visualizer: {(gazeVisualizerAssigned ? "Assigned" : "Not Assigned")}\n";
        
        // Add any additional debug info here
        if (gazeVisualizerAssigned)
        {
            status += $"Arrow Length: {faceDetector.gazeVisualizer.arrowLength:F0}px\n";
        }

        debugText.text = status;
    }

    void TestGazeDetection()
    {
        if (faceDetector == null)
        {
            Debug.LogError("[GAZE] Face detector not found!");
            return;
        }

        Debug.Log("[GAZE] Testing gaze detection...");
        faceDetector.TestGazeDetection();
    }

    void ManualGazeDetection()
    {
        if (faceDetector == null)
        {
            Debug.LogError("[GAZE] Face detector not found!");
            return;
        }

        Debug.Log("[GAZE] Manual gaze detection triggered...");
        faceDetector.ManualGazeDetection();
    }
}
