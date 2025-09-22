// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-GazeDetection")]
    public class GazeDetectionManager : MonoBehaviour
    {
        [Header("Camera Configuration")]
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        
        [Header("Gaze Detection")]
        [SerializeField] private GazeInferenceManager m_gazeInference;
        [SerializeField] private GazeInferenceUiManager m_uiInference;
        
        [Header("Controls")]
        [SerializeField] private OVRInput.RawButton m_toggleButton = OVRInput.RawButton.A;
        [SerializeField] private bool m_autoStart = true; // Automatically start gaze detection
        
        [Header("Performance Settings")]
        [SerializeField] private float m_inferenceInterval = 0.1f; // Run inference every 100ms
        [SerializeField] private bool m_showDebugInfo = true;
        
        [Header("Events")]
        public UnityEvent<bool> OnGazeDetectionToggled; // Fired when gaze detection is toggled
        public UnityEvent<Vector2> OnGazeDirectionChanged; // Fired when gaze direction changes
        public UnityEvent<bool> OnLookingAtCameraChanged; // Fired when looking at camera status changes
        
        // Private variables
        private bool m_isGazeDetectionActive = false;
        private bool m_isStarted = false;
        private bool m_isGazeInferenceReady = false;
        private float m_lastInferenceTime = 0f;
        private float m_gazeDetectionStartTime = 0f;
        
        // Statistics
        private int m_totalInferences = 0;
        private float m_totalGazeTime = 0f;
        
        #region Unity Functions
        
        private void Awake()
        {
            // Subscribe to gaze events
            if (m_uiInference != null)
            {
                m_uiInference.OnGazeDirectionChanged.AddListener(OnGazeDirectionChangedHandler);
                m_uiInference.OnGazeAtCameraChanged.AddListener(OnLookingAtCameraChangedHandler);
            }
        }
        
        private IEnumerator Start()
        {
            // Wait until gaze inference model is loaded
            if (m_gazeInference != null)
            {
                while (!m_gazeInference.IsModelLoaded)
                {
                    yield return null;
                }
                m_isGazeInferenceReady = true;
                Debug.Log("GazeDetectionManager: Gaze inference model is ready");
            }
            else
            {
                Debug.LogError("GazeDetectionManager: No GazeInferenceManager assigned!");
                yield break;
            }
            
            // Wait for camera to be ready
            while (m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.isPlaying)
            {
                yield return null;
            }
            
            Debug.Log($"GazeDetectionManager: Camera ready - {m_webCamTextureManager.WebCamTexture.width}x{m_webCamTextureManager.WebCamTexture.height}");
            
            // Auto-start if enabled
            if (m_autoStart)
            {
                yield return new WaitForSeconds(1f); // Give camera time to stabilize
                ToggleGazeDetection();
            }
            
            m_isStarted = true;
        }
        
        private void Update()
        {
            if (!m_isStarted || !m_isGazeInferenceReady)
                return;
            
            // Check for toggle button press
            if (OVRInput.GetUp(m_toggleButton))
            {
                ToggleGazeDetection();
            }
            
            // Run gaze detection if active
            if (m_isGazeDetectionActive)
            {
                RunGazeDetection();
            }
        }
        
        #endregion
        
        #region Public Functions
        
        /// <summary>
        /// Toggle gaze detection on/off
        /// </summary>
        public void ToggleGazeDetection()
        {
            m_isGazeDetectionActive = !m_isGazeDetectionActive;
            
            if (m_isGazeDetectionActive)
            {
                StartGazeDetection();
            }
            else
            {
                StopGazeDetection();
            }
            
            // Fire event
            OnGazeDetectionToggled?.Invoke(m_isGazeDetectionActive);
            
            Debug.Log($"GazeDetectionManager: Gaze detection {(m_isGazeDetectionActive ? "started" : "stopped")}");
        }
        
        /// <summary>
        /// Start gaze detection
        /// </summary>
        public void StartGazeDetection()
        {
            if (!m_isGazeInferenceReady)
            {
                Debug.LogWarning("GazeDetectionManager: Cannot start - gaze inference not ready");
                return;
            }
            
            m_isGazeDetectionActive = true;
            m_gazeDetectionStartTime = Time.time;
            m_totalInferences = 0;
            m_totalGazeTime = 0f;
            
            // Update UI status
            if (m_uiInference != null)
            {
                m_uiInference.UpdateStatus("Gaze detection active - Look at the camera!");
            }
            
            Debug.Log("GazeDetectionManager: Gaze detection started");
        }
        
        /// <summary>
        /// Stop gaze detection
        /// </summary>
        public void StopGazeDetection()
        {
            m_isGazeDetectionActive = false;
            
            // Calculate statistics
            m_totalGazeTime = Time.time - m_gazeDetectionStartTime;
            float avgFPS = m_totalInferences / m_totalGazeTime;
            
            // Update UI status
            if (m_uiInference != null)
            {
                m_uiInference.UpdateStatus($"Gaze detection stopped - Avg FPS: {avgFPS:F1}");
                m_uiInference.ClearGazeDisplay();
            }
            
            Debug.Log($"GazeDetectionManager: Gaze detection stopped - Total inferences: {m_totalInferences}, Avg FPS: {avgFPS:F1}");
        }
        
        /// <summary>
        /// Get current gaze detection status
        /// </summary>
        /// <returns>True if gaze detection is active</returns>
        public bool IsGazeDetectionActive()
        {
            return m_isGazeDetectionActive;
        }
        
        /// <summary>
        /// Get current gaze direction
        /// </summary>
        /// <param name="useRadians">Whether to return values in radians</param>
        /// <returns>Current gaze direction as Vector2 (x=yaw, y=pitch)</returns>
        public Vector2 GetCurrentGazeDirection(bool useRadians = false)
        {
            if (m_gazeInference != null)
            {
                return m_gazeInference.GetGazeDirection(useRadians);
            }
            return Vector2.zero;
        }
        
        /// <summary>
        /// Check if the person is currently looking at the camera
        /// </summary>
        /// <returns>True if looking at camera</returns>
        public bool IsLookingAtCamera()
        {
            if (m_gazeInference != null)
            {
                return m_gazeInference.IsLookingAtCamera();
            }
            return false;
        }
        
        /// <summary>
        /// Get gaze detection statistics
        /// </summary>
        /// <returns>Statistics as a formatted string</returns>
        public string GetGazeStatistics()
        {
            if (!m_isGazeDetectionActive)
                return "Gaze detection not active";
            
            float currentGazeTime = Time.time - m_gazeDetectionStartTime;
            float avgFPS = m_totalInferences / currentGazeTime;
            
            return $"Active: {currentGazeTime:F1}s | Inferences: {m_totalInferences} | Avg FPS: {avgFPS:F1}";
        }
        
        #endregion
        
        #region Private Functions
        
        /// <summary>
        /// Run gaze detection on the current camera frame
        /// </summary>
        private void RunGazeDetection()
        {
            // Check if we have a valid camera texture
            if (m_webCamTextureManager.WebCamTexture == null || !m_webCamTextureManager.WebCamTexture.isPlaying)
            {
                if (m_showDebugInfo)
                {
                    Debug.LogWarning("GazeDetectionManager: No valid camera texture available");
                }
                return;
            }
            
            // Check if enough time has passed since last inference
            if (Time.time - m_lastInferenceTime < m_inferenceInterval)
            {
                return;
            }
            
            // Check if inference is not already running
            if (!m_gazeInference.IsRunning())
            {
                // Start new inference
                m_gazeInference.RunInference(m_webCamTextureManager.WebCamTexture);
                m_lastInferenceTime = Time.time;
                m_totalInferences++;
                
                if (m_showDebugInfo && m_totalInferences % 30 == 0) // Log every 30 inferences
                {
                    float currentGazeTime = Time.time - m_gazeDetectionStartTime;
                    float avgFPS = m_totalInferences / currentGazeTime;
                    Debug.Log($"GazeDetectionManager: Inference {m_totalInferences} - Avg FPS: {avgFPS:F1}");
                }
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        /// <summary>
        /// Handle gaze direction changes from the UI manager
        /// </summary>
        /// <param name="gazeDirection">New gaze direction</param>
        private void OnGazeDirectionChangedHandler(Vector2 gazeDirection)
        {
            // Fire event for external listeners
            OnGazeDirectionChanged?.Invoke(gazeDirection);
            
            if (m_showDebugInfo)
            {
                Debug.Log($"GazeDetectionManager: Gaze direction changed - Yaw: {gazeDirection.x:F1}°, Pitch: {gazeDirection.y:F1}°");
            }
        }
        
        /// <summary>
        /// Handle looking at camera status changes from the UI manager
        /// </summary>
        /// <param name="isLookingAtCamera">Whether the person is looking at the camera</param>
        private void OnLookingAtCameraChangedHandler(bool isLookingAtCamera)
        {
            // Fire event for external listeners
            OnLookingAtCameraChanged?.Invoke(isLookingAtCamera);
            
            if (m_showDebugInfo)
            {
                string status = isLookingAtCamera ? "looking at camera" : "not looking at camera";
                Debug.Log($"GazeDetectionManager: Person is {status}");
            }
        }
        
        #endregion
        
        #region Editor Functions
        
        /// <summary>
        /// Validate component references in the editor
        /// </summary>
        private void OnValidate()
        {
            if (m_inferenceInterval < 0.05f)
            {
                m_inferenceInterval = 0.05f; // Minimum 50ms interval
                Debug.LogWarning("GazeDetectionManager: Inference interval too low, setting to 0.05s");
            }
            
            if (m_inferenceInterval > 1f)
            {
                m_inferenceInterval = 1f; // Maximum 1 second interval
                Debug.LogWarning("GazeDetectionManager: Inference interval too high, setting to 1.0s");
            }
        }
        
        #endregion
    }
}

