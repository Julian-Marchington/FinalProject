using UnityEngine;


public class NewGazeDetection : MonoBehaviour
{
    public float threshold = 0.9f;              // kept (unused now)
    public Texture2D testPicture;
    public Unity.InferenceEngine.ModelAsset modelAsset;
    public float[] result;                      // will hold [yaw, pitch]
    private Unity.InferenceEngine.Worker worker;

    void Start()
    {
        Unity.InferenceEngine.Model model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        Unity.InferenceEngine.FunctionalGraph graph = new Unity.InferenceEngine.FunctionalGraph();
        Unity.InferenceEngine.FunctionalTensor[] inputs = graph.AddInputs(model);
        Unity.InferenceEngine.FunctionalTensor[] outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);

        // NOTE: Removed Softmax � this is regression, not classification.

        Unity.InferenceEngine.Model runtimeModel = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);

        Vector2 yawPitch = RunAI(testPicture);
        // Log radians and degrees (many gaze papers use radians)
        float yawDeg = yawPitch.x * Mathf.Rad2Deg;
        float pitchDeg = yawPitch.y * Mathf.Rad2Deg;
        Debug.Log($"Gaze (rad): yaw={yawPitch.x:F4}, pitch={yawPitch.y:F4} | (deg): yaw={yawDeg:F2}, pitch={pitchDeg:F2}");
    }

    public Vector2 RunAI(Texture2D picture)
    {
        // Typical full-face input size for these models is 224x224 with 3 channels.
        using Unity.InferenceEngine.Tensor<float> inputTensor = Unity.InferenceEngine.TextureConverter.ToTensor(picture, 448, 448, 3);
        worker.Schedule(inputTensor);

        Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        result = outputTensor.DownloadToArray();

        // Expecting two values: [yaw, pitch]
        float yaw = (result != null && result.Length > 0) ? result[0] : 0f;
        float pitch = (result != null && result.Length > 1) ? result[1] : 0f;
        return new Vector2(yaw, pitch);
    }

    private void OnDisable()
    {
        worker?.Dispose();
    }

    // Kept from original (unused for regression output).
    public int GetMaxIndex(float[] array)
    {
        int maxIndex = 0;
        for (int i = 0; i < array.Length; i++)
            if (array[i] > array[maxIndex]) maxIndex = i;

        if (array[maxIndex] > threshold) return maxIndex;
        return -1;
    }
}
