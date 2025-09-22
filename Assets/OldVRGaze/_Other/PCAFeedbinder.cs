using UnityEngine;
using UnityEngine.UI;

public class PCAFeedBinder : MonoBehaviour
{
    [Tooltip("Drag the PCA sample object that has WebCamTextureManager on it")]
    public MonoBehaviour webCamTextureManager; // sample type: WebCamTextureManager

    [Tooltip("Optional. Leave null to avoid any on-screen video.")]
    public RawImage targetRawImage;

    // Detector reads from this (no UI required)
    public WebCamTexture WebCamTexture { get; private set; }

    void Start()
    {
        // Auto-find WebCamTextureManager if none assigned
        if (webCamTextureManager == null)
        {
            var foundManager = FindObjectOfType<MonoBehaviour>();
            if (foundManager != null)
            {
                webCamTextureManager = foundManager;
                Debug.Log("[PCAFeedBinder] Auto-found WebCamTextureManager: " + foundManager.name);
            }
            else
            {
                Debug.LogError("[PCAFeedBinder] No WebCamTextureManager found in scene! Face detection will not work.");
            }
        }
    }

    void Update()
    {
        if (webCamTextureManager == null) return;

        // Reflect property 'WebCamTexture' from the PCA sample
        var prop = webCamTextureManager.GetType().GetProperty("WebCamTexture");
        if (prop == null) return;

        var wct = prop.GetValue(webCamTextureManager, null) as WebCamTexture;
        if (wct == null) return;

        WebCamTexture = wct;

        // Optional preview
        if (targetRawImage != null && targetRawImage.texture != wct)
            targetRawImage.texture = wct;
    }
}
