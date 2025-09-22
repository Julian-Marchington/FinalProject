using UnityEngine;
using System.Collections;

namespace ConversationAssistant
{
    /// <summary>
    /// Manages Quest headset microphone input for conversation analysis
    /// </summary>
    public class QuestMicrophoneManager : MonoBehaviour
    {
        [Header("Microphone Settings")]
        [SerializeField] private string microphoneDevice = "";
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int bufferLength = 256;
        [SerializeField] private float audioThreshold = 0.1f;
        
        [Header("Audio Output")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private bool playMicrophoneAudio = false; // For debugging
        
        // Internal state
        private AudioClip microphoneClip;
        private bool isMicrophoneActive = false;
        private float[] audioBuffer;
        
        // Events
        public System.Action<float> OnAudioLevelChanged;
        public System.Action<bool> OnSpeechStateChanged;
        
        private void Start()
        {
            InitializeMicrophone();
        }
        
        private void InitializeMicrophone()
        {
            Debug.Log("QuestMicrophoneManager: Initializing microphone...");
            
            // Get available microphone devices
            string[] devices = Microphone.devices;
            Debug.Log($"QuestMicrophoneManager: Found {devices.Length} microphone devices:");
            foreach (string device in devices)
            {
                Debug.Log($"  - {device}");
            }
            
            // Use first available device or specified device
            if (string.IsNullOrEmpty(microphoneDevice) && devices.Length > 0)
            {
                microphoneDevice = devices[0];
                Debug.Log($"QuestMicrophoneManager: Using default device: {microphoneDevice}");
            }
            
            if (string.IsNullOrEmpty(microphoneDevice))
            {
                Debug.LogError("QuestMicrophoneManager: No microphone device available!");
                return;
            }
            
            // Create audio clip for microphone input
            microphoneClip = Microphone.Start(microphoneDevice, true, 1, sampleRate);
            
            if (microphoneClip == null)
            {
                Debug.LogError("QuestMicrophoneManager: Failed to start microphone!");
                return;
            }
            
            // Initialize audio buffer
            audioBuffer = new float[bufferLength];
            
            // Set up audio source if provided
            if (audioSource != null)
            {
                audioSource.clip = microphoneClip;
                audioSource.loop = true;
                audioSource.playOnAwake = false;
                
                if (playMicrophoneAudio)
                {
                    audioSource.Play();
                }
            }
            
            isMicrophoneActive = true;
            Debug.Log($"QuestMicrophoneManager: Microphone initialized successfully on device: {microphoneDevice}");
            
            // Start monitoring audio levels
            StartCoroutine(MonitorAudioLevels());
        }
        
        private IEnumerator MonitorAudioLevels()
        {
            while (isMicrophoneActive)
            {
                if (Microphone.IsRecording(microphoneDevice))
                {
                    // Get current audio level
                    float audioLevel = GetCurrentAudioLevel();
                    
                    // Notify listeners
                    OnAudioLevelChanged?.Invoke(audioLevel);
                    
                    // Detect speech state changes
                    bool isSpeaking = audioLevel > audioThreshold;
                    OnSpeechStateChanged?.Invoke(isSpeaking);
                }
                
                yield return new WaitForSeconds(0.1f); // 10 FPS monitoring
            }
        }
        
        private float GetCurrentAudioLevel()
        {
            if (microphoneClip == null || !Microphone.IsRecording(microphoneDevice))
                return 0f;
            
            // Get current position in the audio clip
            int position = Microphone.GetPosition(microphoneDevice);
            if (position < 0) return 0f;
            
            // Read audio data
            microphoneClip.GetData(audioBuffer, position);
            
            // Calculate RMS (Root Mean Square) for audio level
            float sum = 0f;
            for (int i = 0; i < audioBuffer.Length; i++)
            {
                sum += audioBuffer[i] * audioBuffer[i];
            }
            
            float rms = Mathf.Sqrt(sum / audioBuffer.Length);
            return rms;
        }
        
        /// <summary>
        /// Get the current microphone audio level (0.0 to 1.0)
        /// </summary>
        public float GetAudioLevel()
        {
            return GetCurrentAudioLevel();
        }
        
        /// <summary>
        /// Check if someone is currently speaking
        /// </summary>
        public bool IsSomeoneSpeaking()
        {
            return GetCurrentAudioLevel() > audioThreshold;
        }
        
        /// <summary>
        /// Get the microphone audio source for other components
        /// </summary>
        public AudioSource GetAudioSource()
        {
            return audioSource;
        }
        
        private void OnDestroy()
        {
            if (isMicrophoneActive && !string.IsNullOrEmpty(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
                Debug.Log("QuestMicrophoneManager: Microphone stopped");
            }
        }
        
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // App going to background - pause microphone
                if (audioSource != null && audioSource.isPlaying)
                {
                    audioSource.Pause();
                }
            }
            else
            {
                // App coming to foreground - resume microphone
                if (audioSource != null && !audioSource.isPlaying && playMicrophoneAudio)
                {
                    audioSource.UnPause();
                }
            }
        }
    }
}
