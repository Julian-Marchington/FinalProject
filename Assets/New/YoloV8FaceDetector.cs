using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// YOLOv8n-face ONNX inference (Sentis).
/// Supports output shapes [1, 8400, 16] or [1, 16, 8400].
/// Each detection vector = [cx, cy, w, h, conf, l0x, l0y, l1x, l1y, l2x, l2y, l3x, l3y, l4x, l4y]
/// All values are normalized [0..1] relative to the model input (usually imgsz=640).
/// </summary>
public class YoloV8FaceDetector : MonoBehaviour
{
    [Header("Model (.onnx as Sentis ModelAsset)")]
    public Unity.InferenceEngine.ModelAsset modelAsset;

    [Header("Backend / Input")]
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.GPUCompute;
    public int inputW = 640;
    public int inputH = 640;

    [Header("Thresholds")]
    [Range(0, 1)] public float confThreshold = 0.40f;
    [Range(0, 1)] public float iouThreshold  = 0.45f;
    public int maxDetections = 20;

    private Unity.InferenceEngine.Worker worker;
    private Unity.InferenceEngine.Model runtimeModel;

    void Awake()
    {
        var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        var graph = new Unity.InferenceEngine.FunctionalGraph();
        var inputs = graph.AddInputs(model);
        var outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
        runtimeModel = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, backend);
    }

    public struct Detection
    {
        public Rect box;           // in pixels (source image space)
        public float score;        // confidence
        public Vector2[] lmks;     // 5 landmarks in pixels
    }

    /// <summary>
    /// Run detection on a Texture (WebCamTexture/RenderTexture/Texture2D).
    /// Returns bboxes/landmarks in source texture pixel coordinates.
    /// </summary>
    public List<Detection> Detect(Texture src)
    {
        var results = new List<Detection>();
        if (src == null) return results;

        int srcW, srcH;
        if      (src is WebCamTexture wct) { srcW = wct.width; srcH = wct.height; }
        else if (src is Texture2D t2d)     { srcW = t2d.width; srcH = t2d.height; }
        else if (src is RenderTexture rt)  { srcW = rt.width;  srcH = rt.height;  }
        else return results;

        using var input = Unity.InferenceEngine.TextureConverter.ToTensor(src, inputW, inputH, 3);
        worker.Schedule(input);

        using var output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        if (output == null) return results;

        // Read raw output
        var arr = output.DownloadToArray();
        if (arr == null || arr.Length < 16) return results;

        // Determine layout
        // Common layouts:
        //  A) [1, 8400, 16] => (B, N, C) with C=16
        //  B) [1, 16, 8400] => (B, C, N)
        var shape = output.shape;
        bool isBNxC = (shape[1] > shape[2]); // [1,N,C] if N(=8400) > C(=16)

        int N = isBNxC ? shape[1] : shape[2];
        int C = isBNxC ? shape[2] : shape[1]; // expect 16
        if (C < 15) return results;           // safety

        // Collect candidates (in model input space pixels)
        var cand = new List<Detection>(N);
        for (int i = 0; i < N; i++)
        {
            // Get pointer to detection i
            // idx = i*C if BNxC else i
            int baseIdx = isBNxC ? (i * C) : i;
            float cx, cy, w, h, conf;
            float[] vals = new float[16];

            if (isBNxC)
            {
                for (int c = 0; c < C; c++) vals[c] = arr[baseIdx + c];
            }
            else
            {
                // [1, C, N]: for each channel c, value at arr[c*N + i]
                for (int c = 0; c < C; c++) vals[c] = arr[c * N + i];
            }

            cx   = vals[0];
            cy   = vals[1];
            w    = vals[2];
            h    = vals[3];
            conf = vals[4];
            if (conf < confThreshold) continue;

            // Convert normalized center box -> pixel corner box (model input space)
            float x = (cx - w * 0.5f) * inputW;
            float y = (cy - h * 0.5f) * inputH;
            float pw = w * inputW;
            float ph = h * inputH;

            // Landmarks (5 points)
            var lm = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float lx = vals[5 + k * 2] * inputW;
                float ly = vals[6 + k * 2] * inputH;
                lm[k] = new Vector2(lx, ly);
            }

            cand.Add(new Detection
            {
                box = new Rect(x, y, pw, ph),
                score = conf,
                lmks = lm
            });
        }

        // NMS in model input pixel space
        var keepIdx = NMS(cand, iouThreshold, maxDetections);

        // Map to source texture pixel space (account for letterbox scale)
        // NOTE: TextureConverter.ToTensor uses simple resize (no pad), so we just scale by srcW/srcH.
        float sx = (float)srcW / inputW;
        float sy = (float)srcH / inputH;

        foreach (var id in keepIdx)
        {
            var c = cand[id];

            var mapped = new Detection
            {
                score = c.score,
                box = new Rect(c.box.x * sx, c.box.y * sy, c.box.width * sx, c.box.height * sy),
                lmks = new Vector2[5]
            };
            for (int k = 0; k < 5; k++)
            {
                mapped.lmks[k] = new Vector2(c.lmks[k].x * sx, c.lmks[k].y * sy);
            }
            results.Add(mapped);
        }

        return results;
    }

    private static List<int> NMS(List<Detection> dets, float iouThr, int maxKeep)
    {
        var idxs = new List<int>(dets.Count);
        for (int i = 0; i < dets.Count; i++) idxs.Add(i);
        idxs.Sort((a, b) => dets[b].score.CompareTo(dets[a].score));

        var keep = new List<int>(Mathf.Min(maxKeep, dets.Count));
        while (idxs.Count > 0 && keep.Count < maxKeep)
        {
            int best = idxs[0];
            keep.Add(best);
            idxs.RemoveAt(0);

            for (int j = idxs.Count - 1; j >= 0; j--)
            {
                if (IoU(dets[best].box, dets[idxs[j]].box) > iouThr)
                    idxs.RemoveAt(j);
            }
        }
        return keep;
    }

    private static float IoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);
        float inter = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float uni = a.width * a.height + b.width * b.height - inter + 1e-6f;
        return inter / uni;
    }

    void OnDestroy() => worker?.Dispose();
}
