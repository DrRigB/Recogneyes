# Recogneyes - Magic Leap 2 Face Recognition

This is a complete Unity project for running real-time face recognition on the Magic Leap 2. It uses a Python server to handle the heavy processing of face recognition, while the Unity application captures the video feed, detects faces, and displays the results.

## Final Submission Setup

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

This script will:
1.  Download the `buffalo_l.zip` archive containing the model.
2.  Extract the archive.
3.  Find the correct `w600k_r50.onnx` file.
4.  Move it to `Assets/StreamingAssets/` and rename it to `arcface.onnx`.
5.  Clean up all temporary files.

This automated process ensures the exact correct model is placed where the server expects to find it.

### Step 3: Generate Face Embeddings

Before the server can recognize faces, you must generate the "embeddings" file. This is a one-time process that scans your training photos and creates a database of known faces.

1.  **Ensure your training photos are organized** in `Assets/StreamingAssets/Faces/`, with a separate folder for each person.
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

This script does two things:
*   Sets up ADB port forwarding so the Magic Leap device can talk to your PC.
*   Starts the Python Flask server.

You should see the server running and confirming that it has loaded the model and the embeddings. Leave this terminal window open.

### Step 5: Build and Run in Unity

Now you are ready to build the Unity application and deploy it to your device.

1.  **Open Unity Hub** and add the cloned `Recogneyes` project folder.
2.  **Open the project** in Unity (version 2022.3.21f1 or later is recommended).
3.  Go to **File > Build Settings**.
4.  Ensure the platform is set to **Android**.
5.  Connect your Magic Leap 2 device via USB.
6.  Click **Build and Run**.

Once the application launches on your headset, it will automatically connect to the running server, and you should see face recognition results appear as you look at people.