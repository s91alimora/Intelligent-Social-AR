using UnityEngine;
using System;
using System.IO;
using System.Collections;
using UDebug = UnityEngine.Debug;
using Proc = System.Diagnostics.Process;
using PSI = System.Diagnostics.ProcessStartInfo;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.Networking;

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

    [Header("Loudness Normalization")]
    [Tooltip("Equalize perceived loudness across voices: each clip's speech RMS is measured once and the " +
             "AudioSource volume is set so every voice plays at the target level. Applies to both " +
             "runtime-synthesized and pre-baked audio, and reacts immediately to setting changes.")]
    public bool normalizeLoudness = true;
    [Tooltip("Target speech-level RMS in dBFS. -20 dBFS is below the quietest bundled voice, so the derived gain is always an attenuation (volume <= 1).")]
    public float targetRmsDb = -20f;

    public bool IsSpeaking { get; private set; }

    AudioSource _src;

    class CachedLine
    {
        public AudioClip clip;
        public float speechRms; // gated speech-level RMS of the raw clip; playback volume is derived from this
    }

    static readonly Dictionary<string, CachedLine> _cache = new();
    // Invariant culture keeps cache keys (and thus baked MD5 filenames) identical across machine locales.
    string Key(string text) => $"{modelFileName}|{FloatInv(lengthScale)}|{FloatInv(noiseScale)}|{text}";

    static string FloatInv(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetBakedFilename(string text)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(Key(text));
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++) sb.Append(hashBytes[i].ToString("x2"));
            return sb.ToString() + ".wav";
        }
    }

    void Awake()
    {
        _src = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
    }

    public void Stop()
    {
        StopAllCoroutines();
        if (_src != null) _src.Stop();
        IsSpeaking = false;
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

    /// <summary>Frees all cached clips (native audio memory). Call between condition scenes.</summary>
    public static void ClearCache()
    {
        foreach (var entry in _cache.Values)
        {
            if (entry.clip != null) Destroy(entry.clip);
        }
        _cache.Clear();
    }

    IEnumerator PlayCached(CachedLine line, Action done)
    {
        IsSpeaking = true;
        _src.clip = line.clip;
        _src.volume = GetPlaybackVolume(line.speechRms);
        _src.Play();
        yield return new WaitWhile(() => _src.isPlaying);
        IsSpeaking = false;
        done?.Invoke();
    }

    /// <summary>
    /// Volume that brings a clip with the given speech RMS to targetRmsDb. Computed at playback time,
    /// so toggling normalizeLoudness / changing targetRmsDb takes effect even for already-cached lines.
    /// Always an attenuation for the bundled voices (target sits below their natural level).
    /// </summary>
    float GetPlaybackVolume(float speechRms)
    {
        if (!normalizeLoudness || speechRms <= 0f) return 1f;
        return Mathf.Min(1f, Mathf.Pow(10f, targetRmsDb / 20f) / speechRms);
    }

    /// <summary>
    /// Gated speech-level RMS of a clip, measured over 30 ms frames read in small chunks
    /// (no full-buffer allocation, no sample rewriting). Gate = 3% of the loudest frame's RMS.
    /// </summary>
    static float MeasureSpeechRms(AudioClip clip)
    {
        if (clip == null || clip.samples == 0) return 0f;

        int framePerChannel = Mathf.Max(1, (int)(0.03f * clip.frequency));
        int nFrames = clip.samples / framePerChannel;
        if (nFrames == 0) return 0f;

        var buf = new float[framePerChannel * clip.channels];
        var frameRms = new float[nFrames];
        float maxFrameRms = 0f;
        for (int f = 0; f < nFrames; f++)
        {
            if (!clip.GetData(buf, f * framePerChannel)) return 0f;
            double sum = 0;
            for (int i = 0; i < buf.Length; i++) sum += buf[i] * buf[i];
            frameRms[f] = Mathf.Sqrt((float)(sum / buf.Length));
            if (frameRms[f] > maxFrameRms) maxFrameRms = frameRms[f];
        }

        float gate = Mathf.Max(maxFrameRms * 0.03f, 1e-4f);
        double acc = 0; int n = 0;
        for (int f = 0; f < nFrames; f++)
        {
            if (frameRms[f] > gate) { acc += (double)frameRms[f] * frameRms[f]; n++; }
        }
        return n == 0 ? 0f : Mathf.Sqrt((float)(acc / n));
    }


    public void Prepare(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_cache.ContainsKey(Key(text))) return;
        StartCoroutine(SpeakCo(text, null, prepareOnly: true));
    }



    IEnumerator SpeakCo(string text, Action done, bool prepareOnly = false)
    {
        // 1. Check for Pre-Baked file in StreamingAssets (Crucial for Android/Quest)
        string bakedFilename = GetBakedFilename(text);
        string bakedPath = Path.Combine(Application.streamingAssetsPath, "AudioCache", bakedFilename);
        
        bool isAndroid = Application.platform == RuntimePlatform.Android;
        bool exists = false;
        
        // On Android, we can't use File.Exists for StreamingAssets
        if (isAndroid) exists = true; // We'll try to load it and fail gracefully
        else exists = File.Exists(bakedPath);

        if (exists)
        {
            // Use same path logic as resolve (Android uses URL)
            string url = bakedPath;
            if (isAndroid || bakedPath.Contains("://")) url = bakedPath;
            else url = "file://" + bakedPath;

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                    var line = new CachedLine { clip = clip, speechRms = MeasureSpeechRms(clip) };
                    _cache[Key(text)] = line;
                    if (!prepareOnly)
                    {
                        _src.clip = clip;
                        _src.volume = GetPlaybackVolume(line.speechRms);
                        IsSpeaking = true;
                        _src.Play();
                        yield return new WaitWhile(() => _src.isPlaying);
                        IsSpeaking = false;
                    }
                    done?.Invoke();
                    yield break;
                }
                else if (!isAndroid)
                {
                    UDebug.LogWarning($"Baked file found but failed to load: {req.error}. Falling back to CLI.");
                }
            }
        }

        // 2. Fallback to CLI (Only works on Desktop)
        string exe = ResolvePiperExe();
        if (exe == null) { UDebug.LogError("Piper executable not found and no baked file found."); done?.Invoke(); yield break; }

        string model = ResolveModelPath();
        if (model == null) { UDebug.LogError("Piper model not found."); done?.Invoke(); yield break; }

        string wavPath = Path.Combine(Application.temporaryCachePath, "tts_piper_" + Guid.NewGuid().ToString("N") + ".wav");

        // Build Piper CLI args
        // --model <onnx>  --output_file <wav>  --length_scale <f>  --noise_scale <f>
        string args = $"--model \"{model}\" --output_file \"{wavPath}\" --length_scale {lengthScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} --noise_scale {noiseScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
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
            // Drain stderr asynchronously: with RedirectStandardError set, an undrained pipe can fill
            // and block piper forever (HasExited would never turn true), hanging the session.
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();

            p.StandardInput.Write(text);
            p.StandardInput.Close();

            //p.WaitForExit();
            // Non-blocking wait inside coroutine:
            while (!p.HasExited)
                yield return null;
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
            var line = new CachedLine { clip = clip, speechRms = MeasureSpeechRms(clip) };

            // cache
            _cache[Key(text)] = line;

            if (!prepareOnly)
            {
                _src.clip = clip;
                _src.volume = GetPlaybackVolume(line.speechRms);
                IsSpeaking = true;
                _src.Play();
                yield return new WaitWhile(() => _src.isPlaying);
                IsSpeaking = false;
            }
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

    // --- Editor Baking Support ---
    public void BakeToFile(string text, string targetPath)
    {
        string exe = ResolvePiperExe();
        string model = ResolveModelPath();
        if (exe == null || model == null) return;

        string args = $"--model \"{model}\" --output_file \"{targetPath}\" --length_scale {lengthScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} --noise_scale {noiseScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(extraArgs)) args = extraArgs + " " + args;

        var psi = new PSI
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        using (var p = Proc.Start(psi))
        {
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit();
        }
    }
}
