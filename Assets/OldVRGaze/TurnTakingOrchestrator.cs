// TurnTakingOrchestrator.cs
using UnityEngine;
using System.IO;
using System.Text;

public class TurnTakingOrchestrator : MonoBehaviour
{
    public GroupAttentionFromRetina attention;
    public MicVAD vad;
    public CuePresenter cues;

    // --- State machine ---
    enum TTState { Idle, AddressedHold, Cooldown }
    TTState _state = TTState.Idle;

    [Header("Thresholds")]
    [Tooltip("Pause length that feels like a gap (ms)")]
    public float minSilenceMs = 550f;
    [Tooltip("Min attention score over window to allow cue")]
    public float attentionTheta = 0.45f;
    [Tooltip("You must have been addressed within this many seconds")]
    public float recentAddressWindowSec = 3.0f;      // (optional; not strictly used here)
    [Tooltip("Seconds to wait after cue to avoid spam")]
    public float cueCooldownSec = 3.0f;

    [Tooltip("Minimum time after addressed ends before a new cycle can start (sec)")]
    public float rearmSeconds = 0.8f;

    float _lastCue = -999f;
    float _lastExitAddressed = -999f;

    // --- Experiment Metadata & Logging ---
    [Header("Experiment Metadata")]
    public string participantId = "P00";
    public string conditionId   = "COND";
    public string trialId       = "T00";
    public string sessionIdOverride = ""; // empty = autogen

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
        _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},{evt},{state},{attention.AttentionScore:F2},{attention.Faces},{attention.LookingFaces},{attention.AddressDirLR:F2},{(vad.IsSpeech?1:0)},{vad.SilenceMs:F0},{lastCueAgo:F2},{lastExitAgo:F2},{reason}");
        _csv.Flush();
    }

    void Update()
    {
        if (vad == null || attention == null || cues == null) return;

        bool inSpeech = vad.IsSpeech;
        float silMs   = vad.SilenceMs;

        // Stable addressed + minimum attention
        bool addressed = attention.IsGroupAddressed
                         && attention.AttentionScore >= attentionTheta
                         && attention.Faces > 0;

        bool gapReady = !inSpeech && silMs >= minSilenceMs;

        if (addressed && _state == TTState.Idle)
            Debug.Log($"[TT] ENTER addressed: att={attention.AttentionScore:F2} faces={attention.Faces} dir={attention.AddressDirLR:F2}");

        if (_state == TTState.AddressedHold && gapReady)
            Debug.Log($"[TT] GAP ready: sil={silMs:F0}ms att={attention.AttentionScore:F2} dir={attention.AddressDirLR:F2}");

        // Direction hint for haptics/audio panning (-1 left .. +1 right)
        float dir = Mathf.Clamp(attention.AddressDirLR, -1f, 1f);

        switch (_state)
        {
            case TTState.Idle:
                // rising edge of addressed
                if (addressed && Time.time - _lastExitAddressed >= rearmSeconds)
                {
                    cues.ShowHold(CuePresenter.Side.Auto, dir);   // one subtle cue
                    _lastCue = Time.time;
                    LogRow("HOLD_CUE", "addr_enter");
                    _state = TTState.AddressedHold;
                }
                break;

            case TTState.AddressedHold:
                // addressed must persist; if it drops, re-arm
                if (!addressed)
                {
                    _lastExitAddressed = Time.time;
                    LogRow("ADDR_EXIT");
                    _state = TTState.Idle;
                    break;
                }
                // escalate once when a real gap appears
                if (gapReady && Time.time - _lastCue >= cueCooldownSec)
                {
                    cues.ShowSpeakNow("gap + eyes on you", CuePresenter.Side.Auto, dir);
                    _lastCue = Time.time;
                    LogRow("SPEAK_CUE", "gap_ready");
                    _state = TTState.Cooldown; // prevents double beeps
                }
                break;

            case TTState.Cooldown:
                // wait until addressed truly ends before allowing a new cycle
                if (!addressed)
                {
                    _lastExitAddressed = Time.time;
                    LogRow("ADDR_EXIT");
                    _state = TTState.Idle;
                }
                break;
        }
    }
}
