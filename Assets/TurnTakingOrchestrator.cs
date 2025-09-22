
using UnityEngine;
using System.IO;
using System.Text;

public class TurnTakingOrchestrator : MonoBehaviour
{
    public GroupAttentionFromRetina attention;
    public MicVAD vad;
    public CuePresenter cues;

    
    enum TTState { Idle, AddressedHold, Cooldown }
    TTState _state = TTState.Idle;

    [Header("Cooldown")]
    [Tooltip("Minimum seconds between SPEAK cues.")]
    public float speakCooldown = 4.0f;
    [Tooltip("Minimum seconds between HOLD cues (prevents HOLD spam).")]
    public float holdCooldown = 0.6f;
    private float _lastSpeakFire = -10f;
    private float _lastHoldFire = -10f;

    [Header("Thresholds")]

    [Header("Simple Volume Invite")]
    [Tooltip("Seconds of quiet needed before inviting to speak (measured since address-enter).")]
    public float minQuietToInvite = 1.2f;

    [Tooltip("Absolute dBFS threshold to consider the room quiet.")]
    public float quietDbThreshold = -65f;

    [Tooltip("Pause length (ms) that must also be satisfied (uses vad.SilenceMs).")]
    public float minSilenceMs = 550f;

    [Tooltip("Min attention score to qualify as being addressed.")]
    public float attentionTheta = 0.45f;

    [Tooltip("You must have been addressed within this many seconds (informational/optional).")]
    public float recentAddressWindowSec = 3.0f; 

    [Tooltip("Seconds to wait after ANY cue to avoid repeated cues (informational; not used to block promotion now).")]
    public float cueCooldownSec = 3.0f; 

    [Tooltip("Minimum time after addressed ends before a new cycle can start (sec).")]
    public float rearmSeconds = 0.8f;

    [Tooltip("Max dB above noise floor to still count as quiet (relative gate).")]
    public float quietMarginDb = 3f;

    
    float _lastCue = -999f;
    float _lastExitAddressed = -999f;

    
    private float _addrEnterAt = -1f;      
    private float _quietSinceEnterMs = 0f;  

    
    [Header("Experiment Metadata")]
    public string participantId = "P00";
    public string conditionId = "COND";
    public string trialId = "T00";
    public string sessionIdOverride = ""; 

    [Header("Logging")]
    public bool logToFile = true;

    string _csvPath;
    StreamWriter _csv;
    System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    string SessionId => string.IsNullOrEmpty(sessionIdOverride) ? _autoSession : sessionIdOverride;
    string _autoSession;

    void Awake()
    {
        if (logToFile)
        {
            _autoSession = System.DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            string baseName = $"tt_{SessionId}.csv";
            _csvPath = Path.Combine(Application.persistentDataPath, baseName);
            _csv = new StreamWriter(new FileStream(_csvPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            
            _csv.WriteLine("utc,t_ms,participant,condition,trial,session,event,state,attScore,faces,looking,dirLR,isSpeech,silMs,lastCue_s,lastExitAddr_s,reason");
            _csv.Flush();
            _sw.Start();
        }
    }

    void OnDisable()
    {
        if (_csv != null) { _csv.Flush(); _csv.Close(); _csv.Dispose(); _csv = null; }
        if (_sw.IsRunning) _sw.Stop();
    }

    void LogRow(string evt, string reason = "")
    {
        if (_csv == null || attention == null || vad == null) return;
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
        string state = _state.ToString();
        float lastCueAgo = (Time.time < 0f || _lastCue < -1f) ? -1f : (Time.time - _lastCue);
        float lastExitAgo = (Time.time < 0f || _lastExitAddressed < -1f) ? -1f : (Time.time - _lastExitAddressed);
        _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},{evt},{state},{attention.AttentionScore:F2},{attention.Faces},{attention.LookingFaces},{attention.AddressDirLR:F2},{(vad.IsSpeech ? 1 : 0)},{vad.SilenceMs:F0},{lastCueAgo:F2},{lastExitAgo:F2},{reason}");
        _csv.Flush();
    }

    
    bool IsQuietNow()
    {
        if (vad == null) return false;
        float above = vad.LevelDb - vad.NoiseFloorDb; 
        return (vad.LevelDb <= quietDbThreshold) || (above <= quietMarginDb);
    }

    void Update()
    {
        if (vad == null || attention == null || cues == null) return;

        bool inSpeech = vad.IsSpeech;
        float silMs = vad.SilenceMs;

        
        bool addressed = attention.IsGroupAddressed
                         && attention.AttentionScore >= attentionTheta
                         && attention.Faces > 0;

        
        bool gapReady = !inSpeech && silMs >= minSilenceMs;

        
        if (_state != TTState.Idle && _addrEnterAt > 0f)
        {
            if (!vad.IsSpeech && IsQuietNow())
                _quietSinceEnterMs += Time.deltaTime * 1000f;
            else
                _quietSinceEnterMs = 0f;
        }
        else
        {
            _quietSinceEnterMs = 0f;
        }

        
        float dir = Mathf.Clamp(attention.AddressDirLR, -1f, 1f);

        if (addressed && _state == TTState.Idle)
            Debug.Log($"[TT] ENTER addressed: att={attention.AttentionScore:F2} faces={attention.Faces} dir={attention.AddressDirLR:F2}");

        if (_state == TTState.AddressedHold && gapReady)
            Debug.Log($"[TT] GAP ready: sil={silMs:F0}ms att={attention.AttentionScore:F2} dir={attention.AddressDirLR:F2}");

        switch (_state)
        {
            case TTState.Idle:
                
                if (addressed && Time.time - _lastExitAddressed >= rearmSeconds)
                {
                    
                    _addrEnterAt = Time.time;
                    _quietSinceEnterMs = 0f;

                    
                    bool cooldownOk___ = (Time.time - _lastSpeakFire) >= speakCooldown;
                    bool alreadyQuiet___ =
                        gapReady &&
                        (_quietSinceEnterMs >= minQuietToInvite * 1000f) &&
                        IsQuietNow();

                    if (cooldownOk___ && alreadyQuiet___)
                    {
                        cues.ShowSpeakNow("addr_enter_quiet", CuePresenter.Side.Auto, dir);
                        _lastSpeakFire = Time.time;
                        _lastCue = Time.time;
                        LogRow("SPEAK_CUE", "addr_enter_quiet");
                        _state = TTState.Cooldown;
                    }
                    else
                    {
                        
                        if (Time.time - _lastHoldFire >= holdCooldown)
                        {
                            cues.ShowHold(CuePresenter.Side.Auto, dir);
                            _lastHoldFire = Time.time;
                            _lastCue = Time.time;
                            LogRow("HOLD_CUE", "addr_enter");
                        }
                        _state = TTState.AddressedHold;
                    }
                }
                break;

            case TTState.AddressedHold:
                
                if (!addressed)
                {
                    _lastExitAddressed = Time.time;
                    LogRow("ADDR_EXIT");
                    _state = TTState.Idle;
                    _addrEnterAt = -1f;
                    _quietSinceEnterMs = 0f;
                    break;
                }

                
                if (gapReady)
                {
                    bool cooldownOk___ = (Time.time - _lastSpeakFire) >= speakCooldown;
                    bool quietOk___ = (_quietSinceEnterMs >= minQuietToInvite * 1000f) && IsQuietNow();

                    if (cooldownOk___ && quietOk___)
                    {
                        cues.ShowSpeakNow("gap + eyes on you", CuePresenter.Side.Auto, dir);
                        _lastSpeakFire = Time.time;
                        _lastCue = Time.time;
                        LogRow("SPEAK_CUE", "gap_ready");
                        _state = TTState.Cooldown; 
                    }
                    
                }
                break;

            case TTState.Cooldown:
                
                if (!addressed)
                {
                    _lastExitAddressed = Time.time;
                    LogRow("ADDR_EXIT");
                    _state = TTState.Idle;
                    _addrEnterAt = -1f;
                    _quietSinceEnterMs = 0f;
                }
                break;
        }
    }
}