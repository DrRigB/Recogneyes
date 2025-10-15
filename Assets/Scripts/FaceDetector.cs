
using UnityEngine;
using UnityEngine.UI;
using OpenCvSharp;
using OpenCvSharp.Unity;
using System;
using System.Collections;
using System.IO;
using UnityEngine.Networking;

public class FaceDetector : MonoBehaviour
{
    // Unity will auto-load native libraries from Assets/Plugins/Android/libs/x86_64/
    
    public RawImage DisplayImage;  // Optional - can be disabled for invisible mode
    public float FaceBoxLineWidth = 0.003f;  // Width of border lines in meters (3mm for better visibility)
    public Color FaceBoxColor = Color.green;
    public float EstimatedFaceDepth = 1.0f;  // Estimated distance to faces in meters
    public int DownsampleFactor = 2;  // Lower = better quality, more reliable detection
    [Range(1.1f, 2.0f)]
    public float BoxSizeMultiplier = 1.4f;  // Multiply box size to cover more of the head (1.4 = 40% bigger)
    [Range(0f, 0.95f)]
    public float SmoothingFactor = 0.2f;  // Small smoothing for stability without drift
    [Range(1, 10)]
    public int DetectionFrameSkip = 1;  // Run detection EVERY frame for best tracking
    public bool UseMotionPrediction = false;  // Keep disabled
    public bool ShowFaceIDs = false;  // Hide IDs - focus on detection quality first
    public int FacePersistenceFrames = 45;  // Moderate - keep tracking for 1.5 seconds (45 frames at 30fps)
    public bool DetectProfileFaces = false;  // Disable profile detection - focus on frontal first
    [Range(2, 10)]
    public int StableDetectionFrames = 3;  // Require 3 consecutive frames (faster confirmation, less missed detections)
    public float MovementThreshold = 0.08f;  // Only update box if face moves > 8% of screen (prevents jitter)
    
    [Header("Face Recognition")]
    public FaceRecognitionManager RecognitionManager;  // Assign in Inspector
    public bool ShowRecognizedNames = true;  // Show names instead of IDs

    private WebCamTexture _webCamTexture;
    private CascadeClassifier _cascade;
    private Mat _rgbaMat;
    private Mat _grayMat;
    private Texture2D _displayTexture;
    private bool _isInitialized = false;
    private FaceBoxRenderer[] _faceBoxRenderers;
    private const int MaxFaceBoxes = 10;
    
    // Smoothing and tracking data for each face box
    private Vector3[] _smoothedPositions = new Vector3[MaxFaceBoxes];
    private Vector2[] _smoothedSizes = new Vector2[MaxFaceBoxes];
    private bool[] _boxInitialized = new bool[MaxFaceBoxes];
    private OpenCvSharp.Rect[] _lastDetectedFaces = new OpenCvSharp.Rect[0];  // Cache last detection
    
    // Motion prediction for smoother tracking between detection frames
    private Vector3[] _previousPositions = new Vector3[MaxFaceBoxes];
    private Vector3[] _boxVelocities = new Vector3[MaxFaceBoxes];
    
    // Face ID tracking system (foundation for face recognition)
    private int[] _faceIDs = new int[MaxFaceBoxes];  // Unique ID for each tracked face
    private int _nextFaceID = 1;  // Counter for assigning new IDs
    private float _faceMatchThreshold = 0.35f;  // MODERATE - must be within 35% screen distance to match (increased for stability)
    private int[] _framesSinceLastSeen = new int[MaxFaceBoxes];  // Frames since this face was detected
    private OpenCvSharp.Rect[] _lastKnownFaceRects = new OpenCvSharp.Rect[MaxFaceBoxes];  // Cache face rectangles
    
    // STABLE DETECTION: Require multiple consecutive frames before confirming a face
    private int[] _consecutiveDetections = new int[MaxFaceBoxes];  // How many frames in a row detected
    private bool[] _isConfirmedFace = new bool[MaxFaceBoxes];  // Only show if confirmed
    
    // FACE RECOGNITION: Store recognized names for each face
    private string[] _recognizedNames = new string[MaxFaceBoxes];  // Person's name
    private double[] _recognitionConfidence = new double[MaxFaceBoxes];  // Confidence score
    
    // Multi-cascade detection for better tracking
    private CascadeClassifier _cascadeProfile;  // Profile face detector

    private const string CameraPermission = "android.permission.CAMERA";

    void Start()
    {
        Debug.Log($"FaceDetector starting - DisplayImage assigned: {DisplayImage != null}");
        StartCoroutine(CheckAndRequestPermissions());
    }

    private IEnumerator CheckAndRequestPermissions()
    {
        Debug.Log("Checking for camera permission...");

        if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(CameraPermission))
        {
            Debug.Log("Camera permission already granted.");
            yield return StartCoroutine(InitializeEverything());
        }
        else
        {
            Debug.Log("Camera permission not yet granted. Requesting...");
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += OnPermissionGranted;
            callbacks.PermissionDenied += OnPermissionDenied;
            callbacks.PermissionDeniedAndDontAskAgain += OnPermissionDenied;
            UnityEngine.Android.Permission.RequestUserPermission(CameraPermission, callbacks);
        }
    }

    private void OnPermissionGranted(string permissionName)
    {
        Debug.Log($"Permission {permissionName} was granted. Proceeding with initialization.");
        StartCoroutine(InitializeEverything());
    }

    private void OnPermissionDenied(string permissionName)
    {
        Debug.LogError($"Permission {permissionName} was denied. Face detection cannot start.");
    }

    private IEnumerator InitializeEverything()
    {
        Debug.Log("=== INITIALIZING WEBCAM ===");
        
        // Add timeout protection
        float timeout = 30f; // 30 seconds timeout
        float startTime = Time.time;
        
        // Get available cameras
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"Found {devices.Length} camera devices");
        
        if (devices.Length == 0)
        {
            Debug.LogError("ERROR: No cameras found on device!");
            yield break;
        }

        // Log available cameras
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"Camera {i}: {devices[i].name} (Front: {devices[i].isFrontFacing})");
        }

        // Try Camera 2 instead - Camera 0 was completely black (tracking sensor, not RGB camera)
        // Magic Leap 2 has: 2 tracking sensors + 1 RGB camera on top
        int cameraIndex = 2;  // Try camera 2 (top RGB camera)
        Debug.Log($"🎥 Attempting to use Camera {cameraIndex}: {devices[cameraIndex].name}");
        _webCamTexture = new WebCamTexture(devices[cameraIndex].name, 1280, 720, 30);
        _webCamTexture.Play();

        Debug.Log($"Started camera: {devices[cameraIndex].name}, waiting for first frame...");
        int waitFrames = 0;
        while (!_webCamTexture.didUpdateThisFrame)
        {
            waitFrames++;
            if (waitFrames > 300) // 10 seconds at 30fps
            {
                Debug.LogError($"ERROR: Camera timeout! Camera playing: {_webCamTexture.isPlaying}, Size: {_webCamTexture.width}x{_webCamTexture.height}");
                yield break;
            }
            yield return null;
        }

        Debug.Log($"=== CAMERA STARTED: {_webCamTexture.width}x{_webCamTexture.height} @ {_webCamTexture.requestedFPS}fps ===");

        Debug.Log("OpenCV libraries should be auto-loaded by Unity from Plugins folder...");
        
        // Check timeout
        if (Time.time - startTime > timeout)
        {
            Debug.LogError("TIMEOUT: Initialization took too long!");
            yield break;
        }
        
        // Add a small delay to let Unity load the libraries
        yield return new WaitForSeconds(0.5f);
        
        try
        {
            Debug.Log("Testing OpenCV initialization...");
            // Try a simple OpenCV operation to test if libraries loaded
            using (var testMat = new Mat(1, 1, MatType.CV_8UC1))
            {
                Debug.Log($"OpenCV test Mat created successfully! Size: {testMat.Width}x{testMat.Height}");
            }
            Debug.Log("OpenCV library loaded successfully!");
        }
        catch (Exception ex)
        {
            Debug.LogError($"CRITICAL ERROR: OpenCV failed to initialize! {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"Stack trace: {ex.StackTrace}");
            yield break;
        }
        
        Debug.Log("Loading Haar Cascade classifiers...");
        
        // Load frontal face cascade
        string cascadePath = Path.Combine(Application.streamingAssetsPath, "haarcascade_frontalface_default.xml");
        
        using (UnityWebRequest www = UnityWebRequest.Get(cascadePath))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"ERROR: Failed to load haarcascade file: {www.error}");
                yield break;
            }
            
            string tempPath = Path.Combine(Application.persistentDataPath, "haarcascade_frontalface_default.xml");
            File.WriteAllBytes(tempPath, www.downloadHandler.data);
            Debug.Log($"Wrote frontal cascade to: {tempPath}");
            
            try
            {
                Debug.Log($"Creating frontal face CascadeClassifier from: {tempPath}");
                _cascade = new CascadeClassifier(tempPath);
                Debug.Log("Frontal CascadeClassifier object created");
                
                if (_cascade.Empty())
                {
                    Debug.LogError("ERROR: Frontal cascade classifier is empty!");
                    yield break;
                }
                Debug.Log("Frontal cascade classifier loaded successfully!");
            }
            catch (Exception ex)
            {
                Debug.LogError($"CRITICAL ERROR creating CascadeClassifier! {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.LogError($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                yield break;
            }
        }
        
        // Load profile face cascade if enabled
        if (DetectProfileFaces)
        {
            string profileCascadePath = Path.Combine(Application.streamingAssetsPath, "haarcascade_profileface.xml");
            
            using (UnityWebRequest www = UnityWebRequest.Get(profileCascadePath))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Profile cascade not found: {www.error}. Only frontal faces will be detected.");
                    _cascadeProfile = null;
                }
                else
                {
                    string tempProfilePath = Path.Combine(Application.persistentDataPath, "haarcascade_profileface.xml");
                    File.WriteAllBytes(tempProfilePath, www.downloadHandler.data);
                    Debug.Log($"Wrote profile cascade to: {tempProfilePath}");
                    
                    try
                    {
                        _cascadeProfile = new CascadeClassifier(tempProfilePath);
                        if (_cascadeProfile.Empty())
                        {
                            Debug.LogWarning("Profile cascade is empty. Only frontal faces will be detected.");
                            _cascadeProfile = null;
                        }
                        else
                        {
                            Debug.Log("✅ Profile cascade loaded! Can now detect side-view faces.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to load profile cascade: {ex.Message}. Only frontal faces will be detected.");
                        _cascadeProfile = null;
                    }
                }
            }
        }

        Debug.Log($"Creating Mats and display texture ({_webCamTexture.width}x{_webCamTexture.height})...");
        _grayMat = new Mat(_webCamTexture.height, _webCamTexture.width, MatType.CV_8UC1);
        _displayTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGBA32, false);
        
        if (DisplayImage != null)
        {
            DisplayImage.texture = _displayTexture;
            Debug.Log($"✅✅✅ Display texture assigned to RawImage - you'll see the camera feed with face boxes! ✅✅✅");
            Debug.Log($"📱 RawImage size: {DisplayImage.rectTransform.rect.width}x{DisplayImage.rectTransform.rect.height}");
            
            // Get Canvas info
            var canvas = DisplayImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Debug.Log($"🖼️ Canvas found! Name: '{canvas.gameObject.name}', Position: {canvas.transform.position}, Scale: {canvas.transform.localScale}");
                Debug.Log($"🎨 Canvas Render Mode: {canvas.renderMode} (0=ScreenSpaceOverlay, 1=ScreenSpaceCamera, 2=WorldSpace)");
                
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    Debug.LogWarning($"⚠️⚠️⚠️ Canvas is SCREEN SPACE OVERLAY - This will NOT be visible in VR mode! ⚠️⚠️⚠️");
                }
                else if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    Debug.Log($"✅✅✅ Canvas is WORLD SPACE - This should render in VR/AR! ✅✅✅");
                }
                else if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    Debug.Log($"✅✅✅ Canvas is SCREEN SPACE CAMERA - This should render in VR/AR! ✅✅✅");
                }
                
                // Check if FollowCamera is attached
                var followCam = canvas.GetComponent<FollowCamera>();
                if (followCam != null)
                {
                    if (followCam.enabled)
                    {
                        Debug.Log($"✅ FollowCamera script IS attached and ENABLED on Canvas!");
                    }
                    else
                    {
                        Debug.Log($"ℹ️ FollowCamera script is attached but DISABLED (OK for ScreenSpaceOverlay mode)");
                    }
                }
            }
            else
            {
                Debug.LogError($"❌ No Canvas parent found for RawImage!");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ WARNING: No RawImage found for display. Face detection is running but won't be visible.");
        }

        // Initialize 3D face box renderers
        Debug.Log($"Creating {MaxFaceBoxes} 3D face box renderers...");
        _faceBoxRenderers = new FaceBoxRenderer[MaxFaceBoxes];
        for (int i = 0; i < MaxFaceBoxes; i++)
        {
            GameObject boxObj = new GameObject($"FaceBox_{i}");
            boxObj.transform.SetParent(transform);
            _faceBoxRenderers[i] = boxObj.AddComponent<FaceBoxRenderer>();
            _faceBoxRenderers[i].Initialize(FaceBoxColor, FaceBoxLineWidth);
        }
        Debug.Log($"✅ Created {MaxFaceBoxes} 3D face box renderers!");
        
        // Hide the RawImage - we're using 3D borders only
        if (DisplayImage != null)
        {
            DisplayImage.enabled = false;
            Debug.Log("ℹ️ RawImage disabled - using 3D face borders only for clean AR experience");
        }

        _isInitialized = true;
        Debug.Log("=== INITIALIZATION COMPLETE! Face detection should now be running. ===");
    }

    private int _frameCount = 0;
    private int _totalFacesDetected = 0;
    private bool _savedDebugFrames = false;
    
    void Update()
    {
        if (!_isInitialized || _webCamTexture == null || !_webCamTexture.isPlaying)
        {
            // Log why we're not processing (only every 60 frames to avoid spam)
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"Not processing: _isInitialized={_isInitialized}, _webCamTexture={_webCamTexture != null}, isPlaying={_webCamTexture?.isPlaying}");
            }
            return;
        }

        if (!_webCamTexture.didUpdateThisFrame)
        {
            return;
        }

        _frameCount++;
        
        // Log that we're actually processing
        if (_frameCount == 1)
        {
            Debug.Log("🎬 FIRST FRAME PROCESSING STARTED!");
        }
        
        try
        {
            // PERFORMANCE OPTIMIZATION: Only run detection every N frames
            // But still update box positions smoothly every frame
            OpenCvSharp.Rect[] faces = _lastDetectedFaces;
            
            // Only run expensive detection every N frames
            if (_frameCount % DetectionFrameSkip == 0)
            {
                TextureToMat();
                
                // Debug logging for first few frames
                if (_frameCount <= 3)
                {
                    Debug.Log($"Frame {_frameCount}: GrayMat size {_grayMat.Width}x{_grayMat.Height}, channels={_grayMat.Channels()}");
                }
                
                // Save debug frames (first 3 frames only)
                if (!_savedDebugFrames && _frameCount <= 3)
                {
                    SaveDebugFrame(_frameCount);
                    if (_frameCount == 3)
                    {
                        _savedDebugFrames = true;
                        Debug.Log("✅ Debug frames saved! Use 'adb pull' to retrieve them from device.");
                    }
                }
                
                // Downsample for performance
                var smallMat = new Mat();
                Cv2.Resize(_grayMat, smallMat, new Size(), 1.0 / DownsampleFactor, 1.0 / DownsampleFactor, InterpolationFlags.Linear);
                
                // Apply histogram equalization to improve contrast - VERY important for face detection!
                Cv2.EqualizeHist(smallMat, smallMat);
                
                if (_frameCount <= 3)
                {
                    Debug.Log($"Frame {_frameCount}: SmallMat size {smallMat.Width}x{smallMat.Height} for detection (with histogram equalization)");
                }

                // BALANCED detection parameters - reliable detection with minimal false positives
                var frontalFaces = _cascade.DetectMultiScale(
                    image: smallMat,
                    scaleFactor: 1.1,       // Good balance between speed and accuracy
                    minNeighbors: 5,        // Moderate strictness - catches faces without too many false positives
                    flags: HaarDetectionTypes.ScaleImage,
                    minSize: new Size(30, 30),  // Smaller minimum to detect faces at various distances
                    maxSize: new Size(400, 400) // Allow larger faces
                );
                
                // Also detect profile faces if enabled
                if (DetectProfileFaces && _cascadeProfile != null)
                {
                    var profileFaces = _cascadeProfile.DetectMultiScale(
                        image: smallMat,
                        scaleFactor: 1.08,
                        minNeighbors: 5,        // Slightly less strict than frontal (profile detection is harder)
                        flags: HaarDetectionTypes.ScaleImage,
                        minSize: new Size(40, 40),
                        maxSize: new Size(300, 300)
                    );
                    
                    // Merge frontal and profile detections (remove duplicates)
                    faces = MergeFaceDetections(frontalFaces, profileFaces);
                    
                    if (_frameCount <= 10 && profileFaces.Length > 0)
                    {
                        Debug.Log($"🔄 Profile detection found {profileFaces.Length} additional faces, total after merge: {faces.Length}");
                    }
                }
                else
                {
                    faces = frontalFaces;
                }
                
                // Cache the detection for next frames
                _lastDetectedFaces = faces;
                
                // ===== FACE ID ASSIGNMENT & TRACKING SYSTEM =====
                // This tracks the same person across frames (foundation for face recognition)
                AssignFaceIDs(faces);

                // Log detection results more frequently at first
                if (_frameCount <= 10 || (_frameCount % 30 == 0))
                {
                    Debug.Log($"Frame {_frameCount}: Detected {faces.Length} faces (DETECTION RUN)");
                }

                if (faces.Length > 0)
                {
                    _totalFacesDetected += faces.Length;
                    if (_frameCount <= 10 || _frameCount % 30 == 0)
                    {
                        string faceIDsStr = ShowFaceIDs ? $" IDs: [{string.Join(", ", System.Array.ConvertAll(_faceIDs, x => x.ToString()))}]" : "";
                        Debug.Log($"🟢 FACE DETECTED! Frame {_frameCount}: {faces.Length} face(s){faceIDsStr} - Drawing 3D boxes now!");
                    }
                }
                
                smallMat.Dispose();
            }
            else if (UseMotionPrediction && _frameCount % DetectionFrameSkip != 0)
            {
                // ===== MOTION PREDICTION ON SKIPPED FRAMES =====
                // Apply velocity to smoothed positions for smoother tracking between detections
                for (int i = 0; i < _lastDetectedFaces.Length && i < MaxFaceBoxes; i++)
                {
                    if (_boxInitialized[i] && _boxVelocities[i].magnitude > 0.0001f)
                    {
                        _smoothedPositions[i] += _boxVelocities[i];
                        
                        // Log prediction for first few frames
                        if (_frameCount <= 15 && i == 0)
                        {
                            Debug.Log($"🎯 Frame {_frameCount}: Applying motion prediction to Face {i} (ID:{_faceIDs[i]}), velocity: {_boxVelocities[i]}");
                        }
                    }
                }
            }

            // 3D AR MODE: Position face boxes with PERSISTENCE (don't disappear immediately)
            Camera mainCam = Camera.main;
            if (mainCam != null && _faceBoxRenderers != null)
            {
                // First, update "frames since last seen" for all tracked faces
                for (int i = 0; i < MaxFaceBoxes; i++)
                {
                    if (_faceIDs[i] > 0)
                    {
                        _framesSinceLastSeen[i]++;
                    }
                }
                
                // Update boxes for currently detected faces - WITH LOCKING BEHAVIOR
                for (int i = 0; i < faces.Length && i < MaxFaceBoxes; i++)
                {
                    var face = faces[i];
                    
                    // Reset "last seen" for this tracked face
                    _framesSinceLastSeen[i] = 0;
                    _lastKnownFaceRects[i] = face;
                    
                    // Increment consecutive detection counter
                    _consecutiveDetections[i]++;
                    
                    // Only show box if face has been detected consistently
                    if (_consecutiveDetections[i] >= StableDetectionFrames)
                    {
                        _isConfirmedFace[i] = true;
                        
                        if (_consecutiveDetections[i] == StableDetectionFrames)
                        {
                            Debug.Log($"✅ CONFIRMED FACE ID:{_faceIDs[i]} after {StableDetectionFrames} consecutive frames");
                            
                            // FACE RECOGNITION: Identify who this person is
                            if (RecognitionManager != null && RecognitionManager.IsReady() && ShowRecognizedNames)
                            {
                                PerformRecognition(i, face);
                            }
                        }
                    }
                    // Re-run recognition periodically for confirmed faces (every 30 frames)
                    else if (_isConfirmedFace[i] && _frameCount % 30 == 0 && RecognitionManager != null && RecognitionManager.IsReady())
                    {
                        PerformRecognition(i, face);
                    }
                    
                    // Only render confirmed faces
                    if (!_isConfirmedFace[i])
                    {
                        continue;  // Skip unconfirmed faces
                    }
                    
                    // Scale back to original resolution
                    var scaledRect = new OpenCvSharp.Rect(
                        face.X * DownsampleFactor,
                        face.Y * DownsampleFactor,
                        face.Width * DownsampleFactor,
                        face.Height * DownsampleFactor
                    );
                    
                    // Convert 2D image coordinates to 3D world position
                    float normalizedX = (scaledRect.X + scaledRect.Width / 2f) / (float)_webCamTexture.width;
                    float normalizedY = 1f - ((scaledRect.Y + scaledRect.Height / 2f) / (float)_webCamTexture.height);
                    
                    Vector3 viewportPos = new Vector3(normalizedX, normalizedY, EstimatedFaceDepth);
                    Vector3 targetWorldPos = mainCam.ViewportToWorldPoint(viewportPos);
                    
                    // Calculate box size in world space
                    float baseWorldWidth = (scaledRect.Width / (float)_webCamTexture.width) * EstimatedFaceDepth * 0.6f;
                    float baseWorldHeight = (scaledRect.Height / (float)_webCamTexture.height) * EstimatedFaceDepth * 0.6f;
                    Vector2 targetSize = new Vector2(baseWorldWidth * BoxSizeMultiplier, baseWorldHeight * BoxSizeMultiplier);
                    
                    // LOCKING BEHAVIOR: Only update if movement is significant
                    Vector3 finalPos;
                    Vector2 finalSize;
                    
                    if (!_boxInitialized[i])
                    {
                        // First time showing this box - initialize
                        finalPos = targetWorldPos;
                        finalSize = targetSize;
                        _boxInitialized[i] = true;
                        Debug.Log($"🔒 LOCKED onto Face ID:{_faceIDs[i]} at position {finalPos}");
                    }
                    else
                    {
                        // Calculate movement distance in normalized coordinates
                        // Get previous normalized position from smoothed world position
                        Vector3 prevViewport = mainCam.WorldToViewportPoint(_smoothedPositions[i]);
                        
                        float dx = normalizedX - prevViewport.x;
                        float dy = normalizedY - prevViewport.y;
                        float movementDist = Mathf.Sqrt(dx * dx + dy * dy);
                        
                        // Only update if moved significantly (reduces jitter)
                        if (movementDist > MovementThreshold)
                        {
                            finalPos = targetWorldPos;
                            finalSize = targetSize;
                            
                            if (_frameCount % 30 == 0)
                            {
                                Debug.Log($"📍 Face ID:{_faceIDs[i]} moved {movementDist:F3} - updating position");
                            }
                        }
                        else
                        {
                            // Movement too small - KEEP CURRENT POSITION (LOCKED)
                            finalPos = _smoothedPositions[i];
                            finalSize = _smoothedSizes[i];
                        }
                    }
                    
                    _smoothedPositions[i] = finalPos;
                    _smoothedSizes[i] = finalSize;
                    
                    // Determine what to display on the box
                    string displayText = GetDisplayTextForFace(i);
                    
                    _faceBoxRenderers[i].UpdateBox(finalPos, finalSize, displayText);
                    
                    if (_frameCount <= 5 && i == 0)
                    {
                        Debug.Log($"📦 Box {i}: WorldPos={finalPos}, Size={finalSize.x:F3}x{finalSize.y:F3}m - {displayText}");
                    }
                }
                
                // PERSISTENCE: Keep showing boxes for faces that disappeared recently
                for (int i = 0; i < MaxFaceBoxes; i++)
                {
                    // Only persist CONFIRMED faces
                    if (_faceIDs[i] > 0 && _isConfirmedFace[i] && _framesSinceLastSeen[i] > 0 && _framesSinceLastSeen[i] <= FacePersistenceFrames)
                    {
                        // Face not detected this frame, but keep showing it (LOCKED in place)
                        string displayText = GetDisplayTextForFace(i);
                        _faceBoxRenderers[i].UpdateBox(_smoothedPositions[i], _smoothedSizes[i], displayText);
                        
                        if (_frameCount % 30 == 0)
                        {
                            Debug.Log($"🔄 Persisting {displayText} - not seen for {_framesSinceLastSeen[i]} frames (max: {FacePersistenceFrames})");
                        }
                    }
                    else if (_framesSinceLastSeen[i] > FacePersistenceFrames)
                    {
                        // Face has been gone too long - hide and reset ALL tracking data
                        _faceBoxRenderers[i].Hide();
                        _boxInitialized[i] = false;
                        _boxVelocities[i] = Vector3.zero;
                        _consecutiveDetections[i] = 0;
                        _isConfirmedFace[i] = false;
                        
                        if (_faceIDs[i] > 0)
                        {
                            Debug.Log($"❌ Face ID:{_faceIDs[i]} disappeared (not seen for {_framesSinceLastSeen[i]} frames)");
                            _faceIDs[i] = 0;
                        }
                    }
                }
            }

            // Still update the texture for debugging (but it's hidden)
            if (DisplayImage != null && DisplayImage.enabled)
            {
                MatToTexture();
            }
            
            // Log status every 5 seconds
            if (_frameCount % 150 == 0)
            {
                Debug.Log($"Status - Frame: {_frameCount}, Total faces found: {_totalFacesDetected}, Current faces: {faces.Length}, FPS boost: {DetectionFrameSkip}x");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ERROR in Update: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Merges face detections from multiple cascades, removing overlapping duplicates.
    /// </summary>
    private OpenCvSharp.Rect[] MergeFaceDetections(OpenCvSharp.Rect[] frontalFaces, OpenCvSharp.Rect[] profileFaces)
    {
        if (profileFaces.Length == 0) return frontalFaces;
        if (frontalFaces.Length == 0) return profileFaces;
        
        var merged = new System.Collections.Generic.List<OpenCvSharp.Rect>(frontalFaces);
        
        // Add profile faces that don't overlap with frontal faces
        foreach (var profileFace in profileFaces)
        {
            bool isOverlapping = false;
            
            foreach (var frontalFace in frontalFaces)
            {
                // Calculate overlap using Intersection over Union (IoU)
                var intersection = frontalFace & profileFace;  // Intersection
                if (intersection.Width > 0 && intersection.Height > 0)
                {
                    float intersectionArea = intersection.Width * intersection.Height;
                    float frontalArea = frontalFace.Width * frontalFace.Height;
                    float profileArea = profileFace.Width * profileFace.Height;
                    float unionArea = frontalArea + profileArea - intersectionArea;
                    float iou = intersectionArea / unionArea;
                    
                    // If IoU > 0.3, consider them the same face
                    if (iou > 0.3f)
                    {
                        isOverlapping = true;
                        break;
                    }
                }
            }
            
            if (!isOverlapping)
            {
                merged.Add(profileFace);
            }
        }
        
        return merged.ToArray();
    }
    
    /// <summary>
    /// Assigns persistent IDs to detected faces by matching them with previous frame.
    /// This is the foundation for face recognition - we track the same person across frames.
    /// REWRITTEN: Simplified logic to properly maintain face IDs and prevent "jumping".
    /// </summary>
    private void AssignFaceIDs(OpenCvSharp.Rect[] currentFaces)
    {
        if (currentFaces.Length == 0)
        {
            // No faces detected - increment "last seen" counters
            for (int i = 0; i < MaxFaceBoxes; i++)
            {
                if (_faceIDs[i] > 0)
                {
                    _framesSinceLastSeen[i]++;
                }
            }
            return;
        }
        
        // Track which current detections have been matched
        bool[] currentFaceMatched = new bool[currentFaces.Length];
        
        // Track which existing IDs have been reused this frame
        bool[] existingIDMatched = new bool[MaxFaceBoxes];
        
        // Temporary storage for new assignments
        int[] tempFaceIDs = new int[MaxFaceBoxes];
        OpenCvSharp.Rect[] tempFaceRects = new OpenCvSharp.Rect[MaxFaceBoxes];
        int[] tempFramesSinceLastSeen = new int[MaxFaceBoxes];
        
        // STEP 1: Try to match each current face with an existing tracked face
        for (int i = 0; i < currentFaces.Length && i < MaxFaceBoxes; i++)
        {
            var currentFace = currentFaces[i];
            
            // Scale current face back to original resolution for matching
            var scaledCurrent = new OpenCvSharp.Rect(
                currentFace.X * DownsampleFactor,
                currentFace.Y * DownsampleFactor,
                currentFace.Width * DownsampleFactor,
                currentFace.Height * DownsampleFactor
            );
            
            float currentCenterX = (scaledCurrent.X + scaledCurrent.Width / 2f) / (float)_webCamTexture.width;
            float currentCenterY = (scaledCurrent.Y + scaledCurrent.Height / 2f) / (float)_webCamTexture.height;
            
            int bestMatchIndex = -1;
            float bestMatchDistance = _faceMatchThreshold;
            
            // Search through existing tracked faces
            for (int j = 0; j < MaxFaceBoxes; j++)
            {
                // Skip if no ID assigned or already matched
                if (_faceIDs[j] == 0 || existingIDMatched[j])
                {
                    continue;
                }
                
                // Use last known rect for matching
                var prevFace = _lastKnownFaceRects[j];
                float prevCenterX = (prevFace.X + prevFace.Width / 2f) / (float)_webCamTexture.width;
                float prevCenterY = (prevFace.Y + prevFace.Height / 2f) / (float)_webCamTexture.height;
                
                // Calculate normalized distance
                float dx = currentCenterX - prevCenterX;
                float dy = currentCenterY - prevCenterY;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                
                if (distance < bestMatchDistance)
                {
                    bestMatchDistance = distance;
                    bestMatchIndex = j;
                }
            }
            
            // Assign ID
            if (bestMatchIndex >= 0)
            {
                // MATCHED - reuse existing ID
                tempFaceIDs[i] = _faceIDs[bestMatchIndex];
                tempFaceRects[i] = scaledCurrent;
                tempFramesSinceLastSeen[i] = 0;
                existingIDMatched[bestMatchIndex] = true;
                currentFaceMatched[i] = true;
                
                if (_frameCount <= 15)
                {
                    Debug.Log($"🔗 Matched Face {i} ← ID:{tempFaceIDs[i]} (dist: {bestMatchDistance:F3})");
                }
            }
            else
            {
                // NEW FACE - assign new ID
                tempFaceIDs[i] = _nextFaceID++;
                tempFaceRects[i] = scaledCurrent;
                tempFramesSinceLastSeen[i] = 0;
                currentFaceMatched[i] = true;
                
                Debug.Log($"✨ NEW FACE ID:{tempFaceIDs[i]} detected!");
            }
        }
        
        // STEP 2: Update global arrays
        for (int i = 0; i < MaxFaceBoxes; i++)
        {
            if (i < currentFaces.Length)
            {
                _faceIDs[i] = tempFaceIDs[i];
                _lastKnownFaceRects[i] = tempFaceRects[i];
                _framesSinceLastSeen[i] = tempFramesSinceLastSeen[i];
            }
            else if (!existingIDMatched[i] && _faceIDs[i] > 0)
            {
                // This existing face was NOT matched - increment counter
                _framesSinceLastSeen[i]++;
            }
        }
    }
    
    private void TextureToMat()
    {
        // Dispose the Mat from the previous frame to prevent a memory leak
        _rgbaMat?.Dispose();
        
        // Convert WebCamTexture to Texture2D-compatible format
        Texture2D tempTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGB24, false);
        tempTexture.SetPixels32(_webCamTexture.GetPixels32());
        tempTexture.Apply();
        
        _rgbaMat = TextureConverter.TextureToMat(tempTexture);
        Destroy(tempTexture);
        
        Cv2.CvtColor(_rgbaMat, _grayMat, ColorConversionCodes.BGR2GRAY);
    }

    private void MatToTexture()
    {
        TextureConverter.MatToTexture(_rgbaMat, _displayTexture);
    }
    
    private void SaveDebugFrame(int frameNum)
    {
        try
        {
            // Use app's external files directory - no extra permissions needed!
            // This is at: /storage/emulated/0/Android/data/com.DefaultCompany.MagicLeap_Recogneyes/files/
            string saveDir = Application.persistentDataPath;
            
            Debug.Log($"💾 Saving debug frame {frameNum} to: {saveDir}");
            
            // Save original color frame
            string colorPath = Path.Combine(saveDir, $"frame_{frameNum}_original.jpg");
            Cv2.ImWrite(colorPath, _rgbaMat);
            Debug.Log($"📸 Saved ORIGINAL (1280x720 color) → {colorPath}");
            
            // Save grayscale frame
            string grayPath = Path.Combine(saveDir, $"frame_{frameNum}_grayscale.jpg");
            Cv2.ImWrite(grayPath, _grayMat);
            Debug.Log($"📸 Saved GRAYSCALE (1280x720) → {grayPath}");
            
            // Save downsampled frame (what the detector actually analyzes)
            var smallMat = new Mat();
            Cv2.Resize(_grayMat, smallMat, new Size(), 1.0 / DownsampleFactor, 1.0 / DownsampleFactor, InterpolationFlags.Linear);
            Cv2.EqualizeHist(smallMat, smallMat);
            string smallPath = Path.Combine(saveDir, $"frame_{frameNum}_detection.jpg");
            Cv2.ImWrite(smallPath, smallMat);
            Debug.Log($"📸 Saved DETECTION ({smallMat.Width}x{smallMat.Height} with histogram eq) → {smallPath}");
            smallMat.Dispose();
            
            Debug.Log($"✅✅✅ Frame {frameNum} SAVED SUCCESSFULLY! ✅✅✅");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Failed to save debug frame: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Performs face recognition on a detected face region.
    /// Extracts the face from the grayscale image and asks the RecognitionManager to identify it.
    /// </summary>
    private void PerformRecognition(int faceIndex, OpenCvSharp.Rect faceRect)
    {
        try
        {
            // Scale face rect back to full resolution
            var scaledRect = new OpenCvSharp.Rect(
                faceRect.X * DownsampleFactor,
                faceRect.Y * DownsampleFactor,
                faceRect.Width * DownsampleFactor,
                faceRect.Height * DownsampleFactor
            );
            
            // Ensure rect is within image bounds
            scaledRect.X = Mathf.Max(0, scaledRect.X);
            scaledRect.Y = Mathf.Max(0, scaledRect.Y);
            scaledRect.Width = Mathf.Min(scaledRect.Width, _grayMat.Width - scaledRect.X);
            scaledRect.Height = Mathf.Min(scaledRect.Height, _grayMat.Height - scaledRect.Y);
            
            if (scaledRect.Width <= 0 || scaledRect.Height <= 0)
            {
                Debug.LogWarning($"Invalid face rect for recognition: {scaledRect}");
                return;
            }
            
            // Extract face region from grayscale image
            Mat faceROI = new Mat(_grayMat, scaledRect);
            
            // Recognize the face
            var (name, confidence) = RecognitionManager.RecognizeFace(faceROI);
            
            _recognizedNames[faceIndex] = name;
            _recognitionConfidence[faceIndex] = confidence;
            
            faceROI.Dispose();
            
            if (name != "Unknown")
            {
                Debug.Log($"👤 RECOGNIZED: {name} (confidence: {confidence:F1}, ID:{_faceIDs[faceIndex]})");
            }
            else
            {
                Debug.Log($"❓ Unknown person detected (confidence: {confidence:F1}, ID:{_faceIDs[faceIndex]})");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Recognition error for face {faceIndex}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gets the display text for a face box (either name or ID).
    /// </summary>
    private string GetDisplayTextForFace(int faceIndex)
    {
        if (ShowRecognizedNames && !string.IsNullOrEmpty(_recognizedNames[faceIndex]))
        {
            // Show recognized name with confidence if enabled
            if (RecognitionManager != null && RecognitionManager.ShowConfidenceScores)
            {
                // Only show confidence if it's a reasonable number (not Unknown's large distance)
                if (_recognitionConfidence[faceIndex] < 999.0)
                {
                    return $"{_recognizedNames[faceIndex]} ({_recognitionConfidence[faceIndex]:F0})";
                }
                else
                {
                    // Don't show massive distances for Unknown faces
                    return _recognizedNames[faceIndex];
                }
            }
            else
            {
                return _recognizedNames[faceIndex];
            }
        }
        else if (ShowFaceIDs)
        {
            return $"ID:{_faceIDs[faceIndex]}";
        }
        else
        {
            return "";  // No text
        }
    }
    
    void OnDestroy()
    {
        if (_webCamTexture != null)
        {
            _webCamTexture.Stop();
        }
        _rgbaMat?.Dispose();
        _grayMat?.Dispose();
        
        // Clean up face box renderers
        if (_faceBoxRenderers != null)
        {
            foreach (var renderer in _faceBoxRenderers)
            {
                if (renderer != null)
                {
                    Destroy(renderer.gameObject);
                }
            }
        }
    }
}

namespace OpenCvSharp.Unity
{
    public static class TextureConverter
    {
        public static Mat TextureToMat(Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] colors = texture.GetPixels32();
            Mat mat = new Mat(height, width, MatType.CV_8UC4);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = colors[(height - 1 - y) * width + x];
                    var vec = new Vec4b(color.b, color.g, color.r, color.a);
                    mat.Set(y, x, vec);
                }
            }
            return mat;
        }

        public static void MatToTexture(Mat mat, Texture2D texture)
        {
            int width = mat.Cols;
            int height = mat.Rows;
            Color32[] colors = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec4b color = mat.Get<Vec4b>(y, x);
                    colors[(height - 1 - y) * width + x] = new Color32(color.Item2, color.Item1, color.Item0, color.Item3);
                }
            }
            
            texture.SetPixels32(colors);
            texture.Apply();
        }
    }
}
