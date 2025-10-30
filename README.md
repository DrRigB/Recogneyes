# Recogneyes - Magic Leap 2 Face Recognition

This is a complete Unity project for running real-time face recognition on the Magic Leap 2. It uses a Python server to handle heavy processing, while the Unity application captures the video feed, detects faces, and displays the results.

## Prerequisites

Before you begin, ensure you have the following installed:

1.  **Unity Hub**
2.  **Unity Editor 2022.3.21f1 LTS** (or later)
    *   When installing, you **must** include the **"Android Build Support"** module.
3.  **Python 3.8** or newer.
4.  **Git** command-line tools.

---

## Setup Instructions

This guide provides the full "plug and play" instructions to get the project running from a fresh clone.

### Step 1: Clone the Repository

First, clone this repository to your local machine using Git.

```bash
git clone https://github.com/DrRigB/Recogneyes.git
cd Recogneyes
```

### Step 2: Download and Set Up the Recognition Model

The project requires a specific, large model file that is not stored in the Git repository. A PowerShell script is included to download and configure it automatically.

1.  **Open PowerShell** in the project's root directory.
2.  **Run the download script:**

    ```powershell
    .\download_assets.ps1
    ```

This script will download, extract, and place the correct `arcface.onnx` model into the `Assets/StreamingAssets/` folder.

### Step 3: Generate Face Embeddings

Before the server can recognize faces, you must generate the "embeddings" file. This is a one-time process that scans your training photos and creates a database of known faces.

1.  **Add Training Photos:** Place your training images in `Assets/StreamingAssets/Faces/`, with a separate subfolder for each person (e.g., `Assets/StreamingAssets/Faces/JohnDoe/`).
2.  **Run the generator script** from your PowerShell terminal:

    ```powershell
    python generate_embeddings.py
    ```

This will create the `face_embeddings.json` file. You only need to re-run this script if you add, remove, or change the training photos.

### Step 4: Run the Recognition Server

The server handles the actual face recognition. It must be running in the background for the Magic Leap app to work.

1.  **Keep your PowerShell terminal open.**
2.  **Run the server start-up script:**

    ```powershell
    .\start_server_with_adb.bat
    ```

This script sets up port forwarding from the device and starts the Python server. Leave this terminal window open.

### Step 5: Build and Run in Unity

Now you are ready to build the Unity application.

1.  **Open Unity Hub** and add the cloned `Recogneyes` project folder.
2.  **Open the project** in Unity.
3.  Open the main scene located at **`Assets/Scenes/Face_Detection_OpenCV.unity`**.
4.  Go to **File > Build Settings**.
5.  Ensure the platform is set to **Android**.
6.  Connect your Magic Leap 2 device via USB.
7.  Click **Build and Run**.

Once the application launches on your headset, it will connect to the server, and you should see recognition results appear as you look at people.

---

## Unity Project Settings & Configuration

The project is pre-configured, but if you need to troubleshoot, here are the key settings:

*   **`FaceDetector` GameObject**:
    *   This object in the scene hierarchy contains the `FaceDetector.cs` script.
    *   The `Recognition Manager` field on this script should be linked to the `FaceRecognitionManager` GameObject.
*   **`FaceRecognitionManager` GameObject**:
    *   Contains the `FaceRecognitionManager.cs` script.
    *   **Use Server Recognition** should be checked.
    *   **Primary Server URL** is set to `http://localhost:5000/recognize`.
*   **Player Settings** (`Project Settings > Player`):
    *   **Minimum API Level** is set to `Android 10.0 (API Level 29)`.
    *   **Scripting Backend** is set to `IL2CPP`.
    *   **Target Architectures** includes `ARM64`.
*   **XR Plug-in Management**:
    *   **Magic Leap XR Provider** is enabled in the Android tab.