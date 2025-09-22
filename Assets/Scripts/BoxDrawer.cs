using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class BoxDrawer : MonoBehaviour
{
    [SerializeField] private RectTransform _space;   // drawing space (VideoImage rect)
    [SerializeField] private GameObject _defaultBox; // face box prefab

    private readonly Stack<GameObject> _pool = new Stack<GameObject>();
    private readonly List<GameObject> _active = new List<GameObject>();

    public void Init(RectTransform space, GameObject defaultBoxPrefab)
    {
        _space = space;
        _defaultBox = defaultBoxPrefab;

        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.sizeDelta = Vector2.zero;
    }

    public void Clear()
    {
        // Clear all active objects and return them to the pool
        for (int i = 0; i < _active.Count; i++)
        {
            var go = _active[i];
            if (go != null)
            {
                go.SetActive(false);
                _pool.Push(go);
            }
        }
        _active.Clear();

        // Only clear objects that were created by this BoxDrawer
        // Don't clear objects created by other systems (like gaze visualization)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child != null && child.name.Contains("(Clone)"))
            {
                // Only clear cloned objects (our spawned objects)
                child.gameObject.SetActive(false);
            }
        }

        Debug.Log($"[BoxDrawer] Cleared BoxDrawer objects. Remaining children: {transform.childCount}");
    }

    GameObject Spawn(GameObject prefab)
    {
        // Validate prefab
        if (prefab == null)
        {
            Debug.LogError("[BoxDrawer] Attempted to spawn with null prefab!");
            return null;
        }

        var go = _pool.Count > 0 ? _pool.Pop() : Instantiate(prefab);
        
        // Ensure proper parenting
        if (go.transform.parent != transform)
            go.transform.SetParent(transform, false);
        
        // Ensure object is active and visible
        go.SetActive(true);
        
        // Add to active list
        _active.Add(go);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        // caller will set pivot/rotation as needed
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        // Debug logging
        Debug.Log($"[BoxDrawer] Spawned object: {go.name}, Active: {go.activeInHierarchy}");

        return go;
    }

    // Face box (axis-aligned, pivot BL)
    public void DrawBox(Rect r, string label, float score)
    {
        var go = Spawn(_defaultBox);
        if (go == null) return; // Early exit if spawn failed
        
        var rt = (RectTransform)go.transform;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition3D = new Vector3(r.xMin, r.yMin, 0);
        rt.sizeDelta = new Vector2(r.width, r.height);

        var txt = go.GetComponentInChildren<Text>();
        if (txt) txt.text = string.IsNullOrEmpty(label) ? "" : $"{label} {(score*100f):F0}%";
    }

    // Generic image with rotation (used for arrow shaft/head/dot)
    public RectTransform DrawImage(GameObject prefab, Vector2 pos, Vector2 size, float rotationDeg, Vector2? pivot = null)
    {
        var go = Spawn(prefab);
        if (go == null) return null; // Early exit if spawn failed
        
        var rt = (RectTransform)go.transform;
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition3D = new Vector3(pos.x, pos.y, 0);
        rt.sizeDelta = size;
        rt.localEulerAngles = new Vector3(0, 0, rotationDeg);
        return rt;
    }
}
