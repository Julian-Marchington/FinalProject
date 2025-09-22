using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using PassthroughCameraSamples;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ConversationAssistant
{
    /// <summary>
    /// Combines passthrough camera feed with AI analysis to describe what the user is looking at
    /// </summary>
    public class PassthroughCameraDescription : MonoBehaviour
    {
        [Header("Camera Components")]
        [SerializeField] private WebCamTextureManager webcamManager;
        [SerializeField] private TMPro.TextMeshProUGUI resultText;
        
        [Header("OpenAI Configuration")]
        [SerializeField] private OpenAIConfiguration config;
        
        [Header("Settings")]
        [SerializeField] private bool submitOnStart = false;
        
        // Internal state
        private Texture2D picture;
        private bool isInitialized = false;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            if (submitOnStart)
            {
                StartCoroutine(InitializeSystem());
            }
        }
        
        private void OnDestroy()
        {
            if (picture != null)
            {
                DestroyImmediate(picture);
                picture = null;
            }
        }
        
        #endregion
        
        #region Initialization
        
        private IEnumerator InitializeSystem()
        {
            Debug.Log("PassthroughCameraDescription: Initializing system...");
            
            // Wait for webcam manager to be ready
            yield return StartCoroutine(WaitForWebcamReady());
            
            if (webcamManager != null && webcamManager.WebCamTexture != null)
            {
                Debug.Log("PassthroughCameraDescription: Webcam ready, taking picture...");
                TakePicture();
                yield return new WaitForEndOfFrame();
                SubmitImage();
            }
            else
            {
                Debug.LogWarning("PassthroughCameraDescription: Webcam not available, using test image...");
                SubmitTestImage();
            }
        }
        
        private IEnumerator WaitForWebcamReady()
        {
            if (webcamManager == null)
            {
                Debug.LogError("PassthroughCameraDescription: WebCamTextureManager is null!");
                yield break;
            }
            
            // Wait for webcam texture to be available
            float timeout = 10f;
            float elapsed = 0f;
            
            while (webcamManager.WebCamTexture == null && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            
            if (webcamManager.WebCamTexture == null)
            {
                Debug.LogWarning("PassthroughCameraDescription: Webcam texture not ready after timeout");
            }
        }
        
        #endregion
        
        #region Image Capture
        
        private void TakePicture()
        {
            if (webcamManager?.WebCamTexture == null)
            {
                Debug.LogError("PassthroughCameraDescription: Cannot take picture - webcam not available");
                return;
            }
            
            try
            {
                var webCamTexture = webcamManager.WebCamTexture;
                
                if (webCamTexture.width <= 16 || webCamTexture.height <= 16)
                {
                    Debug.LogWarning("PassthroughCameraDescription: Webcam texture too small, skipping capture");
                    return;
                }
                
                // Create texture if needed
                if (picture == null || picture.width != webCamTexture.width || picture.height != webCamTexture.height)
                {
                    if (picture != null)
                    {
                        DestroyImmediate(picture);
                    }
                    picture = new Texture2D(webCamTexture.width, webCamTexture.height);
                }
                
                // Capture pixels
                Color32[] pixels = webCamTexture.GetPixels32();
                picture.SetPixels32(pixels);
                picture.Apply();
                
                Debug.Log($"PassthroughCameraDescription: Picture captured: {picture.width}x{picture.height}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"PassthroughCameraDescription: Error taking picture: {e.Message}");
            }
        }
        
        #endregion
        
        #region AI Analysis
        
        private async void SubmitImage()
        {
            if (picture == null)
            {
                Debug.LogError("PassthroughCameraDescription: Cannot submit - no picture available");
                return;
            }
            
            if (config == null)
            {
                Debug.LogError("PassthroughCameraDescription: Cannot submit - OpenAI configuration missing");
                return;
            }
            
            StartCoroutine(SubmitImageCoroutine());
        }
        
        private void SubmitTestImage()
        {
            if (config == null)
            {
                Debug.LogError("PassthroughCameraDescription: Cannot submit test - OpenAI configuration missing");
                return;
            }
            
            Debug.Log("PassthroughCameraDescription: Submitting test image...");
            StartCoroutine(SubmitTestImageCoroutine());
        }
        
        private IEnumerator SubmitImageCoroutine()
        {
            SetResultText("Analyzing image...");
            
            var api = new OpenAIClient(config);
            
            var messages = new List<Message>();
            messages.Add(new Message(Role.System, 
                "You are a helpful assistant that describes what you see in images. " +
                "Provide a clear, concise description of the main objects, people, or scene visible in the image."));
            
            var contents = new List<Content>();
            contents.Add("Please describe what you see in this image.");
            contents.Add(picture);
            
            messages.Add(new Message(Role.User, contents));
            
            var request = new ChatRequest(messages, model: "gpt-4o-mini");
            var task = api.ChatEndpoint.GetCompletionAsync(request);
            
            // Wait for the async task to complete
            while (!task.IsCompleted)
            {
                yield return null;
            }
            
            if (task.Exception != null)
            {
                var errorMsg = $"Error analyzing image: {task.Exception.Message}";
                SetResultText(errorMsg);
                Debug.LogError($"PassthroughCameraDescription: {errorMsg}");
                yield break;
            }
            
            var result = task.Result;
            var description = result.FirstChoice.Message.ToString();
            SetResultText(description);
            
            Debug.Log($"PassthroughCameraDescription: AI response: {description}");
        }
        
        private IEnumerator SubmitTestImageCoroutine()
        {
            SetResultText("Testing AI connection...");
            
            var api = new OpenAIClient(config);
            
            var messages = new List<Message>();
            messages.Add(new Message(Role.System, 
                "You are a helpful assistant. Respond with a simple greeting."));
            messages.Add(new Message(Role.User, "Hello!"));
            
            var request = new ChatRequest(messages, model: "gpt-4o-mini");
            var task = api.ChatEndpoint.GetCompletionAsync(request);
            
            // Wait for the async task to complete
            while (!task.IsCompleted)
            {
                yield return null;
            }
            
            if (task.Exception != null)
            {
                var errorMsg = $"Test failed: {task.Exception.Message}";
                SetResultText(errorMsg);
                Debug.LogError($"PassthroughCameraDescription: {errorMsg}");
                yield break;
            }
            
            var result = task.Result;
            var response = result.FirstChoice.Message.ToString();
            SetResultText($"Test successful: {response}");
            
            Debug.Log($"PassthroughCameraDescription: Test response: {response}");
        }
        
        #endregion
        
        #region UI Updates
        
        private void SetResultText(string text)
        {
            if (resultText != null)
            {
                resultText.text = text;
            }
            else
            {
                Debug.LogWarning("PassthroughCameraDescription: Result text component not assigned");
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Manually trigger image capture and analysis
        /// </summary>
        public void ManualSubmit()
        {
            if (webcamManager?.WebCamTexture != null)
            {
                TakePicture();
                SubmitImage();
            }
            else
            {
                Debug.LogWarning("PassthroughCameraDescription: Cannot submit - webcam not available");
            }
        }
        
        #endregion
    }
}
