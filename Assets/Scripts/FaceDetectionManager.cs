using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using PassthroughCameraSamples;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConversationAssistant
{
    /// <summary>
    /// Manages face detection, eye contact detection, and lip movement detection
    /// Currently uses simulated detection - can be upgraded with AI models later
    /// </summary>
    public class FaceDetectionManager : MonoBehaviour
    {
        
        [Header("AI Detection Settings")]
        [SerializeField] private OpenAIConfiguration openaiConfig;
        [SerializeField] private string openaiModel = "gpt-4o-mini";
        [SerializeField] private float detectionCooldown = 0.5f; // Reduced from 1.0f for faster response
        
        [Header("Real-time Settings")]
        [SerializeField] private bool enableRealTimeDetection = true;
        [SerializeField] private float realTimeUpdateRate = 0.2f; // 5 FPS for real-time
        [SerializeField] private bool enableSpeechRecognition = true;
        
        [Header("Audio Analysis")]
        [SerializeField] private QuestMicrophoneManager microphoneManager;
        [SerializeField] private float audioThreshold = 0.03f;
        
        [Header("UI Display")]
        [SerializeField] private TMPro.TextMeshProUGUI faceCountText;
        [SerializeField] private TMPro.TextMeshProUGUI conversationStatusText;
        [SerializeField] private TMPro.TextMeshProUGUI speechGuidanceText;

        [Header("Speech Recognition Debug")]
        [SerializeField] private TMPro.TextMeshProUGUI speechDebugText;

        [Header("Haptic Feedback")]
        [SerializeField] private bool enableHapticFeedback = true;
        [SerializeField] private float hapticDuration = 0.1f; // Brief vibration
        [SerializeField] private float hapticFrequency = 0.5f; // Vibration frequency
        [SerializeField] private float hapticAmplitude = 0.5f; // Vibration strength
        
        // Internal state
        private bool isInitialized = false;
        private List<DetectedFace> lastDetectedFaces = new List<DetectedFace>();
        private float lastDetectionTime = 0f;
        private Texture2D capturedImage;
        private int nextFaceId = 0;
        
        // Real-time processing
        private float lastRealTimeUpdate = 0f;
        private bool isRealTimeActive = false;
        
        // Speech recognition
        private bool isSomeoneSpeaking = false;
        private float currentAudioLevel = 0f;
        private float lastSpeechTime = 0f;

        private float lastHapticTime = 0f;
        //private float hapticCooldown = 1.0f; // Prevent spam haptics
        private bool hasTriggeredHapticForCurrentState = false;

        private enum GuidanceState { None, Wait, SpeakNow, Interject }

        [SerializeField] private int speakNowPulseCount = 2;   // exactly two buzzes
        [SerializeField] private int interjectPulseCount = 1;  // exactly one buzz
        [SerializeField] private float hapticGap = 0.08f;      // gap between pulses (seconds)

        private GuidanceState lastGuidanceState = GuidanceState.None;
        private Coroutine hapticRoutine;

        private OpenAIClient _api;
        
        // Conversation context
        private ConversationContext currentContext = new ConversationContext();
        
        #region Unity Lifecycle

        
        private void Awake() 
        {
            _api = new OpenAIClient(openaiConfig);
        }
        
        private void Start()
        {
            StartCoroutine(Initialize());
        }
        
        private void Update()
        {
            // Check for A button press (same as your other script)
            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                Debug.Log("FaceDetectionManager: A button pressed - triggering face detection");
                TriggerFaceDetection();
            }
            
            // B button toggles real-time mode
            if (OVRInput.GetDown(OVRInput.Button.Two))
            {
                ToggleRealTimeMode();
            }
            
            // Real-time processing
            if (enableRealTimeDetection && isRealTimeActive)
            {
                if (Time.time - lastRealTimeUpdate >= realTimeUpdateRate)
                {
                    lastRealTimeUpdate = Time.time;
                    ProcessRealTimeUpdate();
                }
            }
            
            // Audio level monitoring
            if (enableSpeechRecognition && microphoneManager != null)
            {
                MonitorAudioLevel();
            }
        }
        
        private void OnDestroy()
        {
            // Clean up captured image
            if (capturedImage != null)
            {
                DestroyImmediate(capturedImage);
                capturedImage = null;
            }
        }
        
        #endregion
        
        #region Initialization
        
        public IEnumerator Initialize()
        {
            Debug.Log("FaceDetectionManager: Initializing...");
            
            // Initialize AI-based detection
            yield return new WaitForSeconds(0.1f);
            
            isInitialized = true;
            Debug.Log("FaceDetectionManager: Initialization complete - using AI detection");
            
            if (faceCountText != null)
            {
                faceCountText.text = "Camera ready. Press A to detect faces.";
            }
        }
        
        #endregion
        
        #region Face Detection
        
        public void TriggerFaceDetection()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("FaceDetectionManager: Not initialized yet");
                return;
            }
            
            // Get camera texture from WebCamTextureManager
            var webcamManager = FindObjectOfType<WebCamTextureManager>();
            if (webcamManager?.WebCamTexture != null)
            {
                var faces = DetectFaces(webcamManager.WebCamTexture);
                Debug.Log($"FaceDetectionManager: Detected {faces.Count} faces");
            }
            else
            {
                Debug.LogWarning("FaceDetectionManager: No WebCamTexture available");
                if (faceCountText != null)
                {
                    faceCountText.text = "No camera available";
                }
            }
        }
        
        public List<DetectedFace> DetectFaces(Texture cameraTexture)
        {
            if (!isInitialized || cameraTexture == null)
            {
                return new List<DetectedFace>();
            }
            
            // Check cooldown to prevent spam API calls
            if (Time.time - lastDetectionTime < detectionCooldown)
            {
                Debug.Log("FaceDetectionManager: Detection on cooldown, returning cached results");
                return lastDetectedFaces;
            }
            
            try
            {
                // Capture image from camera texture
                CaptureImage(cameraTexture);
                
                // Use AI to count faces
                CountFacesWithAI();
                
                // Return cached results while AI processes
                return lastDetectedFaces;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FaceDetectionManager: Face detection failed: {e.Message}");
                return new List<DetectedFace>();
            }
        }
        
        // Removed simulated detection - now using AI
        
        #endregion
        
        #region Image Capture and AI Analysis
        
        /// <summary>
        /// Capture image from camera texture (downscaled for faster processing)
        /// </summary>
        private void CaptureImage(Texture cameraTexture)
        {
            try
            {
                if (cameraTexture is WebCamTexture webCamTexture)
                {
                    // Downscale for faster processing - 512x512 is much faster than full resolution
                    int targetWidth = 512;
                    int targetHeight = 512;
                    
                    Debug.Log($"FaceDetectionManager: Capturing downscaled image: {targetWidth}x{targetHeight}");
                    
                    if (capturedImage == null || capturedImage.width != targetWidth)
                    {
                        capturedImage = new Texture2D(targetWidth, targetHeight);
                    }
                    
                    // Use RenderTexture for efficient downscaling
                    RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
                    Graphics.Blit(webCamTexture, rt);
                    
                    // Read the downscaled pixels
                    RenderTexture.active = rt;
                    capturedImage.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                    capturedImage.Apply();
                    RenderTexture.active = null;
                    
                    // Clean up
                    RenderTexture.ReleaseTemporary(rt);
                    
                    Debug.Log($"FaceDetectionManager: Downscaled image captured successfully");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FaceDetectionManager: Error capturing image: {e.Message}");
            }
        }
        
        /// <summary>
        /// Use OpenAI to count faces in the captured image
        /// </summary>
        private async void CountFacesWithAI()
        {
            if (openaiConfig == null)
            {
                Debug.LogError("FaceDetectionManager: OpenAI configuration is missing!");
                return;
            }
            
            if (capturedImage == null)
            {
                Debug.LogError("FaceDetectionManager: No image captured to analyze!");
                return;
            }
            
            try
            {
                Debug.Log("FaceDetectionManager: Analyzing image with AI to count faces...");
                                
                // Prepare the prompt for face counting and eye contact detection
                var messages = new List<Message>();
                var systemMessage = new Message(Role.System, 
                    "Analyze the image for faces and eye contact. For each face, determine if they are looking at the camera. " +
                    "Reply with format: 'FACES:number,LOOKING:number' (e.g., 'FACES:2,LOOKING:1' means 2 faces, 1 looking at camera).");
                
                // Prepare image content
                var imageContents = new List<Content>();
                string textContent = "How many faces and how many are looking at the camera?";
                imageContents.Add(textContent);
                imageContents.Add(capturedImage);
                
                var imageMessage = new Message(Role.User, imageContents);
                messages.Add(systemMessage);
                messages.Add(imageMessage);
                
                // Send to OpenAI
                var chatRequest = new ChatRequest(messages, model: openaiModel);
                var result = await _api.ChatEndpoint.GetCompletionAsync(chatRequest);
                
                Debug.Log($"FaceDetectionManager: AI Response: {result.FirstChoice}");
                
                // Parse the response to get face count and looking count
                var (faceCount, lookingCount) = ParseFaceAndLookingCount(result.FirstChoice);
                
                // Create detected faces with eye contact information
                var detectedFaces = CreateDetectedFacesWithEyeContact(faceCount, lookingCount);
                
                // Update tracking and cache results
                UpdateFaceTracking(detectedFaces);
                lastDetectedFaces = detectedFaces;
                lastDetectionTime = Time.time;
                
                // Update UI text with face count and looking count
                if (faceCountText != null)
                {
                    string lookingText = lookingCount > 0 ? $", {lookingCount} looking" : "";
                    faceCountText.text = $"Faces: {faceCount}{lookingText}";
                }
                
                Debug.Log($"FaceDetectionManager: Detected {faceCount} faces, {lookingCount} looking at camera");
                
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FaceDetectionManager: AI analysis failed: {e.Message}");
            }
        }
        
        /// <summary>
        /// Parse face count and looking count from AI response
        /// </summary>
        private (int faceCount, int lookingCount) ParseFaceAndLookingCount(string aiResponse)
        {
            try
            {
                string cleanResponse = aiResponse.Trim();
                
                // Try to parse the structured format: "FACES:number,LOOKING:number"
                var facesMatch = Regex.Match(cleanResponse, @"FACES:(\d+)");
                var lookingMatch = Regex.Match(cleanResponse, @"LOOKING:(\d+)");
                
                if (facesMatch.Success && lookingMatch.Success)
                {
                    int faces = int.Parse(facesMatch.Groups[1].Value);
                    int looking = int.Parse(lookingMatch.Groups[1].Value);
                    return (Mathf.Max(0, faces), Mathf.Max(0, looking));
                }
                
                // Fallback: try to extract any numbers
                var numbers = Regex.Matches(cleanResponse, @"\d+");
                int numberCount = numbers.Count;
                if (numberCount >= 2)
                {
                    int faces = int.Parse(numbers[0].Value);
                    int looking = int.Parse(numbers[1].Value);
                    return (Mathf.Max(0, faces), Mathf.Max(0, looking));
                }
                else if (numberCount == 1)
                {
                    int faces = int.Parse(numbers[0].Value);
                    return (faces, 0); // Assume no one is looking if only one number
                }
                
                Debug.LogWarning($"FaceDetectionManager: Could not parse face/looking count from AI response: {aiResponse}");
                return (0, 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FaceDetectionManager: Error parsing face/looking count: {e.Message}");
                return (0, 0);
            }
        }
        
        /// <summary>
        /// Create DetectedFace objects with eye contact information
        /// </summary>
        private List<DetectedFace> CreateDetectedFacesWithEyeContact(int faceCount, int lookingCount)
        {
            var faces = new List<DetectedFace>();
            
            for (int i = 0; i < faceCount; i++)
            {
                // Determine if this face is looking at the camera
                bool isLooking = i < lookingCount;
                float eyeContactConfidence = isLooking ? 0.9f : 0.1f;
                
                var face = new DetectedFace
                {
                    Id = nextFaceId++,
                    BoundingBox = new Rect(0.2f + (i * 0.2f), 0.3f, 0.15f, 0.2f), // Simple positioning
                    Confidence = 0.8f, // High confidence from AI
                    EyeContactConfidence = eyeContactConfidence, // Based on AI detection
                    LipMovementConfidence = 0.5f, // Default value
                    Landmarks = new List<Vector2>()
                };
                
                faces.Add(face);
            }
            
            return faces;
        }
        
        #endregion
        
        #region Face Tracking
        
        private void UpdateFaceTracking(List<DetectedFace> detectedFaces)
        {
            // Update last detected faces
            lastDetectedFaces = detectedFaces;
        }
        
        #endregion
        
        // All simulation code removed - now using AI detection
        
        #region Real-time Processing
        
        private void ToggleRealTimeMode()
        {
            isRealTimeActive = !isRealTimeActive;
            
            if (isRealTimeActive)
            {
                Debug.Log("FaceDetectionManager: Real-time mode ENABLED");
                if (conversationStatusText != null)
                    conversationStatusText.text = "Real-time: ON";
            }
            else
            {
                Debug.Log("FaceDetectionManager: Real-time mode DISABLED");
                if (conversationStatusText != null)
                    conversationStatusText.text = "Real-time: OFF";
            }
        }
        
        private void ProcessRealTimeUpdate()
        {
            // ACTUALLY detect faces in real-time
            var webcamManager = FindObjectOfType<WebCamTextureManager>();
            if (webcamManager?.WebCamTexture != null)
            {
                // Do actual face detection for real-time updates
                var faces = DetectFaces(webcamManager.WebCamTexture);
        
                // Update context with fresh data
                UpdateConversationContext();
                AnalyzeConversationState();
                ProvideSpeechGuidance();
            }
        }
        #endregion
        
        #region Audio Analysis
        
        private void MonitorAudioLevel()
        {
            if (microphoneManager == null) return;
            
            // Get current audio level from microphone manager
            currentAudioLevel = microphoneManager.GetAudioLevel();
            
            // Detect if someone is speaking
            bool wasSpeaking = isSomeoneSpeaking;
            isSomeoneSpeaking = currentAudioLevel > audioThreshold;
            
            if (isSomeoneSpeaking && !wasSpeaking)
            {
                OnSpeechDetected();
            }
            else if (!isSomeoneSpeaking && wasSpeaking)
            {
                OnSpeechEnded();
            }

            // Update speech debug text
            UpdateSpeechDebugText();
        }

        // NEW: Method to update speech debug text
        private void UpdateSpeechDebugText()
        {
            if (speechDebugText == null) return;
    
            // For now, show audio level and speech state
            // You can enhance this with actual speech-to-text later
            string debugInfo = $"Audio Level: {currentAudioLevel:F3}\n";
            debugInfo += $"Speaking: {(isSomeoneSpeaking ? "YES" : "NO")}\n";
            debugInfo += $"Threshold: {audioThreshold:F3}\n";
    
            // Add some visual feedback based on audio level
            if (currentAudioLevel > audioThreshold * 2)
            {
                debugInfo += "🔊 LOUD SPEECH";
            }
            else if (currentAudioLevel > audioThreshold)
            {
                debugInfo += "🔉 Speaking";
            }
            else
            {
                debugInfo += "🔇 Silent";
            }
    
            speechDebugText.text = debugInfo;
        }
        
        private void OnSpeechDetected()
        {
            lastSpeechTime = Time.time;
            Debug.Log($"FaceDetectionManager: Speech detected (Level: {currentAudioLevel:F3})");
            
            if (conversationStatusText != null)
                conversationStatusText.text = "Someone speaking...";
        }
        
        private void OnSpeechEnded()
        {
            Debug.Log($"FaceDetectionManager: Speech ended (Level: {currentAudioLevel:F3})");
            
            if (conversationStatusText != null)
                conversationStatusText.text = "Silence";
        }
        
        #endregion
        
        #region Conversation Analysis
        
        private void UpdateConversationContext()
        {
            currentContext.FaceCount = lastDetectedFaces.Count;
            currentContext.LookingAtUser = lastDetectedFaces.Count(f => f.IsLookingAtUser);
            currentContext.IsSomeoneSpeaking = isSomeoneSpeaking;
            currentContext.AudioLevel = currentAudioLevel;
            currentContext.LastUpdateTime = Time.time;
        }
        
        private void AnalyzeConversationState()
        {
            // Determine if user should speak based on context
            bool shouldSpeak = DetermineIfUserShouldSpeak();
            
            if (shouldSpeak)
            {
                if (speechGuidanceText != null)
                    speechGuidanceText.text = "SPEAK NOW - You have the floor!";
            }
            else
            {
                if (speechGuidanceText != null)
                    speechGuidanceText.text = "Wait - Others are speaking";
            }
        }
        
        private bool DetermineIfUserShouldSpeak()
        {
            // Simple decision logic - can be enhanced with AI
            if (currentContext.IsSomeoneSpeaking)
                return false; // Someone else is talking
            
            if (currentContext.LookingAtUser > 0)
                return true; // Someone is looking at you
            
            if (currentContext.FaceCount == 0)
                return false; // No one around
            
            // If no one is speaking and no one is looking at you, it's a good time to speak
            return true;
        }

        // One physical buzz
        private void SendHapticOnce()
        {
            if (!enableHapticFeedback) return;

            // Create a very simple clip of constant amplitude for the given duration
            int sampleCount = Mathf.Max(1, (int)(hapticDuration * 1000f)); // ms-based like your original
            byte[] samples = new byte[sampleCount];
            byte level = (byte)(Mathf.Clamp01(hapticAmplitude) * 255f);

            for (int i = 0; i < samples.Length; i++) samples[i] = level;

            var clip = new OVRHapticsClip(samples, samples.Length);
            OVRHaptics.LeftChannel.Mix(clip);
            OVRHaptics.RightChannel.Mix(clip);
        }

        // Run N pulses with a small gap
        private IEnumerator HapticBurst(int pulses)
        {
            for (int i = 0; i < pulses; i++)
            {
                SendHapticOnce();
                if (i < pulses - 1) yield return new WaitForSeconds(hapticDuration + hapticGap);
            }
        }

        // Public entry: play a burst once (cancels any prior burst)
        private void PlayHaptics(int pulses)
        {
            if (!enableHapticFeedback) return;
            if (hapticRoutine != null) StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(HapticBurst(pulses));
        }
        
        private void ProvideSpeechGuidance()
        {
            string guidance;
            GuidanceState newState;

            if (currentContext.IsSomeoneSpeaking)
            {
                guidance = "Wait for others to finish";
                newState = GuidanceState.Wait;
            }
            else if (currentContext.LookingAtUser > 0)
            {
                guidance = "You have attention - speak now!";
                newState = GuidanceState.SpeakNow;
            }
            else if (currentContext.FaceCount > 0)
            {
                guidance = "Indirect attention";
                newState = GuidanceState.Interject;
            }
            else
            {
                guidance = "Currently no attention";
                newState = GuidanceState.None;
            }

            // Fire haptics ONLY when the state changes
            if (newState != lastGuidanceState)
            {
                lastGuidanceState = newState;

                switch (newState)
                {
                    case GuidanceState.SpeakNow:
                        PlayHaptics(speakNowPulseCount);   // exactly twice
                        break;
                    case GuidanceState.Interject:
                        PlayHaptics(interjectPulseCount);  // exactly once
                        break;
                    default:
                        // stop any in-flight burst if we moved to a non-haptic state
                        if (hapticRoutine != null)
                        {
                            StopCoroutine(hapticRoutine);
                            hapticRoutine = null;
                        }
                        break;
                }
            }

            if (speechGuidanceText != null)
                speechGuidanceText.text = guidance;
        }

        
        #endregion
    }
    
    /// <summary>
    /// Represents a detected face with conversation-relevant features
    /// </summary>
    [System.Serializable]
    public class DetectedFace
    {
        public int Id;
        public Rect BoundingBox;
        public float Confidence;
        public float EyeContactConfidence;
        public float LipMovementConfidence;
        public List<Vector2> Landmarks;
        
        public DetectedFace()
        {
            Landmarks = new List<Vector2>();
        }
        
        public bool IsLookingAtUser => EyeContactConfidence > 0.6f;
        public bool IsSpeaking => LipMovementConfidence > 0.4f;
        public Vector2 Center => BoundingBox.center;
        public Vector2 Size => BoundingBox.size;
    }
    
    /// <summary>
    /// Tracks conversation context for decision making
    /// </summary>
    [System.Serializable]
    public class ConversationContext
    {
        public int FaceCount;
        public int LookingAtUser;
        public bool IsSomeoneSpeaking;
        public float AudioLevel;
        public float LastUpdateTime;
        public string LastSpeechContent;
        
        public ConversationContext()
        {
            FaceCount = 0;
            LookingAtUser = 0;
            IsSomeoneSpeaking = false;
            AudioLevel = 0f;
            LastUpdateTime = 0f;
            LastSpeechContent = "";
        }
    }
}
