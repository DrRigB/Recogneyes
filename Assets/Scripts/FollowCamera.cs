using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public float distance = 0.5f;  // Changed to 0.5 meters - even closer!
    public bool followRotation = true;
    
    private Transform cameraTransform;
    private int frameCount = 0;
    
    void Start()
    {
        // Find the main camera
        cameraTransform = Camera.main.transform;
        
        if (cameraTransform == null)
        {
            Debug.LogError("❌❌❌ FollowCamera: No main camera found! ❌❌❌");
        }
        else
        {
            Debug.Log($"✅✅✅ FollowCamera: SUCCESSFULLY attached to GameObject '{gameObject.name}' following camera '{cameraTransform.name}' ✅✅✅");
            Debug.Log($"📏 FollowCamera: Distance set to {distance} meters");
            Debug.Log($"📐 FollowCamera: Canvas scale is {transform.localScale}");
            Debug.Log($"🔄 FollowCamera: Follow rotation = {followRotation}");
        }
    }
    
    void Update()
    {
        if (cameraTransform == null) return;
        
        // Position the canvas in front of the camera
        Vector3 newPosition = cameraTransform.position + cameraTransform.forward * distance;
        transform.position = newPosition;
        
        // Make it face the camera
        if (followRotation)
        {
            transform.rotation = cameraTransform.rotation;
        }
        
        // More frequent logging at first, then every 60 frames
        frameCount++;
        if (frameCount <= 10 || frameCount % 60 == 0)
        {
            Debug.Log($"🎯 FollowCamera Frame {frameCount}: Canvas at {newPosition}, Camera at {cameraTransform.position}, Distance: {Vector3.Distance(cameraTransform.position, newPosition):F2}m");
            Debug.Log($"📊 FollowCamera Frame {frameCount}: Canvas rotation {transform.rotation.eulerAngles}, Camera rotation {cameraTransform.rotation.eulerAngles}");
        }
    }
}

