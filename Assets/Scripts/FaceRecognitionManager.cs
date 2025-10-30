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
    
    [Header("Server Recognition (NEW - Offload to PC!)")]
    [Tooltip("Use PC server for recognition (better accuracy, no heavy models on device)")]
    public bool UseServerRecognition = true;
    [Tooltip("Primary server URL (tries localhost first for USB)")]
    public string PrimaryServerURL = "http://localhost:5000/recognize";
    [Tooltip("Fallback server URL (tries this if localhost fails - use PC IP for WiFi)")]
    public string FallbackServerURL = "http://localhost:5000/recognize";
    
    [Header("Anonymous Names (Train but show as Unknown)")]
    [Tooltip("People to train for better recognition but always display as 'Unknown' (e.g., celebrities to avoid false positives)")]
    public List<string> AnonymousNames = new List<string> { "Obama", "Jshlatt", "ScarlettJohansson" };
    
    [Header("Training Data")]
    [Tooltip("(RECOMMENDED) ScriptableObject containing person names - more reliable than text files")]
    public FaceManifest FaceManifestAsset;  // Preferred: ScriptableObject manifest
    public string TrainingDataFolder = "Faces";  // Folder in StreamingAssets/Faces/PersonName/photo.jpg
    public string ModelSaveFileName = "face_recognition_model.yml";  // Saved trained model
    
        [Header("Barracuda Deep Learning (Enhanced Recognition)")]
        [Tooltip("DISABLED: Use FaceEmbeddingPreprocessor + LightweightEmbeddingRecognizer instead")]
        public bool EnableBarracudaRecognition = false; // DEPRECATED: Use offline preprocessing instead
        [Tooltip("Fallback to LBPH if ArcFace fails")]
        public bool FallbackToLBPH = false; // Disabled by default - ArcFace should work
    
    [Header("Debug")]
    public bool ShowConfidenceScores = true;
    public bool ForceRetrainOnStart = false;  // Set to TRUE in Inspector to force retrain (ignores cached model)
    // Removed keyboard retrain (useless on AR goggles) - system now auto-validates on load
    
    // OpenCV Face Recognizer (LBPH algorithm) - LEGACY
    private FaceRecognizer _recognizer;
    
    // Mapping of label IDs to person names
    private Dictionary<int, string> _labelToName = new Dictionary<int, string>();
    
    // Is the recognizer trained and ready?
    private bool _isModelTrained = false;
    private bool _isServerConnected = false;
    
    // Statistics
    private int _totalPeopleTrained = 0;
    private int _totalImagesTrained = 0;
    
    // Server recognition cache
    private Dictionary<int, (string name, float confidence, float timestamp)> _serverResultCache = new Dictionary<int, (string, float, float)>();
    private int _currentFaceId = -1;
    private string _activeServerURL = null; // Track which URL is working

    void Start()
    {
        Debug.Log("=== FaceRecognitionManager Starting ===");
        
        // Initialize Enhanced OpenCV Recognizer (PRIMARY - uses ArcFace embeddings from PC!)
        // _enhancedRecognizer = GetComponent<EnhancedOpenCVRecognizer>(); // Removed
        // if (_enhancedRecognizer == null)
        // {
        //     _enhancedRecognizer = gameObject.AddComponent<EnhancedOpenCVRecognizer>();
        // }
        
        // Initialize TensorFlow Lite Recognizer (SECONDARY FALLBACK)
        // _embeddingRecognizer = GetComponent<TensorFlowLiteRecognizer>(); // Removed
        // if (_embeddingRecognizer == null)
        // {
        //     _embeddingRecognizer = gameObject.AddComponent<TensorFlowLiteRecognizer>();
        // }
        
        // Initialize Barracuda if enabled (deprecated)
        if (EnableBarracudaRecognition)
        {
            InitializeBarracuda();
        }
        
        if (EnableRecognition && AutoTrainOnStart)
        {
            if (UseServerRecognition)
            {
                Debug.Log("🌐 Server recognition enabled - establishing connection immediately");
                _isModelTrained = true; // Mark as ready since server handles recognition
                _isServerConnected = true; // Mark as connected immediately - server connection test will verify
                
                // Establish server connection immediately so it's ready when faces are detected
                StartCoroutine(EstablishServerConnection());
            }
            else
            {
                StartCoroutine(InitializeRecognizer());
            }
        }
    }



    /// <summary>
    /// Establish server connection immediately on startup
    /// </summary>
    private IEnumerator EstablishServerConnection()
    {
        Debug.Log("🔌 Establishing server connection immediately...");
        
        // Create a simple test image for connection testing
        byte[] testImage = CreateSimpleTestImage();
        
        // Try localhost first (for USB connection)
        Debug.Log("🔌 Testing localhost connection...");
        bool localhostSuccess = false;
        yield return StartCoroutine(TryServerURL(PrimaryServerURL, testImage, (name, conf) => {
            Debug.Log($"🔍 Localhost test response: {name} (confidence: {conf})");
            // Connection test: ANY response (even Unknown or Error) means server is reachable
            if (name != "Error")
            {
                localhostSuccess = true;
                _activeServerURL = PrimaryServerURL;
                _isServerConnected = true;  // Mark server as connected
                Debug.Log("✅ Server connection established via USB (localhost)");
            }
            else
            {
                Debug.Log($"❌ Localhost test failed: {name}");
            }
        }, markAsActive: true));
        
        if (localhostSuccess)
        {
            yield break;
        }
        
        // Fallback to WiFi IP if localhost failed
        Debug.Log("📡 localhost failed, trying WiFi connection...");
        bool wifiSuccess = false;
        yield return StartCoroutine(TryServerURL(FallbackServerURL, testImage, (name, conf) => {
            Debug.Log($"🔍 WiFi test response: {name} (confidence: {conf})");
            if (name != "Unknown" && name != "Error")
            {
                wifiSuccess = true;
                _activeServerURL = FallbackServerURL;
                _isServerConnected = true;  // Mark server as connected
                Debug.Log("✅ Server connection established via WiFi");
            }
            else
            {
                Debug.Log($"❌ WiFi test failed: {name}");
            }
        }, markAsActive: true));
        
        if (_activeServerURL != null && (localhostSuccess || wifiSuccess))
        {
            Debug.Log("🌐 Server connection ready - recognition will work immediately!");
        }
        else
        {
            Debug.LogWarning("⚠️ Could not establish server connection - will retry when faces are detected");
        }
    }
    
    /// <summary>
    /// Create a simple test image for server testing
    /// </summary>
    private byte[] CreateSimpleTestImage()
    {
        // Create a proper test image (100x100 pixels) that the server can process
        using (Mat testMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(128, 128, 128)))
        {
            // Add some simple pattern to make it more realistic
            Cv2.Rectangle(testMat, new OpenCvSharp.Rect(20, 20, 60, 60), new Scalar(255, 255, 255), -1);
            Cv2.Circle(testMat, new OpenCvSharp.Point(50, 50), 20, new Scalar(0, 0, 0), -1);
            return MatToJpgBytes(testMat);
        }
    }

    /// <summary>
    /// Initialize Barracuda deep learning component
    /// </summary>
    private void InitializeBarracuda()
    {
        try
        {
            // _barracudaGenerator = GetComponent<FaceEmbeddingGenerator>(); // Removed
            // if (_barracudaGenerator == null)
            // {
            //     _barracudaGenerator = gameObject.AddComponent<FaceEmbeddingGenerator>();
            // }
            
            // if (_barracudaGenerator.IsInitialized()) // Removed
            // {
            //     Debug.Log("✅ Barracuda deep learning initialized successfully!");
            // }
            // else
            // {
            //     Debug.LogWarning("⚠️ Barracuda initialization failed - will fallback to LBPH");
            //     EnableBarracudaRecognition = false;
            // }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Barracuda initialization error: {e.Message}");
            EnableBarracudaRecognition = false;
        }
    }


    private IEnumerator InitializeRecognizer()
    {
        Debug.Log("Initializing Face Recognizer...");
        
        // Create FisherFace recognizer - more accurate than LBPH
        // numComponents: number of components to keep for PCA (0 = keep all)
        // threshold: confidence threshold (we set high and handle manually)
        _recognizer = FisherFaceRecognizer.Create(
            numComponents: 0,           // Keep all components for best accuracy
            threshold: double.MaxValue  // We'll handle threshold manually in RecognizeFace()
        );
        
        Debug.Log("✅ FisherFace Recognizer created (more accurate than LBPH)");
        
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
            
            // Train Barracuda if enabled
            // if (EnableBarracudaRecognition && _barracudaGenerator != null && _barracudaGenerator.IsInitialized()) // Removed
            // {
            //     TrainBarracudaFromFolders();
            // }
            
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
        return RecognizeFace(faceGrayMat, -1); // Call with default face ID
    }
    
    /// <summary>
    /// Recognizes a face with face ID for tracking server results
    /// </summary>
    public (string name, double confidence) RecognizeFace(Mat faceGrayMat, int faceId)
    {
        // Try SERVER Recognition FIRST! (offload to PC)
        if (UseServerRecognition)
        {
            try
            {
                // Check if we have a cached result for this face FIRST
                if (faceId >= 0 && _serverResultCache.ContainsKey(faceId))
                {
                    var cached = _serverResultCache[faceId];
                    // Use cached result if less than 2 seconds old (allows periodic re-recognition to send new requests)
                    if (Time.time - cached.timestamp < 2.0f)
                    {
                        return (cached.name, cached.confidence);
                    }
                }
                
                // If just checking cache (no image provided), return "Processing..." ONLY if no cache exists
                if (faceGrayMat == null)
                {
                    return ("Processing...", 0.0);
                }
                
                // Convert Mat to JPG bytes
                byte[] jpgBytes = MatToJpgBytes(faceGrayMat);
                
                // Send to server (async) - try both URLs
                int capturedFaceId = faceId;
                StartCoroutine(RecognizeViaServerWithFallback(jpgBytes, (name, conf) => {
                    Debug.Log($"🌐 Server Recognition: {name} (confidence: {conf:F3})");
                    
                    // Cache the result for next frame
                    if (capturedFaceId >= 0)
                    {
                        _serverResultCache[capturedFaceId] = (name, conf, Time.time);
                        Debug.Log($"💾 CACHED result for Face ID {capturedFaceId}: {name} (confidence: {conf:F3})");
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ Cannot cache result - invalid face ID: {capturedFaceId}");
                    }
                }));
                
                // Return cached result from PREVIOUS request, not the one we just sent
                // The coroutine runs in background and will update cache for next frame
                if (faceId >= 0 && _serverResultCache.ContainsKey(faceId))
                {
                    var cached = _serverResultCache[faceId];
                    // Check if result is still valid (less than 5 seconds old)
                    if (Time.time - cached.timestamp < 5.0f)
                    {
                        return (cached.name, cached.confidence);
                    }
                }
                
                // First time we've seen this face, return Processing while server responds
                return ("Processing...", 0.0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Server recognition error: {e.Message}");
                Debug.Log("🔄 Falling back to local recognition...");
            }
        }
        
        // Try Enhanced OpenCV Recognizer (uses ArcFace embeddings from PC)
        // _enhancedRecognizer != null && _enhancedRecognizer.IsReady() // Removed
        // {
        //     try
        //     {
        //         string recognizedName = _enhancedRecognizer.RecognizeFace(faceGrayMat);
        //         Debug.Log($"🎯 Enhanced OpenCV Recognition: {recognizedName}");
        //         return (recognizedName, 1.0); // Default confidence
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"❌ Enhanced OpenCV recognition error: {e.Message}");
        //         Debug.Log("🔄 Falling back to TensorFlow Lite recognizer...");
        //     }
        // }
        
        // FALLBACK: Try TensorFlow Lite ArcFace Embedding Recognizer
        // _embeddingRecognizer != null // Removed
        // {
        //     try
        //     {
        //         string recognizedName = _embeddingRecognizer.RecognizeFace(faceGrayMat);
        //         Debug.Log($"🎯 TensorFlow Lite Recognition: {recognizedName}");
        //         return (recognizedName, 1.0); // Default confidence since new method doesn't return it
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"❌ TensorFlow Lite recognition error: {e.Message}");
        //         if (!FallbackToLBPH)
        //         {
        //             return ("Unknown", 0.0);
        //         }
        //         Debug.Log("🔄 Falling back to FisherFace...");
        //     }
        // }
        
        // Try Barracuda deep learning if enabled (deprecated)
        // if (EnableBarracudaRecognition && _barracudaGenerator != null && _barracudaGenerator.IsInitialized()) // Removed
        // {
        //     try
        //     {
        //         var barracudaResult = _barracudaGenerator.RecognizeFace(faceGrayMat);
        //         if (barracudaResult.name != "Unknown")
        //         {
        //             Debug.Log($"🎯 Barracuda Recognition: {barracudaResult.name} (confidence: {barracudaResult.confidence:F3})");
        //             return (barracudaResult.name, barracudaResult.confidence);
        //         }
        //         else if (!FallbackToLBPH)
        //         {
        //             return ("Unknown", 0.0);
        //         }
        //         else
        //         {
        //             Debug.Log("🔄 Barracuda failed, falling back to LBPH...");
        //         }
        //     }
        //     catch (System.Exception e)
        //     {
        //         Debug.LogError($"❌ Barracuda recognition error: {e.Message}");
        //         if (!FallbackToLBPH)
        //         {
        //             return ("Unknown", 0.0);
        //         }
        //         Debug.Log("🔄 Falling back to LBPH...");
        //     }
        // }
        
        // Fallback to LBPH if ArcFace/Barracuda disabled, failed, or not available
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
        // For server recognition, we're ready if server is connected (server handles recognition)
        // For local recognition, we need both model trained and recognizer initialized
        if (UseServerRecognition)
        {
            return _isServerConnected;  // Use server connection status instead of model training
        }
        else
        {
            return _isModelTrained && _recognizer != null;
        }
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
    /// Get person names from manifest (for Barracuda training)
    /// </summary>
    private List<string> GetPersonNamesFromManifest()
    {
        List<string> personNames = new List<string>();
        
        // Try to get names from ScriptableObject first
        if (FaceManifestAsset != null && FaceManifestAsset.PersonNames != null)
        {
            personNames.AddRange(FaceManifestAsset.PersonNames);
            Debug.Log($"📋 Found {personNames.Count} people in FaceManifest asset");
            return personNames;
        }
        
        // Fallback to text file
        string manifestPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, "manifest.txt");
        if (File.Exists(manifestPath))
        {
            try
            {
                string[] lines = File.ReadAllLines(manifestPath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                    {
                        personNames.Add(trimmedLine);
                    }
                }
                Debug.Log($"📋 Found {personNames.Count} people in manifest.txt");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error reading manifest.txt: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ No manifest found - checking folders directly");
            // Fallback: scan folders
            string facesPath = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder);
            if (Directory.Exists(facesPath))
            {
                string[] folders = Directory.GetDirectories(facesPath);
                foreach (string folder in folders)
                {
                    string folderName = Path.GetFileName(folder);
                    if (folderName != "Unknown" && !folderName.StartsWith("."))
                    {
                        personNames.Add(folderName);
                    }
                }
                Debug.Log($"📋 Found {personNames.Count} people by scanning folders");
            }
        }
        
        return personNames;
    }

    /// <summary>
    /// Train Barracuda deep learning model with the same data as LBPH
    /// </summary>
    private void TrainBarracudaFromFolders()
    {
        // _barracudaGenerator == null || !_barracudaGenerator.IsInitialized() // Removed
        // {
        //     Debug.LogWarning("⚠️ Barracuda not available for training");
        //     return;
        // }

        Debug.Log("🧠 Training Barracuda deep learning model...");
        
        try
        {
            // Clear existing embeddings
            // _barracudaGenerator.ClearKnownFaces(); // Removed
            
            // Get all person names from manifest
            List<string> personNames = GetPersonNamesFromManifest();
            if (personNames == null || personNames.Count == 0)
            {
                Debug.LogWarning("⚠️ No person names found for Barracuda training");
                return;
            }

            int totalEmbeddings = 0;
            
            foreach (string personName in personNames)
            {
                string personFolder = Path.Combine(Application.streamingAssetsPath, TrainingDataFolder, personName);
                if (!Directory.Exists(personFolder))
                {
                    Debug.LogWarning($"⚠️ Folder not found: {personFolder}");
                    continue;
                }

                // Get all image files
                string[] imageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.bmp" };
                List<string> imageFiles = new List<string>();
                
                foreach (string extension in imageExtensions)
                {
                    imageFiles.AddRange(Directory.GetFiles(personFolder, extension, SearchOption.TopDirectoryOnly));
                }

                if (imageFiles.Count == 0)
                {
                    Debug.LogWarning($"⚠️ No images found in {personFolder}");
                    continue;
                }

                Debug.Log($"📸 Processing {imageFiles.Count} images for {personName}...");
                
                // Process each image and generate embeddings
                foreach (string imagePath in imageFiles)
                {
                    try
                    {
                        // Load image
                        Mat image = Cv2.ImRead(imagePath, ImreadModes.Color);
                        if (image.Empty())
                        {
                            Debug.LogWarning($"⚠️ Failed to load image: {imagePath}");
                            continue;
                        }

                        // Convert to grayscale for face detection
                        Mat grayImage = new Mat();
                        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
                        
                        // Apply same preprocessing as LBPH
                        Mat processedImage = PreprocessForTraining(grayImage);
                        
                        // Generate embedding
                        // float[] embedding = _barracudaGenerator.GenerateEmbedding(processedImage); // Removed
                        // if (embedding != null)
                        // {
                        //     _barracudaGenerator.AddKnownFace(personName, embedding);
                        //     totalEmbeddings++;
                        // }
                        
                        // Cleanup
                        image.Dispose();
                        grayImage.Dispose();
                        processedImage.Dispose();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"❌ Error processing {imagePath}: {e.Message}");
                    }
                }
            }

            Debug.Log($"✅ Barracuda training complete! Generated {totalEmbeddings} embeddings for {personNames.Count} people.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Barracuda training error: {e.Message}");
        }
    }

    /// <summary>
    /// Convert OpenCV Mat to JPG bytes for sending to server
    /// </summary>
    private byte[] MatToJpgBytes(Mat mat)
    {
        // Mat should already be BGR from FaceDetector, just encode it
        Cv2.ImEncode(".jpg", mat, out byte[] jpgBytes);
        return jpgBytes;
    }
    
    /// <summary>
    /// Send face image to server for recognition (tries both localhost and IP)
    /// </summary>
    private IEnumerator RecognizeViaServerWithFallback(byte[] imageBytes, System.Action<string, float> callback)
    {
        // Try active URL first if we know one works
        if (_activeServerURL != null)
        {
            yield return StartCoroutine(TryServerURL(_activeServerURL, imageBytes, callback, markAsActive: false));
            yield break;
        }
        
        // Try localhost first (for USB connection)
        Debug.Log($"🔌 Trying server via USB (localhost)...");
        bool localhostSuccess = false;
        yield return StartCoroutine(TryServerURL(PrimaryServerURL, imageBytes, (name, conf) => {
            Debug.Log($"🔍 Localhost response: {name} (confidence: {conf})");
            if (name != "Error")
            {
                // SUCCESS: Server responded (even if "Unknown")
                localhostSuccess = true;
                _activeServerURL = PrimaryServerURL;
                _isServerConnected = true;  // Mark server as connected
                Debug.Log($"✅ Server connected via USB - Result: {name}");
                callback(name, conf);  // Update cache with result (even if "Unknown")
            }
            else
            {
                Debug.Log($"❌ Localhost connection failed: {name}");
            }
        }, markAsActive: true));
        
        if (localhostSuccess)
        {
            yield break;
        }
        
        // Fallback to WiFi IP if localhost failed
        Debug.Log($"📡 USB failed, trying WiFi ({FallbackServerURL})...");
        yield return StartCoroutine(TryServerURL(FallbackServerURL, imageBytes, (name, conf) => {
            Debug.Log($"🔍 WiFi response: {name} (confidence: {conf})");
            _activeServerURL = FallbackServerURL;
            _isServerConnected = true;  // Mark server as connected
            Debug.Log($"✅ Server connected via WiFi");
            callback(name, conf);
        }, markAsActive: true));
    }
    
    /// <summary>
    /// Try a specific server URL
    /// </summary>
    private IEnumerator TryServerURL(string url, byte[] imageBytes, System.Action<string, float> callback, bool markAsActive)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(imageBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            request.timeout = 5; // 5 second timeout (more time for server response)
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    // Parse JSON response
                    string jsonResponse = request.downloadHandler.text;
                    ServerResponse response = JsonUtility.FromJson<ServerResponse>(jsonResponse);
                    
                    if (response.success)
                    {
                        callback(response.name, response.confidence);
                    }
                    else
                    {
                        Debug.LogError($"❌ Server error: {response.error}");
                        callback("Error", 0.0f);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"❌ Failed to parse server response: {e.Message}");
                    callback("Error", 0.0f);
                }
            }
            else
            {
                Debug.LogError($"❌ Server request to {url} failed: {request.error}");
                callback("Error", 0.0f);
            }
        }
    }
    
    /// <summary>
    /// Server response structure
    /// </summary>
    [Serializable]
    private class ServerResponse
    {
        public string name;
        public float confidence;
        public bool success;
        public string error;
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

