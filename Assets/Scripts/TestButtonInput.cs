using UnityEngine;
using TMPro;

public class TestButtonInput : MonoBehaviour
{
    public TextMeshProUGUI debugText;
    
    private void Start()
    {
        if (debugText == null)
        {
            Debug.LogError("DebugText not assigned to TestButtonInput!");
            return;
        }
        
        debugText.text = "TestButtonInput initialized. Press A button to test.";
    }
    
    private void Update()
    {
        // Test various input methods
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("A button pressed (OVRInput.GetDown)");
            if (debugText != null)
                debugText.text = "A button pressed! (OVRInput.GetDown)";
        }
        
        if (OVRInput.GetUp(OVRInput.Button.One))
        {
            Debug.Log("A button released (OVRInput.GetUp)");
            if (debugText != null)
                debugText.text = "A button released! (OVRInput.GetUp)";
        }
        
        // Test raw button input
        if (OVRInput.GetDown(OVRInput.RawButton.A))
        {
            Debug.Log("Raw A button pressed (OVRInput.RawButton.A)");
            if (debugText != null)
                debugText.text = "Raw A button pressed! (OVRInput.RawButton.A)";
        }
        
        // Test if button is held
        if (OVRInput.Get(OVRInput.Button.One))
        {
            Debug.Log("A button is being held");
        }
    }
}
