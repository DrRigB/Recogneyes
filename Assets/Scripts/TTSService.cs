using UnityEngine;

/// <summary>
/// Simple cross-platform TTS wrapper. On Android, uses Unity's AndroidJava to call platform TTS.
/// In Editor/other platforms, falls back to Debug.Log.
/// </summary>
public class TTSService : MonoBehaviour
{
	[Header("Behavior")]
	[Tooltip("If true, will log speech text in Editor when platform TTS is unavailable.")]
	public bool LogInEditor = true;

	#if UNITY_ANDROID && !UNITY_EDITOR
	AndroidJavaObject _tts; // android.speech.tts.TextToSpeech instance
	bool _ttsReady;
	class OnInitListener : AndroidJavaProxy
	{
		private readonly System.Action<int> _onInit;
		public OnInitListener(System.Action<int> onInit) : base("android.speech.tts.TextToSpeech$OnInitListener") { _onInit = onInit; }
		public void onInit(int status) { _onInit?.Invoke(status); }
	}
	#endif

	void Awake()
	{
		InitializeIfNeeded();
	}

	void InitializeIfNeeded()
	{
#if UNITY_ANDROID && !UNITY_EDITOR
		if (_tts != null || _ttsReady) return;
		try
		{
			using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
			{
				AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				var listener = new OnInitListener((status) =>
				{
					_ttsReady = (status == 0); // TextToSpeech.SUCCESS == 0
					if (_ttsReady)
					{
						try
						{
							// Optionally set language to device default
							var locale = new AndroidJavaObject("java.util.Locale");
							_tts.Call<int>("setLanguage", locale);
						}
						catch {}
					}
				});
				_tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, listener);
			}
		}
		catch (System.SystemException ex)
		{
			Debug.LogWarning($"TTS init failed, will fallback to logs: {ex.Message}");
			_tts = null; _ttsReady = false;
		}
#endif
	}

	public void Speak(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;

#if UNITY_ANDROID && !UNITY_EDITOR
		InitializeIfNeeded();
		if (_tts != null && _ttsReady)
		{
			try
			{
				// Use modern signature: speak(String text, int queueMode, Bundle params, String utteranceId)
				int QUEUE_FLUSH = 0;
				AndroidJavaObject bundle = new AndroidJavaObject("android.os.Bundle");
				_tts.Call<int>("speak", text, QUEUE_FLUSH, bundle, System.Guid.NewGuid().ToString());
			}
			catch (System.SystemException ex)
			{
				Debug.LogWarning($"TTS speak failed: {ex.Message}");
			}
			return;
		}
#endif

		if (LogInEditor)
		{
			Debug.Log($"[TTS] {text}");
		}
	}
}


