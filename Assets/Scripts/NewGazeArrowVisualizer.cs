using System.Collections.Generic;
using UnityEngine;

public class NewGazeArrowVisualizer : MonoBehaviour
{
    [System.Serializable]
    public struct Sample
    {
        public float xPx;       // face center in MODEL space (0..MODEL_SIZE)
        public float yPx;
        public float yawDeg;    // final, bias-applied yaw (deg, +right)
        public float pitchDeg;  // final, bias-applied pitch (deg, +up)
        public bool  isLooking; // inside your “looking gate”
    }

    [Header("Scene refs")]
    public Camera xrCamera;           // XR main camera
    public GameObject arrowPrefab;    // prefab with Shaft + Head children

    [Header("Placement")]
    public float placeDistance = 1.4f;     // meters in front of camera
    public float shaftLength  = 0.25f;     // base length (m)
    public float shaftThickness = 0.012f;  // meters

    [Header("Colors")]
    public Color lookingColor    = new Color(0.2f, 0.95f, 0.2f, 1f);
    public Color notLookingColor = new Color(1f, 0.25f, 0.25f, 1f);
    public float headScale = 1.0f; // multiplier on head size

    // ---- pool ----
    class ArrowInst
    {
        public GameObject go;
        public Transform root;
        public Transform shaft;
        public Transform head;
        public Renderer[] rends;
    }

    readonly List<ArrowInst> _pool = new List<ArrowInst>(8);

    ArrowInst Spawn()
    {
        var go = Instantiate(arrowPrefab, transform);
        var inst = new ArrowInst
        {
            go = go,
            root = go.transform,
            shaft = go.transform.Find("Shaft"),
            head  = go.transform.Find("Head"),
            rends = go.GetComponentsInChildren<Renderer>(true)
        };
        if (inst.shaft == null || inst.head == null)
            Debug.LogWarning("[GazeArrowVisualizer] Arrow prefab must have children named 'Shaft' and 'Head'.");
        _pool.Add(inst);
        return inst;
    }

    void EnsurePool(int n)
    {
        while (_pool.Count < n) Spawn();
    }

    public void Render(IList<Sample> samples, int tensorSize)
    {
        if (xrCamera == null || arrowPrefab == null)
        {
            // nothing to draw; also disable pooled
            for (int i = 0; i < _pool.Count; i++) if (_pool[i].go.activeSelf) _pool[i].go.SetActive(false);
            return;
        }

        int count = (samples == null) ? 0 : samples.Count;
        EnsurePool(count);

        // hide unused
        for (int i = count; i < _pool.Count; i++)
            if (_pool[i].go.activeSelf) _pool[i].go.SetActive(false);

        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            var s = samples[i];
            var inst = _pool[i];
            if (!inst.go.activeSelf) inst.go.SetActive(true);

            // 1) place position: project 2D (MODEL coords) -> viewport -> ray
            float u = Mathf.Clamp01(s.xPx / Mathf.Max(1, tensorSize));
            float v = Mathf.Clamp01(s.yPx / Mathf.Max(1, tensorSize)); // y already bottom-up in your detector
            var ray = xrCamera.ViewportPointToRay(new Vector3(u, v, 0f));
            Vector3 pos = ray.origin + ray.direction.normalized * placeDistance;

            // 2) gaze direction in camera space -> world
            // yaw=+right, pitch=+up assumed
            float yawRad = s.yawDeg * Mathf.Deg2Rad;
            float pitRad = s.pitchDeg * Mathf.Deg2Rad;
            // spherical-ish: X = sin(yaw)*cos(pitch), Y = sin(pitch), Z = cos(yaw)*cos(pitch)
            Vector3 localDir = new Vector3(Mathf.Sin(yawRad) * Mathf.Cos(pitRad),
                                           Mathf.Sin(pitRad),
                                           Mathf.Cos(yawRad) * Mathf.Cos(pitRad));
            Vector3 worldDir = xrCamera.transform.TransformDirection(localDir.normalized);

            // 3) apply transform
            inst.root.position = pos;
            inst.root.rotation = Quaternion.LookRotation(worldDir, xrCamera.transform.up);

            // 4) size: stretch along +Z (arrow prefab expects forward = +Z)
            // Shaft is a unit cube-ish aligned to +Z; keep thickness constant, set length
            float L = Mathf.Max(0.02f, shaftLength);
            if (inst.shaft != null)
            {
                inst.shaft.localScale    = new Vector3(shaftThickness, shaftThickness, L);
                inst.shaft.localPosition = new Vector3(0, 0, L * 0.5f);
            }
            if (inst.head != null)
            {
                float headLen = L * 0.35f;
                float headRad = shaftThickness * 2.2f * headScale;
                // Head is modeled pointing +Z and ~unit sized; scale non-uniform
                inst.head.localScale    = new Vector3(headRad, headRad, headLen);
                inst.head.localPosition = new Vector3(0, 0, L + headLen * 0.5f);
            }

            // 5) colorize
            var col = s.isLooking ? lookingColor : notLookingColor;
            if (inst.rends != null)
            {
                for (int r = 0; r < inst.rends.Length; r++)
                {
                    // Use .material to instance; safe for small pool sizes
                    if (inst.rends[r] != null) inst.rends[r].material.color = col;
                }
            }
        }
    }
}
