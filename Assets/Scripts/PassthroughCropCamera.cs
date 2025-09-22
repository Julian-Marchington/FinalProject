using PassthroughCameraSamples;
using UnityEngine;

public class PassthroughCropCamera : MonoBehaviour
{
    public TMPro.TextMeshPro tmp;
    public NewGazeDetection gazeDetection;
    public float cropPercent;
    public WebCamTextureManager webcamManager;

    public Renderer quadRenderer;
    public Renderer quadRenderer2;
    public float quadDistance = 1;

    private Texture2D picture;
    private RenderTexture webcamRenderTexture;

    // Update is called once per frame
    void Update()
    {
        if (!webcamManager.WebCamTexture)
            return;

        PlaceQuad();
        TakePicture();

        // Use instance method and show yaw/pitch (Vector2)
        Vector2 result = gazeDetection.RunAI(picture);
        tmp.text = $"yaw: {result.x:F2}, pitch: {result.y:F2}";
    }

    public void TakePicture()
    {
        int sourceWidth = webcamManager.WebCamTexture.width;
        int sourceHeight = webcamManager.WebCamTexture.height;

        int cropWidth = (int)(sourceWidth * cropPercent);

        int startX = (sourceWidth - cropWidth) / 2;
        int startY = (sourceHeight - cropWidth) / 2;

        if (webcamRenderTexture == null)
        {
            webcamRenderTexture = new RenderTexture(sourceWidth, sourceHeight, 0);
        }

        Graphics.Blit(webcamManager.WebCamTexture, webcamRenderTexture);

        if (picture == null || picture.width != cropWidth || picture.height != cropWidth)
        {
            // If your Unity version requires flags instead of bool, swap the last arg to TextureCreationFlags.None
            picture = new Texture2D(cropWidth, cropWidth, TextureFormat.RGBA32, false);
        }

        RenderTexture.active = webcamRenderTexture;

        picture.ReadPixels(
            new Rect((float)startX, (float)(sourceHeight - startY - cropWidth - cropWidth), (float)cropWidth, (float)cropWidth),
            0, 0
        );
        picture.Apply();

        quadRenderer2.material.mainTexture = picture;
    }

    public void PlaceQuad()
    {
        Transform quadTransform = quadRenderer.transform;

        Pose cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(PassthroughCameraEye.Left);

        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(PassthroughCameraEye.Left);
        Vector2Int resolution = intrinsics.Resolution;

        int width = (int)(resolution.x * cropPercent);

        quadTransform.position = cameraPose.position + cameraPose.forward * quadDistance;
        quadTransform.rotation = cameraPose.rotation;

        Ray leftSide = PassthroughCameraUtils.ScreenPointToRayInCamera(
            PassthroughCameraEye.Left,
            new Vector2Int((resolution.x - width) / 2, resolution.y / 2)
        );
        Ray rightSide = PassthroughCameraUtils.ScreenPointToRayInCamera(
            PassthroughCameraEye.Left,
            new Vector2Int((resolution.x - width) / 2, resolution.y / 2)
        );

        float horizontalFov = Vector3.Angle(leftSide.direction, rightSide.direction);

        float quadScale = 2 * quadDistance * Mathf.Tan(horizontalFov * Mathf.Deg2Rad) / 2;

        quadTransform.localScale = new Vector3(quadScale, quadScale, 1);
    }
}
