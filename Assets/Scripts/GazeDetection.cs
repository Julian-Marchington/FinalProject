using UnityEngine;


public class GazeDetection : MonoBehaviour
{

    public float threshold = 0.9f;
    public Texture2D testPicture;
    public Unity.InferenceEngine.ModelAsset modelAsset;
    public float[] result;
    private Unity.InferenceEngine.Worker worker;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Unity.InferenceEngine.Model model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        Unity.InferenceEngine.FunctionalGraph graph = new Unity.InferenceEngine.FunctionalGraph();
        Unity.InferenceEngine.FunctionalTensor[] inputs = graph.AddInputs(model);
        Unity.InferenceEngine.FunctionalTensor[] outputs = Unity.InferenceEngine.Functional.Forward(model, inputs);
        Unity.InferenceEngine.FunctionalTensor softmax = Unity.InferenceEngine.Functional.Softmax(outputs[0]);


        Unity.InferenceEngine.Model runtimeModel = graph.Compile(outputs);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);

        Debug.Log(RunAI(testPicture));
    }

    public int RunAI(Texture2D picture)
    {
        using Unity.InferenceEngine.Tensor<float> inputTensor = Unity.InferenceEngine.TextureConverter.ToTensor(picture, 28, 28, 1);
        worker.Schedule(inputTensor);
        Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        result = outputTensor.DownloadToArray();
        return GetMaxIndex(result);
    }

    private void OnDisable()
    {
        worker.Dispose();
    }

    public int GetMaxIndex(float[] array)
    {
        int maxIndex = 0;

        for(int i = 0; i < array.Length; i++)
        {
            if(array[i] > array[maxIndex])
            {
                maxIndex = i;
            }
        }

        if(array[maxIndex] > threshold)
        {
            return maxIndex;
        }

        return -1;
    }
}
