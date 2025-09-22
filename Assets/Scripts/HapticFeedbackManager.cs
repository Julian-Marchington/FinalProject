using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ConversationAssistant
{
    /// <summary>
    /// Manages haptic feedback for conversation assistance
    /// Provides tactile feedback through controller vibrations to help visually impaired users
    /// understand conversation dynamics and confidence levels
    /// </summary>
    public class HapticFeedbackManager : MonoBehaviour
    {
        [Header("Haptic Settings")]
        [SerializeField] private float baseAmplitude = 0.5f;
        [SerializeField] private float baseFrequency = 0.5f;
        [SerializeField] private float minAmplitude = 0.1f;
        [SerializeField] private float maxAmplitude = 1.0f;
        
        [Header("Feedback Patterns")]
        [SerializeField] private HapticPattern highConfidencePattern;
        [SerializeField] private HapticPattern mediumConfidencePattern;
        [SerializeField] private HapticPattern lowConfidencePattern;
        [SerializeField] private HapticPattern attentionPattern;
        [SerializeField] private HapticPattern directionPattern;
        
        [Header("Controller Settings")]
        [SerializeField] private bool enableLeftController = true;
        [SerializeField] private bool enableRightController = true;
        [SerializeField] private bool enableBothControllers = true;
        
        // Internal state
        private bool isInitialized = false;
        private Dictionary<HapticPatternType, HapticPattern> patterns;
        private Queue<HapticRequest> hapticQueue = new Queue<HapticRequest>();
        private Coroutine hapticProcessor;
        
        // Events
        public System.Action<HapticPatternType> OnHapticTriggered;
        public System.Action<string> OnHapticError;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            InitializePatterns();
        }
        
        private void OnDestroy()
        {
            StopAllCoroutines();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializePatterns()
        {
            patterns = new Dictionary<HapticPatternType, HapticPattern>();
            
            // Initialize default patterns if not set
            if (highConfidencePattern == null)
            {
                highConfidencePattern = CreateDefaultHighConfidencePattern();
            }
            
            if (mediumConfidencePattern == null)
            {
                mediumConfidencePattern = CreateDefaultMediumConfidencePattern();
            }
            
            if (lowConfidencePattern == null)
            {
                lowConfidencePattern = CreateDefaultLowConfidencePattern();
            }
            
            if (attentionPattern == null)
            {
                attentionPattern = CreateDefaultAttentionPattern();
            }
            
            if (directionPattern == null)
            {
                directionPattern = CreateDefaultDirectionPattern();
            }
            
            // Add patterns to dictionary
            patterns[HapticPatternType.HighConfidence] = highConfidencePattern;
            patterns[HapticPatternType.MediumConfidence] = mediumConfidencePattern;
            patterns[HapticPatternType.LowConfidence] = lowConfidencePattern;
            patterns[HapticPatternType.Attention] = attentionPattern;
            patterns[HapticPatternType.Direction] = directionPattern;
            
            // Start haptic processor
            hapticProcessor = StartCoroutine(ProcessHapticQueue());
            
            isInitialized = true;
            Debug.Log("HapticFeedbackManager: Initialized with default patterns");
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Provide haptic feedback based on confidence level
        /// </summary>
        public void ProvideFeedback(float confidence)
        {
            if (!isInitialized) return;
            
            HapticPatternType patternType;
            
            if (confidence > 0.8f)
            {
                patternType = HapticPatternType.HighConfidence;
            }
            else if (confidence > 0.5f)
            {
                patternType = HapticPatternType.MediumConfidence;
            }
            else
            {
                patternType = HapticPatternType.LowConfidence;
            }
            
            ProvidePatternFeedback(patternType);
        }
        
        /// <summary>
        /// Provide haptic feedback for attention
        /// </summary>
        public void ProvideAttentionFeedback()
        {
            if (!isInitialized) return;
            ProvidePatternFeedback(HapticPatternType.Attention);
        }
        
        /// <summary>
        /// Provide haptic feedback for directional cues
        /// </summary>
        public void ProvideDirectionalFeedback(Vector3 direction, float intensity = 1.0f)
        {
            if (!isInitialized) return;
            
            var request = new HapticRequest
            {
                PatternType = HapticPatternType.Direction,
                Intensity = intensity,
                Direction = direction,
                Duration = 0.5f
            };
            
            hapticQueue.Enqueue(request);
        }
        
        /// <summary>
        /// Provide custom haptic feedback
        /// </summary>
        public void ProvideCustomFeedback(float amplitude, float frequency, float duration)
        {
            if (!isInitialized) return;
            
            var request = new HapticRequest
            {
                PatternType = HapticPatternType.Custom,
                Amplitude = amplitude,
                Frequency = frequency,
                Duration = duration
            };
            
            hapticQueue.Enqueue(request);
        }
        
        /// <summary>
        /// Provide pattern-based haptic feedback
        /// </summary>
        public void ProvidePatternFeedback(HapticPatternType patternType)
        {
            if (!isInitialized) return;
            
            var request = new HapticRequest
            {
                PatternType = patternType,
                Intensity = 1.0f
            };
            
            hapticQueue.Enqueue(request);
        }
        
        #endregion
        
        #region Haptic Processing
        
        private IEnumerator ProcessHapticQueue()
        {
            while (true)
            {
                if (hapticQueue.Count > 0)
                {
                    var request = hapticQueue.Dequeue();
                    yield return StartCoroutine(ProcessHapticRequest(request));
                }
                
                yield return new WaitForSeconds(0.01f);
            }
        }
        
        private IEnumerator ProcessHapticRequest(HapticRequest request)
        {
            switch (request.PatternType)
            {
                case HapticPatternType.Custom:
                    yield return StartCoroutine(PlayCustomHaptic(request));
                    break;
                    
                case HapticPatternType.Direction:
                    yield return StartCoroutine(PlayDirectionalHaptic(request));
                    break;
                    
                default:
                    yield return StartCoroutine(PlayPatternHaptic(request));
                    break;
            }
            
            OnHapticTriggered?.Invoke(request.PatternType);
        }
        
        private IEnumerator PlayCustomHaptic(HapticRequest request)
        {
            float amplitude = Mathf.Clamp(request.Amplitude, minAmplitude, maxAmplitude);
            float frequency = Mathf.Clamp(request.Frequency, 0.1f, 2.0f);
            
            // Create haptic clip with sample data
            var hapticClip = CreateHapticClip(amplitude, frequency, request.Duration);
            
            // Apply to controllers
            if (enableLeftController)
            {
                OVRHaptics.LeftChannel.Mix(hapticClip);
            }
            
            if (enableRightController)
            {
                OVRHaptics.RightChannel.Mix(hapticClip);
            }
            
            yield return new WaitForSeconds(request.Duration);
        }
        
        private IEnumerator PlayDirectionalHaptic(HapticRequest request)
        {
            // Calculate left/right balance based on direction
            float leftRightBalance = Mathf.Clamp(request.Direction.x, -1f, 1f);
            float intensity = request.Intensity;
            
            // Create haptic clips
            var leftClip = CreateHapticClip(intensity * (1f - leftRightBalance), 0.5f, request.Duration);
            var rightClip = CreateHapticClip(intensity * (1f + leftRightBalance), 0.5f, request.Duration);
            
            // Apply to appropriate controllers
            if (enableLeftController && leftRightBalance < 0.5f)
            {
                OVRHaptics.LeftChannel.Mix(leftClip);
            }
            
            if (enableRightController && leftRightBalance > -0.5f)
            {
                OVRHaptics.RightChannel.Mix(rightClip);
            }
            
            yield return new WaitForSeconds(request.Duration);
        }
        
        private IEnumerator PlayPatternHaptic(HapticRequest request)
        {
            if (!patterns.ContainsKey(request.PatternType))
            {
                Debug.LogWarning($"HapticFeedbackManager: Pattern {request.PatternType} not found");
                yield break;
            }
            
            var pattern = patterns[request.PatternType];
            float totalDuration = 0f;
            
            foreach (var pulse in pattern.Pulses)
            {
                float amplitude = pulse.Amplitude * request.Intensity;
                amplitude = Mathf.Clamp(amplitude, minAmplitude, maxAmplitude);
                
                // Create haptic clip for this pulse
                var hapticClip = CreateHapticClip(amplitude, pulse.Frequency, pulse.Duration);
                
                // Apply haptic pulse
                if (enableLeftController)
                {
                    OVRHaptics.LeftChannel.Mix(hapticClip);
                }
                
                if (enableRightController)
                {
                    OVRHaptics.RightChannel.Mix(hapticClip);
                }
                
                totalDuration += pulse.Duration;
                yield return new WaitForSeconds(pulse.Duration);
            }
            
            yield return new WaitForSeconds(0.1f); // Small gap between patterns
        }
        
        #endregion
        
        #region Pattern Creation
        
        private HapticPattern CreateDefaultHighConfidencePattern()
        {
            return new HapticPattern
            {
                PatternType = HapticPatternType.HighConfidence,
                Pulses = new List<HapticPulse>
                {
                    new HapticPulse { Amplitude = 0.8f, Frequency = 0.8f, Duration = 0.1f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 1.0f, Frequency = 1.0f, Duration = 0.15f }
                }
            };
        }
        
        private HapticPattern CreateDefaultMediumConfidencePattern()
        {
            return new HapticPattern
            {
                PatternType = HapticPatternType.MediumConfidence,
                Pulses = new List<HapticPulse>
                {
                    new HapticPulse { Amplitude = 0.6f, Frequency = 0.6f, Duration = 0.1f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.6f, Frequency = 0.6f, Duration = 0.1f }
                }
            };
        }
        
        private HapticPattern CreateDefaultLowConfidencePattern()
        {
            return new HapticPattern
            {
                PatternType = HapticPatternType.LowConfidence,
                Pulses = new List<HapticPulse>
                {
                    new HapticPulse { Amplitude = 0.3f, Frequency = 0.4f, Duration = 0.08f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.1f },
                    new HapticPulse { Amplitude = 0.3f, Frequency = 0.4f, Duration = 0.08f }
                }
            };
        }
        
        private HapticPattern CreateDefaultAttentionPattern()
        {
            return new HapticPattern
            {
                PatternType = HapticPatternType.Attention,
                Pulses = new List<HapticPulse>
                {
                    new HapticPulse { Amplitude = 0.5f, Frequency = 0.7f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.5f, Frequency = 0.7f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.5f, Frequency = 0.7f, Duration = 0.05f }
                }
            };
        }
        
        private HapticPattern CreateDefaultDirectionPattern()
        {
            return new HapticPattern
            {
                PatternType = HapticPatternType.Direction,
                Pulses = new List<HapticPulse>
                {
                    new HapticPulse { Amplitude = 0.6f, Frequency = 0.5f, Duration = 0.1f },
                    new HapticPulse { Amplitude = 0.0f, Frequency = 0.0f, Duration = 0.05f },
                    new HapticPulse { Amplitude = 0.6f, Frequency = 0.5f, Duration = 0.1f }
                }
            };
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Create a haptic clip with the specified amplitude, frequency, and duration
        /// </summary>
        private OVRHapticsClip CreateHapticClip(float amplitude, float frequency, float duration)
        {
            // Convert amplitude to byte array (0-255 range)
            byte[] samples = new byte[Mathf.RoundToInt(duration * 100)]; // 100 samples per second
            
            for (int i = 0; i < samples.Length; i++)
            {
                // Create a simple sine wave pattern
                float time = (float)i / 100f;
                float wave = Mathf.Sin(time * frequency * 2f * Mathf.PI);
                samples[i] = (byte)(Mathf.Clamp01((wave + 1f) * 0.5f) * amplitude * 255f);
            }
            
            return new OVRHapticsClip(samples, samples.Length);
        }
        
        /// <summary>
        /// Stop all haptic feedback
        /// </summary>
        public void StopAllHaptics()
        {
            if (enableLeftController)
            {
                OVRHaptics.LeftChannel.Clear();
            }
            
            if (enableRightController)
            {
                OVRHaptics.RightChannel.Clear();
            }
        }
        
        /// <summary>
        /// Set the base amplitude for all haptic feedback
        /// </summary>
        public void SetBaseAmplitude(float amplitude)
        {
            baseAmplitude = Mathf.Clamp(amplitude, minAmplitude, maxAmplitude);
        }
        
        /// <summary>
        /// Test haptic feedback on both controllers
        /// </summary>
        public void TestHaptics()
        {
            if (!isInitialized) return;
            
            StartCoroutine(TestHapticSequence());
        }
        
        private IEnumerator TestHapticSequence()
        {
            Debug.Log("HapticFeedbackManager: Testing haptic feedback...");
            
            // Test each pattern type
            foreach (var patternType in System.Enum.GetValues(typeof(HapticPatternType)))
            {
                if (patternType is HapticPatternType type)
                {
                    ProvidePatternFeedback(type);
                    yield return new WaitForSeconds(1.0f);
                }
            }
            
            Debug.Log("HapticFeedbackManager: Haptic test complete");
        }
        
        #endregion
        
        #region Helper Classes
        
        [System.Serializable]
        public class HapticPattern
        {
            public HapticPatternType PatternType;
            public List<HapticPulse> Pulses;
        }
        
        [System.Serializable]
        public class HapticPulse
        {
            public float Amplitude;
            public float Frequency;
            public float Duration;
        }
        
        [System.Serializable]
        public class HapticRequest
        {
            public HapticPatternType PatternType;
            public float Intensity = 1.0f;
            public float Amplitude;
            public float Frequency;
            public float Duration;
            public Vector3 Direction;
        }
        
        public enum HapticPatternType
        {
            HighConfidence,
            MediumConfidence,
            LowConfidence,
            Attention,
            Direction,
            Custom
        }
        
        #endregion
    }
}
