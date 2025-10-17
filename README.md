# Recogneyes - Magic Leap 2 Face Recognition

A Unity-based face recognition system for Magic Leap 2, designed for nursing home applications.

## 🛠️ Setup Instructions

### 1. Download Unity

**Required Unity Version: 2022.3.21f1 LTS**

1. Go to [unity.com](https://unity.com)
2. Download **Unity Hub**
3. Open Unity Hub
4. Go to "Installs" tab
5. Click "Install Editor"
6. Select **"2022.3.21f1 LTS"**
7. **IMPORTANT**: Include "Android Build Support" module
8. Click "Install"

### 2. Clone This Repository

```bash
git clone https://github.com/DrRigB/Recogneyes.git
cd Recogneyes
```

### 3. Open in Unity

1. Open Unity Hub
2. Click "Open" → "Add project from disk"
3. Select the `Recogneyes` folder
4. Unity will automatically import everything

### 4. Build for Magic Leap 2

1. **Connect Magic Leap 2** via USB-C
2. **File** → **Build Settings**
3. Select **"Android"** platform
4. Click **"Build and Run"**
5. Select save location
6. Unity will build and install automatically

## 📁 Project Structure

```
Assets/
├── Scripts/
│   └── FaceRecognitionManager.cs    # Main recognition logic
├── StreamingAssets/
│   └── Faces/
│       ├── manifest.txt             # List of all people
│       ├── [PersonName]/
│       │   ├── image_list.txt       # Auto-generated
│       │   └── [photos...]
│       └── Unknown/                  # Unknown face examples
└── OpenCvSharp/                     # Custom OpenCV build
```

## 🎮 Adding New Faces

1. **Add photos** to `Assets/StreamingAssets/Faces/[PersonName]/`
2. **Update manifest** in `Assets/StreamingAssets/Faces/manifest.txt`
3. **Build and test** on Magic Leap 2

## 🔧 Key Features

- **Face Recognition**: LBPH algorithm for reliable recognition
- **Anonymous Training**: Train on celebrities but show as "Unknown"
- **Smart Preprocessing**: Handles varying photo qualities
- **Auto-Manifest**: Automatically generates training data files
- **Magic Leap 2 Optimized**: Custom OpenCV build included

## 🐛 Troubleshooting

**"OpenCV not found" errors**
- Use the included custom OpenCV build (don't replace it)

**Build fails on Magic Leap 2**
- Ensure Android Build Support is installed
- Check Magic Leap SDK is configured
- Verify device is in Developer Mode

**Face not recognized**
- Try adjusting `MaxDistanceThreshold` in Inspector
- Enable `ForceRetrainOnStart` to retrain
- Add more training photos (5-10 per person)

---

**Note**: This system is designed for nursing home applications where families provide photos to help residents recognize loved ones.