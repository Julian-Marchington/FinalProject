// UILogOverlayUGUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Concurrent;
using System.Text;

public class UiLogOverlayUGUI : MonoBehaviour
{
    [Header("Placement")]
    public Transform head;           // CenterEyeAnchor or Camera.main
    public float distance = 0.8f;
    public Vector2 panelSizePx = new Vector2(900, 420);
    public float  worldScale  = 0.001f;  // 1000 px = 1 m
    public Vector3 localOffset = new Vector3(0, -0.05f, 0);

    [Header("Appearance")]
    public int   maxLines = 30;
    public int   fontPx   = 32;
    public bool  showBackground = true;
    public Color bgColor = new Color(0,0,0,0.55f);
    public Color faceColor = Color.white;

    Canvas _canvas;
    TextMeshProUGUI _tmp;
    readonly ConcurrentQueue<string> _queue = new();
    readonly StringBuilder _sb = new();
    int _lineCount;

    void Awake()
    {
        if (!head && Camera.main) head = Camera.main.transform;

        // Root canvas
        var go = new GameObject("UILogCanvas");
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * worldScale;
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = Camera.main;

        var rt = _canvas.GetComponent<RectTransform>();
        rt.sizeDelta = panelSizePx;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.referencePixelsPerUnit = 100f;

        if (showBackground)
        {
            var bgGO = new GameObject("BG", typeof(RectTransform), typeof(Image));
            var bgrt = bgGO.GetComponent<RectTransform>();
            bgrt.SetParent(go.transform, false);
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
            bgGO.GetComponent<Image>().color = bgColor;
        }

        var txtGO = new GameObject("Text", typeof(RectTransform));
        var txrt = txtGO.GetComponent<RectTransform>();
        txrt.SetParent(go.transform, false);
        txrt.anchorMin = new Vector2(0,0);
        txrt.anchorMax = new Vector2(1,1);
        txrt.offsetMin = new Vector2(16,16);
        txrt.offsetMax = new Vector2(-16,-16);

        _tmp = txtGO.AddComponent<TextMeshProUGUI>();
        _tmp.enableWordWrapping = true;
        _tmp.alignment = TextAlignmentOptions.TopLeft;
        _tmp.fontSize = fontPx;
        _tmp.color = faceColor;

        var fallback = TMP_Settings.defaultFontAsset ??
                       Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fallback) _tmp.font = fallback;

        Application.logMessageReceivedThreaded += OnLog;
    }

    void OnDestroy() => Application.logMessageReceivedThreaded -= OnLog;

    void LateUpdate()
    {
        if (head)
        {
            var fwd = head.forward;
            var pos = head.position + fwd * distance + head.TransformVector(localOffset);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        bool changed = false;
        while (_queue.TryDequeue(out var line))
        {
            _sb.AppendLine(line);
            _lineCount++; changed = true;

            if (_lineCount > maxLines)
            {
                // drop oldest line
                var txt = _sb.ToString();
                int cut = txt.IndexOf('\n');
                if (cut >= 0) { _sb.Clear(); _sb.Append(txt.Substring(cut + 1)); _lineCount--; }
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
}
