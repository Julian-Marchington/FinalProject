// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-GazeDetection")]
    public class GazeInferenceUiManager : MonoBehaviour
    {
        [Header("Camera Configuration")]
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;
        
        [Header("UI Display References")]
        [SerializeField] private RawImage m_displayImage; // Camera feed display
        [SerializeField] private TextMeshProUGUI m_gazeText; // Main gaze display
        [SerializeField] private TextMeshProUGUI m_statusText; // Status information
        [SerializeField] private TextMeshProUGUI m_debugText; // Debug information
        
        [Header("Gaze Visualization")]
        [SerializeField] private GameObject m_gazeIndicator; // Visual indicator for gaze direction
        [SerializeField] private Color m_gazeIndicatorColor = Color.green;
        [SerializeField] private float m_indicatorSize = 50f;
        [SerializeField] private bool m_showGazeIndicator = true;
        
        [Header("Gaze Thresholds")]
        [SerializeField] private float m_lookingAtCameraYawThreshold = 15f; // Degrees
        [SerializeField] private float m_lookingAtCameraPitchThreshold = 10f; // Degrees
        
        [Header("Events")]
        public UnityEvent<bool> OnGazeAtCameraChanged; // Fired when gaze at camera status changes
        public UnityEvent<Vector2> OnGazeDirectionChanged; // Fired when gaze direction changes
        
        // Private variables
        private Transform m_displayLocation;
        private bool m_wasLookingAtCamera = false;
        private Vector2 m_lastGazeDirection = Vector2.zero;
        private float m_lastUpdateTime = 0f;
        private const float UI_UPDATE_INTERVAL = 0.1f; // Update UI every 100ms
        
        // Gaze statistics for debugging
        private float m_gazeUpdateCount = 0f;
        private float m_totalGazeTime = 0f;
        
        #region Unity Functions
        
        private void Start()
        {
            m_displayLocation = m_displayImage.transform;
            InitializeGazeDisplay();
        }
        
        private void Update()
        {
            // Update UI at specified intervals to maintain performance
            if (Time.time - m_lastUpdateTime >= UI_UPDATE_INTERVAL)
            {
                m_lastUpdateTime = Time.time;
                UpdateGazeIndicator();
            }
        }
        
        #endregion
        
        #region Public Functions
        
        /// <summary>
        /// Initialize the gaze display UI
        /// </summary>
        public void InitializeGazeDisplay()
        {
            // Set initial status
            if (m_statusText != null)
            {
                m_statusText.text = "Initializing gaze detection...";
            }
            
            if (m_gazeText != null)
            {
                m_gazeText.text = "Gaze: —";
            }
            
            if (m_debugText != null)
            {
                m_debugText.text = "Waiting for camera feed...";
            }
            
            // Initialize gaze indicator
            if (m_gazeIndicator != null && m_showGazeIndicator)
            {
                m_gazeIndicator.SetActive(false);
            }
            
            Debug.Log("GazeInferenceUiManager: Gaze display initialized");
        }
        
        /// <summary>
        /// Set the camera capture texture for display
        /// </summary>
        /// <param name="image">Camera texture to display</param>
        public void SetGazeCapture(Texture image)
        {
            if (m_displayImage != null)
            {
                m_displayImage.texture = image;
                
                if (m_debugText != null)
                {
                    m_debugText.text = $"Camera: {image.width}x{image.height}";
                }
            }
        }
        
        /// <summary>
        /// Update the gaze display with new values
        /// </summary>
        /// <param name="yaw">Yaw angle (left/right)</param>
        /// <param name="pitch">Pitch angle (up/down)</param>
        /// <param name="useRadians">Whether the values are in radians</param>
        public void UpdateGazeDisplay(float yaw, float pitch, bool useRadians)
        {
            // Convert to degrees if needed
            float yawDegrees = useRadians ? yaw * Mathf.Rad2Deg : yaw;
            float pitchDegrees = useRadians ? pitch * Mathf.Rad2Deg : pitch;
            
            // Update main gaze text
            if (m_gazeText != null)
            {
                m_gazeText.text = $"Gaze: Yaw: {yawDegrees:F1}° Pitch: {pitchDegrees:F1}°";
            }
            
            // Check if looking at camera
            bool isLookingAtCamera = Mathf.Abs(yawDegrees) < m_lookingAtCameraYawThreshold && 
                                   Mathf.Abs(pitchDegrees) < m_lookingAtCameraPitchThreshold;
            
            // Update status text
            if (m_statusText != null)
            {
                if (isLookingAtCamera)
                {
                    m_statusText.text = "Looking at you! 👀";
                    m_statusText.color = Color.green;
                }
                else
                {
                    m_statusText.text = "Not looking at camera";
                    m_statusText.color = Color.yellow;
                }
            }
            
            // Update debug text with additional information
            if (m_debugText != null)
            {
                m_gazeUpdateCount++;
                m_totalGazeTime += Time.deltaTime;
                float avgFPS = m_gazeUpdateCount / m_totalGazeTime;
                
                m_debugText.text = $"Updates: {m_gazeUpdateCount:F0} | Avg FPS: {avgFPS:F1} | " +
                                  $"Thresholds: Yaw ±{m_lookingAtCameraYawThreshold:F0}° Pitch ±{m_lookingAtCameraPitchThreshold:F0}°";
            }
            
            // Fire events
            Vector2 gazeDirection = new Vector2(yawDegrees, pitchDegrees);
            if (Vector2.Distance(gazeDirection, m_lastGazeDirection) > 0.1f) // Only fire if changed significantly
            {
                OnGazeDirectionChanged?.Invoke(gazeDirection);
                m_lastGazeDirection = gazeDirection;
            }
            
            // Fire camera gaze event if status changed
            if (isLookingAtCamera != m_wasLookingAtCamera)
            {
                OnGazeAtCameraChanged?.Invoke(isLookingAtCamera);
                m_wasLookingAtCamera = isLookingAtCamera;
            }
        }
        
        /// <summary>
        /// Update the status text
        /// </summary>
        /// <param name="status">Status message to display</param>
        public void UpdateStatus(string status)
        {
            if (m_statusText != null)
            {
                m_statusText.text = status;
                m_statusText.color = Color.white; // Reset color for status updates
            }
            
            Debug.Log($"GazeInferenceUiManager: {status}");
        }
        
        /// <summary>
        /// Clear all gaze displays
        /// </summary>
        public void ClearGazeDisplay()
        {
            if (m_gazeText != null)
            {
                m_gazeText.text = "Gaze: —";
            }
            
            if (m_statusText != null)
            {
                m_statusText.text = "No gaze data";
                m_statusText.color = Color.white;
            }
            
            if (m_debugText != null)
            {
                m_debugText.text = "Gaze detection stopped";
            }
            
            // Hide gaze indicator
            if (m_gazeIndicator != null)
            {
                m_gazeIndicator.SetActive(false);
            }
        }
        
        #endregion
        
        #region Private Functions
        
        /// <summary>
        /// Update the visual gaze indicator
        /// </summary>
        private void UpdateGazeIndicator()
        {
            if (!m_showGazeIndicator || m_gazeIndicator == null || m_displayImage == null)
                return;
            
            // Only show indicator if we have valid gaze data
            if (m_lastGazeDirection != Vector2.zero)
            {
                m_gazeIndicator.SetActive(true);
                
                // Position indicator based on gaze direction
                Vector2 indicatorPosition = CalculateIndicatorPosition(m_lastGazeDirection);
                m_gazeIndicator.transform.localPosition = indicatorPosition;
                
                // Scale indicator based on how centered the gaze is
                float distanceFromCenter = Vector2.Distance(m_lastGazeDirection, Vector2.zero);
                float maxDistance = Mathf.Max(m_lookingAtCameraYawThreshold, m_lookingAtCameraPitchThreshold);
                float scale = Mathf.Lerp(1f, 0.5f, distanceFromCenter / maxDistance);
                
                m_gazeIndicator.transform.localScale = Vector3.one * scale * m_indicatorSize;
                
                // Change color based on whether looking at camera
                bool isLookingAtCamera = Mathf.Abs(m_lastGazeDirection.x) < m_lookingAtCameraYawThreshold && 
                                       Mathf.Abs(m_lastGazeDirection.y) < m_lookingAtCameraPitchThreshold;
                
                Image indicatorImage = m_gazeIndicator.GetComponent<Image>();
                if (indicatorImage != null)
                {
                    indicatorImage.color = isLookingAtCamera ? Color.green : Color.yellow;
                }
            }
            else
            {
                m_gazeIndicator.SetActive(false);
            }
        }
        
        /// <summary>
        /// Calculate the position for the gaze indicator based on gaze direction
        /// </summary>
        /// <param name="gazeDirection">Gaze direction in degrees</param>
        /// <returns>Local position for the indicator</returns>
        private Vector2 CalculateIndicatorPosition(Vector2 gazeDirection)
        {
            if (m_displayImage == null)
                return Vector2.zero;
            
            // Get display dimensions
            RectTransform displayRect = m_displayImage.rectTransform;
            float displayWidth = displayRect.rect.width;
            float displayHeight = displayRect.rect.height;
            
            // Normalize gaze direction to -1 to 1 range
            float normalizedYaw = Mathf.Clamp(gazeDirection.x / m_lookingAtCameraYawThreshold, -1f, 1f);
            float normalizedPitch = Mathf.Clamp(gazeDirection.y / m_lookingAtCameraPitchThreshold, -1f, 1f);
            
            // Convert to display coordinates
            float x = normalizedYaw * (displayWidth * 0.3f); // Limit movement to 30% of display width
            float y = -normalizedPitch * (displayHeight * 0.3f); // Invert Y and limit to 30% of display height
            
            return new Vector2(x, y);
        }
        
        #endregion
        
        #region Editor Functions
        
        /// <summary>
        /// Create a gaze indicator GameObject if none exists
        /// </summary>
        [ContextMenu("Create Gaze Indicator")]
        private void CreateGazeIndicator()
        {
            if (m_gazeIndicator != null)
            {
                Debug.LogWarning("Gaze indicator already exists!");
                return;
            }
            
            // Create indicator GameObject
            GameObject indicator = new GameObject("GazeIndicator");
            indicator.transform.SetParent(m_displayImage.transform, false);
            
            // Add UI components
            RectTransform rectTransform = indicator.AddComponent<RectTransform>();
            Image image = indicator.AddComponent<Image>();
            
            // Configure appearance
            image.color = m_gazeIndicatorColor;
            image.sprite = CreateCircleSprite();
            
            // Set size
            rectTransform.sizeDelta = new Vector2(m_indicatorSize, m_indicatorSize);
            
            // Position at center
            rectTransform.anchoredPosition = Vector2.zero;
            
            m_gazeIndicator = indicator;
            Debug.Log("Gaze indicator created successfully!");
        }
        
        /// <summary>
        /// Create a simple circle sprite for the gaze indicator
        /// </summary>
        /// <returns>Circle sprite</returns>
        private Sprite CreateCircleSprite()
        {
            int size = 64;
            Texture2D texture = new Texture2D(size, size);
            
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = distance <= radius ? 1f : 0f;
                    
                    // Create a soft edge
                    if (distance > radius - 2f)
                    {
                        alpha = Mathf.Lerp(1f, 0f, (distance - (radius - 2f)) / 2f);
                    }
                    
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
        
        #endregion
    }
}

