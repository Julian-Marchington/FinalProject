using UnityEngine;
using UnityEngine.UI;

public class ManLookAroundUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button lookAtCameraButton;
    [SerializeField] private Button resetPositionsButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button resumeButton;
    
    [Header("Manager Reference")]
    [SerializeField] private ManLookAroundManager manager;
    
    private void Start()
    {
        // Find the manager if not assigned
        if (manager == null)
        {
            manager = FindObjectOfType<ManLookAroundManager>();
        }
        
        // Setup button listeners
        SetupButtonListeners();
        
        // Log the current setup
        if (manager != null)
        {
            Debug.Log("ManLookAroundUI: Connected to manager successfully");
        }
        else
        {
            Debug.LogWarning("ManLookAroundUI: No manager found!");
        }
    }
    
    private void SetupButtonListeners()
    {
        if (lookAtCameraButton != null)
        {
            lookAtCameraButton.onClick.AddListener(OnLookAtCameraClicked);
        }
        
        if (resetPositionsButton != null)
        {
            resetPositionsButton.onClick.AddListener(OnResetPositionsClicked);
        }
        
        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(OnPauseClicked);
        }
        
        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(OnResumeClicked);
        }
    }
    
    private void OnLookAtCameraClicked()
    {
        if (manager != null)
        {
            manager.MakeAllLookAtCamera();
            Debug.Log("UI: Look at camera button clicked");
        }
        else
        {
            Debug.LogWarning("UI: Cannot look at camera - no manager found");
        }
    }
    
    private void OnResetPositionsClicked()
    {
        if (manager != null)
        {
            manager.ResetAllToOriginalPositions();
            Debug.Log("UI: Reset positions button clicked");
        }
        else
        {
            Debug.LogWarning("UI: Cannot reset positions - no manager found");
        }
    }
    
    private void OnPauseClicked()
    {
        if (manager != null)
        {
            manager.PauseAllManPrefabs();
            Debug.Log("UI: Pause button clicked");
        }
        else
        {
            Debug.LogWarning("UI: Cannot pause - no manager found");
        }
    }
    
    private void OnResumeClicked()
    {
        if (manager != null)
        {
            manager.ResumeAllManPrefabs();
            Debug.Log("UI: Resume button clicked");
        }
        else
        {
            Debug.LogWarning("UI: Cannot resume - no manager found");
        }
    }
    
    // Public methods that can be called from other scripts or UnityEvents
    public void LookAtCamera()
    {
        OnLookAtCameraClicked();
    }
    
    public void ResetPositions()
    {
        OnResetPositionsClicked();
    }
    
    public void PauseAll()
    {
        OnPauseClicked();
    }
    
    public void ResumeAll()
    {
        OnResumeClicked();
    }
    
    private void OnDestroy()
    {
        // Clean up button listeners
        if (lookAtCameraButton != null)
        {
            lookAtCameraButton.onClick.RemoveListener(OnLookAtCameraClicked);
        }
        
        if (resetPositionsButton != null)
        {
            resetPositionsButton.onClick.RemoveListener(OnResetPositionsClicked);
        }
        
        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(OnPauseClicked);
        }
        
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveListener(OnResumeClicked);
        }
    }
}

