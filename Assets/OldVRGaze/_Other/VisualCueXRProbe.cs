using UnityEngine;
#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
using UnityEngine.XR;
#endif

public class VisualCueXRProbe : MonoBehaviour
{
    public VisualCueOverlay overlay;        // optional; will auto-find
    public CuePresenter cues;               // optional; will auto-find
    public ControllerUIStatusUGUI hud;      // optional HUD for on-head text

#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
    InputDevice _left, _right;
#endif

    void Awake()
    {
        if (!overlay) overlay = FindObjectOfType<VisualCueOverlay>(includeInactive: true);
        if (!cues) cues = FindObjectOfType<CuePresenter>(includeInactive: true);
        if (!hud) hud = FindObjectOfType<ControllerUIStatusUGUI>(includeInactive: true);

#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
        TryGetXRDevices();
#endif
        Toast($"XRProbe ready | overlay={(overlay ? "OK" : "NULL")} | cues={(cues ? "OK" : "NULL")}");
    }

    void Update()
    {
        // A (right) => show Speak icon, X (left) => show Listen icon
        if (RightPrimaryDown()) { overlay?.ShowSpeak(); Toast("ShowSpeak (A)"); }
        if (LeftPrimaryDown()) { overlay?.ShowListen(); Toast("ShowListen (X)"); }

        // Both grips held ~0.6s -> toggle Visual modality on CuePresenter (so you can verify modality switch)
        if (BothGripsHeld(0.6f))
        {
            if (cues != null)
            {
                // flip Visual bit
                cues.ToggleModality(CuePresenter.Modality.Visual);
                Toast("Toggled Visual modality");
            }
        }
    }

    // ---------- XR helpers ----------
#if UNITY_XR_MANAGEMENT || UNITY_2019_4_OR_NEWER
    float _gripTimer = 0f;

    void TryGetXRDevices()
    {
        var tmp = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, tmp);
        if (tmp.Count > 0) _left = tmp[0];
        tmp.Clear();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, tmp);
        if (tmp.Count > 0) _right = tmp[0];
    }

    bool RightPrimaryDown()
    {
        if (!_right.isValid) TryGetXRDevices();
        bool val = false;
        return _right.isValid && _right.TryGetFeatureValue(CommonUsages.primaryButton, out val) && val && Edge(ref _rPrev, val);
    }
    bool LeftPrimaryDown()
    {
        if (!_left.isValid) TryGetXRDevices();
        bool val = false;
        return _left.isValid && _left.TryGetFeatureValue(CommonUsages.primaryButton, out val) && val && Edge(ref _lPrev, val);
    }
    bool BothGripsHeld(float sec)
    {
        if (!_left.isValid || !_right.isValid) TryGetXRDevices();
        bool l = false, r = false;
        if (_left.isValid) _left.TryGetFeatureValue(CommonUsages.gripButton, out l);
        if (_right.isValid) _right.TryGetFeatureValue(CommonUsages.gripButton, out r);
        if (l && r) _gripTimer += Time.deltaTime; else _gripTimer = 0f;
        return _gripTimer >= sec;
    }

    bool _lPrev = false, _rPrev = false;
    bool Edge(ref bool prev, bool cur) { bool down = (cur && !prev); prev = cur; return down; }
#endif

    // ---------- HUD toast ----------
    void Toast(string s)
    {
        if (hud) hud.OverrideLine(s, 1.2f);
    }
}