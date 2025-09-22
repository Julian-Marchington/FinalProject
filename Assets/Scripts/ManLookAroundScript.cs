using UnityEngine;
using System.Collections;

public class ManLookAroundScript : MonoBehaviour
{
    [Header("Look Around Settings")]
    [SerializeField] private float lookAroundDuration = 3f;
    [SerializeField] private float lookAtCameraDuration = 2f;
    [SerializeField] private float rotationSpeed = 2f;
    [SerializeField] private float maxRotationAngle = 45f;
    
    [Header("References")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform mainCamera;
    
    private Vector3 originalHeadRotation;
    private Vector3 targetRotation;
    private bool isLookingAtCamera = false;
    private Coroutine lookAroundCoroutine;
    
    // Debug variables
    private Transform pelvisTransform;
    private Transform spine01Transform;
    private Transform spine02Transform;
    private Transform neck01Transform;
    
    private void Start()
    {
        // Find the head transform if not assigned
        if (headTransform == null)
        {
            headTransform = FindHeadTransform();
        }
        
        // Find the main camera if not assigned
        if (mainCamera == null)
        {
            mainCamera = Camera.main?.transform;
        }
        
        // Store the original head rotation
        if (headTransform != null)
        {
            originalHeadRotation = headTransform.localEulerAngles;
            Debug.Log($"{gameObject.name}: Original head rotation: {originalHeadRotation}");
        }
        
        // Start the look around behavior
        StartLookAroundBehavior();
    }
    
    private Transform FindHeadTransform()
    {
        // Navigate through the specific hierarchy: root -> pelvis -> spine 01 -> spine 02 -> neck 01 -> head
        Transform current = transform;
        
        // Find pelvis
        pelvisTransform = FindChildByName(current, "pelvis");
        if (pelvisTransform == null)
        {
            Debug.LogWarning("Pelvis not found for " + gameObject.name);
            return transform;
        }
        Debug.Log($"Found pelvis: {pelvisTransform.name} at position {pelvisTransform.position}");
        
        // Find spine 01
        spine01Transform = FindChildByName(pelvisTransform, "spine 01");
        if (spine01Transform == null)
        {
            Debug.LogWarning("Spine 01 not found for " + gameObject.name);
            return pelvisTransform;
        }
        Debug.Log($"Found spine 01: {spine01Transform.name} at position {spine01Transform.position}");
        
        // Find spine 02
        spine02Transform = FindChildByName(spine01Transform, "spine 02");
        if (spine02Transform == null)
        {
            Debug.LogWarning("Spine 02 not found for " + gameObject.name);
            return spine01Transform;
        }
        Debug.Log($"Found spine 02: {spine02Transform.name} at position {spine02Transform.position}");
        
        // Find neck 01
        neck01Transform = FindChildByName(spine02Transform, "neck 01");
        if (neck01Transform == null)
        {
            Debug.LogWarning("Neck 01 not found for " + gameObject.name);
            return spine02Transform;
        }
        Debug.Log($"Found neck 01: {neck01Transform.name} at position {neck01Transform.position}");
        
        // Find head
        Transform head = FindChildByName(neck01Transform, "head");
        if (head == null)
        {
            Debug.LogWarning("Head not found for " + gameObject.name);
            return neck01Transform;
        }
        
        Debug.Log($"Found head transform for {gameObject.name}: {head.name} at position {head.position}");
        Debug.Log($"Head parent: {head.parent.name}");
        Debug.Log($"Head local position: {head.localPosition}");
        Debug.Log($"Head local rotation: {head.localEulerAngles}");
        
        return head;
    }
    
    private Transform FindChildByName(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        return null;
    }
    
    private void StartLookAroundBehavior()
    {
        if (lookAroundCoroutine != null)
        {
            StopCoroutine(lookAroundCoroutine);
        }
        
        lookAroundCoroutine = StartCoroutine(LookAroundBehavior());
    }
    
    private IEnumerator LookAroundBehavior()
    {
        while (true)
        {
            // Look around randomly
            yield return StartCoroutine(LookAroundRandomly());
            
            // Small pause before looking at camera
            yield return new WaitForSeconds(0.5f);
            
            // Look at the camera
            yield return StartCoroutine(LookAtCamera());
            
            // Small pause before returning
            yield return new WaitForSeconds(0.3f);
            
            // Return to original position
            yield return StartCoroutine(ReturnToOriginalPosition());
            
            // Pause before next cycle
            yield return new WaitForSeconds(1f);
        }
    }
    
    private IEnumerator LookAroundRandomly()
    {
        float elapsedTime = 0f;
        
        // Generate random target rotation from the original position
        float randomY = Random.Range(-maxRotationAngle, maxRotationAngle);
        float randomX = Random.Range(-maxRotationAngle * 0.3f, maxRotationAngle * 0.3f);
        
        // Create target rotation based on original position, not current
        targetRotation = new Vector3(
            originalHeadRotation.x + randomX,
            originalHeadRotation.y + randomY,
            originalHeadRotation.z
        );
        
        Debug.Log($"{gameObject.name}: Looking around randomly to ({randomX:F1}, {randomY:F1})");
        Debug.Log($"{gameObject.name}: Target rotation: {targetRotation}");
        
        while (elapsedTime < lookAroundDuration)
        {
            elapsedTime += Time.deltaTime;
            
            if (headTransform != null)
            {
                // Store current rotations for debugging
                Vector3 currentRotation = headTransform.localEulerAngles;
                Vector3 pelvisRotation = pelvisTransform.localEulerAngles;
                Vector3 spine01Rotation = spine01Transform.localEulerAngles;
                Vector3 spine02Rotation = spine02Transform.localEulerAngles;
                Vector3 neck01Rotation = neck01Transform.localEulerAngles;
                
                // Smoothly rotate from current position to target
                Vector3 smoothedRotation = Vector3.Lerp(currentRotation, targetRotation, elapsedTime / lookAroundDuration);
                headTransform.localEulerAngles = smoothedRotation;
                
                // Debug: Check if other transforms are being affected
                if (elapsedTime > lookAroundDuration * 0.5f) // Check halfway through
                {
                    Debug.Log($"{gameObject.name}: Head rotation changed from {currentRotation} to {headTransform.localEulerAngles}");
                    Debug.Log($"{gameObject.name}: Pelvis rotation: {pelvisTransform.localEulerAngles} (was {pelvisRotation})");
                    Debug.Log($"{gameObject.name}: Spine01 rotation: {spine01Transform.localEulerAngles} (was {spine01Rotation})");
                    Debug.Log($"{gameObject.name}: Spine02 rotation: {spine02Transform.localEulerAngles} (was {spine02Rotation})");
                    Debug.Log($"{gameObject.name}: Neck01 rotation: {neck01Transform.localEulerAngles} (was {neck01Rotation})");
                }
            }
            
            yield return null;
        }
    }
    
    private IEnumerator LookAtCamera()
    {
        if (mainCamera == null)
        {
            Debug.LogWarning("Main camera not found!");
            yield break;
        }
        
        isLookingAtCamera = true;
        float elapsedTime = 0f;
        
        // Calculate direction to camera from head position
        Vector3 directionToCamera = (mainCamera.position - headTransform.position).normalized;
        
        // Calculate the rotation needed to look at camera
        Quaternion targetLookRotation = Quaternion.LookRotation(directionToCamera);
        
        // Convert to local rotation relative to the head's parent (neck 01)
        Quaternion localTargetRotation = Quaternion.Inverse(headTransform.parent.rotation) * targetLookRotation;
        
        // Extract only the head rotation (Y and X, keep Z minimal)
        Vector3 localEuler = localTargetRotation.eulerAngles;
        Vector3 targetRotation = new Vector3(
            Mathf.Clamp(localEuler.x, -20f, 20f),  // Limit X rotation (up/down) more strictly
            Mathf.Clamp(localEuler.y, -60f, 60f),  // Limit Y rotation (left/right) to prevent over-rotation
            originalHeadRotation.z                  // Keep original Z rotation
        );
        
        Debug.Log($"{gameObject.name}: Looking at camera for {lookAtCameraDuration} seconds");
        Debug.Log($"{gameObject.name}: Camera target rotation: {targetRotation}");
        
        while (elapsedTime < lookAtCameraDuration)
        {
            elapsedTime += Time.deltaTime;
            
            if (headTransform != null)
            {
                // Smoothly rotate only the head to look at camera
                Vector3 currentRotation = headTransform.localEulerAngles;
                Vector3 smoothedRotation = Vector3.Lerp(currentRotation, targetRotation, Time.deltaTime * rotationSpeed);
                headTransform.localEulerAngles = smoothedRotation;
            }
            
            yield return null;
        }
        
        isLookingAtCamera = false;
        Debug.Log($"{gameObject.name}: Finished looking at camera");
    }
    
    private IEnumerator ReturnToOriginalPosition()
    {
        float elapsedTime = 0f;
        float returnDuration = 1f;
        
        Debug.Log($"{gameObject.name}: Returning to original position");
        
        while (elapsedTime < returnDuration)
        {
            elapsedTime += Time.deltaTime;
            
            if (headTransform != null)
            {
                // Smoothly return only the head to original position
                Vector3 currentRotation = headTransform.localEulerAngles;
                Vector3 smoothedRotation = Vector3.Lerp(currentRotation, originalHeadRotation, elapsedTime / returnDuration);
                headTransform.localEulerAngles = smoothedRotation;
            }
            
            yield return null;
        }
        
        // Ensure we're exactly at the original rotation
        if (headTransform != null)
        {
            headTransform.localEulerAngles = originalHeadRotation;
        }
    }
    
    private void OnDestroy()
    {
        if (lookAroundCoroutine != null)
        {
            StopCoroutine(lookAroundCoroutine);
        }
    }
    
    // Public method to manually trigger looking at camera
    public void LookAtCameraNow()
    {
        if (lookAroundCoroutine != null)
        {
            StopCoroutine(lookAroundCoroutine);
        }
        
        StartCoroutine(LookAtCamera());
    }
    
    // Public method to reset to original position
    public void ResetToOriginalPosition()
    {
        if (headTransform != null)
        {
            headTransform.localEulerAngles = originalHeadRotation;
        }
    }
}
