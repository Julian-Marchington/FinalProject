using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GazeRayVisualizer — with One-Euro smoothing and fine alignment
/// </summary>
public class GazeRayVisualizer : MonoBehaviour
{
    // -------- One Euro (inline helpers) --------
    [System.Serializable]
    public class OneEuroFilter1D
    {
        float _xPrev, _dxPrev; bool _hasPrev = false;
        public float minCutoff = 1.2f, beta = 0.007f, dCutoff = 1.0f;

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(1e-6f, cutoff));
            return 1f / (1f + tau / Mathf.Max(1e-6f, dt));
        }
        public void Reset(float x0) { _xPrev = x0; _dxPrev = 0f; _hasPrev = true; }
        public float Filter(float x, float dt)
        {
            if (!_hasPrev) { Reset(x); return x; }
            float dx = (x - _xPrev) / Mathf.Max(1e-6f, dt);
            float aD = Alpha(dCutoff, dt);
            float dxHat = Mathf.Lerp(_dxPrev, dx, aD);
            float cutoff = minCutoff + beta * Mathf.Abs(dxHat);
            float aX = Alpha(cutoff, dt);
            float xHat = Mathf.Lerp(_xPrev, x, aX);
            _xPrev = xHat; _dxPrev = dxHat; return xHat;
        }
    }

    [System.Serializable]
    public class OneEuroFilter2D
    {
        public OneEuroFilter1D fx = new OneEuroFilter1D();
        public OneEuroFilter1D fy = new OneEuroFilter1D();
        public void Params(float minCut, float beta, float dCut)
        { fx.minCutoff = minCut; fx.beta = beta; fx.dCutoff = dCut; fy.minCutoff = minCut; fy.beta = beta; fy.dCutoff = dCut; }
        public void Reset(Vector2 v) { fx.Reset(v.x); fy.Reset(v.y); }
        public Vector2 Filter(Vector2 v, float dt) => new Vector2(fx.Filter(v.x, dt), fy.Filter(v.y, dt));
    }

    // ------------------ Sources ------------------
    [Header("Sources")]
    public RetinaFaceRunner retina;
    public Camera cam;

    // ------------------ Mapping ------------------
    [Header("Mapping")]
    [Tooltip("Leave 0 to auto-read from retina.inputSize (e.g., 640).")]
    public int retinaInputSizeOverride = 0;
    [Tooltip("World depth (metres) to place overlay in front of camera.")]
    public float overlayDepthM = 2.0f;

    [Header("Orientation Fixes (if mirrored/rotated)")]
    public bool flipX = false;
    public bool flipY = false;
    public bool rotate90 = false;

    // ------------------ Fine Alignment ------------------
    [Header("Fine Alignment Calibration (viewport space)")]
    public Vector2 uvOffset = Vector2.zero;
    public Vector2 uvScale = Vector2.one;

    // ------------------ Direction & Style ------------------
    [Header("Ray Direction")]
    [Tooltip("If true, ray points FROM face TOWARD camera.")]
    public bool invertRay = true;

    [Header("Heuristic Direction (landmark-based)")]
    public float yawGain = 35f;
    public float pitchGain = 15f;

    [Header("Ray Style")]
    public float rayLength = 1.25f;
    public float lineWidth = 0.01f;
    public Material lineMaterial;

    [Header("Colors")]
    public Color lookingRayColor = new Color(0.15f, 1f, 0.2f, 0.95f);
    public Color notLookingRayColor = new Color(1f, 0.4f, 0.2f, 0.85f);
    public Color landmarkColor = new Color(1f, 0.85f, 0.2f, 0.95f);
    public Color centerColor = new Color(0.2f, 0.6f, 1f, 0.95f);
    public Color hitColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Physics Raycast (optional)")]
    public bool doPhysicsRaycast = true;
    public float raycastHitRadius = 0.015f;
    public LayerMask raycastLayers = ~0;

    [Header("Landmark Spheres")]
    public float landmarkSphereRadius = 0.012f;
    public float centerSphereRadius = 0.010f;

    // Add field near other Ray Direction fields:
    [Tooltip("Force-flip yaw sign if needed.")]
    public bool invertYaw = false;

    // -------- One Euro controls for visualization --------
    [Header("Smoothing (One Euro)")]
    public bool smoothLandmarks = true;
    public float oeMinCutoff = 1.2f;
    public float oeBeta = 0.007f;
    public float oeDerivCutoff = 1.0f;

    [Tooltip("Max px distance to keep a face's smoothing state.")]
    public float trackMaxDistPx = 120f;
    [Tooltip("Forget tracks not seen for this many seconds.")]
    public float trackForgetSec = 0.8f;

    // ------------------ internals ------------------
    class LinePoolItem { public GameObject go; public LineRenderer lr; }
    class DotPoolItem { public GameObject go; public MeshRenderer mr; }
    readonly List<LinePoolItem> _linePool = new List<LinePoolItem>();
    readonly List<DotPoolItem> _dotPool = new List<DotPoolItem>();
    int _activeLines = 0, _activeDots = 0;
    Material _defaultMat;

    // Per-face smoothing tracks
    class FaceTrack
    {
        public Vector2 lastCenter;
        public float lastSeen;
        public OneEuroFilter2D fLE = new OneEuroFilter2D();
        public OneEuroFilter2D fRE = new OneEuroFilter2D();
        public OneEuroFilter2D fN = new OneEuroFilter2D();
        public OneEuroFilter1D fYaw = new OneEuroFilter1D();
        public OneEuroFilter1D fPit = new OneEuroFilter1D();
        public void SetParams(float minCut, float beta, float dCut)
        {
            fLE.Params(minCut, beta, dCut);
            fRE.Params(minCut, beta, dCut);
            fN.Params(minCut, beta, dCut);
            fYaw.minCutoff = minCut; fYaw.beta = beta; fYaw.dCutoff = dCut;
            fPit.minCutoff = minCut; fPit.beta = beta; fPit.dCutoff = dCut;
        }
    }
    readonly List<FaceTrack> _tracks = new List<FaceTrack>();

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!lineMaterial)
        {
            _defaultMat = new Material(Shader.Find("Sprites/Default"));
            _defaultMat.renderQueue = 3000;
        }
    }

    void LateUpdate()
    {
        if (!retina || !cam) { HideAll(); return; }

        int srcW, srcH;
        ResolveSourceSize(out srcW, out srcH);

        int inputSize; float scale, padX, padY;
        ResolveLetterbox(srcW, srcH, out inputSize, out scale, out padX, out padY);

        var faces = retina.FacesObs;
        _activeLines = 0; _activeDots = 0;

        // decay/forget old tracks
        float now = Time.time;
        for (int i = _tracks.Count - 1; i >= 0; --i)
            if (now - _tracks[i].lastSeen > trackForgetSec) _tracks.RemoveAt(i);

        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];

            // Model -> Source px (undo letterbox) -> orientation fixes
            Vector2 pLE = FixOrientation(ModelToSrc(f.LE, scale, padX, padY), srcW, srcH);
            Vector2 pRE = FixOrientation(ModelToSrc(f.RE, scale, padX, padY), srcW, srcH);
            Vector2 pN = FixOrientation(ModelToSrc(f.N, scale, padX, padY), srcW, srcH);
            Vector2 pC = FixOrientation(ModelToSrc(f.rect.center, scale, padX, padY), srcW, srcH);

            // ---- One-Euro smoothing (screen-space landmarks) ----
            Vector2 sLE = pLE, sRE = pRE, sN = pN, sC = pC;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);

            if (smoothLandmarks)
            {
                var tr = AcquireTrack(pC);
                tr.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);

                if (tr.lastSeen <= 0f) // first time
                {
                    tr.fLE.Reset(pLE); tr.fRE.Reset(pRE); tr.fN.Reset(pN);
                    tr.fYaw.Reset(0f); tr.fPit.Reset(0f);
                }

                sLE = tr.fLE.Filter(pLE, dt);
                sRE = tr.fRE.Filter(pRE, dt);
                sN = tr.fN.Filter(pN, dt);
                sC = tr.fLE.Filter(pC, dt); // reuse fLE just to smooth center consistently

                tr.lastCenter = sC;
                tr.lastSeen = now;
            }

            // Source px -> viewport (apply fine-calibration) -> world
            Vector3 wLE = ViewToWorld(SrcToUV(sLE, srcW, srcH));
            Vector3 wRE = ViewToWorld(SrcToUV(sRE, srcW, srcH));
            Vector3 wN = ViewToWorld(SrcToUV(sN, srcW, srcH));
            Vector3 wC = ViewToWorld(SrcToUV(sC, srcW, srcH));

            // Direction from (SMOOTHED + ORIENTED) landmarks
            EstimateYawPitchFromLandmarks(sLE, sRE, sN, out float yawDegRaw, out float pitchDegRaw);

            if (invertYaw) yawDegRaw = -yawDegRaw;

            // Optional smoothing on yaw/pitch themselves (prevents micro-wobble in ray)
            float yawDeg = yawDegRaw, pitchDeg = pitchDegRaw;
            if (smoothLandmarks)
            {
                var tr = FindNearestTrack(sC);
                if (tr != null)
                {
                    yawDeg = tr.fYaw.Filter(yawDegRaw, dt);
                    pitchDeg = tr.fPit.Filter(pitchDegRaw, dt);
                }
            }

            Vector3 dir = DirFromYawPitch(yawDeg, pitchDeg);
            Vector3 rayDir = invertRay ? -dir : dir;

            // Draw line
            var lr = GetLine().lr;
            lr.material = lineMaterial ? lineMaterial : _defaultMat;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.positionCount = 2;
            lr.startColor = lr.endColor = (f.isLooking ? lookingRayColor : notLookingRayColor);
            lr.SetPosition(0, wC);
            lr.SetPosition(1, wC + rayDir * rayLength);

            // Optional physics dot
            if (doPhysicsRaycast)
            {
                if (Physics.Raycast(new Ray(wC, rayDir.normalized), out var hit, rayLength, raycastLayers, QueryTriggerInteraction.Ignore))
                {
                    DrawDot(hit.point, raycastHitRadius, hitColor);
                }
            }

            // Landmarks
            DrawDot(wLE, landmarkSphereRadius, landmarkColor);
            DrawDot(wRE, landmarkSphereRadius, landmarkColor);
            DrawDot(wN, landmarkSphereRadius, landmarkColor);
            DrawDot(wC, centerSphereRadius, centerColor);
        }

        // Hide unused
        for (int i = _activeLines; i < _linePool.Count; i++) _linePool[i].go.SetActive(false);
        for (int i = _activeDots; i < _dotPool.Count; i++) _dotPool[i].go.SetActive(false);
    }

    // ---------- simple nearest-track matching ----------
    FaceTrack AcquireTrack(Vector2 centerPx)
    {
        FaceTrack tr = FindNearestTrack(centerPx);
        if (tr != null) return tr;
        tr = new FaceTrack { lastCenter = centerPx, lastSeen = 0f };
        tr.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);
        _tracks.Add(tr);
        return tr;
    }

    FaceTrack FindNearestTrack(Vector2 centerPx)
    {
        FaceTrack best = null; float bestD2 = trackMaxDistPx * trackMaxDistPx + 1f;
        for (int i = 0; i < _tracks.Count; i++)
        {
            float d2 = (centerPx - _tracks[i].lastCenter).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = _tracks[i]; }
        }
        return (bestD2 <= trackMaxDistPx * trackMaxDistPx) ? best : null;
    }

    // ---------- mapping helpers (unchanged from your version except kept tidy) ----------
    void ResolveSourceSize(out int srcW, out int srcH)
    {
        srcW = 0; srcH = 0;
        if (retina.useQuestPCA && retina.pcaFeed && retina.pcaFeed.WebCamTexture != null)
        {
            var wct = retina.pcaFeed.WebCamTexture;
            if (wct.width > 8 && wct.height > 8) { srcW = wct.width; srcH = wct.height; }
        }
        if (srcW <= 0 || srcH <= 0) { srcW = Screen.width; srcH = Screen.height; }
        srcW = Mathf.Max(16, srcW); srcH = Mathf.Max(16, srcH);
    }

    void ResolveLetterbox(int srcW, int srcH, out int inputSize, out float scale, out float padX, out float padY)
    {
        inputSize = (retinaInputSizeOverride > 0) ? retinaInputSizeOverride : Mathf.Max(1, retina.inputSize);
        // fallback approximate letterboxing (your reflection-based exact read is fine too)
        scale = Mathf.Min((float)inputSize / srcW, (float)inputSize / srcH);
        float drawW = srcW * scale, drawH = srcH * scale;
        padX = (inputSize - drawW) * 0.5f; padY = (inputSize - drawH) * 0.5f;
    }

    static Vector2 ModelToSrc(Vector2 modelPt, float scale, float padX, float padY)
    {
        float x = (modelPt.x - padX) / Mathf.Max(1e-6f, scale);
        float y = (modelPt.y - padY) / Mathf.Max(1e-6f, scale);
        return new Vector2(x, y);
    }

    Vector2 FixOrientation(Vector2 p, int srcW, int srcH)
    {
        float x = p.x, y = p.y;
        if (rotate90) { float rx = y; float ry = srcW - x; x = rx; y = ry; int t = srcW; srcW = srcH; srcH = t; }
        if (flipX) x = (srcW - 1) - x;
        if (flipY) y = (srcH - 1) - y;
        return new Vector2(x, y);
    }

    Vector2 SrcToUV(Vector2 srcPx, int srcW, int srcH)
    {
        float u = srcPx.x / Mathf.Max(1, srcW);
        float v = 1f - (srcPx.y / Mathf.Max(1, srcH)); // viewport Y-up
        u = (u * uvScale.x) + uvOffset.x;
        v = (v * uvScale.y) + uvOffset.y;
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    Vector3 ViewToWorld(Vector2 uv) =>
        cam.ViewportToWorldPoint(new Vector3(uv.x, uv.y, overlayDepthM));

    void EstimateYawPitchFromLandmarks(Vector2 LE, Vector2 RE, Vector2 N, out float yawDeg, out float pitchDeg)
    {
        Vector2 mid = 0.5f * (LE + RE);
        float eyeDist = Mathf.Max(1e-3f, (RE - LE).magnitude);
        float asymX = Mathf.Clamp((N.x - mid.x) / eyeDist, -1.2f, 1.2f);
        float asymY = Mathf.Clamp((N.y - mid.y) / eyeDist, -1.2f, 1.2f);
        yawDeg = Mathf.Clamp(asymX * yawGain, -75f, 75f);
        pitchDeg = Mathf.Clamp(-asymY * pitchGain, -50f, 50f);
    }

    Vector3 DirFromYawPitch(float yawDeg, float pitchDeg)
    {
        Quaternion qYaw = Quaternion.AngleAxis(yawDeg, cam.transform.up);
        Quaternion qPitch = Quaternion.AngleAxis(pitchDeg, cam.transform.right);
        return (qYaw * qPitch) * cam.transform.forward;
    }

    LinePoolItem GetLine()
    {
        if (_activeLines < _linePool.Count) { var item = _linePool[_activeLines++]; item.go.SetActive(true); return item; }
        var go = new GameObject($"GazeRay_{_linePool.Count}");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.numCapVertices = 4; lr.numCornerVertices = 4; lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.alignment = LineAlignment.View;
        var newItem = new LinePoolItem { go = go, lr = lr };
        _linePool.Add(newItem); _activeLines++; return newItem;
    }

    DotPoolItem GetDot()
    {
        if (_activeDots < _dotPool.Count) { var item = _dotPool[_activeDots++]; item.go.SetActive(true); return item; }
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var col = go.GetComponent<Collider>(); if (col) Destroy(col);
        go.name = $"Dot_{_dotPool.Count}"; go.transform.SetParent(transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var newItem = new DotPoolItem { go = go, mr = mr };
        _dotPool.Add(newItem); _activeDots++; return newItem;
    }

    void DrawDot(Vector3 pos, float radius, Color color)
    {
        var d = GetDot();
        d.go.transform.position = pos;
        d.go.transform.localScale = Vector3.one * (radius * 2f);
        var mat = lineMaterial ? lineMaterial : (_defaultMat ?? new Material(Shader.Find("Sprites/Default")));
        d.mr.material = mat; d.mr.material.color = color;
    }

    void HideAll()
    {
        for (int i = 0; i < _linePool.Count; i++) _linePool[i].go.SetActive(false);
        for (int i = 0; i < _dotPool.Count; i++) _dotPool[i].go.SetActive(false);
        _activeLines = _activeDots = 0;
    }
}