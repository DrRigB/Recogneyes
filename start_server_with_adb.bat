@echo off
echo ==========================================
echo Starting Face Recognition Server + ADB
echo ==========================================
echo.

REM Step 1: Set up ADB forwarding
echo [1/2] Setting up ADB port forwarding...
"C:\Users\Seniors\MagicLeap\MLHub\plugins\com.magicleap.adb.win32.x86_64_1.0.41.28_0_2_202304071616\adb\adb.exe" reverse tcp:5000 tcp:5000

if %ERRORLEVEL% EQU 0 (
    echo ADB forwarding configured
) else (
    echo WARNING: ADB forwarding failed - make sure Magic Leap is connected via USB
)
echo.

REM Step 2: Start the server
echo [2/2] Starting Flask server...
echo Press CTRL+C to stop the server
echo.

python server_face_recognition.py

