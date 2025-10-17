using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Scriptable Object to store face recognition person names.
/// This is more reliable than text files because Unity always includes it in builds.
/// </summary>
[CreateAssetMenu(fileName = "FaceManifest", menuName = "Face Recognition/Face Manifest", order = 1)]
public class FaceManifest : ScriptableObject
{
    [Header("Person Names")]
    [Tooltip("List of person names - must match folder names in StreamingAssets/Faces/")]
    public List<string> PersonNames = new List<string>();

    /// <summary>
    /// Get all active person names (non-empty, non-comment)
    /// </summary>
    public List<string> GetActivePersonNames()
    {
        List<string> activeNames = new List<string>();
        
        foreach (string name in PersonNames)
        {
            string trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
            {
                activeNames.Add(trimmed);
            }
        }
        
        return activeNames;
    }
}

