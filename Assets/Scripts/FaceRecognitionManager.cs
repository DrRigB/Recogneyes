using UnityEngine;
using OpenCvSharp;
using OpenCvSharp.Face;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;

/// <summary>
/// Manages face recognition training and prediction.
/// Uses OpenCV's LBPH (Local Binary Patterns Histograms) algorithm.
/// Can be upgraded later to use deep learning or load embeddings from a database.
/// </summary>
public class FaceRecognitionManager : MonoBehaviour
{
    [Header("Recognition Settings")]
    public bool EnableRecognition = true;
    public double MaxDistanceThreshold = 120.0;  // Max distance for match (LBPH returns distance: lower=better, higher=worse). With universal preprocessing: 90-120 for strict, 120-140 for balanced, 140-170 for lenient
    public bool AutoTrainOnStart = true;
    
    [Header("Anonymous Names (Train but show as Unknown)")]
    [Tooltip("People to train for better recognition but always display as 'Unknown' (e.g., celebrities to avoid false positives)")]
    public List<string> AnonymousNames = new List<string> { "Obama", "Jshlatt", "ScarlettJohansson" };
    
    [Header("Training Data")]
    [Tooltip("(RECOMMENDED) ScriptableObject containing person names - more reliable than text files")]
    public FaceManifest FaceManifestAsset;  // Preferred: ScriptableObject manifest
    public string TrainingDataFolder = "Faces";  // Folder in StreamingAssets/Faces/PersonName/photo.jpg
    public string ModelSaveFileName = "face_recognition_model.yml";  // Saved trained model
    
    [Header("Debug")]
    public bool ShowConfidenceScores = true;
    public bool ForceRetrainOnStart = false;  // Set to TRUE in Inspector to force retrain (ignores cached model)
    // Removed keyboard retrain (useless on AR goggles) - system now auto-validates on load
    
    // OpenCV Face Recognizer (LBPH algorithm)
    private FaceRecognizer _recognizer;
    
    // Mapping of label IDs to person names
    private Dictionary<int, string> _labelToName = new Dictionary<int, string>();
    
    // Is the recognizer trained and ready?
    private bool _isModelTrained = false;
    
    // Statistics
    private int _totalPeopleTrained = 0;
    private int _totalImagesTrained = 0;

    void Start()
    {
        Debug.Log("=== FaceRecognitionManager Starting ===");
        
        if (EnableRecognition && AutoTrainOnStart)
        {
            StartCoroutine(InitializeRecognizer());
        }
    }


    private IEnumerator InitializeRecognizer()
    {
        Debug.Log("Initializing Face Recognizer...");
        
        // Create LBPH recognizer with optimized parameters
        // radius=1, neighbors=8, gridX=8, gridY=8 are default values that work well
        // threshold is set very high (double.MaxValue) so we can handle it manually
        _recognizer = LBPHFaceRecognizer.Create(
            radius: 1,
            neighbors: 8,
            gridX: 8,
            gridY: 8,
            threshold: double.MaxValue  // We'll handle threshold manually in RecognizeFace()
        );
        
        Debug.Log("✅ LBPH Face Recognizer created");
        
        // Check if training data has changed since last training
        string currentDataHash = null;
        yield return StartCoroutine(CalculateTrainingDataHash((hash) => currentDataHash = hash));
        
        string savedHashPath = Path.Combine(Application.persistentDataPath, "training_data_hash.txt");
        string savedHash = File.Exists(savedHashPath) ? File.ReadAllText(savedHashPath) : null;
        
        // Try to load existing trained model first (faster than retraining)
        string modelPath = Path.Combine(Application.persistentDataPath, ModelSaveFileName);
        bool modelExists = File.Exists(modelPath);
        
        // FORCE RETRAIN: If checkbox is set, skip loading and retrain from scratch
        if (ForceRetrainOnStart && modelExists)
        {
            Debug.LogWarning("🔥 FORCE RETRAIN ENABLED! Deleting old model and retraining...");
            File.Delete(modelPath);
            string mappingPath = Path.Combine(Application.persistentDataPath, "label_mapping.json");
            string hashPath = Path.Combine(Application.persistentDataPath, "training_data_hash.txt");
            if (File.Exists(mappingPath)) File.Delete(mappingPath);
            if (File.Exists(hashPath)) File.Delete(hashPath);
            modelExists = false;
        }
        
        // AUTO-RETRAIN DETECTION: Check if training data changed
        if (modelExists && currentDataHash != null && savedHash != null && currentDataHash == savedHash)
        {
            Debug.Log($"📂 Found existing trained model at: {modelPath}");
            Debug.Log($"✅ Training data unchanged (hash: {currentDataHash.Substring(0, 8)}...)");
            Debug.Log("⏳ Loading trained model (this should be instant)...");
            
            bool modelLoadedSuccessfully = false;
            
            try
            {
                _recognizer.Read(modelPath);
                
                // Load the label-to-name mapping
                string mappingPath = Path.Combine(Application.persistentDataPath, "label_mapping.json");
                if (File.Exists(mappingPath))
                {
                    string json = File.ReadAllText(mappingPath);
                    LabelMappingData data = JsonUtility.FromJson<LabelMappingData>(json);
                    _labelToName = new Dictionary<int, string>();
                    
                    for (int i = 0; i < data.labels.Length; i++)
                    {
                        _labelToName[data.labels[i]] = data.names[i];
                    }
                    
                    _isModelTrained = true;
                    _totalPeopleTrained = _labelToName.Count;
                    modelLoadedSuccessfully = true;
                    
                    Debug.Log($"✅✅✅ Model loaded successfully! Recognizes {_totalPeopleTrained} people.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"⚠️ Failed to load model: {ex.Message}. Will retrain from scratch.");
            }
            
            // VALIDATION: Check if manifest has more/fewer people than cached model (OUTSIDE try-catch)
            if (modelLoadedSuccessfully)
            {
                int actualPeopleCount = 0;
                
                // OPTION 1: Count from ScriptableObject (if assigned)
                if (FaceManifestAsset != null)
                {
                    Debug.Log($"🔍 VALIDATION: Counting people from FaceManifestAsset");
                    actualPeopleCount = FaceManifestAsset.GetActivePersonNames().Count;
                    Debug.Log($"🔍 VALIDATION: ScriptableObject has {actualPeopleCount} people");
                }
                // OPTION 2: Fallback to text file
                else
                {
                    string manifestPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, "manifest.txt");
                    Debug.Log($"🔍 VALIDATION: Reading manifest from: {manifestPath}");
                    
                    using (UnityWebRequest www = UnityWebRequest.Get(manifestPath))
                    {
                        yield return www.SendWebRequest();
                        if (www.result == UnityWebRequest.Result.Success)
                        {
                            string manifestContent = www.downloadHandler.text;
                            Debug.Log($"🔍 VALIDATION: Raw manifest content ({manifestContent.Length} chars):\n{manifestContent}");
                            
                            string[] lines = manifestContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                            Debug.Log($"🔍 VALIDATION: Split into {lines.Length} non-empty lines");
                            
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                string trimmed = line.Trim();
                                bool isComment = trimmed.StartsWith("#");
                                bool isEmpty = string.IsNullOrEmpty(trimmed);
                                bool willCount = !isEmpty && !isComment;
                                
                                Debug.Log($"🔍 VALIDATION Line {i}: '{line}' | Trimmed: '{trimmed}' | Comment: {isComment} | Empty: {isEmpty} | COUNT: {willCount}");
                                
                                if (willCount)
                                {
                                    actualPeopleCount++;
                                }
                            }
                            
                            Debug.Log($"🔍 VALIDATION: Total people counted: {actualPeopleCount}");
                        }
                        else
                        {
                            Debug.LogError($"🔍 VALIDATION: Failed to read manifest! Result: {www.result}, Error: {www.error}");
                        }
                    }
                }
                
                Debug.Log($"🔍 VALIDATION: Comparing actualPeopleCount ({actualPeopleCount}) vs _totalPeopleTrained ({_totalPeopleTrained})");
                
                if (actualPeopleCount != _totalPeopleTrained)
                {
                    Debug.LogWarning($"🔄 VALIDATION FAILED! Manifest has {actualPeopleCount} people but cached model has {_totalPeopleTrained} people.");
                    Debug.LogWarning("🗑️ Cached model is outdated. Forcing retrain...");
                    // Don't return - fall through to retrain
                }
                else
                {
                    Debug.Log($"✅ Validation passed: {actualPeopleCount} people in manifest matches cached model.");
                    yield break;
                }
            }
        }
        else
        {
            // Training data changed or no model exists - retrain!
            if (modelExists && currentDataHash != savedHash)
            {
                Debug.Log("🔄 TRAINING DATA CHANGED! Old model is outdated.");
                Debug.Log($"   Old hash: {savedHash?.Substring(0, 8)}...");
                Debug.Log($"   New hash: {currentDataHash?.Substring(0, 8)}...");
            }
            else
            {
                Debug.Log("📚 No existing model found.");
            }
            
            Debug.Log($"🚀 Training from scratch with current data in: StreamingAssets/{TrainingDataFolder}/");
        }
        
        yield return StartCoroutine(TrainFromFolders());
    }

    /// <summary>
    /// Trains the recognizer from image folders in StreamingAssets/Faces/
    /// Expected structure: StreamingAssets/Faces/PersonName/photo1.jpg, photo2.jpg, ...
    /// </summary>
    private IEnumerator TrainFromFolders()
    {
        Debug.Log("=== STARTING TRAINING ===");
        
        List<Mat> trainingImages = new List<Mat>();
        List<int> trainingLabels = new List<int>();
        
        string basePath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder);
        Debug.Log($"Training data path: {basePath}");
        
        // Since StreamingAssets is read-only on Android, we need to use UnityWebRequest
        // For now, we'll require the user to manually specify person folders
        // TODO: Add automatic folder discovery or manifest file
        
        // Declare these outside the using block so we can use them later
        string[] personNames = null;
        int peopleSkipped = 0;
        
        // OPTION 1: Use ScriptableObject manifest (PREFERRED - more reliable)
        if (FaceManifestAsset != null)
        {
            Debug.Log("✅ Using ScriptableObject manifest (FaceManifestAsset)");
            List<string> activeNames = FaceManifestAsset.GetActivePersonNames();
            personNames = activeNames.ToArray();
            
            Debug.Log($"📋 MANIFEST: Found {personNames.Length} people from ScriptableObject");
            for (int i = 0; i < personNames.Length; i++)
            {
                Debug.Log($"   Person {i}: '{personNames[i]}'");
            }
        }
        // OPTION 2: Fallback to text file (if ScriptableObject not assigned)
        else
        {
            Debug.LogWarning("⚠️ FaceManifestAsset not assigned - falling back to manifest.txt (less reliable on Android)");
            string manifestPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, "manifest.txt");
            Debug.Log($"Looking for manifest at: {manifestPath}");
            
            using (UnityWebRequest www = UnityWebRequest.Get(manifestPath))
            {
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"❌ No manifest found! Please either:");
                    Debug.LogError("  1. Assign FaceManifestAsset in Inspector (RECOMMENDED), or");
                    Debug.LogError("  2. Create StreamingAssets/Faces/manifest.txt with person names");
                    yield break;
                }
                
                string manifestContent = www.downloadHandler.text;
                personNames = manifestContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                Debug.Log($"📋 RAW MANIFEST: Found {personNames.Length} lines total");
                for (int i = 0; i < personNames.Length; i++)
                {
                    Debug.Log($"   Line {i}: '{personNames[i]}' (starts with #: {personNames[i].Trim().StartsWith("#")})");
                }
            }
        }
        
        int currentLabel = 0;
        
        // Load images for each person
        foreach (string personName in personNames)
        {
            string trimmedName = personName.Trim();
            if (string.IsNullOrEmpty(trimmedName) || trimmedName.StartsWith("#"))
            {
                peopleSkipped++;
                Debug.Log($"⏭️ SKIPPING line: '{personName}' (empty or comment)");
                continue;  // Skip empty lines and comments
            }
            
            Debug.Log($"🔵 PROCESSING person #{currentLabel}: '{trimmedName}'");
            
            // First, try to load the image list file
            string imageListPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, trimmedName, "image_list.txt");
            string[] imageFilenames = null;
            
            using (UnityWebRequest listWww = UnityWebRequest.Get(imageListPath))
            {
                yield return listWww.SendWebRequest();
                
                if (listWww.result == UnityWebRequest.Result.Success)
                {
                    string listContent = listWww.downloadHandler.text;
                    Debug.Log($"  📄 RAW image_list.txt content for {trimmedName} ({listContent.Length} chars): '{listContent}'");
                    
                    imageFilenames = listContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    Debug.Log($"  📋 Found image list with {imageFilenames.Length} files for {trimmedName}");
                    
                    for (int i = 0; i < imageFilenames.Length; i++)
                    {
                        Debug.Log($"    File {i}: '{imageFilenames[i]}' (length: {imageFilenames[i].Length})");
                    }
                }
                else
                {
                    Debug.LogWarning($"  ❌ No image_list.txt found for {trimmedName} at {imageListPath}. Result: {listWww.result}, Error: {listWww.error}");
                    continue;
                }
            }
            
            // Load all images from the list
            int imageCount = 0;
            
            foreach (string filename in imageFilenames)
            {
                string trimmedFilename = filename.Trim();
                Debug.Log($"    🔍 Processing filename: '{filename}' → trimmed: '{trimmedFilename}' (empty: {string.IsNullOrEmpty(trimmedFilename)}, is .meta: {trimmedFilename.EndsWith(".meta")})");
                
                if (string.IsNullOrEmpty(trimmedFilename) || trimmedFilename.EndsWith(".meta"))
                {
                    Debug.Log($"    ⏭️ SKIPPING: '{trimmedFilename}' (empty or .meta file)");
                    continue;  // Skip empty lines and Unity .meta files
                }
                
                string imagePath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, trimmedName, trimmedFilename);
                Debug.Log($"    📂 Attempting to load image from: {imagePath}");
                
                using (UnityWebRequest imgWww = UnityWebRequest.Get(imagePath))
                {
                    yield return imgWww.SendWebRequest();
                    
                    if (imgWww.result == UnityWebRequest.Result.Success)
                    {
                        byte[] imageData = imgWww.downloadHandler.data;
                        Debug.Log($"    ✅ Downloaded {trimmedFilename} ({imageData.Length} bytes)");
                        
                        try
                        {
                            // Convert to OpenCV Mat
                            Mat colorMat = Mat.FromImageData(imageData, ImreadModes.Color);
                            Debug.Log($"      → Decoded to Mat: {colorMat.Width}x{colorMat.Height}, {colorMat.Channels()} channels");
                            
                            // Convert to grayscale
                            Mat grayMat = new Mat();
                            Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);
                            
                            // UNIVERSAL PREPROCESSING: Make all photos match AR camera quality
                            Mat processedMat = PreprocessForTraining(grayMat);
                            
                            trainingImages.Add(processedMat);
                            trainingLabels.Add(currentLabel);
                            
                            imageCount++;
                            colorMat.Dispose();
                            grayMat.Dispose();
                            
                            Debug.Log($"    ✅ Successfully processed {trimmedFilename} → added to training set (count: {imageCount})");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"    ❌ Failed to process {trimmedFilename}: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"    ❌ Could not download {trimmedFilename} from {imagePath}. Result: {imgWww.result}, Error: {imgWww.error}");
                    }
                }
            }
            
            if (imageCount > 0)
            {
                _labelToName[currentLabel] = trimmedName;
                Debug.Log($"✅ Loaded {imageCount} images for {trimmedName} (Label: {currentLabel})");
                currentLabel++;
                _totalPeopleTrained++;
                _totalImagesTrained += imageCount;
            }
            else
            {
                Debug.LogWarning($"⚠️ No images found for {trimmedName}");
            }
        }
        
        Debug.Log($"📊 MANIFEST PARSING COMPLETE: Processed {personNames.Length} lines, skipped {peopleSkipped} lines, training {_totalPeopleTrained} people");
        Debug.Log($"🏷️ LABEL MAPPING: {string.Join(", ", _labelToName.Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
        
        // Check if we have enough training data
        if (trainingImages.Count == 0)
        {
            Debug.LogError("❌ NO TRAINING DATA FOUND! Recognition disabled.");
            Debug.LogError("Please add training images to: StreamingAssets/Faces/PersonName/photo1.jpg, photo2.jpg, etc.");
            yield break;
        }
        
        if (_totalPeopleTrained < 2)
        {
            Debug.LogWarning($"⚠️ Only {_totalPeopleTrained} person found. Need at least 2 people for meaningful recognition.");
        }
        
        Debug.Log($"📊 Training with {_totalImagesTrained} images from {_totalPeopleTrained} people...");
        
        // Train the recognizer
        bool trainingSuccess = false;
        try
        {
            _recognizer.Train(trainingImages, trainingLabels);
            _isModelTrained = true;
            trainingSuccess = true;
            
            Debug.Log($"✅✅✅ TRAINING COMPLETE! Model can now recognize {_totalPeopleTrained} people.");
            
            // Save the trained model for faster startup next time
            string modelPath = Path.Combine(Application.persistentDataPath, ModelSaveFileName);
            _recognizer.Write(modelPath);
            Debug.Log($"💾 Model saved to: {modelPath}");
            
            // Save label-to-name mapping
            SaveLabelMapping();
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Training failed! {ex.Message}\n{ex.StackTrace}");
        }
        
        // Save training data hash (must be outside try-catch due to yield return)
        if (trainingSuccess)
        {
            string currentHash = null;
            yield return StartCoroutine(CalculateTrainingDataHash((hash) => currentHash = hash));
            if (currentHash != null)
            {
                string hashPath = Path.Combine(Application.persistentDataPath, "training_data_hash.txt");
                File.WriteAllText(hashPath, currentHash);
                Debug.Log($"💾 Training data hash saved: {currentHash.Substring(0, 16)}...");
            }
        }
        
        // Clean up training mats
        foreach (var mat in trainingImages)
        {
            mat?.Dispose();
        }
    }

    /// <summary>
    /// Recognizes a face from a grayscale Mat (should be the detected face region).
    /// Returns the person's name and confidence score.
    /// </summary>
    public (string name, double confidence) RecognizeFace(Mat faceGrayMat)
    {
        if (!_isModelTrained || _recognizer == null)
        {
            return ("Unknown", 0.0);
        }
        
        try
        {
            // UNIVERSAL PREPROCESSING: Same as training to ensure consistency
            Mat processedFace = PreprocessForTraining(faceGrayMat);
            
            // Predict - LBPH returns a distance metric (lower = better match)
            _recognizer.Predict(processedFace, out int predictedLabel, out double distance);
            
            processedFace.Dispose();
            
            // Get predicted person name for logging
            string predictedName = _labelToName.ContainsKey(predictedLabel) ? _labelToName[predictedLabel] : "UNKNOWN_LABEL";
            
            // VERBOSE LOGGING: Show what model thinks
            Debug.Log($"🔍 RECOGNITION: Best match = '{predictedName}' (label:{predictedLabel}) | Distance: {distance:F1} | Threshold: {MaxDistanceThreshold}");
            
            // Check if distance is within acceptable threshold
            // Lower distance = better match (0 = perfect, higher = worse)
            if (distance > MaxDistanceThreshold)
            {
                // Too far away, not a match
                Debug.Log($"❌ REJECTED: Distance {distance:F1} > threshold {MaxDistanceThreshold} - returning Unknown");
                return ("Unknown", distance);
            }
            
            // Get person name from label
            if (_labelToName.ContainsKey(predictedLabel))
            {
                string name = _labelToName[predictedLabel];
                
                // Check if this person should be shown as Anonymous/Unknown
                if (AnonymousNames != null && AnonymousNames.Contains(name))
                {
                    Debug.Log($"🎭 RECOGNIZED AS ANONYMOUS: '{name}' with distance {distance:F1} → Displaying as 'Unknown'");
                    return ("Unknown", distance);
                }
                
                Debug.Log($"✅ ACCEPTED: '{name}' with distance {distance:F1}");
                return (name, distance);
            }
            else
            {
                Debug.LogWarning($"⚠️ Predicted label {predictedLabel} not in mapping!");
                return ("Unknown", distance);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Recognition error: {ex.Message}");
            return ("Error", 0.0);
        }
    }

    /// <summary>
    /// Save the label-to-name mapping as JSON for persistence
    /// </summary>
    private void SaveLabelMapping()
    {
        try
        {
            LabelMappingData data = new LabelMappingData();
            data.labels = new int[_labelToName.Count];
            data.names = new string[_labelToName.Count];
            
            int index = 0;
            foreach (var kvp in _labelToName)
            {
                data.labels[index] = kvp.Key;
                data.names[index] = kvp.Value;
                index++;
            }
            
            string json = JsonUtility.ToJson(data, true);
            string mappingPath = Path.Combine(Application.persistentDataPath, "label_mapping.json");
            File.WriteAllText(mappingPath, json);
            
            Debug.Log($"💾 Label mapping saved to: {mappingPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save label mapping: {ex.Message}");
        }
    }

    /// <summary>
    /// Public API: Check if recognizer is ready
    /// </summary>
    public bool IsReady()
    {
        return _isModelTrained && _recognizer != null;
    }

    /// <summary>
    /// Public API: Get number of people the model can recognize
    /// </summary>
    public int GetTotalPeopleTrained()
    {
        return _totalPeopleTrained;
    }

    /// <summary>
    /// Public API: Retrain the model (call this when new training data is added)
    /// </summary>
    public void Retrain()
    {
        Debug.Log("🔄 Retraining requested...");
        _isModelTrained = false;
        StartCoroutine(TrainFromFolders());
    }

    /// <summary>
    /// Force retrain by deleting cached model and hash, then retraining
    /// </summary>
    public void ForceRetrain()
    {
        Debug.Log("🔥 FORCE RETRAIN: Deleting cached model and retraining...");
        
        // Delete cached model files
        string modelPath = Path.Combine(Application.persistentDataPath, ModelSaveFileName);
        string mappingPath = Path.Combine(Application.persistentDataPath, "label_mapping.json");
        string hashPath = Path.Combine(Application.persistentDataPath, "training_data_hash.txt");
        
        try
        {
            if (File.Exists(modelPath)) 
            {
                File.Delete(modelPath);
                Debug.Log("🗑️ Deleted old model");
            }
            if (File.Exists(mappingPath))
            {
                File.Delete(mappingPath);
                Debug.Log("🗑️ Deleted old label mapping");
            }
            if (File.Exists(hashPath))
            {
                File.Delete(hashPath);
                Debug.Log("🗑️ Deleted old training hash");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error deleting cached files: {ex.Message}");
        }
        
        // Retrain
        _isModelTrained = false;
        StartCoroutine(InitializeRecognizer());
    }

    /// <summary>
    /// Calculates a hash of all training data (manifest + image lists) to detect changes
    /// </summary>
    private IEnumerator CalculateTrainingDataHash(System.Action<string> callback)
    {
        System.Text.StringBuilder dataString = new System.Text.StringBuilder();
        
        // Include manifest.txt
        string manifestPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, "manifest.txt");
        using (UnityWebRequest www = UnityWebRequest.Get(manifestPath))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                dataString.Append(www.downloadHandler.text);
            }
            else
            {
                Debug.LogWarning("Could not read manifest.txt for hash calculation");
                callback(null);
                yield break;
            }
        }
        
        // Get person names from manifest
        string[] personNames = dataString.ToString().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Include all image_list.txt files
        foreach (string personName in personNames)
        {
            string trimmedName = personName.Trim();
            if (string.IsNullOrEmpty(trimmedName) || trimmedName.StartsWith("#"))
            {
                continue;
            }
            
            string imageListPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, trimmedName, "image_list.txt");
            using (UnityWebRequest www = UnityWebRequest.Get(imageListPath))
            {
                yield return www.SendWebRequest();
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    dataString.Append(trimmedName);
                    dataString.Append(www.downloadHandler.text);
                }
            }
        }
        
        // Calculate SHA256 hash
        string hash = ComputeHash(dataString.ToString());
        callback(hash);
    }

    /// <summary>
    /// Simple hash function for training data
    /// </summary>
    private string ComputeHash(string input)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
    }

    /// <summary>
    /// Universal preprocessing to normalize ALL images (training and recognition)
    /// This ensures professional photos, phone selfies, and AR camera feed all look similar
    /// </summary>
    private Mat PreprocessForTraining(Mat grayImage)
    {
        // Step 1: Resize to consistent size (100x100)
        Mat resized = new Mat();
        Cv2.Resize(grayImage, resized, new Size(100, 100), interpolation: InterpolationFlags.Area);
        
        // Step 2: Apply Gaussian blur to reduce noise and quality differences
        // This helps professional photos (very sharp) match phone photos (slightly blurry)
        Mat blurred = new Mat();
        Cv2.GaussianBlur(resized, blurred, new Size(3, 3), 0);
        
        // Step 3: Histogram equalization to normalize lighting
        // Makes bright professional studio photos match dimmer phone/AR photos
        Mat equalized = new Mat();
        Cv2.EqualizeHist(blurred, equalized);
        
        // Step 4: CLAHE (Contrast Limited Adaptive Histogram Equalization)
        // Better than regular histogram equalization - handles local lighting variations
        // This is KEY for handling different photo qualities!
        using (var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8)))
        {
            Mat enhanced = new Mat();
            clahe.Apply(equalized, enhanced);
            
            // Clean up intermediate mats
            resized.Dispose();
            blurred.Dispose();
            equalized.Dispose();
            
            return enhanced;
        }
    }

    void OnDestroy()
    {
        _recognizer?.Dispose();
    }

    /// <summary>
    /// Serializable data structure for saving label mappings
    /// </summary>
    [Serializable]
    private class LabelMappingData
    {
        public int[] labels;
        public string[] names;
    }
}

