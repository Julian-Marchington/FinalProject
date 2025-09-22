// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using Meta.XR.Samples;

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-GazeDetection")]
    public class GazeInferenceManager : MonoBehaviour
    {
        [Header("Gaze Model Configuration")]
        [SerializeField] private Vector2Int m_inputSize = new(224, 224); // MobileNet2 standard input size
        [SerializeField] private Unity.InferenceEngine.BackendType m_backend = Unity.InferenceEngine.BackendType.GPUCompute; // Use GPU for better performance
        [SerializeField] private Unity.InferenceEngine.ModelAsset m_gazeModel; // Your MobileNet2 gaze ONNX model
        [SerializeField] private int m_layersPerFrame = 25; // Process 25 layers per frame to prevent blocking
        
        [Header("Gaze Detection Settings")]
        [SerializeField] private float m_updateInterval = 0.1f; // Update gaze every 100ms
        [SerializeField] private float m_smoothingFactor = 0.7f; // Smoothing factor for gaze values (0-1)
        [SerializeField] private bool m_useRadians = false; // Output in radians (true) or degrees (false)
        
        [Header("UI Display References")]
        [SerializeField] private GazeInferenceUiManager m_uiInference;
        
        [Header("Gaze Results")]
        [SerializeField] private float m_currentYaw = 0f; // Left/Right gaze direction
        [SerializeField] private float m_currentPitch = 0f; // Up/Down gaze direction
        [SerializeField] private float m_smoothedYaw = 0f; // Smoothed yaw value
        [SerializeField] private float m_smoothedPitch = 0f; // Smoothed pitch value
        
        // Public properties for external access
        public bool IsModelLoaded { get; private set; } = false;
        public Vector2 CurrentGazeDirection => new Vector2(m_currentYaw, m_currentPitch);
        public Vector2 SmoothedGazeDirection => new Vector2(m_smoothedYaw, m_smoothedPitch);
        
        // Private variables
        private Unity.InferenceEngine.Worker m_engine;
        private IEnumerator m_schedule;
        private bool m_started = false;
        private Unity.InferenceEngine.Tensor<float> m_input;
        private Unity.InferenceEngine.Model m_model;
        private int m_download_state = 0;
        private Unity.InferenceEngine.Tensor<float> m_output;
        private bool m_isWaiting = false;
        private float m_lastUpdateTime = 0f;
        
        // Gaze smoothing variables
        private bool m_isFirstGaze = true;
        private Vector2 m_previousGaze = Vector2.zero;
        
        #region Unity Functions
        
        private IEnumerator Start()
        {
            // Wait for the UI to be ready because when Sentis loads the model it will block the main thread
            yield return new WaitForSeconds(0.05f);
            
            // Initialize UI if available
            if (m_uiInference != null)
            {
                m_uiInference.InitializeGazeDisplay();
            }
            
            // Load the gaze detection model
            LoadModel();
        }
        
        private void Update()
        {
            // Only update gaze at specified intervals to maintain performance
            if (Time.time - m_lastUpdateTime >= m_updateInterval)
            {
                m_lastUpdateTime = Time.time;
                InferenceUpdate();
            }
        }
        
        private void OnDestroy()
        {
            // Clean up resources
            if (m_schedule != null)
            {
                StopCoroutine(m_schedule);
            }
            m_input?.Dispose();
            m_output?.Dispose();
            m_engine?.Dispose();
        }
        
        #endregion
        
        #region Public Functions
        
        /// <summary>
        /// Start gaze detection inference on the provided texture
        /// </summary>
        /// <param name="targetTexture">Camera texture to analyze for gaze</param>
        public void RunInference(Texture targetTexture)
        {
            // Only start new inference if not already running
            if (!m_started)
            {
                // Clean up previous input
                m_input?.Dispose();
                
                // Check if we have a valid texture
                if (!targetTexture)
                {
                    Debug.LogWarning("GazeInferenceManager: No valid texture provided for inference");
                    return;
                }
                
                // Update UI with current camera feed
                if (m_uiInference != null)
                {
                    m_uiInference.SetGazeCapture(targetTexture);
                }
                
                // Convert texture to tensor and schedule inference
                // Note: MobileNet2 expects 224x224x3 input
                m_input = Unity.InferenceEngine.TextureConverter.ToTensor(targetTexture, m_inputSize.x, m_inputSize.y, 3);
                m_schedule = m_engine.ScheduleIterable(m_input);
                m_download_state = 0;
                m_started = true;
                
                Debug.Log($"GazeInferenceManager: Started inference on {targetTexture.width}x{targetTexture.height} texture");
            }
        }
        
        /// <summary>
        /// Check if gaze inference is currently running
        /// </summary>
        /// <returns>True if inference is active</returns>
        public bool IsRunning()
        {
            return m_started;
        }
        
        /// <summary>
        /// Get the current gaze direction in the specified units
        /// </summary>
        /// <param name="useRadians">True for radians, false for degrees</param>
        /// <returns>Gaze direction as Vector2 (x=yaw, y=pitch)</returns>
        public Vector2 GetGazeDirection(bool useRadians = false)
        {
            if (useRadians)
            {
                return m_useRadians ? SmoothedGazeDirection : SmoothedGazeDirection * Mathf.Deg2Rad;
            }
            else
            {
                return m_useRadians ? SmoothedGazeDirection * Mathf.Rad2Deg : SmoothedGazeDirection;
            }
        }
        
        /// <summary>
        /// Check if the person is looking at the camera (gaze is centered)
        /// </summary>
        /// <param name="yawThreshold">Maximum yaw deviation in degrees (default: 15°)</param>
        /// <param name="pitchThreshold">Maximum pitch deviation in degrees (default: 10°)</param>
        /// <returns>True if looking at camera</returns>
        public bool IsLookingAtCamera(float yawThreshold = 15f, float pitchThreshold = 10f)
        {
            Vector2 gaze = GetGazeDirection(false); // Get in degrees
            return Mathf.Abs(gaze.x) < yawThreshold && Mathf.Abs(gaze.y) < pitchThreshold;
        }
        
        #endregion
        
        #region Private Functions
        
        /// <summary>
        /// Load the MobileNet2 gaze detection model
        /// </summary>
        private void LoadModel()
        {
            try
            {
                // Check if model is assigned
                if (m_gazeModel == null)
                {
                    Debug.LogError("GazeInferenceManager: No gaze model assigned! Please assign your MobileNet2 ONNX model.");
                    return;
                }
                
                // Load the ONNX model
                m_model = Unity.InferenceEngine.ModelLoader.Load(m_gazeModel);
                Debug.Log($"GazeInferenceManager: MobileNet2 gaze model loaded successfully");
                
                // Create inference engine
                m_engine = new Unity.InferenceEngine.Worker(m_model, m_backend);
                Debug.Log($"GazeInferenceManager: Inference engine created with backend: {m_backend}");
                
                // Run a test inference to warm up the model and prevent blocking later
                var testInput = Unity.InferenceEngine.TextureConverter.ToTensor(new Texture2D(m_inputSize.x, m_inputSize.y), m_inputSize.x, m_inputSize.y, 3);
                m_engine.Schedule(testInput);
                
                // Mark model as loaded
                IsModelLoaded = true;
                Debug.Log($"GazeInferenceManager: Model initialization complete. Input size: {m_inputSize.x}x{m_inputSize.y}");
                
                // Update UI status
                if (m_uiInference != null)
                {
                    m_uiInference.UpdateStatus("Gaze model loaded successfully");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GazeInferenceManager: Failed to load model: {e.Message}");
                if (m_uiInference != null)
                {
                    m_uiInference.UpdateStatus($"Model load failed: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Update inference processing - runs layer by layer to prevent blocking
        /// </summary>
        private void InferenceUpdate()
        {
            if (!m_started) return;
            
            try
            {
                if (m_download_state == 0)
                {
                    // Process inference layers
                    var layersProcessed = 0;
                    while (m_schedule.MoveNext())
                    {
                        layersProcessed++;
                        if (layersProcessed % m_layersPerFrame == 0)
                        {
                            // Yield control back to main thread after processing specified number of layers
                            return;
                        }
                    }
                    
                    // All layers processed, move to download state
                    m_download_state = 1;
                    Debug.Log("GazeInferenceManager: Inference processing complete, downloading results...");
                }
                else if (m_download_state == 1)
                {
                    // Download and process results
                    GetGazeResults();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"GazeInferenceManager: Inference error: {e.Message}");
                m_download_state = 0;
                m_started = false;
                
                if (m_uiInference != null)
                {
                    m_uiInference.UpdateStatus($"Inference error: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Download and process gaze detection results
        /// </summary>
        private void GetGazeResults()
        {
            try
            {
                // Get the output tensor from the model
                m_output = m_engine.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                
                if (m_output != null && m_output.shape[0] > 0)
                {
                    // Download the results
                    float[] gazeData = m_output.DownloadToArray();
                    
                    // Process gaze data (MobileNet2 typically outputs [yaw, pitch])
                    if (gazeData.Length >= 2)
                    {
                        // Extract yaw and pitch values
                        float rawYaw = gazeData[0];
                        float rawPitch = gazeData[1];
                        
                        // Apply smoothing if this isn't the first reading
                        if (m_isFirstGaze)
                        {
                            m_currentYaw = rawYaw;
                            m_currentPitch = rawPitch;
                            m_smoothedYaw = rawYaw;
                            m_smoothedPitch = rawPitch;
                            m_isFirstGaze = false;
                        }
                        else
                        {
                            // Update current values
                            m_currentYaw = rawYaw;
                            m_currentPitch = rawPitch;
                            
                            // Apply exponential moving average smoothing
                            m_smoothedYaw = Mathf.Lerp(m_smoothedYaw, rawYaw, 1f - m_smoothingFactor);
                            m_smoothedPitch = Mathf.Lerp(m_smoothedPitch, rawPitch, 1f - m_smoothingFactor);
                        }
                        
                        // Update UI with gaze results
                        if (m_uiInference != null)
                        {
                            m_uiInference.UpdateGazeDisplay(m_smoothedYaw, m_smoothedPitch, m_useRadians);
                        }
                        
                        // Log results for debugging
                        string units = m_useRadians ? "rad" : "deg";
                        float yawDisplay = m_useRadians ? m_smoothedYaw : m_smoothedYaw * Mathf.Rad2Deg;
                        float pitchDisplay = m_useRadians ? m_smoothedPitch : m_smoothedPitch * Mathf.Rad2Deg;
                        
                        Debug.Log($"GazeInferenceManager: Gaze detected - Yaw: {yawDisplay:F2}{units}, Pitch: {pitchDisplay:F2}{units}");
                    }
                    else
                    {
                        Debug.LogWarning($"GazeInferenceManager: Unexpected output size: {gazeData.Length}. Expected at least 2 values.");
                    }
                }
                else
                {
                    Debug.LogWarning("GazeInferenceManager: No valid output from model");
                }
                
                // Reset for next inference
                m_download_state = 0;
                m_started = false;
                m_output?.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"GazeInferenceManager: Error processing results: {e.Message}");
                m_download_state = 0;
                m_started = false;
                
                if (m_uiInference != null)
                {
                    m_uiInference.UpdateStatus($"Result processing error: {e.Message}");
                }
            }
        }
        
        #endregion
    }
}

