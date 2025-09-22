using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
using System.IO;
using System.Text;

public class CuePresenter : MonoBehaviour
{
    // ---------- HUD / Audio ----------
    [Header("UI & Audio")]
    public ControllerUIStatusUGUI hud;   // has OverrideLine(string, float)
    public AudioSource audioSrc;

    [Header("Audio Cues")]
    public AudioClip holdClip;    // addressed: "listen/hold"
    public AudioClip speakClip;   // speak now
    [Range(0f, 1f)] public float holdVolume = 1f;
    [Range(0f, 1f)] public float speakVolume = 1f;

    public enum Side { Auto, Left, Right, Both }

    // ---------- Haptics ----------
    [Header("Haptic Cues")]
    [Range(0f, 1f)] public float holdAmp = 0.35f;
    [Range(0f, 1f)] public float speakAmp = 0.65f;
    public float holdDur = 0.10f;
    public float speakDur = 0.18f;

    // ---------- Visual ----------
    [Header("Visual Cues")]
    public VisualCueOverlay visualOverlay; // optional; shows listen/speak icons

    // ---------- Modality Mask ----------
    [System.Flags]
    public enum Modality { None = 0, Audio = 1, Haptic = 2, Visual = 4, All = Audio | Haptic | Visual }

    [Header("Modes")]
    public Modality enabledModalities = Modality.All;

    [Tooltip("Editor-only hotkeys: 1=Audio, 2=Haptic, 3=Visual, 0=All")]
    public bool enableEditorHotkeys = true;

    // ---------- (Optional) Logging ----------
    [Header("Logging (optional)")]
    public bool logToFile = false;
    public string participantId = "P00";
    public string conditionId = "COND";
    public string trialId = "T00";
    public string sessionIdOverride = "";
    string _autoSession;
    string SessionId => string.IsNullOrEmpty(sessionIdOverride) ? _autoSession : sessionIdOverride;
    StreamWriter _csv;
    System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
    InputDevice _left, _right;
    readonly List<InputDevice> _tmp = new List<InputDevice>();
#endif

    void Awake()
    {
        if (!visualOverlay) visualOverlay = FindObjectOfType<VisualCueOverlay>(includeInactive: true);

        if (logToFile)
        {
            _autoSession = System.DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(Application.persistentDataPath, $"cues_{SessionId}.csv");
            _csv = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _csv.WriteLine("utc,t_ms,participant,condition,trial,session,event,side,dirHint,amp,dur,clip");
            _csv.Flush();
            _sw.Start();
        }

#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
        TryGetXRDevices();
#endif
        UpdateHudText(); // show A/H/V mask briefly on HUD if present
    }

    void OnDisable()
    {
        if (_csv != null) { _csv.Flush(); _csv.Close(); _csv.Dispose(); _csv = null; }
        if (_sw.IsRunning) _sw.Stop();
    }

    void Update()
    {
#if UNITY_EDITOR
        if (enableEditorHotkeys)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) ToggleModality(Modality.Audio);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleModality(Modality.Haptic);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ToggleModality(Modality.Visual);
            if (Input.GetKeyDown(KeyCode.Alpha0)) SetModalities(Modality.All);
        }
#endif
    }

    // ===========================================================
    // Back-compat shims used by TurnTakingOrchestrator
    // ===========================================================
    // ShowHold (any of these may be called by older code)
    public void ShowHold() => PresentHoldCue(Side.Auto, 0f);
    public void ShowHold(Side side) => PresentHoldCue(side, 0f);
    public void ShowHold(Side side, float dirHint) => PresentHoldCue(side, dirHint);
    public void ShowHold(float dirHint) => PresentHoldCue(Side.Auto, dirHint);

    // ShowSpeakNow
    public void ShowSpeakNow() => PresentSpeakCue(Side.Auto, 0f, "speak");
    public void ShowSpeakNow(Side side) => PresentSpeakCue(side, 0f, "speak");
    public void ShowSpeakNow(float dirHint) => PresentSpeakCue(Side.Auto, dirHint, "speak");
    public void ShowSpeakNow(string reason) => PresentSpeakCue(Side.Auto, 0f, reason);
    public void ShowSpeakNow(string reason, Side side) => PresentSpeakCue(side, 0f, reason);
    public void ShowSpeakNow(string reason, Side side, float dirHint) => PresentSpeakCue(side, dirHint, reason);

    // ===========================================================
    // Primary API (modality-aware)
    // ===========================================================
    public void PresentHoldCue(Side side = Side.Auto, float dirHint = 0f)
    {
        // HUD
        hud?.OverrideLine("👀 They’re looking at you — hold");

        // Audio
        if (Has(Modality.Audio) && audioSrc && holdClip)
            audioSrc.PlayOneShot(holdClip, holdVolume);

        // Visual
        if (Has(Modality.Visual))
            visualOverlay?.ShowListen();

        // Haptics
        if (Has(Modality.Haptic))
            HapticPulse(holdAmp, holdDur, side, dirHint);

        LogCue("HOLD", side, dirHint, holdAmp, holdDur, holdClip ? holdClip.name : "none");
    }

    public void PresentSpeakCue(Side side = Side.Auto, float dirHint = 0f, string reason = "speak")
    {
        // HUD
        hud?.OverrideLine("🗣️ Speak now");

        // Audio
        if (Has(Modality.Audio) && audioSrc && speakClip)
            audioSrc.PlayOneShot(speakClip, speakVolume);

        // Visual
        if (Has(Modality.Visual))
            visualOverlay?.ShowSpeak();

        // Haptics
        if (Has(Modality.Haptic))
            HapticPulse(speakAmp, speakDur, side, dirHint);

        LogCue($"SPEAK({reason})", side, dirHint, speakAmp, speakDur, speakClip ? speakClip.name : "none");
    }

    // ===========================================================
    // Modalities control
    // ===========================================================
    public void SetModalities(Modality m)
    {
        enabledModalities = m;
        UpdateHudText();
    }

    public void ToggleModality(Modality m)
    {
        enabledModalities ^= m;
        UpdateHudText();
    }

    // UI Dropdown/Button helper — pass mask: 1=Audio, 2=Haptic, 4=Visual, 7=All
    public void SetModalitiesFromInt(int mask)
    {
        enabledModalities = (Modality)mask;
        UpdateHudText();
    }

    bool Has(Modality m) => (enabledModalities & m) != 0;

    void UpdateHudText()
    {
        if (!hud) return;
        string s = ShortMask(enabledModalities);
        hud.OverrideLine($"Mode: {s}", 1.2f);
    }

    string ShortMask(Modality m)
    {
        if (m == Modality.None) return "None";
        if (m == Modality.All) return "A+H+V";
        List<string> p = new List<string>(3);
        if (Has(Modality.Audio)) p.Add("A");
        if (Has(Modality.Haptic)) p.Add("H");
        if (Has(Modality.Visual)) p.Add("V");
        return string.Join("+", p);
    }

    // ===========================================================
    // Haptics
    // ===========================================================
    void HapticPulse(float amp, float dur, Side side, float dirHint = 0f)
    {
        amp = Mathf.Clamp01(amp);
        dur = Mathf.Max(0f, dur);

        void ping(XRNode node)
        {
#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
            var dev = GetDevice(node);
            if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                dev.SendHapticImpulse(0u, amp, dur);
#endif
#if OCULUS_INTEGRATION || OVRPLUGIN_PRESENT
            var ctrl = (node == XRNode.LeftHand) ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            OVRInput.SetControllerVibration(1f, amp, ctrl);
            StartCoroutine(StopOVRVibrationAfter(dur, ctrl));
#endif
        }

        if (side == Side.Both) { ping(XRNode.LeftHand); ping(XRNode.RightHand); return; }
        if (side == Side.Left) { ping(XRNode.LeftHand); return; }
        if (side == Side.Right) { ping(XRNode.RightHand); return; }

        // Auto: route by dirHint (-1 left .. +1 right), else both
        if (dirHint < -0.1f) ping(XRNode.LeftHand);
        else if (dirHint > 0.1f) ping(XRNode.RightHand);
        else { ping(XRNode.LeftHand); ping(XRNode.RightHand); }
    }

#if OCULUS_INTEGRATION || OVRPLUGIN_PRESENT
    IEnumerator StopOVRVibrationAfter(float dur, OVRInput.Controller ctrl)
    {
        yield return new WaitForSeconds(dur);
        OVRInput.SetControllerVibration(0f, 0f, ctrl);
    }
#endif

#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
    void TryGetXRDevices()
    {
        _tmp.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, _tmp);
        if (_tmp.Count > 0) _left = _tmp[0];
        _tmp.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, _tmp);
        if (_tmp.Count > 0) _right = _tmp[0];
    }

    InputDevice GetDevice(XRNode node)
    {
        var dev = (node == XRNode.LeftHand) ? _left : _right;
        if (!dev.isValid) TryGetXRDevices();
        return (node == XRNode.LeftHand) ? _left : _right;
    }
#endif

    // ===========================================================
    // Logging helper
    // ===========================================================
    void LogCue(string evt, Side side, float dirHint, float amp, float dur, string clip)
    {
        if (_csv == null) return;
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
        _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},{evt},{side},{dirHint:F2},{amp:F2},{dur:F2},{clip}");
        _csv.Flush();
    }
}