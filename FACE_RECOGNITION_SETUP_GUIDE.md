# Face Recognition Setup Guide

## ✅ What We Just Built

I've implemented a complete face recognition system for your Magic Leap 2 app! Here's what's now in place:

### **New Files Created:**
1. `FaceRecognitionManager.cs` - Handles training and recognition using OpenCV's LBPH algorithm
2. Updated `FaceDetector.cs` - Now integrates with recognition to show names instead of IDs
3. Updated `FaceBoxRenderer.cs` - Can display names, IDs, or confidence scores

---

## 📁 Setting Up Training Data

### **Folder Structure:**

You need to create training images in your Unity project:

```
Assets/
  StreamingAssets/
    Faces/
      manifest.txt          ← List of all people (one name per line)
      John/
        photo1.jpg
        photo2.jpg
        photo3.jpg
        ...
      Sarah/
        photo1.jpg
        photo2.jpg
        photo3.jpg
        ...
      Mike/
        photo1.jpg
        ...
```

### **Steps:**

1. **Create the Folders:**
   - In Unity, right-click in `Assets/StreamingAssets` → Create folder → `Faces`
   - Inside `Faces`, create one folder per person (use their name)

2. **Add Training Photos:**
   - Put 5-10 photos of each person in their folder
   - Name them: `photo1.jpg`, `photo2.jpg`, etc.
   - **Photo Tips:**
     - Different angles (frontal, slight left/right)
     - Different lighting conditions
     - Different expressions
     - Same quality/resolution as your Magic Leap camera (~1280x720 or similar)
     - Crop to just the face region if possible

3. **Create manifest.txt:**
   - In `Assets/StreamingAssets/Faces/`, create a text file called `manifest.txt`
   - Add one name per line (must match folder names):
     ```
     John
     Sarah
     Mike
     ```

---

## 🎮 Unity Inspector Setup

1. **Add FaceRecognitionManager to Scene:**
   - In your Unity scene, find the `Main Camera` or create a new empty GameObject called `RecognitionManager`
   - Add Component → `FaceRecognitionManager`
   
2. **Configure Settings (Inspector):**
   - ✅ **Enable Recognition:** Checked
   - **Confidence Threshold:** `70` (lower = stricter, higher = more lenient)
   - ✅ **Auto Train On Start:** Checked
   - ✅ **Show Confidence Scores:** Checked (to see how confident the match is)

3. **Link to FaceDetector:**
   - Select your `Main Camera` (or whatever has `FaceDetector.cs`)
   - Find the `FaceDetector` component in Inspector
   - Under "Face Recognition" section:
     - Drag the `RecognitionManager` GameObject into the **Recognition Manager** field
     - ✅ **Show Recognized Names:** Checked

---

## 🚀 How It Works

1. **On Startup:**
   - App looks for `StreamingAssets/Faces/manifest.txt`
   - Loads all training images for each person listed
   - Trains the LBPH recognizer (~5-30 seconds depending on # of images)
   - Saves the trained model to device storage as `face_recognition_model.yml`

2. **Next Time:**
   - App loads the saved `.yml` model instantly (no retraining needed!)
   - Only retrains if the model file doesn't exist

3. **During Face Detection:**
   - When a face is confirmed (after 5 consecutive frames), it runs recognition
   - Extracts the face region → compares to all trained faces
   - Shows the person's name if confidence is below threshold
   - Shows "Unknown" if no match found

---

## 📊 Expected Output (Logs)

```
=== FaceRecognitionManager Starting ===
Initializing Face Recognizer...
✅ LBPH Face Recognizer created
📚 No existing model found. Training from scratch...
📋 Found 3 people in manifest: John, Sarah, Mike
Loading training images for: John
  ✅ Loaded .../Faces/John/photo1.jpg
  ✅ Loaded .../Faces/John/photo2.jpg
✅ Loaded 5 images for John (Label: 0)
...
✅✅✅ TRAINING COMPLETE! Model can now recognize 3 people.
💾 Model saved to: /storage/.../face_recognition_model.yml

// During detection:
✅ CONFIRMED FACE ID:1 after 5 consecutive frames
👤 RECOGNIZED: John (confidence: 45.2, ID:1)
```

---

## 🎯 Testing & Tuning

### **Confidence Threshold:**
- **Lower (30-50):** Very strict - only matches if very confident (fewer false positives, more "Unknown")
- **Medium (50-70):** Balanced (recommended)
- **Higher (70-100):** Lenient - will match even if not very confident (more false positives)

### **If Recognition is Poor:**
1. **Add more training photos** (aim for 10+ per person)
2. **Vary conditions** (different lighting, angles, expressions)
3. **Lower confidence threshold** (make it stricter)
4. **Use better quality photos** (same lighting/resolution as ML2 camera)

### **If It Says "Unknown" Too Often:**
1. **Raise confidence threshold** (make it more lenient)
2. **Add more varied training photos**
3. **Check photo quality/lighting matches your environment**

---

## 🗄️ Database Integration (Future)

Right now, the system loads from local folders. To upgrade to database:

### **Option 1: Simple API (Recommended)**

1. Create an API endpoint:
   ```
   GET /api/faces/embeddings
   Returns: [
     { "id": 1, "name": "John", "embedding": [0.1, 0.2, ...] },
     { "id": 2, "name": "Sarah", "embedding": [0.3, 0.4, ...] }
   ]
   ```

2. Modify `FaceRecognitionManager.cs`:
   - Instead of `TrainFromFolders()`, call `LoadFromAPI()`
   - Download embeddings from server
   - Train the recognizer with downloaded data

3. **Benefits:**
   - Centralized person database
   - Add new people without app update
   - Multiple devices stay in sync

### **Option 2: Full Cloud Training**

1. Server trains the model, saves `.yml` file
2. App downloads `.yml` directly
3. Faster startup, no client-side training

### **Database Schema Example:**

```sql
CREATE TABLE people (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    embedding BYTEA,              -- Trained facial features (binary)
    photo_url VARCHAR(255),       -- Optional profile picture URL
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE training_photos (
    id SERIAL PRIMARY KEY,
    person_id INT REFERENCES people(id),
    photo_url VARCHAR(255),       -- S3/Cloud storage URL
    uploaded_at TIMESTAMP DEFAULT NOW()
);
```

---

## 🔧 Troubleshooting

### **"No manifest.txt found"**
- Create `Assets/StreamingAssets/Faces/manifest.txt`
- Make sure it's a plain text file (not .txt.txt)

### **"No images found for [Person]"**
- Check folder name matches `manifest.txt` exactly (case-sensitive)
- Photos must be named `photo1.jpg`, `photo2.jpg`, etc.
- Supported formats: `.jpg`, `.jpeg`, `.png`

### **"Only 1 person found. Need at least 2"**
- LBPH works best with multiple people to compare against
- You can still use it with 1 person, but add an "Unknown" person folder with random faces

### **Recognition is slow**
- Reduce number of training photos (5-7 per person is usually enough)
- Model loads instantly after first training
- Recognition runs once per face at confirmation, then every 30 frames

---

## ✨ Next Steps

1. Add training photos and test!
2. Tune the confidence threshold
3. (Later) Set up database integration
4. (Later) Upgrade to deep learning for better accuracy

**You're all set!** Let me know how the recognition works once you test it! 🎉

