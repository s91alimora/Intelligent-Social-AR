using UnityEngine;
using System.IO;
using System.Diagnostics;
using System.Collections;

public class CrossPlatformTTS : MonoBehaviour
{
    [TextArea(3, 6)] public string text = "Hello from eSpeak NG!";
    [Tooltip("Voice/language code, e.g. en-us, en-gb, zh, zh+yue, fr, de")]
    public string voice = "en-us";
    [Tooltip("Words per minute (80¨C450)")]
    [Range(80, 450)] public int wpm = 170;
    [Tooltip("Pitch (0¨C99)")]
    [Range(0, 99)] public int pitch = 50;
    [Tooltip("Optional extra args passed to espeak-ng, e.g. -a 150")]
    public string extraArgs = "";

    private AudioSource _source;

    void Awake()
    {
        _source = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Space))
            StartCoroutine(SpeakCo(text, voice, wpm, pitch, extraArgs));
#endif
    }

#if UNITY_STANDALONE || UNITY_EDITOR
    IEnumerator SpeakCo(string msg, string v, int rate, int pit, string extra)
    {
        string exe = ResolveEspeakPath();
        if (exe == null)
        {
            UnityEngine.Debug.LogError("eSpeak NG not found for this platform/path.");
            yield break;
        }
        if (string.IsNullOrWhiteSpace(msg))
            yield break;

        string wavPath = Path.Combine(Application.temporaryCachePath, "tts_espeak_" + System.Guid.NewGuid().ToString("N") + ".wav");

        // Build eSpeak NG arguments
        string args = $"-w \"{wavPath}\" -v {v} -s {rate} -p {pit} --stdin";
        if (!string.IsNullOrWhiteSpace(extra)) args = extra + " " + args;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using (var p = Process.Start(psi))
        {
            p.StandardInput.Write(msg);
            p.StandardInput.Close();

            // Uncomment to capture CLI errors:
            // string err = p.StandardError.ReadToEnd();
            // if (!string.IsNullOrEmpty(err)) Debug.LogWarning(err);

            p.WaitForExit();
        }

        if (!File.Exists(wavPath))
        {
            UnityEngine.Debug.LogError("eSpeak NG did not produce a WAV file.");
            yield break;
        }

        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + wavPath, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError("Failed to load WAV: " + req.error);
                yield break;
            }

            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
            _source.clip = clip;
            _source.Play();
        }

        // Clean up temp file
        try { File.Delete(wavPath); } catch { /* ignore */ }
    }

    string ResolveEspeakPath()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        // Bundled copy in StreamingAssets (recommended)
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/win/espeak-ng.exe");
        return File.Exists(exe) ? exe : null;

#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        // Prefer bundled binary; fall back to common Homebrew paths
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/mac/espeak-ng");
        if (File.Exists(exe)) return exe;
        if (File.Exists("/opt/homebrew/bin/espeak-ng")) return "/opt/homebrew/bin/espeak-ng";
        if (File.Exists("/usr/local/bin/espeak-ng"))   return "/usr/local/bin/espeak-ng";
        return null;

#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        // Prefer bundled binary; fall back to system paths
        string exe = Path.Combine(Application.streamingAssetsPath, "tts/espeak-ng/linux/espeak-ng");
        if (File.Exists(exe)) return exe;
        if (File.Exists("/usr/bin/espeak-ng"))        return "/usr/bin/espeak-ng";
        if (File.Exists("/usr/local/bin/espeak-ng"))  return "/usr/local/bin/espeak-ng";
        return null;

#else
        // Unsupported platforms (Android/iOS/WebGL/¡­)
        return null;
#endif
    }
#endif
}
