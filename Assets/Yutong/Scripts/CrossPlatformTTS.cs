using UnityEngine;
using System;
using System.IO;
using System.Collections;
using UDebug = UnityEngine.Debug;
using Proc = System.Diagnostics.Process;
using PSI = System.Diagnostics.ProcessStartInfo;
using System.Collections.Generic;


//[RequireComponent(typeof(AudioSource))]
//public class CrossPlatformTTS : MonoBehaviour
//{
//    [Header("Voice")]
//    [Tooltip("eSpeak NG voice code (e.g., en-us, en-us+f3, en-gb+x-rp, zh, zh+yue¡­)")]
//    public string voice = "en-us";

//    [Header("Prosody")]
//    [Range(80, 450)] public int wpm = 170;
//    [Range(0, 99)] public int pitch = 50;
//    [Tooltip("Optional extra args passed to espeak-ng (e.g. \"-a 160 -g 6\")")]
//    public string extraArgs = "";

//    public bool IsSpeaking { get; private set; }

//    AudioSource _source;

//    void Awake()
//    {
//        _source = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
//        _source.playOnAwake = false;
//    }

//    /// <summary>Speak text, invoking onComplete when audio finishes (or immediately if text invalid).</summary>
//    public void Speak(string text, Action onComplete = null)
//    {
//        if (string.IsNullOrWhiteSpace(text)) { onComplete?.Invoke(); return; }
//        StartCoroutine(SpeakCo(text, onComplete));
//    }

//    IEnumerator SpeakCo(string text, Action done)
//    {
//        string exe = ResolveEspeakPath();
//        if (exe == null)
//        {
//            UDebug.LogError("CrossPlatformTTS: eSpeak NG not found for this platform/path.");
//            done?.Invoke();
//            yield break;
//        }

//        string wavPath = Path.Combine(Application.temporaryCachePath, "tts_espeak_" + Guid.NewGuid().ToString("N") + ".wav");
//        string args = $"-w \"{wavPath}\" -v {voice} -s {wpm} -p {pitch} --stdin";
//        if (!string.IsNullOrWhiteSpace(extraArgs)) args = extraArgs + " " + args;

//        var psi = new PSI
//        {
//            FileName = exe,
//            Arguments = args,
//            UseShellExecute = false,
//            RedirectStandardInput = true,
//            RedirectStandardError = true,
//            CreateNoWindow = true,
//            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
//        };

//        using (var p = Proc.Start(psi))
//        {
//            p.StandardInput.Write(text);
//            p.StandardInput.Close();

//            // Uncomment for CLI diagnostics:
//            // string err = p.StandardError.ReadToEnd();
//            // if (!string.IsNullOrEmpty(err)) UDebug.LogWarning(err);

//            p.WaitForExit();
//        }

//        if (!File.Exists(wavPath))
//        {
//            UDebug.LogError("CrossPlatformTTS: eSpeak NG did not produce a WAV file.");
//            done?.Invoke();
//            yield break;
//        }

//        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + wavPath, AudioType.WAV))
//        {
//            yield return req.SendWebRequest();
//            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
//            {
//                UDebug.LogError("CrossPlatformTTS: Failed to load WAV: " + req.error);
//                done?.Invoke();
//                yield break;
//            }

//            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
//            _source.clip = clip;

//            IsSpeaking = true;
//            _source.Play();

//            // wait for clip to finish
//            yield return new WaitWhile(() => _source.isPlaying);
//            IsSpeaking = false;
//        }

//        try { File.Delete(wavPath); } catch { /* ignore */ }
//        done?.Invoke();
//    }

//    string ResolveEspeakPath()
//    {
//#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
//        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/win/espeak-ng.exe");
//        return File.Exists(exe) ? exe : null;
//#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
//        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/mac/espeak-ng");
//        if (File.Exists(exe)) return exe;
//        if (File.Exists("/opt/homebrew/bin/espeak-ng")) return "/opt/homebrew/bin/espeak-ng";
//        if (File.Exists("/usr/local/bin/espeak-ng"))   return "/usr/local/bin/espeak-ng";
//        return null;
//#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
//        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/linux/espeak-ng");
//        if (File.Exists(exe)) return exe;
//        if (File.Exists("/usr/bin/espeak-ng"))        return "/usr/bin/espeak-ng";
//        if (File.Exists("/usr/local/bin/espeak-ng"))  return "/usr/local/bin/espeak-ng";
//        return null;
//#else
//        return null;
//#endif
//    }
//}


[RequireComponent(typeof(AudioSource))]
public class CrossPlatformTTS : MonoBehaviour
{
    [Header("Piper Voice")]
    [Tooltip("File name of the voice model (e.g. en-us-amy.onnx) located under StreamingAssets/tts/piper/<platform>/voices")]
    public string modelFileName = "en-us.onnx";

    [Header("Prosody")]
    [Tooltip("Duration multiplier (<1 faster, >1 slower).")]
    [Range(0.6f, 1.6f)] public float lengthScale = 1.0f;
    [Tooltip("Controls randomness/timbre (lower is cleaner, higher is breathier).")]
    [Range(0.0f, 1.5f)] public float noiseScale = 0.33f;
    [Tooltip("Optional extra CLI args passed to Piper.")]
    public string extraArgs = "";

    public bool IsSpeaking { get; private set; }

    AudioSource _src;

    static readonly Dictionary<string, AudioClip> _cache = new();
    string Key(string text) => $"{modelFileName}|{lengthScale}|{noiseScale}|{text}";

    void Awake()
    {
        _src = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
    }

    public void Speak(string text, Action onComplete = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { onComplete?.Invoke(); return; }

        if (_cache.TryGetValue(Key(text), out var cached))
        {
            StartCoroutine(PlayCached(cached, onComplete));
            return;
        }
        StartCoroutine(SpeakCo(text, onComplete));
    }

    IEnumerator PlayCached(AudioClip clip, Action done)
    {
        IsSpeaking = true;
        _src.clip = clip;
        _src.Play();
        yield return new WaitWhile(() => _src.isPlaying);
        IsSpeaking = false;
        done?.Invoke();
    }




    IEnumerator SpeakCo(string text, Action done)
    {
        string exe = ResolvePiperExe();
        if (exe == null) { UDebug.LogError("Piper executable not found."); done?.Invoke(); yield break; }

        string model = ResolveModelPath();
        if (model == null) { UDebug.LogError("Piper model not found (check modelFileName)."); done?.Invoke(); yield break; }

        string wavPath = Path.Combine(Application.temporaryCachePath, "tts_piper_" + Guid.NewGuid().ToString("N") + ".wav");

        // Build Piper CLI args
        // --model <onnx>  --output_file <wav>  --length_scale <f>  --noise_scale <f>
        string args = $"--model \"{model}\" --output_file \"{wavPath}\" --length_scale {lengthScale:0.###} --noise_scale {noiseScale:0.###}";
        if (!string.IsNullOrWhiteSpace(extraArgs)) args = extraArgs + " " + args;

        var psi = new PSI
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,   // send text via stdin
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using (var p = Proc.Start(psi))
        {
            p.StandardInput.Write(text);
            p.StandardInput.Close();

            // For debugging, uncomment:
            // string err = p.StandardError.ReadToEnd();
            // if (!string.IsNullOrEmpty(err)) UDebug.LogWarning(err);

            p.WaitForExit();
        }

        if (!File.Exists(wavPath)) { UDebug.LogError("Piper did not produce a WAV."); done?.Invoke(); yield break; }

        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + wavPath, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                UDebug.LogError("Failed to load Piper WAV: " + req.error);
                done?.Invoke(); yield break;
            }
            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);

            // cache it BEFORE playing
            _cache[Key(text)] = clip;

            _src.clip = clip;
            IsSpeaking = true;
            _src.Play();
            yield return new WaitWhile(() => _src.isPlaying);
            IsSpeaking = false;
        }

        try { File.Delete(wavPath); } catch { }
        done?.Invoke();
    }

    string ResolvePiperExe()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/piper/win/piper.exe");
        return File.Exists(exe) ? exe : null;
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/piper/mac/piper");
        if (File.Exists(exe)) return exe;
        return null;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/piper/linux/piper");
        if (File.Exists(exe)) return exe;
        return null;
#else
        return null;
#endif
    }

    string ResolveModelPath()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string dir = Path.Combine(Application.streamingAssetsPath, "tts/piper/win/voices");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string dir = Path.Combine(Application.streamingAssetsPath, "tts/piper/mac/voices");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        string dir = Path.Combine(Application.streamingAssetsPath, "tts/piper/linux/voices");
#else
        string dir = null;
#endif
        if (string.IsNullOrEmpty(dir)) return null;

        string onnx = Path.Combine(dir, modelFileName);
        if (File.Exists(onnx)) return onnx;

        // allow omitting extension in inspector
        onnx = Path.Combine(dir, modelFileName + ".onnx");
        return File.Exists(onnx) ? onnx : null;
    }
}