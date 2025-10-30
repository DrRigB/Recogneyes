# Recogneyes Asset Downloader (PowerShell Version)
# This script downloads and extracts the required face recognition model.

$ErrorActionPreference = "Stop" # Exit script on any error

# --- Configuration ---
$ModelUrl = "https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip"
$ZipFileName = "buffalo_l.zip"
$TempExtractFolder = "temp_buffalo_l"
$TargetModelName = "w600k_r50.onnx"
$FinalModelPath = "Assets/StreamingAssets/arcface.onnx"
$StreamingAssetsFolder = "Assets/StreamingAssets"

# --- Script Start ---
Write-Host "============================================="
Write-Host "Recogneyes Asset Downloader"
Write-Host "This will download and set up the ArcFace model."
Write-Host "============================================="
Write-Host ""

# 1. Create StreamingAssets directory if it doesn't exist
if (-not (Test-Path -Path $StreamingAssetsFolder)) {
    Write-Host "[INFO] Creating directory: $StreamingAssetsFolder"
    New-Item -ItemType Directory -Force -Path $StreamingAssetsFolder
}

# 2. Download the model zip file
Write-Host "[1/4] Downloading model archive from $ModelUrl..."
try {
    Invoke-WebRequest -Uri $ModelUrl -OutFile $ZipFileName
    Write-Host "[SUCCESS] Downloaded $ZipFileName"
} catch {
    Write-Host "[ERROR] Failed to download model. Please check your internet connection."
    exit 1
}
Write-Host ""

# 3. Extract the zip file
Write-Host "[2/4] Extracting archive..."
try {
    Expand-Archive -Path $ZipFileName -DestinationPath $TempExtractFolder -Force
    Write-Host "[SUCCESS] Extracted files to $TempExtractFolder"
} catch {
    Write-Host "[ERROR] Failed to extract archive. Make sure you have permissions to write in this folder."
    Remove-Item -Path $ZipFileName -Force -ErrorAction SilentlyContinue
    exit 1
}
Write-Host ""

# 4. Find and move the correct ONNX model
Write-Host "[3/4] Locating and setting up the model file..."
$SourceModelPath = Join-Path -Path $TempExtractFolder -ChildPath $TargetModelName
if (Test-Path -Path $SourceModelPath) {
    try {
        Move-Item -Path $SourceModelPath -Destination $FinalModelPath -Force
        Write-Host "[SUCCESS] Model file configured at $FinalModelPath"
    } catch {
        Write-Host "[ERROR] Failed to move the model file. Please check file permissions."
        Remove-Item -Path $ZipFileName -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $TempExtractFolder -Recurse -Force -ErrorAction SilentlyContinue
        exit 1
    }
} else {
    Write-Host "[ERROR] Could not find '$TargetModelName' in the extracted folder."
    Write-Host "The model provider may have updated the archive. Please check the contents of '$TempExtractFolder'."
    Remove-Item -Path $ZipFileName -Force -ErrorAction SilentlyContinue
    exit 1 # Do not remove the temp folder so the user can inspect it
}
Write-Host ""

# 5. Clean up temporary files
Write-Host "[4/4] Cleaning up temporary files..."
try {
    Remove-Item -Path $ZipFileName -Force
    Remove-Item -Path $TempExtractFolder -Recurse -Force
    Write-Host "[SUCCESS] Cleanup complete."
} catch {
    Write-Host "[WARN] Could not automatically clean up all temporary files. You may manually delete '$ZipFileName' and the '$TempExtractFolder' folder."
}
Write-Host ""

Write-Host "============================================="
Write-Host "✅ All assets set up successfully!"
Write-Host "You can now proceed to the next setup step."
Write-Host "============================================="
