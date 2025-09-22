using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Globalization;

public class RetinaFaceRunner : MonoBehaviour
{
    // ========== One Euro Filter (inline helper) ==========
    [System.Serializable]
    public class OneEuroFilter1D
    {
        float _xPrev, _dxPrev;
        bool _hasPrev = false;

        public float minCutoff = 1.2f; // Hz
        public float beta = 0.007f;    // speed coefficient
        public float dCutoff = 1.0f;   // Hz

        static float Alpha(float cutoff, float dt)
        {
            // tau = 1/(2π f); alpha = 1/(1 + tau/dt)
            float tau = 1.0f / (2.0f * Mathf.PI * Mathf.Max(1e-6f, cutoff));
            return 1.0f / (1.0f + tau / Mathf.Max(1e-6f, dt));
        }

        public void Reset(float x0 = 0f)
        {
            _xPrev = x0; _dxPrev = 0f; _hasPrev = true;
        }

        public float Filter(float x, float dt)
        {
            if (!_hasPrev) { Reset(x); return x; }

            // derivative (raw)
            float dx = (x - _xPrev) / Mathf.Max(1e-6f, dt);
            // smooth derivative
            float aD = Alpha(dCutoff, dt);
            float dxHat = Mathf.Lerp(_dxPrev, dx, aD);

            // adaptive cutoff
            float cutoff = minCutoff + beta * Mathf.Abs(dxHat);
            float aX = Alpha(cutoff, dt);
            float xHat = Mathf.Lerp(_xPrev, x, aX);

            _xPrev = xHat;
            _dxPrev = dxHat;
            return xHat;
        }
    }

    [System.Serializable]
    public class OneEuroFilter2D
    {
        public OneEuroFilter1D fx = new OneEuroFilter1D();
        public OneEuroFilter1D fy = new OneEuroFilter1D();
        public void SetParams(float minCut, float beta, float dCut)
        { fx.minCutoff = minCut; fx.beta = beta; fx.dCutoff = dCut; fy.minCutoff = minCut; fy.beta = beta; fy.dCutoff = dCut; }
        public void Reset(Vector2 v) { fx.Reset(v.x); fy.Reset(v.y); }
        public Vector2 Filter(Vector2 v, float dt) => new Vector2(fx.Filter(v.x, dt), fy.Filter(v.y, dt));
    }

    // ------------- your original fields (unchanged unless commented) -------------
    [Header("Model (RetinaFace ONNX)")] public Unity.InferenceEngine.ModelAsset retinaModel;
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.CPU;

    [Header("Video Source")] public PCAFeedBinder pcaFeed; public bool useQuestPCA = true;
    [Header("Webcam (PC)")] public int requestWidth = 1280, requestHeight = 720, requestFPS = 30;

    [Header("Preprocess (RetinaFace standard)")]
    public int inputSize = 640; public bool inputIsBGR = true; public bool subtractBGRMean = true;
    public Vector3 bgrMean = new Vector3(104, 117, 123);

    [Header("Priors (RetinaFace default)")]
    public int[] steps = new int[] { 8, 16, 32 };
    public Vector2Int[] minSizes = new Vector2Int[] { new Vector2Int(16, 32), new Vector2Int(64, 128), new Vector2Int(256, 512) };
    public Vector2 variance = new Vector2(0.1f, 0.2f);

    [Header("Thresholds")]
    [Range(0.05f, 0.99f)] public float confThreshold = 0.6f;
    [Range(0.0f, 1.0f)] public float nmsIou = 0.4f;
    public float minFaceSidePx = 100f; public int maxFaces = 10;

    [Header("Look-at (landmark symmetry)")]
    public float maxNoseAsym = 0.12f; public float maxEyeTilt = 0.05f;

    [Header("Look-at (frontal thresholds, roll-invariant)")]
    public float maxRollDeg = 18f; public float maxNoseAsymX = 0.40f;
    public float minInterFrac = 0.22f; public float minInterAbsPx = 20f;

    [Header("Audio & Debounce")]
    public AudioSource audioSource; public AudioClip lookAtClip;
    public float enterCooldown = 0.8f; public bool beepOnLook = false;

    [Header("Look Stability (anti-flicker)")]
    public float enterLookDwellSec = 0.45f; public float exitLookDwellSec = 0.25f;
    public float noseAsymEnter = 0.35f; public float noseAsymExit = 0.50f;
    public float rollEnterDeg = 15f; public float rollExitDeg = 25f;
    [Range(0f, 1f)] public float iouTrackThreshold = 0.20f;

    [Header("Debug")] public bool drawGizmos = true;
    public Color boxColor = new Color(0, 1, 0, 0.8f);
    public Color landmarkColor = new Color(1, 0.7f, 0, 0.9f);

    [Header("Logging")] public bool logToFile = true;

    [Header("Experiment Metadata")]
    public string participantId = "P00"; public string conditionId = "COND";
    public string trialId = "T00"; public string sessionIdOverride = "";

    // Public status
    public int FacesCount { get; private set; } = -1;
    public int FacesLookingCount { get; private set; } = 0;
    public string LatestStatus { get; private set; } = "";

    Unity.InferenceEngine.Worker _wk; Unity.InferenceEngine.Model _model;
    WebCamTexture _pcWebcam; RenderTexture _rt; Texture2D _readback; Color32[] _cols; bool _haveCols;

    struct Prior { public float cx, cy, w, h; }
    List<Prior> _priors = new List<Prior>(20000);

    float _lastBeep = -999f;
    float _lastDetTime = -999f;
    Rect _lastBestRect;
    public float dropoutGraceSec = 0.55f;

    const int LANDMS = 5;
    struct Det { public Rect rect; public float score; public Vector2[] landmarks; }
    List<Det> _lastDets = new List<Det>(16);

    public struct FaceObs { public Rect rect; public float side; public float score; public Vector2 LE, RE, N; public bool isLooking; }
    public IReadOnlyList<FaceObs> FacesObs => _facesObs;
    readonly List<FaceObs> _facesObs = new List<FaceObs>(8);

    string _outLoc, _outConf, _outLandm;

    // Logging
    string _logPathTxt, _logPathCsv; StreamWriter _logTxt, _logCsv;
    System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    string _sessionId;

    // Best-face stability
    Rect _bestTrackRect; bool _bestLookStable;
    float _bestEnterTimer, _bestExitTimer; float _bestLastSeenTime;

    // ----- OLD EMA (kept for compatibility) -----
    [Range(0f, 1f)] public float smoothAlpha = 0.5f; // legacy

    // ----- NEW: One Euro smoothing for best-face landmarks -----
    [Header("Smoothing (One Euro for BEST face)")]
    public bool useOneEuro = true;
    [Tooltip("Base cutoff (Hz). Lower = smoother.")]
    public float oeMinCutoff = 1.2f;
    [Tooltip("Speed coefficient. Higher = react faster to motion.")]
    public float oeBeta = 0.007f;
    [Tooltip("Derivative cutoff (Hz).")]
    public float oeDerivCutoff = 1.0f;

    OneEuroFilter2D _oeLE = new OneEuroFilter2D();
    OneEuroFilter2D _oeRE = new OneEuroFilter2D();
    OneEuroFilter2D _oeN = new OneEuroFilter2D();
    bool _haveSmooth = false;
    Vector2 _LEs, _REs, _Ns; // smoothed

    // Edge-trigger beep
    bool _prevBestLook = false;
    bool _beepedForThisTrack = false;
    float _stableFalseTimer = 0f;
    public float rearmAfterFalseSec = 2.0f;

    string SessionId => string.IsNullOrEmpty(sessionIdOverride) ? _sessionId : sessionIdOverride;

    void Start()
    {
        // Source
        if (!useQuestPCA)
        {
            _pcWebcam = new WebCamTexture(requestWidth, requestHeight, requestFPS);
            _pcWebcam.Play();
        }

        // Model
        if (retinaModel == null)
        {
            Debug.LogError("[RET] Assign retinaface_resnet50.onnx to 'retinaModel'.");
            enabled = false; return;
        }
        _model = Unity.InferenceEngine.ModelLoader.Load(retinaModel);
        _wk = new Unity.InferenceEngine.Worker(_model, backend);
        Debug.Log("[RET] RetinaFace model loaded.");

        // Figure out output names from the model itself
        var outs = _model.outputs; // IList<Model.Output>
        if (outs != null && outs.Count >= 3)
        {
            foreach (var o in outs)
            {
                var name = o.name.ToLowerInvariant();
                if (name.Contains("loc")) _outLoc = o.name;
                if (name.Contains("conf")) _outConf = o.name;
                if (name.Contains("land")) _outLandm = o.name;
            }
            if (string.IsNullOrEmpty(_outLoc)) _outLoc = outs[0].name;
            if (string.IsNullOrEmpty(_outConf)) _outConf = outs[Mathf.Min(1, outs.Count - 1)].name;
            if (string.IsNullOrEmpty(_outLandm)) _outLandm = outs[Mathf.Min(2, outs.Count - 1)].name;

            Debug.Log($"[RET] Outputs -> loc:'{_outLoc}' conf:'{_outConf}' landms:'{_outLandm}' (declared {outs.Count})");
        }
        else
        {
            _outLoc = "loc"; _outConf = "conf"; _outLandm = "landms";
            Debug.LogWarning("[RET] Model outputs not listed; will try names: loc/conf/landms.");
        }

        // Buffers
        _rt = new RenderTexture(inputSize, inputSize, 0, RenderTextureFormat.ARGB32);
        _rt.Create();
        _readback = new Texture2D(inputSize, inputSize, TextureFormat.RGBA32, false);

        // Priors
        BuildPriors(inputSize, inputSize);
        Debug.Log($"[RET] Priors built: {_priors.Count}");

        // One Euro params
        _oeLE.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);
        _oeRE.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);
        _oeN.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);

        // ===== Logging setup =====
        if (logToFile)
        {
            _sessionId = System.DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            string baseName = "retinaface_" + SessionId;

            _logPathTxt = Path.Combine(Application.persistentDataPath, baseName + ".txt");
            _logPathCsv = Path.Combine(Application.persistentDataPath, baseName + ".csv");

            _logTxt = new StreamWriter(new FileStream(_logPathTxt, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _logCsv = new StreamWriter(new FileStream(_logPathCsv, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));

            _logTxt.WriteLine($"===== RETINAFACE START {System.DateTime.UtcNow:O} size={inputSize}x{inputSize} priors={_priors.Count} outs=({_outLoc},{_outConf},{_outLandm}) =====");
            _logTxt.WriteLine($"[META] participant={participantId} condition={conditionId} trial={trialId} session={SessionId}");

            _logCsv.WriteLine("utc,t_ms,participant,condition,trial,session,event,faces,numLooking,dirLR,best_w,best_h,best_score,best_look,best_x,best_y,LE_x,LE_y,RE_x,RE_y,N_x,N_y,meanScore,maxScore,dropout");
            LogParamsSnapshot();
            _logTxt.Flush(); _logCsv.Flush();

            Debug.Log("[RET] Logging to " + _logPathTxt + " and " + _logPathCsv);
            _sw.Reset(); _sw.Start();
        }
    }

    void OnDisable()
    {
        _wk?.Dispose();
        if (_pcWebcam != null && _pcWebcam.isPlaying) _pcWebcam.Stop();
        if (_rt != null) { _rt.Release(); _rt = null; }
        if (_readback != null) Destroy(_readback);
        if (_sw.IsRunning) _sw.Stop();
        if (_logTxt != null) { _logTxt.Flush(); _logTxt.Close(); _logTxt.Dispose(); _logTxt = null; }
        if (_logCsv != null) { _logCsv.Flush(); _logCsv.Close(); _logCsv.Dispose(); _logCsv = null; }
    }

    void Update()
    {
        Texture src = null;
        if (useQuestPCA)
        {
            if (pcaFeed == null || pcaFeed.WebCamTexture == null) return;
            src = pcaFeed.WebCamTexture;
        }
        else
        {
            if (_pcWebcam == null || !_pcWebcam.didUpdateThisFrame) return;
            src = _pcWebcam;
        }

        // ---- Copy -> square (PRESERVE ASPECT via letterbox) ----
        if (src.width <= 8 || src.height <= 8) return;

        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        GL.Clear(true, true, Color.black);

        float srcW = src.width, srcH = src.height;
        float scale = Mathf.Min((float)inputSize / srcW, (float)inputSize / srcH);
        float drawW = srcW * scale, drawH = srcH * scale;
        float x = (inputSize - drawW) * 0.5f;
        float y = (inputSize - drawH) * 0.5f;

        GL.PushMatrix();
        GL.LoadPixelMatrix(0, inputSize, inputSize, 0); // draw in pixel space
        Graphics.DrawTexture(new Rect(x, y, drawW, drawH), src);
        GL.PopMatrix();

        _readback.ReadPixels(new Rect(0, 0, inputSize, inputSize), 0, 0);
        _readback.Apply(false);
        _cols = _readback.GetPixels32(); _haveCols = true;
        RenderTexture.active = prev;

        if (!_haveCols) return;

        _lastDets = InferOnceAndDecode();

        // FIRST: handle zero case (keep last faces during grace)
        if (_lastDets.Count == 0)
        {
            if (Time.time - _lastDetTime <= dropoutGraceSec)
            {
                FacesCount = 1;
                LatestStatus = $"Faces: 1 | Looking#: {FacesLookingCount} (grace)";
                return;
            }
            FacesCount = 0;
            FacesLookingCount = 0;
            LatestStatus = "Faces: 0 | Looking#: 0";
            _facesObs.Clear();
            LogLine("faces=0");
            LogFrameSummary(0, 0, 0f, false, new Rect(), 0f, false, Vector2.zero, Vector2.zero, Vector2.zero, "FACES_0", meanScore: 0f, maxScore: 0f, dropout: 1);
            return;
        }

        // ELSE: publish per-face instantaneous obs (for UI/diagnostics)
        _facesObs.Clear();
        float sumScore = 0f, maxScore = 0f;
        for (int i = 0; i < _lastDets.Count; i++)
        {
            var d = _lastDets[i];
            bool lk = IsFrontal(d); // instantaneous
            _facesObs.Add(new FaceObs
            {
                rect = d.rect,
                side = Mathf.Min(d.rect.width, d.rect.height),
                score = d.score,
                LE = d.landmarks[0],
                RE = d.landmarks[1],
                N = d.landmarks[2],
                isLooking = lk
            });
            sumScore += d.score;
            if (d.score > maxScore) maxScore = d.score;
        }

        var best = _lastDets.OrderByDescending(d => Mathf.Min(d.rect.width, d.rect.height)).First();
        _lastBestRect = best.rect;
        _lastDetTime = Time.time;

        // --- Anti-flicker: hysteresis + dwell + simple tracking for BEST face ---
        bool sameTarget = (_bestTrackRect.width > 0f) && (IoU(best.rect, _bestTrackRect) >= iouTrackThreshold);
        if (!sameTarget) { _bestEnterTimer = 0f; _bestExitTimer = 0f; }
        _bestTrackRect = best.rect;
        _bestLastSeenTime = Time.time;

        // --- SMOOTH best-face landmarks before decision ---
        if (!sameTarget || !_haveSmooth)
        {
            _LEs = best.landmarks[0];
            _REs = best.landmarks[1];
            _Ns = best.landmarks[2];

            // reset filters when target changes
            _oeLE.Reset(_LEs); _oeRE.Reset(_REs); _oeN.Reset(_Ns);
            _haveSmooth = true;
        }
        else
        {
            float dtFilter = Mathf.Max(1e-4f, Time.deltaTime);
            if (useOneEuro)
            {
                // live-update parameters (if tweaked in inspector)
                _oeLE.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);
                _oeRE.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);
                _oeN.SetParams(oeMinCutoff, oeBeta, oeDerivCutoff);

                _LEs = _oeLE.Filter(best.landmarks[0], dtFilter);
                _REs = _oeRE.Filter(best.landmarks[1], dtFilter);
                _Ns = _oeN.Filter(best.landmarks[2], dtFilter);
            }
            else
            {
                float a = smoothAlpha; // legacy EMA
                _LEs = Vector2.Lerp(_LEs, best.landmarks[0], a);
                _REs = Vector2.Lerp(_REs, best.landmarks[1], a);
                _Ns = Vector2.Lerp(_Ns, best.landmarks[2], a);
            }
        }

        var feats = ComputeLookFeaturesFromPoints(best, _LEs, _REs, _Ns);

        // ENTER: stricter thresholds
        bool enterOk = feats.validGeom && (feats.noseAsymX <= noseAsymEnter) && (feats.rollDeg <= rollEnterDeg);
        // EXIT: looser thresholds
        bool exitOk = (!feats.validGeom) || (feats.noseAsymX > noseAsymExit) || (feats.rollDeg > rollExitDeg);

        float dt = Time.deltaTime; // <-- timer dt (separate from dtFilter)
        bool before = _bestLookStable;

        if (_bestLookStable)
        {
            _bestExitTimer = exitOk ? _bestExitTimer + dt : 0f;
            if (_bestExitTimer >= exitLookDwellSec)
            {
                _bestLookStable = false;
                _bestExitTimer = 0f;
                LogFrameSummary(_lastDets.Count, CountLookers(), ComputeDirLRFromLookers(), true, best.rect, best.score, false, _LEs, _REs, _Ns, "LOOK_EXIT",
                                meanScore: sumScore / Mathf.Max(1, _lastDets.Count), maxScore: maxScore, dropout: 0);
            }
        }
        else
        {
            _bestEnterTimer = enterOk ? _bestEnterTimer + dt : 0f;
            if (_bestEnterTimer >= enterLookDwellSec)
            {
                _bestLookStable = true;
                _bestEnterTimer = 0f;
                LogFrameSummary(_lastDets.Count, CountLookers(), ComputeDirLRFromLookers(), true, best.rect, best.score, true, _LEs, _REs, _Ns, "LOOK_ENTER",
                                meanScore: sumScore / Mathf.Max(1, _lastDets.Count), maxScore: maxScore, dropout: 0);
            }
        }

        bool bestLook = _bestLookStable;

        LogLine(
          $"faces={_lastDets.Count} best=[{best.rect.x:F1},{best.rect.y:F1},{best.rect.width:F1},{best.rect.height:F1}] " +
          $"score={best.score:F2} bestLook={bestLook} " +
          $"LE=({best.landmarks[0].x:F1},{best.landmarks[0].y:F1}) RE=({best.landmarks[1].x:F1},{best.landmarks[1].y:F1}) " +
          $"N=({best.landmarks[2].x:F1},{best.landmarks[2].y:F1}) LM=({best.landmarks[3].x:F1},{best.landmarks[3].y:F1}) RM=({best.landmarks[4].x:F1},{best.landmarks[4].y:F1})"
        );

        // Count how many are looking this frame (instantaneous UI only)
        int numLooking = CountLookers();

        FacesCount = _lastDets.Count;
        FacesLookingCount = numLooking;
        LatestStatus = $"Faces: {FacesCount} | Looking#: {FacesLookingCount}";

        // Structured per-frame CSV row
        float dirLR = ComputeDirLRFromLookers();

        LogFrameSummary(
            faces: _lastDets.Count,
            numLooking: FacesLookingCount,
            dirLR: dirLR,
            hasBest: true,
            bestRect: best.rect,
            bestScore: best.score,
            bestLook: bestLook,
            LE: _LEs,
            RE: _REs,
            N: _Ns,
            evt: "FRAME",
            meanScore: sumScore / Mathf.Max(1, _lastDets.Count),
            maxScore: maxScore,
            dropout: 0
        );

        // ===== Edge-triggered beep with re-arm =====
        if (!sameTarget)
        {
            _beepedForThisTrack = false;
            _stableFalseTimer = 0f;
        }
        if (_bestLookStable)
        {
            _stableFalseTimer = 0f;
        }
        else
        {
            _stableFalseTimer += Time.deltaTime;
            if (_stableFalseTimer >= rearmAfterFalseSec)
            {
                _beepedForThisTrack = false;
            }
        }
        if (beepOnLook && _bestLookStable && !_prevBestLook && !_beepedForThisTrack)
        {
            TryBeep();
            _beepedForThisTrack = true;
            LogFrameSummary(_lastDets.Count, FacesLookingCount, dirLR, true, best.rect, best.score, bestLook, _LEs, _REs, _Ns, "BEEP",
                            meanScore: sumScore / Mathf.Max(1, _lastDets.Count), maxScore: maxScore, dropout: 0);
        }
        _prevBestLook = _bestLookStable;
    }

    int CountLookers()
    {
        int n = 0; for (int i = 0; i < _facesObs.Count; i++) if (_facesObs[i].isLooking) n++; return n;
    }

    void BuildPriors(int w, int h)
    {
        _priors.Clear();
        for (int s = 0; s < steps.Length; s++)
        {
            int step = steps[s];
            int fmW = Mathf.CeilToInt((float)w / step);
            int fmH = Mathf.CeilToInt((float)h / step);

            int ms0 = minSizes[s].x, ms1 = minSizes[s].y;
            int[] sizes = new int[] { ms0, ms1 };

            for (int y = 0; y < fmH; y++)
                for (int x = 0; x < fmW; x++)
                {
                    float cx = (x + 0.5f) * step / w;
                    float cy = (y + 0.5f) * step / h;
                    foreach (var m in sizes)
                    {
                        float pw = (float)m / w;
                        float ph = (float)m / h;
                        _priors.Add(new Prior { cx = cx, cy = cy, w = pw, h = ph });
                    }
                }
        }
    }

    List<Det> InferOnceAndDecode()
    {
        var outDets = new List<Det>(16);

        using (var input = new Unity.InferenceEngine.Tensor<float>(
            new Unity.InferenceEngine.TensorShape(1, 3, inputSize, inputSize)))
        {
            int plane = inputSize * inputSize;
            int iR = 0 * plane, iG = 1 * plane, iB = 2 * plane;

            for (int y = 0; y < inputSize; y++)
            {
                int row = y * inputSize;
                for (int x = 0; x < inputSize; x++)
                {
                    int ofs = row + x;
                    var c = _cols[ofs];
                    float r = c.r, g = c.g, b = c.b;

                    if (subtractBGRMean)
                    {
                        if (inputIsBGR) { input[ofs + iR] = b - bgrMean.x; input[ofs + iG] = g - bgrMean.y; input[ofs + iB] = r - bgrMean.z; }
                        else { input[ofs + iR] = r - bgrMean.z; input[ofs + iG] = g - bgrMean.y; input[ofs + iB] = b - bgrMean.x; }
                    }
                    else
                    {
                        if (inputIsBGR) { input[ofs + iR] = b / 255f; input[ofs + iG] = g / 255f; input[ofs + iB] = r / 255f; }
                        else { input[ofs + iR] = r / 255f; input[ofs + iG] = g / 255f; input[ofs + iB] = b / 255f; }
                    }
                }
            }
            _wk.Schedule(input);
        }

        var locT = _wk.PeekOutput(_outLoc) as Unity.InferenceEngine.Tensor<float>;
        var confT = _wk.PeekOutput(_outConf) as Unity.InferenceEngine.Tensor<float>;
        var landmT = _wk.PeekOutput(_outLandm) as Unity.InferenceEngine.Tensor<float>;

        if (locT == null) locT = _wk.PeekOutput("loc") as Unity.InferenceEngine.Tensor<float>;
        if (confT == null) confT = _wk.PeekOutput("conf") as Unity.InferenceEngine.Tensor<float>;
        if (landmT == null) landmT = _wk.PeekOutput("landms") as Unity.InferenceEngine.Tensor<float>;

        if (locT == null || confT == null || landmT == null)
        {
            LogLine($"ERROR missing outputs. Tried '{_outLoc}','{_outConf}','{_outLandm}' and defaults.");
            if (_logCsv != null)
            {
                long tt = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
                _logCsv.WriteLine($"{System.DateTime.UtcNow:O},{tt},{participantId},{conditionId},{trialId},{SessionId},ERROR,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1");
                _logCsv.Flush();
            }
            return outDets;
        }

        var loc = locT.DownloadToArray();
        var conf = confT.DownloadToArray();
        var landm = landmT.DownloadToArray();

        int nPriors = _priors.Count;
        if (loc.Length < 4 || landm.Length < 10 || conf.Length < nPriors)
        {
            LogLine($"ERROR unexpected output sizes: priors={nPriors} loc={loc.Length} conf={conf.Length} landm={landm.Length}");
            if (_logCsv != null)
            {
                long tt = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
                _logCsv.WriteLine($"{System.DateTime.UtcNow:O},{tt},{participantId},{conditionId},{trialId},{SessionId},ERROR,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1");
                _logCsv.Flush();
            }
            return outDets;
        }

        bool confTwoChan = (conf.Length == 2 * nPriors) || (conf.Length % nPriors == 0 && (conf.Length / nPriors) == 2);
        var cand = new List<Det>(64);

        for (int i = 0; i < nPriors; i++)
        {
            float score = confTwoChan ? conf[i * 2 + 1] : conf[i];
            if (score < confThreshold) continue;

            var pr = _priors[i];

            int l = i * 4;
            float dx = loc[l + 0], dy = loc[l + 1], dw = loc[l + 2], dh = loc[l + 3];

            float cx = pr.cx + dx * variance.x * pr.w;
            float cy = pr.cy + dy * variance.x * pr.h;
            float w = pr.w * Mathf.Exp(dw * variance.y);
            float h = pr.h * Mathf.Exp(dh * variance.y);

            float x0 = (cx - 0.5f * w) * inputSize;
            float y0 = (cy - 0.5f * h) * inputSize;
            float x1 = (cx + 0.5f * w) * inputSize;
            float y1 = (cy + 0.5f * h) * inputSize;

            var r = new Rect(x0, y0, x1 - x0, y1 - y0);
            if (Mathf.Min(r.width, r.height) < minFaceSidePx) continue;

            int baseLm = i * 10;
            Vector2[] pts = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float lx = landm[baseLm + k * 2 + 0];
                float ly = landm[baseLm + k * 2 + 1];
                float px = pr.cx + lx * variance.x * pr.w;
                float py = pr.cy + ly * variance.x * pr.h;
                pts[k] = new Vector2(px * inputSize, py * inputSize);
            }

            cand.Add(new Det { rect = ClampRect(r, inputSize), score = score, landmarks = pts });
        }

        var kept = NMS(cand, nmsIou).OrderByDescending(d => d.score).Take(maxFaces).ToList();
        return kept;
    }

    Rect ClampRect(Rect r, int size)
    {
        float x = Mathf.Clamp(r.x, 0, size - 1);
        float y = Mathf.Clamp(r.y, 0, size - 1);
        float w = Mathf.Clamp(r.width, 1, size - x);
        float h = Mathf.Clamp(r.height, 1, size - y);
        return new Rect(x, y, w, h);
    }

    List<Det> NMS(List<Det> dets, float iou)
    {
        var sorted = dets.OrderByDescending(d => d.score).ToList();
        var kept = new List<Det>();
        while (sorted.Count > 0)
        {
            var a = sorted[0]; kept.Add(a); sorted.RemoveAt(0);
            for (int j = sorted.Count - 1; j >= 0; j--)
                if (IoU(a.rect, sorted[j].rect) > iou) sorted.RemoveAt(j);
        }
        return kept;
    }
    float IoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
        float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float uni = a.width * a.height + b.width * b.height - inter;
        return uni <= 0 ? 0 : inter / uni;
    }

    // Per-face instantaneous test (kept for diagnostics/UI)
    bool IsFrontal(Det d)
    {
        var LE = d.landmarks[0];
        var RE = d.landmarks[1];
        var N = d.landmarks[2];

        // 1) Basic geometry checks (use face WIDTH + absolute px floor)
        float inter = Vector2.Distance(LE, RE);
        float faceW = d.rect.width;
        float req = Mathf.Max(minInterFrac * faceW, minInterAbsPx);
        if (inter <= 1e-3f) return false;
        if (inter < req) return false; // landmarks too collapsed / unreliable

        // 2) Roll tolerance (angle of the eye line)
        float dx = RE.x - LE.x, dy = RE.y - LE.y;
        float rollDeg = Mathf.Abs(Mathf.Rad2Deg * Mathf.Atan2(dy, dx));
        if (rollDeg > 90f) rollDeg = 180f - rollDeg; // wrap to [0,90]
        if (rollDeg > maxRollDeg) return false;

        // 3) De-roll: rotate points so eyes are horizontal
        Vector2 midEye = 0.5f * (LE + RE);
        float ang = Mathf.Atan2(dy, dx);
        float cs = Mathf.Cos(-ang), sn = Mathf.Sin(-ang);
        System.Func<Vector2, Vector2> R = p => {
            var dxy = p - midEye;
            return new Vector2(dxy.x * cs - dxy.y * sn, dxy.x * sn + dxy.y * cs) + midEye;
        };
        var LE2 = R(LE); var RE2 = R(RE); var N2 = R(N);

        // 4) Yaw proxy = horizontal nose offset normalised by horizontal eye spacing
        float horiz = Mathf.Abs(RE2.x - LE2.x);
        if (horiz <= 1e-3f) return false;

        float noseAsymX = Mathf.Abs(N2.x - midEye.x) / horiz;

        // 5) Pass if roll is okay and yaw (nose asym) is within tolerance
        return (rollDeg <= rollEnterDeg) && (noseAsymX <= noseAsymEnter);
    }

    // Features for hysteresis/dwell decision on BEST face (smoothed points)
    struct LookFeatures
    {
        public bool validGeom;
        public float noseAsymX;
        public float rollDeg;
    }
    LookFeatures ComputeLookFeaturesFromPoints(Det d, Vector2 LE, Vector2 RE, Vector2 N)
    {
        float inter = Vector2.Distance(LE, RE);
        float faceW = d.rect.width;
        float req = Mathf.Max(minInterFrac * faceW, minInterAbsPx);
        if (inter <= 1e-3f || inter < req)
            return new LookFeatures { validGeom = false, noseAsymX = 999f, rollDeg = 999f };

        float dx = RE.x - LE.x, dy = RE.y - LE.y;
        float rollDeg = Mathf.Abs(Mathf.Rad2Deg * Mathf.Atan2(dy, dx));
        if (rollDeg > 90f) rollDeg = 180f - rollDeg;

        Vector2 midEye = 0.5f * (LE + RE);
        float ang = Mathf.Atan2(dy, dx);
        float cs = Mathf.Cos(-ang), sn = Mathf.Sin(-ang);
        System.Func<Vector2, Vector2> R = p => {
            var dxy = p - midEye;
            return new Vector2(dxy.x * cs - dxy.y * sn, dxy.x * sn + dxy.y * cs) + midEye;
        };
        var LE2 = R(LE); var RE2 = R(RE); var N2 = R(N);

        float horiz = Mathf.Abs(RE2.x - LE2.x);
        if (horiz <= 1e-3f)
            return new LookFeatures { validGeom = false, noseAsymX = 999f, rollDeg = 999f };

        float noseAsymX = Mathf.Abs(N2.x - midEye.x) / horiz;

        return new LookFeatures { validGeom = true, noseAsymX = noseAsymX, rollDeg = rollDeg };
    }

    void TryBeep()
    {
        if (audioSource == null || lookAtClip == null) return;
        if (Time.time - _lastBeep < enterCooldown) return;
        audioSource.PlayOneShot(lookAtClip, 1f);
        _lastBeep = Time.time;
    }

    // ===== Enhanced logging helpers =====

    void LogParamsSnapshot()
    {
        if (_logCsv == null) return;
        string kv = $"confThreshold={confThreshold};nmsIou={nmsIou};minFaceSidePx={minFaceSidePx};maxFaces={maxFaces};" +
                    $"maxRollDeg={maxRollDeg};maxNoseAsymX={maxNoseAsymX};minInterFrac={minInterFrac};minInterAbsPx={minInterAbsPx};" +
                    $"dropoutGraceSec={dropoutGraceSec};inputSize={inputSize};backend={backend};useQuestPCA={useQuestPCA};" +
                    $"enterLookDwellSec={enterLookDwellSec};exitLookDwellSec={exitLookDwellSec};" +
                    $"noseAsymEnter={noseAsymEnter};noseAsymExit={noseAsymExit};rollEnterDeg={rollEnterDeg};rollExitDeg={rollExitDeg};" +
                    $"iouTrackThreshold={iouTrackThreshold};smoothAlpha={smoothAlpha}";
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
        _logCsv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},PARAMS,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");
        _logTxt?.WriteLine($"[{System.DateTime.UtcNow:O}] PARAMS {kv}");
    }

    void LogFrameSummary(
        int faces, int numLooking, float dirLR,
        bool hasBest, Rect bestRect, float bestScore, bool bestLook,
        Vector2 LE, Vector2 RE, Vector2 N, string evt = "FRAME",
        float meanScore = 0f, float maxScore = 0f, int dropout = 0)
    {
        if (_logCsv == null) return;
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;

        string line;
        if (hasBest)
        {
            line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9:F3},{10:F1},{11:F1},{12:F3},{13},{14:F1},{15:F1},{16:F1},{17:F1},{18:F1},{19:F1},{20:F1},{21:F1},{22:F3},{23:F3},{24}",
                System.DateTime.UtcNow.ToString("O"), t, participantId, conditionId, trialId, SessionId, evt,
                faces, numLooking, dirLR,
                bestRect.width, bestRect.height, bestScore, bestLook ? 1 : 0,
                bestRect.x, bestRect.y, LE.x, LE.y, RE.x, RE.y, N.x, N.y,
                meanScore, maxScore, dropout
            );
        }
        else
        {
            line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9:F3},0,0,0,0,0,0,0,0,0,0,0,0,{10:F3},{11:F3},{12}",
                System.DateTime.UtcNow.ToString("O"), t, participantId, conditionId, trialId, SessionId, evt,
                faces, numLooking, dirLR, meanScore, maxScore, dropout
            );
        }

        _logCsv.WriteLine(line);
        _logCsv.Flush();
    }

    // Text log (human-readable)
    void LogLine(string s)
    {
        string line = $"[{System.DateTime.UtcNow:O}] {s}";
        Debug.Log("[RET] " + line);
        if (_logTxt != null) { _logTxt.WriteLine(line); _logTxt.Flush(); }
    }

    // Direction of "lookers" centre-of-mass. -1 = left, +1 = right
    float ComputeDirLRFromLookers()
    {
        if (_facesObs == null || _facesObs.Count == 0) return 0f;
        float sumX = 0f, sumW = 0f;
        for (int i = 0; i < _facesObs.Count; i++)
        {
            var f = _facesObs[i];
            if (!f.isLooking) continue;
            float cx = f.rect.center.x;
            float w = f.rect.width;
            sumX += cx * w;
            sumW += w;
        }
        if (sumW <= 0f) return 0f;
        float cxNorm = (sumX / sumW) / Mathf.Max(1f, inputSize); // 0..1
        return Mathf.Clamp((cxNorm - 0.5f) * 2f, -1f, 1f);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || _lastDets == null) return;
        foreach (var d in _lastDets)
        {
            Gizmos.color = boxColor; DrawRect(d.rect);
            Gizmos.color = landmarkColor; foreach (var p in d.landmarks) Gizmos.DrawSphere(P2V(p), 2f);
        }
    }
    void DrawRect(Rect r)
    {
        Vector3 a = P2V(new Vector2(r.xMin, r.yMin));
        Vector3 b = P2V(new Vector2(r.xMax, r.yMin));
        Vector3 c = P2V(new Vector2(r.xMax, r.yMax));
        Vector3 d = P2V(new Vector2(r.xMin, r.yMax));
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
    }
    Vector3 P2V(Vector2 p) => new Vector3(p.x - inputSize * 0.5f, -(p.y - inputSize * 0.5f), 0f);
}