using UnityEngine;
using TMPro;
using UnityEngine.XR;
using System.IO;
using System.Text;

public class OldCue : MonoBehaviour
{
    public ControllerUIStatusUGUI hud;  // wrist HUD (optional)
    public AudioSource audioSrc;

    // --- Audio Cues ---
    [Header("Audio Cues")]
    public AudioClip holdClip;    // addressed, wait
    public AudioClip speakClip;   // go ahead, speak
    [Range(0f, 1f)] public float holdVolume = 1f;
    [Range(0f, 1f)] public float speakVolume = 1f;

    public enum Side { Auto, Left, Right, Both }

    // --- Haptics ---
    [Header("Haptics")]
    [Range(0f, 1f)] public float holdAmp = 0.35f;
    [Range(0f, 1f)] public float speakAmp = 0.65f;
    public float holdDur = 0.10f;
    public float speakDur = 0.18f;

    // --- Experiment Metadata & Logging ---
    [Header("Experiment Metadata")]
    public string participantId = "P00";
    public string conditionId = "COND";
    public string trialId = "T00";
    public string sessionIdOverride = ""; // leave empty to auto-generate

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
            string baseName = $"cues_{SessionId}.csv";
            _csvPath = Path.Combine(Application.persistentDataPath, baseName);
            _csv = new StreamWriter(new FileStream(_csvPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _csv.WriteLine("utc,t_ms,participant,condition,trial,session,event,side,dirHint,amp,dur,clip");
            _csv.Flush();
            _sw.Start();
        }
    }

    void OnDisable()
    {
        if (_csv != null) { _csv.Flush(); _csv.Close(); _csv.Dispose(); _csv = null; }
        if (_sw.IsRunning) _sw.Stop();
    }

    void HapticPulse(float amp, float dur, Side side, float dirHint = 0f)
    {
        void ping(XRNode n)
        {
            var dev = InputDevices.GetDeviceAtXRNode(n);
            if (dev.isValid)
            {
                dev.SendHapticImpulse(0u, Mathf.Clamp01(amp), dur);
            }
        }

        if (side == Side.Both) { ping(XRNode.LeftHand); ping(XRNode.RightHand); return; }
        if (side == Side.Left) { ping(XRNode.LeftHand); return; }
        if (side == Side.Right) { ping(XRNode.RightHand); return; }

        // Auto: choose based on dirHint (-1 left, +1 right), else both
        if (dirHint < -0.1f) ping(XRNode.LeftHand);
        else if (dirHint > 0.1f) ping(XRNode.RightHand);
        else { ping(XRNode.LeftHand); ping(XRNode.RightHand); }
    }

    public void ShowSpeakNow(string reason, Side side = Side.Auto, float dirHint = 0f)
    {
        if (hud) hud.OverrideLine($"✅ Speak now — {reason}");
        if (audioSrc && speakClip) audioSrc.PlayOneShot(speakClip, speakVolume);
        HapticPulse(speakAmp, speakDur, side, dirHint);

        // Log
        if (_csv != null)
        {
            long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
            _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},SPEAK,{side},{dirHint:F2},{speakAmp:F2},{speakDur:F2},{(speakClip ? speakClip.name : "none")}");
            _csv.Flush();
        }
    }

    public void ShowHold(Side side = Side.Auto, float dirHint = 0f)
    {
        if (hud) hud.OverrideLine($"👀 They’re looking at you — hold");
        if (audioSrc && holdClip) audioSrc.PlayOneShot(holdClip, holdVolume);
        HapticPulse(holdAmp, holdDur, side, dirHint);

        // Log
        if (_csv != null)
        {
            long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
            _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},HOLD,{side},{dirHint:F2},{holdAmp:F2},{holdDur:F2},{(holdClip ? holdClip.name : "none")}");
            _csv.Flush();
        }
    }
}
