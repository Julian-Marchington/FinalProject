using UnityEngine;
using System.Collections.Generic;

public class ManLookAroundManager : MonoBehaviour
{
    [Header("Manager Settings")]
    [SerializeField] private bool enableRandomTiming = true;
    [SerializeField] private float minDelayBetweenLooks = 1f;
    [SerializeField] private float maxDelayBetweenLooks = 3f;
    
    [Header("Man Prefabs")]
    [SerializeField] private List<ManLookAroundScript> manScripts = new List<ManLookAroundScript>();
    
    private List<ManLookAroundScript> activeManScripts = new List<ManLookAroundScript>();
    
    private void Start()
    {
        // Find all Man prefabs in the scene if not manually assigned
        if (manScripts.Count == 0)
        {
            FindAllManPrefabs();
        }
        
        // Initialize the manager
        InitializeManager();
    }
    
    private void FindAllManPrefabs()
    {
        // Find all GameObjects with ManLookAroundScript components
        ManLookAroundScript[] foundScripts = FindObjectsOfType<ManLookAroundScript>();
        
        foreach (ManLookAroundScript script in foundScripts)
        {
            if (!manScripts.Contains(script))
            {
                manScripts.Add(script);
            }
        }
        
        // If still no scripts found, try to find Man prefabs by name
        if (manScripts.Count == 0)
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("Man_") && !obj.GetComponent<ManLookAroundScript>())
                {
                    // Add the script to this Man prefab
                    ManLookAroundScript newScript = obj.AddComponent<ManLookAroundScript>();
                    manScripts.Add(newScript);
                    Debug.Log($"Added ManLookAroundScript to {obj.name}");
                }
            }
        }
        
        Debug.Log($"Found {manScripts.Count} Man prefabs in the scene");
    }
    
    private void InitializeManager()
    {
        activeManScripts.Clear();
        
        foreach (ManLookAroundScript script in manScripts)
        {
            if (script != null)
            {
                activeManScripts.Add(script);
                
                // If random timing is enabled, add random delays to each script
                if (enableRandomTiming)
                {
                    StartCoroutine(AddRandomDelay(script));
                }
            }
        }
        
        Debug.Log($"Initialized {activeManScripts.Count} active Man scripts");
    }
    
    private System.Collections.IEnumerator AddRandomDelay(ManLookAroundScript script)
    {
        // Add a random delay before starting the behavior
        float randomDelay = Random.Range(minDelayBetweenLooks, maxDelayBetweenLooks);
        yield return new WaitForSeconds(randomDelay);
        
        // Start the script's behavior
        if (script != null)
        {
            script.enabled = true;
        }
    }
    
    // Public method to make all Man prefabs look at camera simultaneously
    public void MakeAllLookAtCamera()
    {
        Debug.Log("Making all Man prefabs look at camera simultaneously");
        
        foreach (ManLookAroundScript script in activeManScripts)
        {
            if (script != null)
            {
                script.LookAtCameraNow();
            }
        }
    }
    
    // Public method to reset all Man prefabs to original positions
    public void ResetAllToOriginalPositions()
    {
        Debug.Log("Resetting all Man prefabs to original positions");
        
        foreach (ManLookAroundScript script in activeManScripts)
        {
            if (script != null)
            {
                script.ResetToOriginalPosition();
            }
        }
    }
    
    // Public method to pause all Man prefabs
    public void PauseAllManPrefabs()
    {
        Debug.Log("Pausing all Man prefabs");
        
        foreach (ManLookAroundScript script in activeManScripts)
        {
            if (script != null)
            {
                script.enabled = false;
            }
        }
    }
    
    // Public method to resume all Man prefabs
    public void ResumeAllManPrefabs()
    {
        Debug.Log("Resuming all Man prefabs");
        
        foreach (ManLookAroundScript script in activeManScripts)
        {
            if (script != null)
            {
                script.enabled = true;
            }
        }
    }
    
    // Public method to add a new Man prefab at runtime
    public void AddManPrefab(ManLookAroundScript newScript)
    {
        if (newScript != null && !manScripts.Contains(newScript))
        {
            manScripts.Add(newScript);
            activeManScripts.Add(newScript);
            Debug.Log($"Added new Man prefab: {newScript.gameObject.name}");
        }
    }
    
    // Public method to remove a Man prefab
    public void RemoveManPrefab(ManLookAroundScript scriptToRemove)
    {
        if (scriptToRemove != null)
        {
            manScripts.Remove(scriptToRemove);
            activeManScripts.Remove(scriptToRemove);
            Debug.Log($"Removed Man prefab: {scriptToRemove.gameObject.name}");
        }
    }
    
    private void OnValidate()
    {
        // Ensure values are valid
        if (minDelayBetweenLooks < 0)
            minDelayBetweenLooks = 0;
        
        if (maxDelayBetweenLooks < minDelayBetweenLooks)
            maxDelayBetweenLooks = minDelayBetweenLooks;
    }
}

