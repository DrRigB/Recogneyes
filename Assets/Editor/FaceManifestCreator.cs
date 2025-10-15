using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Editor utility to create and populate FaceManifest assets
/// </summary>
public class FaceManifestCreator : EditorWindow
{
    [MenuItem("Tools/Face Recognition/Create Face Manifest Asset")]
    static void CreateFaceManifestAsset()
    {
        // Create the asset
        FaceManifest manifest = ScriptableObject.CreateInstance<FaceManifest>();
        
        // Try to read existing manifest.txt and populate the asset
        string manifestPath = Path.Combine(Application.dataPath, "StreamingAssets", "Faces", "manifest.txt");
        if (File.Exists(manifestPath))
        {
            string[] lines = File.ReadAllLines(manifestPath);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    manifest.PersonNames.Add(trimmed); // Include comments too, the getter will filter them
                }
            }
            
            Debug.Log($"✅ Populated FaceManifest with {manifest.PersonNames.Count} entries from manifest.txt");
        }
        else
        {
            // Add default entries
            manifest.PersonNames.Add("# Add person names below (must match folder names in StreamingAssets/Faces/)");
            manifest.PersonNames.Add("MrSekol");
            manifest.PersonNames.Add("Rigdon");
            manifest.PersonNames.Add("Other");
            
            Debug.LogWarning("⚠️ manifest.txt not found, created FaceManifest with default entries");
        }
        
        // Save the asset
        string assetPath = "Assets/Resources/FaceData/FaceManifest.asset";
        
        // Ensure directory exists
        string dir = Path.GetDirectoryName(assetPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        AssetDatabase.CreateAsset(manifest, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        // Select it in the Project window
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = manifest;
        
        Debug.Log($"✅ FaceManifest asset created at: {assetPath}");
        Debug.Log("Next step: Assign this asset to FaceRecognitionManager's 'Face Manifest Asset' field in the Inspector");
    }
    
    [MenuItem("Tools/Face Recognition/Sync Face Manifest from Text File")]
    static void SyncFromTextFile()
    {
        // Find existing FaceManifest asset
        string[] guids = AssetDatabase.FindAssets("t:FaceManifest");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "No FaceManifest asset found. Create one first using 'Create Face Manifest Asset'", "OK");
            return;
        }
        
        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        FaceManifest manifest = AssetDatabase.LoadAssetAtPath<FaceManifest>(assetPath);
        
        // Read manifest.txt
        string manifestPath = Path.Combine(Application.dataPath, "StreamingAssets", "Faces", "manifest.txt");
        if (!File.Exists(manifestPath))
        {
            EditorUtility.DisplayDialog("Error", $"manifest.txt not found at:\n{manifestPath}", "OK");
            return;
        }
        
        // Clear and repopulate
        manifest.PersonNames.Clear();
        string[] lines = File.ReadAllLines(manifestPath);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                manifest.PersonNames.Add(trimmed);
            }
        }
        
        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"✅ Synced FaceManifest with {manifest.PersonNames.Count} entries from manifest.txt");
        EditorUtility.DisplayDialog("Success", $"FaceManifest synced with {manifest.PersonNames.Count} entries from manifest.txt", "OK");
    }
    
    [MenuItem("Tools/Face Recognition/Auto-Generate All image_list.txt Files")]
    static void AutoGenerateImageLists()
    {
        string facesPath = Path.Combine(Application.dataPath, "StreamingAssets", "Faces");
        
        if (!Directory.Exists(facesPath))
        {
            EditorUtility.DisplayDialog("Error", $"Faces folder not found at:\n{facesPath}", "OK");
            return;
        }
        
        int foldersProcessed = 0;
        int totalImages = 0;
        
        // Get all subdirectories (each represents a person)
        string[] personFolders = Directory.GetDirectories(facesPath);
        
        foreach (string personFolder in personFolders)
        {
            string personName = Path.GetFileName(personFolder);
            
            // Skip hidden folders
            if (personName.StartsWith("."))
                continue;
            
            // Find all image files in this folder
            List<string> imageFiles = new List<string>();
            
            // Search for common image formats
            string[] extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.JPG", "*.JPEG", "*.PNG" };
            foreach (string ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(personFolder, ext, SearchOption.TopDirectoryOnly));
            }
            
            if (imageFiles.Count == 0)
            {
                Debug.LogWarning($"⚠️ No images found in {personName}/ - skipping");
                continue;
            }
            
            // Create image_list.txt with just the filenames (not full paths)
            string imageListPath = Path.Combine(personFolder, "image_list.txt");
            List<string> filenames = new List<string>();
            
            foreach (string fullPath in imageFiles)
            {
                string filename = Path.GetFileName(fullPath);
                filenames.Add(filename);
            }
            
            File.WriteAllLines(imageListPath, filenames);
            
            foldersProcessed++;
            totalImages += imageFiles.Count;
            
            Debug.Log($"✅ Generated image_list.txt for '{personName}' with {imageFiles.Count} images");
        }
        
        AssetDatabase.Refresh();
        
        string message = $"Auto-generated image lists for {foldersProcessed} people ({totalImages} total images).\n\nNow you can just drop photos into folders - no manual text editing needed!";
        Debug.Log($"✅✅✅ {message}");
        EditorUtility.DisplayDialog("Success", message, "OK");
    }
}

/// <summary>
/// Automatically generates image_list.txt files before each build
/// This means you can just drop images into folders without manually updating text files!
/// </summary>
public class AutoGenerateImageListsOnBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log("🔄 AUTO-GENERATING image_list.txt files before build...");
        
        string facesPath = Path.Combine(Application.dataPath, "StreamingAssets", "Faces");
        
        if (!Directory.Exists(facesPath))
        {
            Debug.LogWarning($"⚠️ Faces folder not found at: {facesPath}");
            return;
        }
        
        int foldersProcessed = 0;
        int totalImages = 0;
        
        // Get all subdirectories (each represents a person)
        string[] personFolders = Directory.GetDirectories(facesPath);
        
        foreach (string personFolder in personFolders)
        {
            string personName = Path.GetFileName(personFolder);
            
            // Skip hidden folders and Unknown folder
            if (personName.StartsWith(".") || personName == "Unknown")
                continue;
            
            // Find all image files in this folder
            List<string> imageFiles = new List<string>();
            
            // Search for common image formats
            string[] extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.JPG", "*.JPEG", "*.PNG" };
            foreach (string ext in extensions)
            {
                imageFiles.AddRange(Directory.GetFiles(personFolder, ext, SearchOption.TopDirectoryOnly));
            }
            
            if (imageFiles.Count == 0)
            {
                Debug.LogWarning($"⚠️ No images found in {personName}/ - skipping");
                continue;
            }
            
            // Create image_list.txt with just the filenames (not full paths)
            string imageListPath = Path.Combine(personFolder, "image_list.txt");
            List<string> filenames = new List<string>();
            
            foreach (string fullPath in imageFiles)
            {
                string filename = Path.GetFileName(fullPath);
                filenames.Add(filename);
            }
            
            File.WriteAllLines(imageListPath, filenames);
            
            foldersProcessed++;
            totalImages += imageFiles.Count;
            
            Debug.Log($"✅ Generated image_list.txt for '{personName}' with {imageFiles.Count} images");
        }
        
        AssetDatabase.Refresh();
        
        Debug.Log($"✅✅✅ AUTO-GENERATED image lists for {foldersProcessed} people ({totalImages} total images)");
        Debug.Log("📱 Build will include updated image lists!");
    }
}

