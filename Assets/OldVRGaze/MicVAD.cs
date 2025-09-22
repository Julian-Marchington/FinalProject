using UnityEngine;
using System;
using System.IO;
using System.Text;



public class MicVAD : MonoBehaviour
{
    [Header("Mic")]
    public string deviceName = null;       
    public int sampleRate = 16000;         
    public int loopSeconds = 10;           
    public int frameMs = 20;               

    [Header("VAD tuning")]
    [Tooltip("dB above noise floor to count as speech (enter threshold)")]
    public float speechRiseDb = 9f;
    [Tooltip("dB above noise floor to remain in speech (lower than rise for hysteresis)")]
    public float speechHoldDb = 6f;
    [Tooltip("ms to keep speech after it dips below threshold (hangover)")]
    public int hangoverMs = 200;
    [Tooltip("How fast the noise floor adapts in silence (0..1). Lower = slower.")]
    [Range(0.001f, 0.2f)] public float noiseEma = 0.02f;

    [Header("Simple Volume Gate")]
    [Tooltip("Treat the room as quiet if LevelDb ≤ this (dBFS). Typical: -60 to -70 dB.")]
    public float quietDbThreshold = -65f;

    [Tooltip("How long LevelDb must stay ≤ quietDbThreshold to consider it a real pause (ms).")]
    public int quietMinMs = 1200;

    [Tooltip("Max dB above noise floor to still count as quiet (relative gate).")]
    public float quietMarginDb = 3f;

    [Header("Debug")]
    public bool logChanges = false;

    
    [Header("Experiment Metadata")]
    public string participantId = "P00";
    public string conditionId   = "COND";
    public string trialId       = "T00";
    public string sessionIdOverride = "";

    [Header("Logging")]
    public bool logToFile = true;

    string _csvPath;
    StreamWriter _csv;
    System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    string SessionId => string.IsNullOrEmpty(sessionIdOverride) ? _autoSession : sessionIdOverride;
    string _autoSession;

    
    public bool IsSpeech { get; private set; }
    public float QuietMs { get; private set; }    
    public float SilenceMs { get; private set; }    
    public float LevelDb { get; private set; }      
    public float NoiseFloorDb { get; private set; } 

    public event Action OnSpeechStart;
    public event Action OnSpeechEnd;

    AudioClip _clip;
    int _pos;
    int _channels;
    int _frameSamples;
    float _hangTimer;

    void Start()
    {

        
        var devs = Microphone.devices;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[VAD] Microphone.devices:");
        for (int i = 0; i < devs.Length; i++)
            sb.AppendLine($"  [{i}] \"{devs[i]}\"");
        Debug.Log(sb.ToString());

        
        Debug.Log($"[VAD] Requested deviceName = \"{deviceName ?? "(null -> default)"}\"");

        _frameSamples = Mathf.Max(1, (int)(sampleRate * (frameMs / 1000f)));
        _clip = Microphone.Start(deviceName, true, loopSeconds, sampleRate);
        StartCoroutine(WaitMicThenInit());

        if (logToFile)
        {
            _autoSession = System.DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            string baseName = $"vad_{SessionId}.csv";
            _csvPath = Path.Combine(Application.persistentDataPath, baseName);
            _csv = new StreamWriter(new FileStream(_csvPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _csv.WriteLine("utc,t_ms,participant,condition,trial,session,event,levelDb,noiseDb,isSpeech,silMs,above_dB,quietMs");
            _csv.Flush();
            _sw.Start();
        }
    }

    System.Collections.IEnumerator WaitMicThenInit()
    {
        while (Microphone.GetPosition(deviceName) <= 0) yield return null;
        _channels = _clip.channels;
        
        NoiseFloorDb = -50f;
        SilenceMs = 0f;
        _hangTimer = 0f;
        LogRow("INIT");
    }

    void OnDisable()
    {
        if (Microphone.IsRecording(deviceName)) Microphone.End(deviceName);
        if (_csv != null) { _csv.Flush(); _csv.Close(); _csv.Dispose(); _csv = null; }
        if (_sw.IsRunning) _sw.Stop();
    }

    void LogRow(string evt, float above = 0f)
    {
        if (_csv == null) return;
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
        _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},{evt},{LevelDb:F1},{NoiseFloorDb:F1},{(IsSpeech?1:0)},{SilenceMs:F0},{above:F1},{QuietMs:F0}");
        _csv.Flush();
    }

    void Update()
    {
        if (_clip == null) return;

        float[] buf = new float[_frameSamples * _channels];

        
        _clip.GetData(buf, _pos);
        _pos += _frameSamples;
        if (_pos >= _clip.samples) _pos -= _clip.samples;

        
        double sum = 0.0;
        for (int i = 0; i < buf.Length; i += _channels)
        {
            float s = buf[i]; 
            sum += s * s;
        }
        double rms = System.Math.Sqrt(sum / (buf.Length / _channels + 1e-9));
        LevelDb = (float)(20.0 * System.Math.Log10(rms + 1e-9)); 

        
        if (LevelDb <= quietDbThreshold)
            QuietMs += frameMs;
        else
            QuietMs = 0f;

        
        if (!IsSpeech)
        {
            
            float target = Mathf.Min(LevelDb, -20f);
            NoiseFloorDb = Mathf.Lerp(NoiseFloorDb, target, noiseEma);
        }

        float above = LevelDb - NoiseFloorDb;
        float dtMs = frameMs;

        if (!IsSpeech)
        {
            
            if (above >= speechRiseDb)
            {
                IsSpeech = true;
                _hangTimer = hangoverMs;
                SilenceMs = 0f;
                if (logChanges) Debug.Log("[VAD] Speech start");
                OnSpeechStart?.Invoke();
                LogRow("SPEECH_START", above);
            }
            else
            {
                SilenceMs += dtMs;
                LogRow("FRAME", above);
            }
        }
        else
        {
            
            if (above >= speechHoldDb)
            {
                _hangTimer = hangoverMs;
                SilenceMs = 0f;
                LogRow("FRAME", above);
            }
            else
            {
                _hangTimer -= dtMs;
                if (_hangTimer <= 0f)
                {
                    IsSpeech = false;
                    SilenceMs = dtMs;
                    if (logChanges) Debug.Log("[VAD] Speech end");
                    OnSpeechEnd?.Invoke();
                    LogRow("SPEECH_END", above);
                }
                else
                {
                    LogRow("FRAME", above);
                }
            }
        }
    }
}
