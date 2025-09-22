using System.Collections.Generic;
using UnityEngine;
using PassthroughCameraSamples; // for WebCamTextureManager

public class FaceToGaze : MonoBehaviour
{
    [Header("Sources")]
    public WebCamTextureManager webCamMgr;   // your existing prefab instance
    public YoloV8FaceDetector faceDetector;  // script above
    public SentisGazeRunner gazeRunner;      // your existing gaze script
    public Transform head;                   // CenterEyeAnchor / Main Camera
    public Transform arrow;                  // small arrow (local +Z forward)

    [Header("Crop & Visuals")]
    [Range(0f, 0.4f)] public float bboxPadding = 0.15f; // grow the box before squaring
    [Range(0, 1)] public float smooth = 0.2f;
    public float maxYawDeg = 60f, maxPitchDeg = 40f;

    private Texture2D cropTex;

    void Reset()
    {
        if (Camera.main) head = Camera.main.transform;
    }

    void Update()
    {
        var wct = webCamMgr ? webCamMgr.WebCamTexture : null;
        if (wct == null || wct.width <= 0) return;

        // 1) Detect faces
        List<YoloV8FaceDetector.Detection> dets = faceDetector.Detect(wct);
        if (dets.Count == 0) return;

        // 2) Pick best (highest score)
        int best = 0;
        for (int i = 1; i < dets.Count; i++) if (dets[i].score > dets[best].score) best = i;
        var b = dets[best].box;

        // 3) Pad and square the crop
        float padX = b.width * bboxPadding;
        float padY = b.height * bboxPadding;
        Rect padded = new Rect(
            Mathf.Max(0, b.x - padX),
            Mathf.Max(0, b.y - padY),
            b.width + 2 * padX,
            b.height + 2 * padY
        );

        float side = Mathf.Min(padded.width, padded.height);
        Rect square = new Rect(
            padded.center.x - side / 2f,
            padded.center.y - side / 2f,
            side, side
        );

        // 4) Copy pixels into a square Texture2D
        int sw = Mathf.Clamp(Mathf.RoundToInt(square.width), 1, wct.width);
        int sh = Mathf.Clamp(Mathf.RoundToInt(square.height), 1, wct.height);
        int sx = Mathf.Clamp(Mathf.RoundToInt(square.x), 0, Mathf.Max(0, wct.width - sw));
        int sy = Mathf.Clamp(Mathf.RoundToInt(square.y), 0, Mathf.Max(0, wct.height - sh));

        if (cropTex == null || cropTex.width != sw || cropTex.height != sh)
            cropTex = new Texture2D(sw, sh, TextureFormat.RGBA32, false);

        var pixels = wct.GetPixels32();
        var row = new Color32[sw];
        for (int yy = 0; yy < sh; yy++)
        {
            int srcY = sy + yy;
            int srcStart = srcY * wct.width + sx;
            System.Array.Copy(pixels, srcStart, row, 0, sw);
            cropTex.SetPixels32(0, yy, sw, 1, row);
        }
        cropTex.Apply(false, false);

        // 5) Run gaze on this crop
        Vector2 yawPitch = gazeRunner.Infer(cropTex);
        float yawDeg   = Mathf.Clamp(yawPitch.x * Mathf.Rad2Deg, -maxYawDeg, maxYawDeg);
        float pitchDeg = Mathf.Clamp(yawPitch.y * Mathf.Rad2Deg, -maxPitchDeg, maxPitchDeg);

        // 6) Visualize as an arrow near the head (flip pitch if needed for your model)
        var targetLocal = Quaternion.Euler(pitchDeg, yawDeg, 0f);
        var targetWorld = head.rotation * targetLocal;

        if (arrow)
        {
            arrow.position = head.position + head.forward * 0.25f - head.up * 0.02f;
            arrow.rotation = Quaternion.Slerp(arrow.rotation, targetWorld, 1f - Mathf.Pow(1f - smooth, Time.deltaTime * 60f));
        }
    }

    void OnDestroy()
    {
        if (cropTex) Destroy(cropTex);
    }
}
