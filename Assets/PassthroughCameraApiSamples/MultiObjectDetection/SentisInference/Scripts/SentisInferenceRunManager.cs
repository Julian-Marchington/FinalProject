// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.Samples;

using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceRunManager : MonoBehaviour
    {
        [Header("Gaze Detection Model Config")]
        [SerializeField] private Vector2Int m_inputSize = new(224, 224); // MobileNet2 standard input size
        [SerializeField] private Unity.InferenceEngine.BackendType m_backend = Unity.InferenceEngine.BackendType.GPUCompute; // Use GPU for better performance
        [SerializeField] private Unity.InferenceEngine.ModelAsset m_sentisModel; // YOLO model for face detection
        [SerializeField] private Unity.InferenceEngine.ModelAsset m_gazeModel; // MobileNet2 gaze model for pitch/yaw
        [SerializeField] private int m_layersPerFrame = 25;
        [SerializeField] private TextAsset m_labelsAsset; // Keep for compatibility, but won't be used for gaze
        public bool IsModelLoaded { get; private set; } = false;
        
        // Gaze detection public properties
        public Vector2 CurrentGazeDirection => new Vector2(m_currentYaw, m_currentPitch);
        public Vector2 SmoothedGazeDirection => new Vector2(m_smoothedYaw, m_smoothedPitch);
        public bool IsFaceDetectionReady => m_yoloEngine != null && m_gazeEngine != null;

        [Header("UI display references")]
        [SerializeField] private SentisInferenceUiManager m_uiInference;

        [Header("Face Detection + Gaze Analysis")]
        [SerializeField] private Vector2Int m_yoloInputSize = new(640, 640); // YOLO input size
        [SerializeField] private Vector2Int m_gazeInputSize = new(448, 448); // MobileNet2 input size
        [SerializeField] private float m_gazeSmoothingFactor = 0.7f; // Smoothing factor for gaze values (0-1)
        [SerializeField] private bool m_useRadians = false; // Output in radians (true) or degrees (false)
        [SerializeField] private float m_faceConfidenceThreshold = 0.5f; // Minimum confidence for face detection
        
        [Header("[Editor Only] Convert to Sentis")]
        public Unity.InferenceEngine.ModelAsset OnnxModel;
        [SerializeField, Range(0, 1)] private float m_iouThreshold = 0.6f;
        [SerializeField, Range(0, 1)] private float m_scoreThreshold = 0.23f;
        [Space(40)]

        private Unity.InferenceEngine.Worker m_engine;
        private IEnumerator m_schedule;
        private bool m_started = false;
        private Unity.InferenceEngine.Tensor<float> m_input;
        private Unity.InferenceEngine.Model m_model;
        private int m_download_state = 0;
        private Unity.InferenceEngine.Tensor<float> m_output;
        private Unity.InferenceEngine.Tensor<int> m_labelIDs;
        private Unity.InferenceEngine.Tensor<float> m_pullOutput;
        private Unity.InferenceEngine.Tensor<int> m_pullLabelIDs;
        private bool m_isWaiting = false;
        
        // Dual detection variables
        private float m_currentYaw = 0f;
        private float m_currentPitch = 0f;
        private float m_smoothedYaw = 0f;
        private float m_smoothedPitch = 0f;
        private bool m_isFirstGaze = true;
        
        // Face detection and gaze analysis
        private Unity.InferenceEngine.Worker m_yoloEngine; // YOLO engine for face detection
        private Unity.InferenceEngine.Worker m_gazeEngine; // Gaze engine for pitch/yaw
        private Unity.InferenceEngine.Model m_yoloModel; // YOLO model
        private Unity.InferenceEngine.Model m_gazeModelInstance; // Gaze model instance
        private List<DetectedFace> m_detectedFaces = new(); // List of detected faces
        private int m_currentFaceIndex = 0; // Index of face currently being analyzed

        #region Unity Functions
        private IEnumerator Start()
        {
            // Wait for the UI to be ready because when Sentis load the model it will block the main thread.
            yield return new WaitForSeconds(0.05f);

            m_uiInference.SetLabels(m_labelsAsset);
            LoadModel();
        }

        private void Update()
        {
            InferenceUpdate();
        }

        private void OnDestroy()
        {
            if (m_schedule != null)
            {
                StopCoroutine(m_schedule);
            }
            m_input?.Dispose();
            m_engine?.Dispose();
            
            // Clean up dual detection engines
            m_yoloEngine?.Dispose();
            m_gazeEngine?.Dispose();
        }
        #endregion

        #region Public Functions
        public void RunInference(Texture targetTexture)
        {
            // If the inference is not running prepare the input
            if (!m_started)
            {
                // clean last input
                m_input?.Dispose();
                // check if we have a texture from the camera
                if (!targetTexture)
                {
                    return;
                }
                // Update Capture data
                m_uiInference.SetDetectionCapture(targetTexture);
                // Convert the texture to a Tensor and schedule the inference
                // Use YOLO input size for face detection
                m_input = Unity.InferenceEngine.TextureConverter.ToTensor(targetTexture, m_yoloInputSize.x, m_yoloInputSize.y, 3);
                
                // Always use YOLO engine for face detection
                m_schedule = m_yoloEngine.ScheduleIterable(m_input);
                
                m_download_state = 0;
                m_started = true;
            }
        }

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

        #region Inference Functions
        private void LoadModel()
        {
            Debug.Log("SentisInferenceRunManager: Starting model loading process...");
            
            try
            {
                // Check if YOLO model is assigned
                if (m_sentisModel == null)
                {
                    Debug.LogError("SentisInferenceRunManager: No YOLO model assigned! Please assign your YOLO ONNX model.");
                    return;
                }
                Debug.Log($"SentisInferenceRunManager: YOLO model found: {m_sentisModel.name}");
                
                // Check if gaze model is assigned
                if (m_gazeModel == null)
                {
                    Debug.LogError("SentisInferenceRunManager: No gaze model assigned! Please assign your MobileNet2 ONNX model.");
                    return;
                }
                Debug.Log($"SentisInferenceRunManager: Gaze model found: {m_gazeModel.name}");
                
                Debug.Log("SentisInferenceRunManager: Loading YOLO model...");
                // Load the YOLO model for face detection
                m_yoloModel = Unity.InferenceEngine.ModelLoader.Load(m_sentisModel);
                Debug.Log($"SentisInferenceRunManager: YOLO face detection model loaded successfully");
                
                Debug.Log("SentisInferenceRunManager: Loading gaze model...");
                // Load the gaze model for pitch/yaw calculation
                m_gazeModelInstance = Unity.InferenceEngine.ModelLoader.Load(m_gazeModel);
                Debug.Log($"SentisInferenceRunManager: MobileNet2 gaze model loaded successfully");
                
                Debug.Log("SentisInferenceRunManager: Creating YOLO engine...");
                // Create YOLO engine for face detection
                m_yoloEngine = new Unity.InferenceEngine.Worker(m_yoloModel, m_backend);
                Debug.Log($"SentisInferenceRunManager: YOLO engine created with backend: {m_backend}");
                
                Debug.Log("SentisInferenceRunManager: Creating gaze engine...");
                // Create gaze engine for pitch/yaw calculation
                m_gazeEngine = new Unity.InferenceEngine.Worker(m_gazeModelInstance, Unity.InferenceEngine.BackendType.GPUCompute); // Use GPU for gaze
                Debug.Log("SentisInferenceRunManager: Gaze engine created with GPU backend");
                
                Debug.Log("SentisInferenceRunManager: Running test inference to warm up models...");
                // Run a test inference to warm up both models
                // Test YOLO with its expected input size (640x640)
                var yoloTestInput = Unity.InferenceEngine.TextureConverter.ToTensor(new Texture2D(m_yoloInputSize.x, m_yoloInputSize.y), m_yoloInputSize.x, m_yoloInputSize.y, 3);
                m_yoloEngine.Schedule(yoloTestInput);
                
                // Test gaze model with its expected input size (448x448)
                var gazeTestInput = Unity.InferenceEngine.TextureConverter.ToTensor(new Texture2D(m_gazeInputSize.x, m_gazeInputSize.y), m_gazeInputSize.x, m_gazeInputSize.y, 3);
                m_gazeEngine.Schedule(gazeTestInput);
                
                IsModelLoaded = true;
                Debug.Log($"SentisInferenceRunManager: Dual detection system ready - YOLO for faces, MobileNet2 for gaze");
            }
            catch (Exception e)
            {
                Debug.LogError($"SentisInferenceRunManager: Failed to load models: {e.Message}");
                Debug.LogError($"SentisInferenceRunManager: Stack trace: {e.StackTrace}");
            }
        }

        private void InferenceUpdate()
        {
            // Run the inference layer by layer to not block the main thread.
            if (m_started)
            {
                try
                {
                    if (m_download_state == 0)
                    {
                        var it = 0;
                        while (m_schedule.MoveNext())
                        {
                            if (++it % m_layersPerFrame == 0)
                                return;
                        }
                        m_download_state = 1;
                    }
                    else
                    {
                        // Get the result once all layers are processed
                        GetInferencesResults();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Sentis error: {e.Message}");
                }
            }
        }

        private void PollRequestOuput()
        {
            // Get the output 0 (coordinates data) from the model output using Sentis pull request.
            m_pullOutput = m_engine.PeekOutput(0) as Unity.InferenceEngine.Tensor<float>;
            if (m_pullOutput.dataOnBackend != null)
            {
                m_pullOutput.ReadbackRequest();
                m_isWaiting = true;
            }
            else
            {
                Debug.LogError("Sentis: No data output m_output");
                m_download_state = 4;
            }
        }

        private void PollRequestLabelIDs()
        {
            // Get the output 1 (labels ID data) from the model output using Sentis pull request.
            m_pullLabelIDs = m_engine.PeekOutput(1) as Unity.InferenceEngine.Tensor<int>;
            if (m_pullLabelIDs.dataOnBackend != null)
            {
                m_pullLabelIDs.ReadbackRequest();
                m_isWaiting = true;
            }
            else
            {
                Debug.LogError("Sentis: No data output m_labelIDs");
                m_download_state = 4;
            }
        }

        private void GetInferencesResults()
        {
            // Get the different outputs in diferent frames to not block the main thread.
            switch (m_download_state)
            {
                case 1:
                    if (!m_isWaiting)
                    {
                        PollRequestOuput();
                    }
                    else
                    {
                        if (m_pullOutput.IsReadbackRequestDone())
                        {
                            m_output = m_pullOutput.ReadbackAndClone();
                            m_isWaiting = false;

                            if (m_output.shape[0] > 0)
                            {
                                Debug.Log("Sentis: m_output ready");
                                m_download_state = 2;
                            }
                            else
                            {
                                Debug.LogError("Sentis: m_output empty");
                                m_download_state = 4;
                            }
                        }
                    }
                    break;
                case 2:
                    if (!m_isWaiting)
                    {
                        PollRequestLabelIDs();
                    }
                    else
                    {
                        if (m_pullLabelIDs.IsReadbackRequestDone())
                        {
                            m_labelIDs = m_pullLabelIDs.ReadbackAndClone();
                            m_isWaiting = false;

                            if (m_labelIDs.shape[0] > 0)
                            {
                                Debug.Log("Sentis: m_labelIDs ready");
                                m_download_state = 3;
                            }
                            else
                            {
                                Debug.LogError("Sentis: m_labelIDs empty");
                                m_download_state = 4;
                            }
                        }
                    }
                    break;
                case 3:
                    // Always process YOLO face detection results
                    ProcessYoloFaceDetection();
                    m_download_state = 5;
                    break;
                case 4:
                    m_uiInference.OnObjectDetectionError();
                    m_download_state = 5;
                    break;
                case 5:
                    m_download_state++;
                    m_started = false;
                    m_output?.Dispose();
                    m_labelIDs?.Dispose();
                    break;
            }
        }
        /// <summary>
        /// Process YOLO face detection results and prepare for gaze analysis
        /// </summary>
        private void ProcessYoloFaceDetection()
        {
            try
            {
                // Get the YOLO output tensor
                var yoloOutput = m_yoloEngine.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                
                if (yoloOutput != null && yoloOutput.shape[0] > 0)
                {
                    // Download the results
                    float[] yoloData = yoloOutput.DownloadToArray();
                    
                    // Parse YOLO output to find faces
                    m_detectedFaces.Clear();
                    ParseYoloOutput(yoloData);
                    
                    // Draw face bounding boxes
                    if (m_uiInference != null)
                    {
                        DrawFaceBoundingBoxes();
                    }
                    
                    // If we have faces, always analyze gaze for the first face
                    if (m_detectedFaces.Count > 0)
                    {
                        m_currentFaceIndex = 0;
                        AnalyzeFaceGaze();
                    }
                    
                    Debug.Log($"YOLO detected {m_detectedFaces.Count} faces");
                }
                else
                {
                    Debug.LogWarning("No valid YOLO output from model");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing YOLO results: {e.Message}");
            }
        }
        
        /// <summary>
        /// Parse YOLO output to extract face detections
        /// </summary>
        /// <param name="yoloData">Raw YOLO output data</param>
        private void ParseYoloOutput(float[] yoloData)
        {
            // YOLO output format: [x, y, w, h, confidence, class_scores...]
            int stride = 5 + 80; // 5 for bbox + 80 for COCO classes (adjust if using different dataset)
            
            for (int i = 0; i < yoloData.Length; i += stride)
            {
                if (i + 4 >= yoloData.Length) break;
                
                float confidence = yoloData[i + 4];
                if (confidence < m_faceConfidenceThreshold) continue;
                
                // Find the class with highest score
                int bestClass = 0;
                float bestScore = 0;
                for (int j = 5; j < 5 + 80; j++)
                {
                    if (i + j < yoloData.Length && yoloData[i + j] > bestScore)
                    {
                        bestScore = yoloData[i + j];
                        bestClass = j - 5;
                    }
                }
                
                // Check if this is a person class (class 0 in COCO) - we'll use this for face detection
                if (bestClass == 0 && bestScore > m_faceConfidenceThreshold)
                {
                    DetectedFace face = new DetectedFace
                    {
                        center = new Vector2(yoloData[i], yoloData[i + 1]),
                        size = new Vector2(yoloData[i + 2], yoloData[i + 3]),
                        confidence = confidence,
                        classId = bestClass,
                        hasGazeData = false
                    };
                    
                    m_detectedFaces.Add(face);
                }
            }
        }
        
        /// <summary>
        /// Draw face bounding boxes on the UI
        /// </summary>
        private void DrawFaceBoundingBoxes()
        {
            // This will be implemented in the UI manager
            // For now, we'll use the existing DrawUIBoxes method
            // but we'll need to convert our DetectedFace data to the expected format
        }
        
        /// <summary>
        /// Analyze gaze for a detected face
        /// </summary>
        private void AnalyzeFaceGaze()
        {
            if (m_currentFaceIndex >= m_detectedFaces.Count) return;
            
            DetectedFace face = m_detectedFaces[m_currentFaceIndex];
            
            // Crop the face from the original texture
            Texture2D faceTexture = CropFaceFromTexture(face);
            face.faceTexture = faceTexture;
            
            // Run gaze inference on the face
            if (faceTexture != null)
            {
                // Convert face texture to tensor for gaze model
                var faceTensor = Unity.InferenceEngine.TextureConverter.ToTensor(faceTexture, m_gazeInputSize.x, m_gazeInputSize.y, 3); // Use configured gaze input size
                
                // Schedule gaze inference
                m_gazeEngine.Schedule(faceTensor);
                
                Debug.Log($"Analyzing gaze for face {m_currentFaceIndex + 1}/{m_detectedFaces.Count}");
            }
        }
        
        /// <summary>
        /// Crop face region from the original camera texture
        /// </summary>
        /// <param name="face">Detected face data</param>
        /// <returns>Cropped face texture</returns>
        private Texture2D CropFaceFromTexture(DetectedFace face)
        {
            // This is a simplified implementation
            // In practice, you'd want to properly crop the texture based on face coordinates
            // For now, we'll return a default texture
            // TODO: Implement proper face cropping based on bounding box coordinates
            return new Texture2D(224, 224); // Return empty texture for now
        }
        
        /// <summary>
        /// Process gaze detection results (MobileNet2 typically outputs [yaw, pitch])
        /// </summary>
        private void ProcessGazeResults()
        {
            try
            {
                // Get the output tensor from the model
                var gazeOutput = m_engine.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                
                if (gazeOutput != null && gazeOutput.shape[0] > 0)
                {
                    // Download the results
                    float[] gazeData = gazeOutput.DownloadToArray();
                    
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
                            m_smoothedYaw = Mathf.Lerp(m_smoothedYaw, rawYaw, 1f - m_gazeSmoothingFactor);
                            m_smoothedPitch = Mathf.Lerp(m_smoothedPitch, rawPitch, 1f - m_gazeSmoothingFactor);
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
                        
                        Debug.Log($"Gaze detected - Yaw: {yawDisplay:F2}{units}, Pitch: {pitchDisplay:F2}{units}");
                    }
                    else
                    {
                        Debug.LogWarning($"Unexpected gaze output size: {gazeData.Length}. Expected at least 2 values.");
                    }
                }
                else
                {
                    Debug.LogWarning("No valid gaze output from model");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing gaze results: {e.Message}");
            }
        }
        
        // Face detection data structure
        [System.Serializable]
        public struct DetectedFace
        {
            public Vector2 center; // Center of face in image coordinates
            public Vector2 size; // Width and height of face
            public float confidence; // Detection confidence
            public int classId; // Class ID (should be "face" class)
            public Texture2D faceTexture; // Cropped face texture for gaze analysis
            public Vector2 gazeDirection; // Calculated gaze direction
            public bool hasGazeData; // Whether gaze has been calculated for this face
        }
        
        #endregion
    }
}
