using UnityEngine;

public class GazeArrowVisualizer : MonoBehaviour
{
    [Header("Sources")]
    public PassthroughCameraFeedAdapter cameraFeed;
    public SentisGazeRunner gazeRunner;

    [Header("VR Anchors")]
    public Transform head;   // CenterEyeAnchor (or MainCamera)
    public Transform arrow;  // a small arrow/capsule whose +Z is forward

    [Header("Tuning")]
    [Range(0f, 1f)] public float smooth = 0.2f;
    public float maxYawDeg = 60f;
    public float maxPitchDeg = 40f;

    private Quaternion targetLocalRot = Quaternion.identity;

    void Reset()
    {
        if (Camera.main) head = Camera.main.transform;
    }

    void Update()
    {
        if (cameraFeed == null || gazeRunner == null || head == null || arrow == null) return;
        if (!cameraFeed.TryGetTexture(out var tex) || tex == null) return;

        // 1) Inference
        Vector2 yawPitch = gazeRunner.Infer(tex);

        // 2) Radians -> degrees, clamp
        float yawDeg   = Mathf.Clamp(yawPitch.x * Mathf.Rad2Deg, -maxYawDeg, maxYawDeg);     // left/right
        float pitchDeg = Mathf.Clamp(yawPitch.y * Mathf.Rad2Deg, -maxPitchDeg, maxPitchDeg); // up/down

        // If axes feel inverted, try: pitchDeg = -pitchDeg; or swap order below.
        targetLocalRot = Quaternion.Euler(pitchDeg, yawDeg, 0f);

        // 3) Place and rotate arrow relative to head
        arrow.position = head.position + head.forward * 0.25f - head.up * 0.02f;
        arrow.rotation = Quaternion.Slerp(
            arrow.rotation,
            head.rotation * targetLocalRot,
            1f - Mathf.Pow(1f - smooth, Time.deltaTime * 60f)
        );
    }
}
