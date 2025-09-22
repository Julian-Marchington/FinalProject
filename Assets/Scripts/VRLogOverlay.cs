// VRLogOverlay.cs  (URP-safe, no background by default)
using UnityEngine;
using System.Collections.Concurrent;
using System.Text;
using TMPro;

public class VRLogOverlay : MonoBehaviour
{
    [Header("Placement")]
    public Transform head;                 // CenterEyeAnchor or Camera.main
    public float distance = 0.8f;          // meters in front of head
    public Vector3 localOffset = new Vector3(0, -0.05f, 0);

    [Header("Appearance")]
    public bool  showBackground = false;   // <-- OFF by default
    public int   maxLines = 25;
    public float width    = 0.8f;          // meters
    public float height   = 0.4f;          // meters
    public float fontSizeMeters = 0.03f;
    public Color faceColor = Color.white;
    public Color bgColor   = new Color(0,0,0,0.55f);

    private TextMeshPro _tmp;
    private Transform   _bg;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly StringBuilder _sb = new();
    private int _lineCount = 0;

    void Awake()
    {
        if (!head && Camera.main) head = Camera.main.transform;

        // (Optional) Background quad — only if URP/Unlit exists
        if (showBackground)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(bgGo.GetComponent<Collider>());
                _bg = bgGo.transform;
                _bg.SetParent(transform, false);
                _bg.localScale = new Vector3(width, height, 1f);
                _bg.localPosition = Vector3.zero;

                var mr = bgGo.GetComponent<MeshRenderer>();
                var mat = new Material(shader);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", bgColor);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", bgColor);
                mr.sharedMaterial = mat;
            }
            else
            {
                Debug.LogWarning("[VRLog] URP Unlit shader not found. Skipping background to avoid magenta.");
            }
        }

        // Text
        var textGo = new GameObject("TMP");
        textGo.transform.SetParent(transform, false);
        _tmp = textGo.AddComponent<TextMeshPro>();
        _tmp.enableWordWrapping = true;
        _tmp.alignment = TextAlignmentOptions.TopLeft;
        _tmp.color = faceColor;
        _tmp.fontSize = fontSizeMeters;

        // Ensure a font exists
        var fallback = TMP_Settings.defaultFontAsset ??
                       Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fallback != null) _tmp.font = fallback;

        Application.logMessageReceivedThreaded += OnLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnLog;
    }

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
            _lineCount++;
            changed = true;
            if (_lineCount > maxLines)
            {
                var txt = _sb.ToString();
                int firstNewline = txt.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    _sb.Clear();
                    _sb.Append(txt.Substring(firstNewline + 1));
                    _lineCount--;
                }
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
