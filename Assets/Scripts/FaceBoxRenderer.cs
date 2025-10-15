using UnityEngine;

/// <summary>
/// Renders a 3D wireframe box in AR space using LineRenderer.
/// This creates a proper 3D border that maintains consistent appearance at any distance.
/// </summary>
public class FaceBoxRenderer : MonoBehaviour
{
    private LineRenderer _lineRenderer;
    private readonly Vector3[] _boxCorners = new Vector3[16]; // 4 corners x 4 sides = 16 line segments
    private TextMesh _textLabel;  // Text label for displaying Face ID or Name
    private GameObject _labelObject;
    private string _currentText = "";
    
    public void Initialize(Color color, float lineWidth)
    {
        _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.startColor = color;
        _lineRenderer.endColor = color;
        _lineRenderer.startWidth = lineWidth;
        _lineRenderer.endWidth = lineWidth;
        _lineRenderer.positionCount = 16;
        _lineRenderer.loop = false;
        _lineRenderer.useWorldSpace = true;
        
        // Ensure it renders in front of everything in AR
        _lineRenderer.sortingOrder = 1000;
        
        // Create text label (for ID or Name)
        _labelObject = new GameObject("FaceText_Label");
        _labelObject.transform.SetParent(transform);
        _textLabel = _labelObject.AddComponent<TextMesh>();
        _textLabel.fontSize = 50;
        _textLabel.color = color;
        _textLabel.anchor = TextAnchor.MiddleCenter;
        _textLabel.alignment = TextAlignment.Center;
        _textLabel.characterSize = 0.02f;  // Small text in world space
        
        gameObject.SetActive(false); // Start hidden
    }
    
    /// <summary>
    /// Updates the box to render at a specific 3D position with given dimensions.
    /// Box always faces the camera (billboard effect).
    /// </summary>
    public void UpdateBox(Vector3 center, Vector2 size, string displayText = "")
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
        
        _currentText = displayText;
        
        // Calculate the 4 corners of the rectangle in 3D space
        float halfWidth = size.x / 2f;
        float halfHeight = size.y / 2f;
        
        // Get camera to make box face it (billboard effect)
        Camera mainCam = Camera.main;
        
        Vector3 topLeft, topRight, bottomRight, bottomLeft;
        
        if (mainCam == null)
        {
            // Fallback to simple forward-facing if no camera
            topLeft = center + new Vector3(-halfWidth, halfHeight, 0);
            topRight = center + new Vector3(halfWidth, halfHeight, 0);
            bottomRight = center + new Vector3(halfWidth, -halfHeight, 0);
            bottomLeft = center + new Vector3(-halfWidth, -halfHeight, 0);
        }
        else
        {
            // Calculate box orientation to face camera
            Vector3 right = mainCam.transform.right;
            Vector3 up = mainCam.transform.up;
            
            // Calculate corners based on camera orientation (billboard)
            topLeft = center - right * halfWidth + up * halfHeight;
            topRight = center + right * halfWidth + up * halfHeight;
            bottomRight = center + right * halfWidth - up * halfHeight;
            bottomLeft = center - right * halfWidth - up * halfHeight;
        }
        
        DrawBox(topLeft, topRight, bottomRight, bottomLeft);
        
        // Update text label (ID or Name)
        if (!string.IsNullOrEmpty(displayText))
        {
            if (!_labelObject.activeSelf)
            {
                _labelObject.SetActive(true);
            }
            
            _textLabel.text = displayText;
            
            // Position label above the face box
            Vector3 up = mainCam != null ? mainCam.transform.up : Vector3.up;
            _labelObject.transform.position = center + up * (halfHeight + 0.05f);
            
            // Make label face camera (billboard)
            if (mainCam != null)
            {
                _labelObject.transform.rotation = Quaternion.LookRotation(_labelObject.transform.position - mainCam.transform.position);
            }
        }
        else
        {
            if (_labelObject.activeSelf)
            {
                _labelObject.SetActive(false);
            }
        }
    }
    
    private void DrawBox(Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft)
    {
        // Draw the box as 4 lines (top, right, bottom, left)
        // Each line needs 2 points, but we connect them to form a continuous loop
        int idx = 0;
        
        // Top line
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topRight;
        
        // Right line
        _boxCorners[idx++] = topRight;
        _boxCorners[idx++] = bottomRight;
        
        // Bottom line
        _boxCorners[idx++] = bottomRight;
        _boxCorners[idx++] = bottomLeft;
        
        // Left line
        _boxCorners[idx++] = bottomLeft;
        _boxCorners[idx++] = topLeft;
        
        // Duplicate the last segment to close the loop visually
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        _boxCorners[idx++] = topLeft;
        
        _lineRenderer.SetPositions(_boxCorners);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}

