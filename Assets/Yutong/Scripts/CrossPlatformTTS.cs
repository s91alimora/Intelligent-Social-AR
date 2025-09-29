using UnityEngine;
using System;
using System.IO;
using System.Collections;
using UDebug = UnityEngine.Debug;
using Proc = System.Diagnostics.Process;
using PSI = System.Diagnostics.ProcessStartInfo;

[RequireComponent(typeof(AudioSource))]
public class CrossPlatformTTS : MonoBehaviour
{
    [Header("Voice")]
    [Tooltip("eSpeak NG voice code (e.g., en-us, en-us+f3, en-gb+x-rp, zh, zh+yue¡­)")]
    public string voice = "en-us";

    [Header("Prosody")]
    [Range(80, 450)] public int wpm = 170;
    [Range(0, 99)] public int pitch = 50;
    [Tooltip("Optional extra args passed to espeak-ng (e.g. \"-a 160 -g 6\")")]
    public string extraArgs = "";

    public bool IsSpeaking { get; private set; }

    AudioSource _source;

    void Awake()
    {
        _source = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
    }

    /// <summary>Speak text, invoking onComplete when audio finishes (or immediately if text invalid).</summary>
    public void Speak(string text, Action onComplete = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { onComplete?.Invoke(); return; }
        StartCoroutine(SpeakCo(text, onComplete));
    }

    IEnumerator SpeakCo(string text, Action done)
    {
        string exe = ResolveEspeakPath();
        if (exe == null)
        {
            UDebug.LogError("CrossPlatformTTS: eSpeak NG not found for this platform/path.");
            done?.Invoke();
            yield break;
        }

        string wavPath = Path.Combine(Application.temporaryCachePath, "tts_espeak_" + Guid.NewGuid().ToString("N") + ".wav");
        string args = $"-w \"{wavPath}\" -v {voice} -s {wpm} -p {pitch} --stdin";
        if (!string.IsNullOrWhiteSpace(extraArgs)) args = extraArgs + " " + args;

        var psi = new PSI
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using (var p = Proc.Start(psi))
        {
            p.StandardInput.Write(text);
            p.StandardInput.Close();

            // Uncomment for CLI diagnostics:
            // string err = p.StandardError.ReadToEnd();
            // if (!string.IsNullOrEmpty(err)) UDebug.LogWarning(err);

            p.WaitForExit();
        }

        if (!File.Exists(wavPath))
        {
            UDebug.LogError("CrossPlatformTTS: eSpeak NG did not produce a WAV file.");
            done?.Invoke();
            yield break;
        }

        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + wavPath, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                UDebug.LogError("CrossPlatformTTS: Failed to load WAV: " + req.error);
                done?.Invoke();
                yield break;
            }

            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
            _source.clip = clip;

            IsSpeaking = true;
            _source.Play();

            // wait for clip to finish
            yield return new WaitWhile(() => _source.isPlaying);
            IsSpeaking = false;
        }

        try { File.Delete(wavPath); } catch { /* ignore */ }
        done?.Invoke();
    }

    string ResolveEspeakPath()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/win/espeak-ng.exe");
        return File.Exists(exe) ? exe : null;
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/mac/espeak-ng");
        if (File.Exists(exe)) return exe;
        if (File.Exists("/opt/homebrew/bin/espeak-ng")) return "/opt/homebrew/bin/espeak-ng";
        if (File.Exists("/usr/local/bin/espeak-ng"))   return "/usr/local/bin/espeak-ng";
        return null;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/linux/espeak-ng");
        if (File.Exists(exe)) return exe;
        if (File.Exists("/usr/bin/espeak-ng"))        return "/usr/bin/espeak-ng";
        if (File.Exists("/usr/local/bin/espeak-ng"))  return "/usr/local/bin/espeak-ng";
        return null;
#else
        return null;
#endif
    }
}
