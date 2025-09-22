using UnityEngine;


public class QuestGazeFromCamera : MonoBehaviour
{
    public Unity.InferenceEngine.ModelAsset modelAsset;
    public UnityEngine.UI.RawImage preview; // optional
    private Unity.InferenceEngine.Worker worker;
    private WebCamTexture webcam;
    private Texture2D frameTex; // CPU-side readable copy

    void Start()
    {
        // 1) Start camera
        var devices = WebCamTexture.devices;
        if (devices.Length == 0) { Debug.LogError("No cameras found."); return; }
        webcam = new WebCamTexture(devices[0].name, 1280, 720, 30);
        webcam.Play();
        if (preview) preview.texture = webcam;

        // 2) Load Sentis model
        var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        var graph = new Unity.InferenceEngine.FunctionalGraph();
        var inputs = graph.AddInputs(model);
        var outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
        var runtimeModel = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
    }

    void Update()
    {
        if (webcam == null || !webcam.didUpdateThisFrame) return;

        // 3) Copy webcam to a readable Texture2D
        if (frameTex == null || frameTex.width != webcam.width || frameTex.height != webcam.height)
            frameTex = new Texture2D(webcam.width, webcam.height, TextureFormat.RGBA32, false);
        frameTex.SetPixels32(webcam.GetPixels32());
        frameTex.Apply(false);

        // 4) Run inference (448�448 expected by this repo�s ONNX)
        Vector2 yawPitch = RunAI(frameTex);

        float yawDeg = yawPitch.x * Mathf.Rad2Deg;
        float pitchDeg = yawPitch.y * Mathf.Rad2Deg;
        Debug.Log($"Gaze deg  yaw={yawDeg:F1}  pitch={pitchDeg:F1}");
    }

    Vector2 RunAI(Texture2D picture)
    {
        using Unity.InferenceEngine.Tensor<float> input = Unity.InferenceEngine.TextureConverter.ToTensor(picture, 448, 448, 3);
        // Optional: normalize inside the graph if your model expects ImageNet mean/std (see next section).
        worker.Schedule(input);
        using var output = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        var arr = output.DownloadToArray();

        float yaw = (arr != null && arr.Length > 0) ? arr[0] : 0f;
        float pitch = (arr != null && arr.Length > 1) ? arr[1] : 0f;
        return new Vector2(yaw, pitch);
    }

    void OnDisable()
    {
        worker?.Dispose();
        if (webcam != null) { webcam.Stop(); webcam = null; }
    }
}
