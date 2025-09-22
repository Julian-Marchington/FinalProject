using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples; // for WebCamTextureManager

/// <summary>
/// World-space UI preview of the Quest passthrough camera with YOLO face boxes overlaid.
/// Put this on a GameObject that has a Canvas (World Space) with a RawImage child.
/// </summary>
public class FaceDebugOverlay : MonoBehaviour
{
    [Header("Sources")]
    public WebCamTextureManager webCamMgr;     // your existing prefab instance
    public YoloV8FaceDetector faceDetector;    // the YOLO script you added earlier

    [Header("UI")]
    public RawImage preview;                   // RawImage that shows the camera feed
    public RectTransform boxesLayer;           // an empty RectTransform layered over the RawImage

    [Header("Box Style")]
    public float outlineWidth = 2f;
    public Color outlineColor = Color.green;

    // pool of box UI elements
    private readonly List<Image> _boxPool = new();
    private Vector2 _prevSize;

    void Awake()
    {
        if (preview == null) Debug.LogError("[FaceDebugOverlay] Assign a RawImage to 'preview'.");
        if (boxesLayer == null) Debug.LogError("[FaceDebugOverlay] Assign a RectTransform to 'boxesLayer'.");
    }

    void Update()
    {
        var wct = webCamMgr ? webCamMgr.WebCamTexture : null;
        if (wct == null || wct.width <= 0 || wct.height <= 0) return;

        // Ensure the preview displays the live camera feed
        if (preview.texture != wct) preview.texture = wct;

        // Run detection on the live frame
        List<YoloV8FaceDetector.Detection> dets = faceDetector.Detect(wct);

        // Compute how the source texture maps into the RawImage rect (handles letterboxing)
        RectTransform rt = preview.rectTransform;
        Vector2 targetSize = rt.rect.size;
        float srcW = wct.width, srcH = wct.height;

        // scale to fit inside target while preserving aspect
        float scale = Mathf.Min(targetSize.x / srcW, targetSize.y / srcH);
        float dispW = srcW * scale;
        float dispH = srcH * scale;
        float offsetX = (targetSize.x - dispW) * 0.5f;
        float offsetY = (targetSize.y - dispH) * 0.5f;

        // Make sure our boxes layer matches the preview rect exactly
        if (_prevSize != targetSize)
        {
            boxesLayer.anchorMin = Vector2.zero;
            boxesLayer.anchorMax = Vector2.one;
            boxesLayer.offsetMin = Vector2.zero;
            boxesLayer.offsetMax = Vector2.zero;
            _prevSize = targetSize;
        }

        // Grow pool if needed
        EnsureBoxPool(dets.Count);

        // Update box rects
        int active = 0;
        for (int i = 0; i < dets.Count; i++)
        {
            var d = dets[i];
            // Map from source pixels -> preview local coords
            float x = d.box.x * scale + offsetX;
            float y = d.box.y * scale + offsetY;
            float w = d.box.width * scale;
            float h = d.box.height * scale;

            var img = _boxPool[active++];
            img.gameObject.SetActive(true);

            // Position the UI box (bottom-left anchored)
            var r = img.rectTransform;
            r.anchorMin = new Vector2(0, 0);
            r.anchorMax = new Vector2(0, 0);
            r.pivot = new Vector2(0, 0);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
        }

        // Hide unused boxes
        for (int i = active; i < _boxPool.Count; i++)
            _boxPool[i].gameObject.SetActive(false);
    }

    private void EnsureBoxPool(int needed)
    {
        while (_boxPool.Count < needed)
        {
            var go = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
            go.transform.SetParent(boxesLayer, false);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(0, 0, 0, 0); // transparent fill

            var outline = go.GetComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(outlineWidth, -outlineWidth); // crisp outline

            _boxPool.Add(img);
        }
        // Update style (in case you tweak Inspector at runtime)
        foreach (var img in _boxPool)
        {
            var o = img.GetComponent<Outline>();
            o.effectColor = outlineColor;
            o.effectDistance = new Vector2(outlineWidth, -outlineWidth);
        }
    }
}
