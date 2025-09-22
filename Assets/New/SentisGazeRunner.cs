using UnityEngine;


public class SentisGazeRunner : MonoBehaviour
{
    [Header("Model")]
    public Unity.InferenceEngine.ModelAsset modelAsset;
    public Unity.InferenceEngine.BackendType backend = Unity.InferenceEngine.BackendType.GPUCompute;
    public int inputWidth = 448;
    public int inputHeight = 448;

    [Tooltip("Leave ON only if your Sentis version supports graph ops you add manually. For now, we keep it OFF.")]
    public bool applyImageNetNormalization = false;  // <-- turned OFF to avoid ConstantOfShape

    private Unity.InferenceEngine.Worker worker;
    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.FunctionalGraph graph;
    private Unity.InferenceEngine.FunctionalTensor[] inputs;
    private Unity.InferenceEngine.FunctionalTensor[] outputs;

    void Awake()
    {
        var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);

        // Keep the graph minimal: just forward the original model
        graph = new Unity.InferenceEngine.FunctionalGraph();
        inputs = graph.AddInputs(model);
        outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);

        runtimeModel = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, backend);
    }

    /// <summary>
    /// Run inference. Returns (yaw, pitch) in radians.
    /// </summary>
    public Vector2 Infer(Texture src)
    {
        if (src == null) return Vector2.zero;

        // TextureConverter handles resize & channel pack. No extra normalization here.
        using var input = Unity.InferenceEngine.TextureConverter.ToTensor(src, inputWidth, inputHeight, 3);
        worker.Schedule(input);

        using var output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        var arr = output.DownloadToArray();
        if (arr == null || arr.Length < 2) return Vector2.zero;

        // Expected order: [yaw, pitch] (radians)
        return new Vector2(arr[0], arr[1]);
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }
}
