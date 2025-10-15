using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Helper script to generate a file list for face recognition training.
/// Run this in the Editor to create a manifest of all images in each person's folder.
/// </summary>
public class FaceRecognitionTrainingHelper : MonoBehaviour
{
    [ContextMenu("Generate Image Lists")]
    public void GenerateImageLists()
    {
        string facesPath = Path.Combine(Application.streamingAssetsPath, "Faces");
        
        if (!Directory.Exists(facesPath))
        {
            Debug.LogError($"Faces folder not found at: {facesPath}");
            return;
        }
        
        // Get all subdirectories (person folders)
        string[] personFolders = Directory.GetDirectories(facesPath);
        
        Debug.Log($"Found {personFolders.Length} person folders");
        
        foreach (string personFolder in personFolders)
        {
            string personName = Path.GetFileName(personFolder);
            Debug.Log($"\n=== {personName} ===");
            
            // Get all image files
            List<string> imageFiles = new List<string>();
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.jpg"));
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.jpeg"));
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.png"));
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.JPG"));
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.JPEG"));
            imageFiles.AddRange(Directory.GetFiles(personFolder, "*.PNG"));
            
            // Filter out Unity .meta files
            imageFiles.RemoveAll(f => f.EndsWith(".meta"));
            
            Debug.Log($"Found {imageFiles.Count} images:");
            foreach (string imageFile in imageFiles)
            {
                string filename = Path.GetFileName(imageFile);
                Debug.Log($"  - {filename}");
            }
            
            // Create image list file
            string listPath = Path.Combine(personFolder, "image_list.txt");
            File.WriteAllLines(listPath, imageFiles.ConvertAll(f => Path.GetFileName(f)));
            
            Debug.Log($"✅ Created image list at: {listPath}");
        }
        
        Debug.Log("\n✅ Done! Image lists created for all people.");
    }
}

