using UnityEngine;

public class VisualCueOverlay : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite listenSprite;
    public Sprite speakSprite;

    [Header("Style")]
    public Color tint = Color.cyan;

    [Header("Placement (relative to HMD)")]
    [Tooltip("Meters forward from the HMD")]
    public float distance = 1.0f;
    [Tooltip("Meters offset right (+) / left (-)")]
    public float xOffsetMeters = -0.25f;
    [Tooltip("Meters offset up (+) / down (-)")]
    public float yOffsetMeters = 0.20f;
    [Tooltip("Physical width in meters")]
    public float widthMeters = 0.22f;

    [Header("Timing")]
    public float fadeIn = 0.12f;
    public float hold = 0.90f;
    public float fadeOut = 0.22f;
    public bool restartOnRepeat = true;

    enum Mode { None, Listen, Speak }
    Mode _mode = Mode.None;
    float _t = -1f;

    Camera _cam;
    GameObject _quadGO;
    Material _mat;
    Texture _texCurrent;

    void Awake()
    {
        _cam = Camera.main ?? FindObjectOfType<Camera>();
        EnsureQuad();
    }

    void LateUpdate()
    {
        if (!_quadGO || !_cam) return;

        // Position in front of HMD with offsets
        Vector3 forward = _cam.transform.forward;
        Vector3 right = _cam.transform.right;
        Vector3 up = _cam.transform.up;

        var t = _quadGO.transform;
        t.position = _cam.transform.position +
                     forward * distance +
                     right * xOffsetMeters +
                     up * yOffsetMeters;
        t.rotation = Quaternion.LookRotation(forward, up);

        // Size
        float w = Mathf.Max(0.01f, widthMeters);
        float aspect = (_texCurrent) ? (float)_texCurrent.height / Mathf.Max(1, _texCurrent.width) : 1f;
        t.localScale = new Vector3(w, w * aspect, 1f);

        // Fade envelope
        if (_t >= 0f && _mode != Mode.None)
        {
            _t += Time.deltaTime;
            float a;
            if (_t <= fadeIn) a = Mathf.SmoothStep(0f, 1f, _t / fadeIn);
            else if (_t <= fadeIn + hold) a = 1f;
            else if (_t <= fadeIn + hold + fadeOut)
            {
                float u = (_t - (fadeIn + hold)) / fadeOut;
                a = Mathf.SmoothStep(1f, 0f, u);
            }
            else { _mode = Mode.None; _t = -1f; a = 0f; }
            ApplyAlpha(a);
        }
    }

    // --- Public API ---
    public void ShowListen() { SetIcon(listenSprite); StartCue(Mode.Listen); }
    public void ShowSpeak() { SetIcon(speakSprite); StartCue(Mode.Speak); }
    public void HideImmediate() { _mode = Mode.None; _t = -1f; ApplyAlpha(0f); }

    void StartCue(Mode m) { if (_mode != m || restartOnRepeat) _t = 0f; _mode = m; }

    void SetIcon(Sprite s)
    {
        if (!s) return;
        _texCurrent = s.texture;
        EnsureQuad();
        _mat.mainTexture = _texCurrent;
    }

    void ApplyAlpha(float a)
    {
        if (_mat == null) return;
        var c = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(a));
        if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        _quadGO.SetActive(a > 0.001f);
    }

    void EnsureQuad()
    {
        if (_quadGO != null && _mat != null) return;
        if (_quadGO == null)
        {
            _quadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quadGO.name = "VisualCueQuad";
            DestroyImmediate(_quadGO.GetComponent<Collider>());
        }
        var mr = _quadGO.GetComponent<MeshRenderer>();
        if (_mat == null)
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(sh);
            _mat.renderQueue = 5000;
            mr.sharedMaterial = _mat;
        }
        ApplyAlpha(0f);
    }
}