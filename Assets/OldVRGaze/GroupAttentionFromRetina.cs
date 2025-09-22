﻿// GroupAttentionFromRetina.cs
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class GroupAttentionFromRetina : MonoBehaviour
{
    public RetinaFaceRunner retina;

    [Header("Tracking")]
    [Tooltip("Max tracks to maintain")]
    public int maxTracks = 8;
    [Tooltip("IoU to match a detection to an existing track")]
    [Range(0f,1f)] public float matchIou = 0.4f;
    [Tooltip("Seconds a track stays alive without a match")]
    public float trackTimeoutSec = 0.4f;
    [Tooltip("Ignore faces smaller than this (px)")]
    public float minFaceSidePxGroup = 90f;

    [Header("Per-face look dwell")]
    public float lookDwellSec = 0.35f;
    public float lookReleaseSec = 0.30f;

    [Header("Group addressed dwell")]
    [Tooltip("Proportion of faces that must be looking, e.g., 0.5 = half the group")]
    [Range(0f,1f)] public float groupMinProportion = 0.5f;
    public float enterStableSec = 0.80f;   // need sustained majority
    public float exitStableSec  = 0.50f;   // allow short dropouts

    [Header("Direction smoothing")]
    [Tooltip("0..1 low-pass factor per frame for AddressDirLR (higher = snappier)")]
    [Range(0f,1f)] public float dirLerp = 0.2f;

    // Outputs (contract used by TurnTakingOrchestrator)
    public float AttentionScore { get; private set; }  // 0..1 proportion looking
    public int   Faces          { get; private set; }  // active tracks
    public int   LookingFaces   { get; private set; }  // how many tracks are 'looked'
    public float SecondsSinceAddressed { get; private set; } = 999f;

    public bool  IsGroupAddressed { get { return _groupAddressed; } }   // stable flag
    /// <summary>Direction of addressed cluster, -1 = left, +1 = right (camera/image space)</summary>
    public float AddressDirLR { get; private set; } = 0f;

    class Track {
        public Rect rect;
        public float lastSeen;
        public bool  looked;
        public float lookHold, notHold;
    }
    readonly List<Track> _tracks = new List<Track>(12);
    float _enterTimer, _exitTimer;
    bool  _groupAddressed;

    float _dirSmoothed = 0f;

    // --- Experiment Metadata & Logging ---
    [Header("Experiment Metadata")]
    public string participantId = "P00";
    public string conditionId   = "COND";
    public string trialId       = "T00";
    public string sessionIdOverride = "";

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
            string baseName = $"ga_{SessionId}.csv";
            _csvPath = Path.Combine(Application.persistentDataPath, baseName);
            _csv = new StreamWriter(new FileStream(_csvPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _csv.WriteLine("utc,t_ms,participant,condition,trial,session,event,faces,looking,attScore,addrStable,enterTimer,exitTimer,dirLR");
            _csv.Flush();
            _sw.Start();
        }
    }

    void OnDisable()
    {
        if (_csv != null) { _csv.Flush(); _csv.Close(); _csv.Dispose(); _csv = null; }
        if (_sw.IsRunning) _sw.Stop();
    }

    void LogRow(string evt)
    {
        if (_csv == null) return;
        long t = _sw.IsRunning ? _sw.ElapsedMilliseconds : 0;
        _csv.WriteLine($"{System.DateTime.UtcNow:O},{t},{participantId},{conditionId},{trialId},{SessionId},{evt},{Faces},{LookingFaces},{AttentionScore:F2},{(_groupAddressed?1:0)},{_enterTimer:F2},{_exitTimer:F2},{AddressDirLR:F2}");
        _csv.Flush();
    }

    void Update()
    {
        if (!retina) return;
        float now = Time.time;
        var faces = retina.FacesObs; // per-frame faces + isLooking

        // 1) expire old tracks
        for (int i = _tracks.Count-1; i >= 0; --i)
            if (now - _tracks[i].lastSeen > trackTimeoutSec) _tracks.RemoveAt(i);

        // 2) match detections to tracks by IoU
        var used = new bool[faces.Count];
        for (int i=0; i<faces.Count; i++)
        {
            if (faces[i].side < minFaceSidePxGroup) continue;
            int best = -1; float bestIou = 0f;
            for (int t=0; t<_tracks.Count; t++)
            {
                float iou = IoU(faces[i].rect, _tracks[t].rect);
                if (iou > bestIou) { bestIou = iou; best = t; }
            }
            if (best >= 0 && bestIou >= matchIou)
            {
                // update track
                _tracks[best].rect = faces[i].rect;
                _tracks[best].lastSeen = now;
                UpdateLook(_tracks[best], faces[i].isLooking);
                used[i] = true;
            }
        }

        // 3) create new tracks for unmatched detections
        for (int i=0; i<faces.Count && _tracks.Count < maxTracks; i++)
        {
            if (used[i]) continue;
            if (faces[i].side < minFaceSidePxGroup) continue;
            var tr = new Track{ rect = faces[i].rect, lastSeen = now };
            UpdateLook(tr, faces[i].isLooking, fresh:true);
            _tracks.Add(tr);
        }

        // 4) group stats
        int active = _tracks.Count;
        int looking = 0;
        for (int t=0; t<_tracks.Count; t++) if (_tracks[t].looked) looking++;
        Faces = active;
        LookingFaces = looking;
        AttentionScore = (active == 0) ? 0f : (float)looking / active;

        // 5) stable “addressed by group” with dwell/hysteresis
        bool candidate = AttentionScore >= groupMinProportion && active > 0;
        float dt = Mathf.Max(0f, Time.deltaTime);
        if (candidate) { _enterTimer += dt; _exitTimer = 0f; }
        else           { _exitTimer  += dt; _enterTimer = 0f; }

        bool wasAddr = _groupAddressed;
        if (!_groupAddressed && _enterTimer >= enterStableSec) _groupAddressed = true;
        if (_groupAddressed && _exitTimer  >= exitStableSec)  _groupAddressed = false;

        if (_groupAddressed) SecondsSinceAddressed = 0f;
        else                 SecondsSinceAddressed += dt;

        // 6) compute AddressDirLR (−1..+1), smoothed
        AddressDirLR = SmoothDir(ComputeDirLR(), dirLerp);

        // Log per-frame and transitions
        LogRow("FRAME");
        if (!wasAddr && _groupAddressed) LogRow("ADDR_ENTER");
        if (wasAddr && !_groupAddressed) LogRow("ADDR_EXIT");

        if (Time.frameCount % 10 == 0) // ~6 Hz
            Debug.Log($"[GA] Faces={Faces} Looking={LookingFaces} Att={AttentionScore:F2} Addr={IsGroupAddressed} Dir={AddressDirLR:F2}");
    }

    void UpdateLook(Track tr, bool sampleLook, bool fresh=false)
    {
        float dt = Mathf.Max(0f, Time.deltaTime);
        if (fresh) { tr.lookHold = tr.notHold = 0f; tr.looked = sampleLook; return; }
        if (sampleLook)
        {
            tr.lookHold += dt; tr.notHold = 0f;
            if (!tr.looked && tr.lookHold >= lookDwellSec) tr.looked = true;
        }
        else
        {
            tr.notHold  += dt; tr.lookHold = 0f;
            if ( tr.looked && tr.notHold  >= lookReleaseSec) tr.looked = false;
        }
    }

    float ComputeDirLR()
    {
        if (_tracks.Count == 0) return 0f;

        // Prefer only "looked" tracks; if none, fall back to all tracks
        float sumX = 0f, sumW = 0f;
        int lookedCount = 0;
        foreach (var tr in _tracks) if (tr.looked) lookedCount++;

        foreach (var tr in _tracks)
        {
            bool include = (lookedCount > 0) ? tr.looked : true;
            if (!include) continue;

            float cx = tr.rect.center.x;
            float w  = tr.rect.width; // weight by apparent size
            sumX += cx * w;
            sumW += w;
        }
        if (sumW <= 0f) return 0f;

        // Normalize to −1..+1 across Retina input size
        float width = Mathf.Max(1f, retina.inputSize);
        float cxNorm = (sumX / sumW) / width;         // 0..1
        float dir = Mathf.Clamp((cxNorm - 0.5f) * 2f, -1f, 1f);
        return dir;
    }

    float SmoothDir(float target, float lerp)
    {
        // Exponential smoothing per frame
        _dirSmoothed = Mathf.Lerp(_dirSmoothed, target, Mathf.Clamp01(lerp));
        return _dirSmoothed;
    }

    static float IoU(Rect a, Rect b)
    {
        float x1=Mathf.Max(a.xMin,b.xMin), y1=Mathf.Max(a.yMin,b.yMin);
        float x2=Mathf.Min(a.xMax,b.xMax), y2=Mathf.Min(a.yMax,b.yMax);
        float inter = Mathf.Max(0,x2-x1)*Mathf.Max(0,y2-y1);
        float uni = a.width*a.height + b.width*b.height - inter;
        return uni <= 0 ? 0 : inter/uni;
    }
}
