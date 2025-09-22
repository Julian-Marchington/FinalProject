// ControllerLogOnHandUGUI.cs  (Follow + onBeforeRender billboard)
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Concurrent;
using System.Text;

public class ControllerLogOnHandUGUI : MonoBehaviour
{
    [Header("Placement")]
    public Transform head;                 // XR main camera / CenterEyeAnchor
    public Transform hand;                 // Which hand shows LOGS (Right or Left)
    public bool     attachAsChild = false; // FALSE = follow (recommended)
    public Vector3  localOffset = new Vector3(0.05f, 0.03f, 0.00f);
    public bool     flip180 = false;

    [Header("UI look")]
    public Vector2 panelSizePx = new Vector2(520, 260);
    public float   worldScale  = 0.001f;
    public int     fontPx      = 28;
    public bool    showBackground = true;
    public Color   bgColor = new Color(0,0,0,0.55f);
    public Color   textColor = Color.white;
    public int     maxLines = 18;

    Canvas _canvas;
    TextMeshProUGUI _tmp;
    readonly ConcurrentQueue<string> _queue = new();
    readonly StringBuilder _sb = new();
    int _lineCount;

    void OnEnable()
    {
        if (!head) head = FindHead();
        if (!hand) hand = FindByHint("RightHandAnchor","Right Controller","RightHand","Right") ?? transform;
        CreatePanel();
        Application.onBeforeRender += OnBeforeRenderBillboard;
        Application.logMessageReceivedThreaded += OnLog;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRenderBillboard;
        Application.logMessageReceivedThreaded -= OnLog;
    }

    void LateUpdate()
    {
        // Follow pose (position only)
        if (hand && _canvas)
        {
            if (attachAsChild)
            {
                _canvas.transform.SetParent(hand, false);
                _canvas.transform.localPosition = localOffset;
            }
            else
            {
                _canvas.transform.SetParent(null, true);
                _canvas.transform.position = hand.TransformPoint(localOffset);
            }
        }
        DrainQueue();
    }

    void OnBeforeRenderBillboard()
    {
        if (!_canvas || !head) return;
        var t = _canvas.transform;
        Vector3 toHead = head.position - t.position;
        Vector3 flat = Vector3.ProjectOnPlane(toHead, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) flat = toHead;
        var look = Quaternion.LookRotation(flat.normalized, Vector3.up);
        if (flip180) look *= Quaternion.Euler(0,180f,0);
        t.rotation = look;
    }

    void DrainQueue()
    {
        if (!_tmp) return;
        bool changed = false;
        while (_queue.TryDequeue(out var line))
        {
            _sb.AppendLine(line);
            _lineCount++; changed = true;
            if (_lineCount > maxLines)
            {
                var txt = _sb.ToString();
                int cut = txt.IndexOf('\n');
                if (cut >= 0) { _sb.Clear(); _sb.Append(txt[(cut + 1)..]); _lineCount--; }
            }
        }
        if (changed) _tmp.text = _sb.ToString();
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        string prefix = type switch
        {
            LogType.Error => "[E] ",
            LogType.Exception => "[E] ",
            LogType.Warning => "[W] ",
            _ => ""
        };
        _queue.Enqueue(prefix + condition);
    }

    // --- UI creation ---
    void CreatePanel()
    {
        if (_canvas) return;

        var root = new GameObject("ControllerLogCanvas");
        root.layer = (hand ? hand.gameObject.layer : gameObject.layer);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = Camera.main;

        var rt = _canvas.GetComponent<RectTransform>();
        rt.sizeDelta = panelSizePx;
        _canvas.transform.localScale = Vector3.one * worldScale;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = 100f;

        if (showBackground)
        {
            var bgGO = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGO.layer = root.layer;
            var bgrt = bgGO.GetComponent<RectTransform>();
            bgrt.SetParent(root.transform, false);
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
            bgGO.GetComponent<Image>().color = bgColor;
        }

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.layer = root.layer;
        var txrt = txtGO.GetComponent<RectTransform>();
        txrt.SetParent(root.transform, false);
        txrt.anchorMin = new Vector2(0,0);
        txrt.anchorMax = new Vector2(1,1);
        txrt.offsetMin = new Vector2(12,12);
        txrt.offsetMax = new Vector2(-12,-12);

        _tmp = txtGO.AddComponent<TextMeshProUGUI>();
        _tmp.enableWordWrapping = true;
        _tmp.alignment = TextAlignmentOptions.TopLeft;
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

    // --- find helpers ---
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
