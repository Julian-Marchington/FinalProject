using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace ConversationAssistant
{
    /// <summary>
    /// Manages audio cues and spatial audio feedback for conversation assistance
    /// Provides directional audio cues to help visually impaired users understand conversation dynamics
    /// </summary>
    public class AudioCueManager : MonoBehaviour
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource leftEarSource;
        [SerializeField] private AudioSource rightEarSource;
        [SerializeField] private AudioSource centerSource;
        
        [Header("Audio Cues")]
        [SerializeField] private AudioClip attentionCue;
        [SerializeField] private AudioClip speakingCue;
        [SerializeField] private AudioClip confidenceCue;
        [SerializeField] private AudioClip directionCue;
        
        [Header("Audio Settings")]
        [SerializeField] private float baseVolume = 0.7f;
        [SerializeField] private float spatialBlend = 1.0f; // 0 = 2D, 1 = 3D
        [SerializeField] private float minDistance = 0.5f;
        [SerializeField] private float maxDistance = 10.0f;
        
        [Header("Cue Timing")]
        [SerializeField] private float minCueInterval = 0.5f; // Minimum time between cues
        [SerializeField] private float cueFadeInTime = 0.1f;
        [SerializeField] private float cueFadeOutTime = 0.2f;
        
        // Internal state
        private bool isInitialized = false;
        private Dictionary<AudioClip, float> lastCueTimes = new Dictionary<AudioClip, float>();
        private ConcurrentQueue<AudioCueRequest> cueQueue = new ConcurrentQueue<AudioCueRequest>();
        private Coroutine cueProcessor;
        
        // Events
        public System.Action<AudioClip> OnCuePlayed;
        public System.Action<string> OnCueError;
        
        #region Unity Lifecycle
        
        private void OnDestroy()
        {
            StopAllCoroutines();
        }
        
        #endregion
        
        #region Initialization
        
        public IEnumerator Initialize()
        {
            Debug.Log("AudioCueManager: Initializing...");
            
            // Setup audio sources if not provided
            yield return StartCoroutine(SetupAudioSources());
            
            // Initialize cue timing
            InitializeCueTiming();
            
            // Start cue processor
            cueProcessor = StartCoroutine(ProcessCueQueue());
            
            isInitialized = true;
            Debug.Log("AudioCueManager: Initialization complete");
        }
        
        private IEnumerator SetupAudioSources()
        {
            // Create audio sources if they don't exist
            if (leftEarSource == null)
            {
                leftEarSource = CreateAudioSource("LeftEarSource", Vector3.left * 0.1f);
            }
            
            if (rightEarSource == null)
            {
                rightEarSource = CreateAudioSource("RightEarSource", Vector3.right * 0.1f);
            }
            
            if (centerSource == null)
            {
                centerSource = CreateAudioSource("CenterSource", Vector3.zero);
            }
            
            // Configure audio sources
            ConfigureAudioSource(leftEarSource);
            ConfigureAudioSource(rightEarSource);
            ConfigureAudioSource(centerSource);
            
            yield return new WaitForSeconds(0.1f);
        }
        
        private AudioSource CreateAudioSource(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            go.transform.localPosition = position;
            
            var audioSource = go.AddComponent<AudioSource>();
            return audioSource;
        }
        
        private void ConfigureAudioSource(AudioSource source)
        {
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.volume = baseVolume;
            source.playOnAwake = false;
            source.loop = false;
        }
        
        private void InitializeCueTiming()
        {
            if (attentionCue != null) lastCueTimes[attentionCue] = 0f;
            if (speakingCue != null) lastCueTimes[speakingCue] = 0f;
            if (confidenceCue != null) lastCueTimes[confidenceCue] = 0f;
            if (directionCue != null) lastCueTimes[directionCue] = 0f;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Play an audio cue with default settings
        /// </summary>
        public void PlayCue(AudioClip clip, float volume = 1.0f)
        {
            if (!isInitialized || clip == null) return;
            
            var request = new AudioCueRequest
            {
                Clip = clip,
                Volume = volume,
                Position = Vector3.zero,
                Spatial = false
            };
            
            cueQueue.Enqueue(request);
        }
        
        /// <summary>
        /// Play a spatial audio cue at a specific position
        /// </summary>
        public void PlaySpatialCue(AudioClip clip, Vector3 position, float volume = 1.0f)
        {
            if (!isInitialized || clip == null) return;
            
            var request = new AudioCueRequest
            {
                Clip = clip,
                Volume = volume,
                Position = position,
                Spatial = true
            };
            
            cueQueue.Enqueue(request);
        }
        
        /// <summary>
        /// Play a directional cue based on conversation direction
        /// </summary>
        public void PlayDirectionalCue(Vector3 direction, float intensity = 1.0f)
        {
            if (!isInitialized || directionCue == null) return;
            
            // Calculate left/right balance based on direction
            float leftRightBalance = Mathf.Clamp(direction.x, -1f, 1f);
            
            var request = new AudioCueRequest
            {
                Clip = directionCue,
                Volume = intensity,
                Position = direction,
                Spatial = true,
                LeftRightBalance = leftRightBalance
            };
            
            cueQueue.Enqueue(request);
        }
        
        /// <summary>
        /// Play a confidence-based cue
        /// </summary>
        public void PlayConfidenceCue(float confidence)
        {
            if (!isInitialized) return;
            
            AudioClip clip = null;
            float volume = baseVolume;
            
            if (confidence > 0.8f && confidenceCue != null)
            {
                clip = confidenceCue;
                volume = baseVolume * 1.2f; // Louder for high confidence
            }
            else if (confidence > 0.5f && attentionCue != null)
            {
                clip = attentionCue;
                volume = baseVolume;
            }
            else if (speakingCue != null)
            {
                clip = speakingCue;
                volume = baseVolume * 0.8f; // Quieter for low confidence
            }
            
            if (clip != null)
            {
                PlayCue(clip, volume);
            }
        }
        
        #endregion
        
        #region Getters
        
        /// <summary>
        /// Get the speaking cue audio clip
        /// </summary>
        public AudioClip GetSpeakingCue()
        {
            return speakingCue;
        }
        
        /// <summary>
        /// Get the attention cue audio clip
        /// </summary>
        public AudioClip GetAttentionCue()
        {
            return attentionCue;
        }
        
        /// <summary>
        /// Get the confidence cue audio clip
        /// </summary>
        public AudioClip GetConfidenceCue()
        {
            return confidenceCue;
        }
        
        #endregion
        
        #region Cue Processing
        
        private IEnumerator ProcessCueQueue()
        {
            while (true)
            {
                if (cueQueue.TryDequeue(out AudioCueRequest request))
                {
                    yield return StartCoroutine(ProcessCueRequest(request));
                }
                
                yield return new WaitForSeconds(0.01f); // Small delay to prevent blocking
            }
        }
        
        private IEnumerator ProcessCueRequest(AudioCueRequest request)
        {
            // Check if enough time has passed since last cue
            if (!CanPlayCue(request.Clip))
            {
                yield break;
            }
            
            // Update last cue time
            lastCueTimes[request.Clip] = Time.time;
            
            if (request.Spatial)
            {
                yield return StartCoroutine(PlaySpatialCue(request));
            }
            else
            {
                yield return StartCoroutine(PlayCenterCue(request));
            }
            
            OnCuePlayed?.Invoke(request.Clip);
        }
        
        private IEnumerator PlaySpatialCue(AudioCueRequest request)
        {
            // Determine which audio source to use based on position
            AudioSource source = DetermineAudioSource(request.Position, request.LeftRightBalance);
            
            if (source == null) yield break;
            
            // Set position and play
            source.transform.localPosition = request.Position;
            source.volume = request.Volume;
            source.clip = request.Clip;
            
            // Fade in
            yield return StartCoroutine(FadeAudioSource(source, 0f, request.Volume, cueFadeInTime));
            
            source.Play();
            
            // Wait for audio to finish
            yield return new WaitForSeconds(request.Clip.length);
            
            // Fade out
            yield return StartCoroutine(FadeAudioSource(source, request.Volume, 0f, cueFadeOutTime));
        }
        
        private IEnumerator PlayCenterCue(AudioCueRequest request)
        {
            if (centerSource == null) yield break;
            
            centerSource.volume = request.Volume;
            centerSource.clip = request.Clip;
            
            // Fade in
            yield return StartCoroutine(FadeAudioSource(centerSource, 0f, request.Volume, cueFadeInTime));
            
            centerSource.Play();
            
            // Wait for audio to finish
            yield return new WaitForSeconds(request.Clip.length);
            
            // Fade out
            yield return StartCoroutine(FadeAudioSource(centerSource, request.Volume, 0f, cueFadeOutTime));
        }
        
        private AudioSource DetermineAudioSource(Vector3 position, float leftRightBalance)
        {
            if (Mathf.Abs(leftRightBalance) < 0.1f)
            {
                return centerSource;
            }
            else if (leftRightBalance < 0)
            {
                return leftEarSource;
            }
            else
            {
                return rightEarSource;
            }
        }
        
        private IEnumerator FadeAudioSource(AudioSource source, float fromVolume, float toVolume, float duration)
        {
            float elapsed = 0f;
            source.volume = fromVolume;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                source.volume = Mathf.Lerp(fromVolume, toVolume, t);
                yield return null;
            }
            
            source.volume = toVolume;
        }
        
        #endregion
        
        #region Utility Methods
        
        private bool CanPlayCue(AudioClip clip)
        {
            if (!lastCueTimes.ContainsKey(clip)) return true;
            
            float timeSinceLastCue = Time.time - lastCueTimes[clip];
            return timeSinceLastCue >= minCueInterval;
        }
        
        /// <summary>
        /// Stop all currently playing cues
        /// </summary>
        public void StopAllCues()
        {
            if (leftEarSource != null) leftEarSource.Stop();
            if (rightEarSource != null) rightEarSource.Stop();
            if (centerSource != null) centerSource.Stop();
        }
        
        /// <summary>
        /// Set the overall volume for all cues
        /// </summary>
        public void SetMasterVolume(float volume)
        {
            baseVolume = Mathf.Clamp01(volume);
            
            if (leftEarSource != null) leftEarSource.volume = baseVolume;
            if (rightEarSource != null) rightEarSource.volume = baseVolume;
            if (centerSource != null) centerSource.volume = baseVolume;
        }
        
        #endregion
        
        #region Helper Classes
        
        [System.Serializable]
        private class AudioCueRequest
        {
            public AudioClip Clip;
            public float Volume;
            public Vector3 Position;
            public bool Spatial;
            public float LeftRightBalance;
        }
        
        #endregion
    }
}
