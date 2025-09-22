using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SimpleGazeVisualizer : MonoBehaviour
{
    [Header("Gaze Visualization")]
    public RectTransform videoRect;           // Assign VideoImage.rectTransform
    public GameObject gazeArrowPrefab;        // Assign a simple arrow prefab
    public float arrowLength = 80f;           // Length of each gaze arrow

    [Header("Debug")]
    public bool showDebugInfo = true;

    // one arrow per id
    private readonly Dictionary<int, GameObject> _arrows = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, RectTransform> _arrowRects = new Dictionary<int, RectTransform>();

    void Start()
    {
        if (gazeArrowPrefab == null)
        {
            Debug.LogError("[GAZE] GazeArrow prefab not assigned!");
        }

        if (videoRect == null)
        {
            Debug.LogError("[GAZE] VideoRect not assigned!");
        }
    }

    GameObject GetOrCreateArrow(int id)
    {
        if (_arrows.TryGetValue(id, out var go)) return go;

        var newArrow = Instantiate(gazeArrowPrefab, transform);
        var rt = newArrow.GetComponent<RectTransform>();
        var img = newArrow.GetComponent<Image>();

        if (rt == null || img == null)
        {
            Debug.LogError("[GAZE] GazeArrow prefab must have RectTransform + Image!");
            return null;
        }

        // Set up the arrow rect
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        newArrow.SetActive(true);

        _arrows[id] = newArrow;
        _arrowRects[id] = rt;
        return newArrow;
    }

    /// <summary>
    /// Update / create the arrow for a given face id.
    /// Coordinates are expected in the same 640x640 space as the detector texture.
    /// </summary>
    public void UpdateGazeDirectionForId(int id, Rect faceRect, float yawDeg, float pitchDeg)
    {
        if (videoRect == null || gazeArrowPrefab == null) return;

        var go = GetOrCreateArrow(id);
        if (go == null) return;

        var arrowRect = _arrowRects[id];

        // Origin at face center (in detector space)
        Vector2 centerPos = new Vector2(faceRect.x + faceRect.width * 0.5f,
                                        faceRect.y + faceRect.height * 0.5f);

        float len = arrowLength;
        float dx = len * Mathf.Sin(yawDeg * Mathf.Deg2Rad);
        float dy = -len * Mathf.Sin(pitchDeg * Mathf.Deg2Rad);
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

        // Place/rotate/size
        arrowRect.anchoredPosition = centerPos;
        arrowRect.sizeDelta = new Vector2(len, 8f);
        arrowRect.localEulerAngles = new Vector3(0, 0, angle);

        if (showDebugInfo)
        {
            Debug.Log($"[GAZE] id={id} pos=({centerPos.x:F1},{centerPos.y:F1}) angle={angle:F1} yaw={yawDeg:F1} pitch={pitchDeg:F1}");
        }
    }

    /// <summary>
    /// Hide any arrows with id >= activeCount.
    /// </summary>
    public void HideUnusedArrows(int activeCount)
    {
        foreach (var kv in _arrows)
        {
            bool active = kv.Key < activeCount;
            if (kv.Value.activeSelf != active) kv.Value.SetActive(active);
        }
    }

    public void ClearAll()
    {
        foreach (var go in _arrows.Values)
        {
            if (go != null) Destroy(go);
        }
        _arrows.Clear();
        _arrowRects.Clear();
    }

    void OnDestroy()
    {
        ClearAll();
    }
}
