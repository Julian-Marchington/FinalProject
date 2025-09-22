// ControllerUIStatusUGUI.cs  (Follow mode + onBeforeRender billboard)
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ControllerUIStatusUGUI : MonoBehaviour
{
    [Header("Sources")]
    public RetinaFaceRunner retina;        // <-- use RetinaFaceRunner
    public Transform head;                 // XR main camera / CenterEyeAnchor
    public Transform hand;                 // Which hand shows STATUS (Left or Right)

    [Header("Attach mode")]
    public bool attachAsChild = false;     // FALSE = follow (recommended)
    public Vector3 localOffset = new Vector3(-0.05f, 0.03f, 0.00f);

    [Header("UI look")]
    public Vector2 panelSizePx = new Vector2(420, 120);
    public float   worldScale  = 0.001f;   // 1000 px = 1 m
    public int     fontPx      = 42;
    public Color   textColor   = Color.white;
    public bool    showBackground = false; // UI Image; safe in URP
    public Color   bgColor     = new Color(0,0,0,0.40f);
    public bool    flip180     = false;    // enable if text faces away

    Canvas _canvas;
    TextMeshProUGUI _tmp;

    // transient override line (for cues)
    string _overrideLine;
    float _overrideUntil;

    void OnEnable()
    {
        if (!retina) retina = FindObjectOfType<RetinaFaceRunner>();
        if (!head)   head   = FindHead();
        if (!hand)   hand   = FindByHint("LeftHandAnchor","RightHandAnchor","Left Controller","Right Controller","LeftHand","RightHand") ?? transform;

        CreatePanel();
        Application.onBeforeRender += OnBeforeRenderBillboard;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRenderBillboard;
    }

    void LateUpdate()
    {
        // Update text
        string s = BuildStatusText();
        if (_tmp) _tmp.text = s;

        // Follow pose (position only) — rotation is set in OnBeforeRender
        if (hand && _canvas)
        {
            if (attachAsChild)
            {
                _canvas.transform.SetParent(hand, false);
                _canvas.transform.localPosition = localOffset;
            }
            else
            {
                _canvas.transform.SetParent(null, true); // detach
                _canvas.transform.position = hand.TransformPoint(localOffset);
            }
        }
    }

    /// <summary>Temporarily overrides the line shown on the HUD.</summary>
    public void OverrideLine(string s, float seconds = 1.25f)
    {
        _overrideLine  = s;
        _overrideUntil = Time.time + Mathf.Max(0.05f, seconds);
    }

    string BuildStatusText()
    {
        // show override if active
        if (Time.time < _overrideUntil && !string.IsNullOrEmpty(_overrideLine))
            return _overrideLine;

        if (retina != null)
        {
            // Prefer the properties; fall back to its LatestStatus if present
            if (retina.FacesCount >= 0)
                return $"Faces: {retina.FacesCount} | Looking#: {retina.FacesLookingCount}";
            if (!string.IsNullOrEmpty(retina.LatestStatus))
                return retina.LatestStatus;
        }
        return "Faces: ? | Looking#: ?";
    }

    void OnBeforeRenderBillboard()
    {
        if (!_canvas || !head) return;

        var t = _canvas.transform;
        Vector3 toHead = head.position - t.position;

        // Keep text upright by removing vertical tilt
        Vector3 flatToHead = Vector3.ProjectOnPlane(toHead, Vector3.up);
        if (flatToHead.sqrMagnitude < 1e-6f) flatToHead = toHead;

        var look = Quaternion.LookRotation(flatToHead.normalized, Vector3.up);
        if (flip180) look *= Quaternion.Euler(0, 180f, 0);
        t.rotation = look;
    }

    // ---------- UI creation ----------
    void CreatePanel()
    {
        if (_canvas) return;

        var go = new GameObject("ControllerStatusCanvas");
        go.layer = (hand ? hand.gameObject.layer : gameObject.layer);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = Camera.main;

        var rt = _canvas.GetComponent<RectTransform>();
        rt.sizeDelta = panelSizePx;
        _canvas.transform.localScale = Vector3.one * worldScale;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = 100f;

        if (showBackground)
        {
            var bgGO = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGO.layer = go.layer;
            var bgrt = bgGO.GetComponent<RectTransform>();
            bgrt.SetParent(go.transform, false);
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
            bgGO.GetComponent<Image>().color = bgColor;
        }

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.layer = go.layer;
        var txrt = txtGO.GetComponent<RectTransform>();
        txrt.SetParent(go.transform, false);
        txrt.anchorMin = new Vector2(0,0);
        txrt.anchorMax = new Vector2(1,1);
        txrt.offsetMin = new Vector2(12,12);
        txrt.offsetMax = new Vector2(-12,-12);

        _tmp = txtGO.AddComponent<TextMeshProUGUI>();
        _tmp.enableWordWrapping = false;
        _tmp.alignment = TextAlignmentOptions.Left;
        _tmp.fontSize = fontPx;
        _tmp.color = textColor;

        var fallback = TMP_Settings.defaultFontAsset ??
                       Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fallback) _tmp.font = fallback;

        // initial placement
        if (hand)
        {
            if (attachAsChild)
            {
                _canvas.transform.SetParent(hand, false);
                _canvas.transform.localPosition = localOffset;
            }
            else
            {
                _canvas.transform.position = hand.TransformPoint(localOffset);
            }
        }
    }

    // ---------- find helpers ----------
    Transform FindHead()
    {
        if (Camera.main) return Camera.main.transform;
        var cams = FindObjectsOfType<Camera>(true);
        foreach (var c in cams) if (c.stereoTargetEye != StereoTargetEyeMask.None) return c.transform;
        return FindByHint("CenterEyeAnchor","Main Camera","XRCamera","Camera Offset") ?? transform;
    }
    Transform FindByHint(params string[] hints)
    {
        foreach (var tr in FindObjectsOfType<Transform>(true))
        {
            string n = tr.name.ToLowerInvariant();
            foreach (var h in hints) if (n.Contains(h.ToLowerInvariant())) return tr;
        }
        return null;
    }
}
