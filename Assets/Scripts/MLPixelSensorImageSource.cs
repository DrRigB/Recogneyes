// Copyright (c) 2025
// Minimal ImageSource that feeds MediaPipe from Magic Leap 2 Pixel Sensor via OpenXR

using System;
using System.Collections;
using System.Collections.Generic;
using MagicLeap.OpenXR.Features.PixelSensors;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.OpenXR;

// A simplified struct to hold resolution data, replacing the MediaPipe one.
[System.Serializable]
public struct SimpleResolution
{
    public int width;
    public int height;
    public uint frameRate;

    public SimpleResolution(int width, int height, uint frameRate)
    {
        this.width = width;
        this.height = height;
        this.frameRate = frameRate;
    }
}

// Attach this to a GameObject to get a live feed from the Magic Leap 2's camera.
public class MLPixelSensorImageSource : MonoBehaviour
{
    [Header("Sensor Selection")]
    [Tooltip("OpenXR XR path of the sensor. Typical world-facing color path is /pixelsensor/world/color")]
    [SerializeField] private string _sensorPath = "/pixelsensor/world/color";

    [Tooltip("Stream index to pull (usually 0 for color)")]
    [SerializeField] private uint _streamIndex = 0;

    [Header("Requested Configuration")]
    [Tooltip("The desired resolution and frame rate for the camera feed.")]
    [SerializeField] private SimpleResolution _requestedResolution = new SimpleResolution(1280, 720, 30);
    [SerializeField] private PixelSensorFrameFormat _frameFormat = PixelSensorFrameFormat.Rgba8888;
    
    // Public texture that other scripts can access.
    public Texture2D Texture;
    
    public bool IsPlaying { get; private set; }

    private MagicLeapPixelSensorFeature _feature;
    private PixelSensorId _sensorId;
    private bool _prepared;
    private readonly Dictionary<uint, PixelSensorMetaDataType[]> _metadata = new();

    void Start()
    {
        StartCoroutine(RunCamera());
    }

    void OnDestroy()
    {
        StopCamera();
    }
    
    private IEnumerator RunCamera()
    {
        // Resolve feature
        _feature = OpenXRSettings.Instance.GetFeature<MagicLeapPixelSensorFeature>();
        if (_feature == null)
        {
            throw new InvalidOperationException("MagicLeapPixelSensorFeature not enabled. Enable OpenXR → Magic Leap 2 Pixel Sensor.");
        }

        // Resolve sensor id
        if (!_feature.GetPixelSensorFromXrPath(_sensorPath, out _sensorId))
        {
            // Fallback: first supported sensor containing "world" if exact path not found
            var sensors = _feature.GetSupportedSensors();
            foreach (var s in sensors)
            {
                if (s.XrPathString.Contains("/pixelsensor/world/"))
                {
                    _sensorId = s;
                    break;
                }
            }
            if (_sensorId.Equals(default(PixelSensorId)))
            {
                throw new InvalidOperationException("No suitable Pixel Sensor found. Check Pixel Sensor feature and permissions.");
            }
        }

        // Create
        if (!_feature.CreatePixelSensor(_sensorId))
        {
            throw new InvalidOperationException("Failed to create Pixel Sensor");
        }

        // Configure minimal required caps: Resolution, Format, UpdateRate
        _feature.ClearAllAppliedConfigs(_sensorId);
        _feature.ApplySensorConfig(_sensorId, PixelSensorCapabilityType.Resolution, new Vector2Int(_requestedResolution.width, _requestedResolution.height), _streamIndex);
        _feature.ApplySensorConfig(_sensorId, PixelSensorCapabilityType.Format, (uint)_frameFormat, _streamIndex);
        _feature.ApplySensorConfig(_sensorId, PixelSensorCapabilityType.UpdateRate, _requestedResolution.frameRate, _streamIndex);

        // Configure and start
        var configure = _feature.ConfigureSensorWithDefaultCapabilities(_sensorId, _streamIndex);
        yield return configure;
        if (!configure.DidOperationSucceed)
        {
            throw new InvalidOperationException("Pixel Sensor configure failed");
        }

        var start = _feature.StartSensor(_sensorId, new[] { _streamIndex }, _metadata);
        yield return start;
        if (!start.DidOperationSucceed)
        {
            throw new InvalidOperationException("Pixel Sensor start failed");
        }

        // Allocate Texture2D matching resolution/format
        Texture = new Texture2D(_requestedResolution.width, _requestedResolution.height, TextureFormat.RGBA32, false);

        _prepared = true;
        IsPlaying = true;
    }

    private void StopCamera()
    {
        IsPlaying = false;
        if (_feature != null && !_sensorId.Equals(default(PixelSensorId)))
        {
            _feature.StopSensor(_sensorId, new[] { _streamIndex });
            _feature.DestroyPixelSensor(_sensorId);
        }
        _prepared = false;
    }

    void Update()
    {
        if (!IsPlaying || !_prepared)
        {
            return;
        }

        // Fetch one frame; convert to RGBA if needed
        if (_feature.GetSensorData(_sensorId, _streamIndex, out var frame, out _, Allocator.Temp, 5, shouldFlipTexture: true))
        {
            try
            {
                switch (frame.FrameType)
                {
                    case PixelSensorFrameType.Rgba8888:
                    {
                        var plane = frame.Planes[0];
                        if (Texture.width != (int)plane.Width || Texture.height != (int)plane.Height)
                        {
                            Texture.Reinitialize((int)plane.Width, (int)plane.Height, TextureFormat.RGBA32, false);
                        }
                        Texture.LoadRawTextureData(plane.ByteData);
                        Texture.Apply(false);
                        break;
                    }
                    case PixelSensorFrameType.Yuv420888:
                    {
                        // Simple and slow YUV420 to RGBA conversion on CPU (sufficient for prototype)
                        ConvertYuv420ToRgba(frame, ref Texture);
                        break;
                    }
                    case PixelSensorFrameType.Jpeg:
                    {
                        var plane = frame.Planes[0];
                        var bytes = plane.ByteData.ToArray();
                        Texture.LoadImage(bytes, false);
                        break;
                    }
                    default:
                        break;
                }
            }
            finally
            {
                // frame.Planes[*].ByteData allocated with Allocator.Temp by SDK; no explicit dispose required here
            }
        }
    }

    private static void ConvertYuv420ToRgba(PixelSensorFrame frame, ref Texture2D texture)
    {
      // Expect planes: Y, U, V
      if (frame.Planes.Length < 3)
      {
        return;
      }

      var y = frame.Planes[0];
      var u = frame.Planes[1];
      var v = frame.Planes[2];

      var width = (int)y.Width;
      var height = (int)y.Height;

      if (texture.width != width || texture.height != height)
      {
        texture.Reinitialize(width, height, TextureFormat.RGBA32, false);
      }

      var rgba = new byte[width * height * 4];
      int idx = 0;
      for (int j = 0; j < height; j++)
      {
        for (int i = 0; i < width; i++)
        {
          int yIndex = (int)(j * y.Stride + i);
          int uvIndex = (int)((j / 2) * u.Stride + (i / 2) * u.PixelStride);
          float Y = y.ByteData[yIndex];
          float U = u.ByteData[uvIndex] - 128f;
          float V = v.ByteData[uvIndex] - 128f;

          float R = Y + 1.402f * V;
          float G = Y - 0.344136f * U - 0.714136f * V;
          float B = Y + 1.772f * U;

          rgba[idx++] = (byte)Mathf.Clamp(R, 0, 255);
          rgba[idx++] = (byte)Mathf.Clamp(G, 0, 255);
          rgba[idx++] = (byte)Mathf.Clamp(B, 0, 255);
          rgba[idx++] = 255;
        }
      }
      texture.LoadRawTextureData(rgba);
      texture.Apply(false);
    }
}
