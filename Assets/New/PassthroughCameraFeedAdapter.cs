using UnityEngine;
using PassthroughCameraSamples;

public class PassthroughCameraFeedAdapter : MonoBehaviour
{
    [Header("Reference")]
    public WebCamTextureManager webCamTextureManager;

    /// <summary>Latest camera texture from the Quest passthrough API.</summary>
    public bool TryGetTexture(out Texture tex)
    {
        tex = null;
        if (webCamTextureManager == null) return false;
        if (webCamTextureManager.WebCamTexture == null) return false;

        // Sentis can read directly from WebCamTexture
        tex = webCamTextureManager.WebCamTexture;
        return true;
    }
}
