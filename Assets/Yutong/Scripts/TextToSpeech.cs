using UnityEngine;
using System.IO;
using System;
using System.Threading.Tasks;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Speech.Synthesis; // requires System.Speech.dll in Assets/Plugins/Windows
#endif

[RequireComponent(typeof(AudioSource))]
public class TextToSpeech : MonoBehaviour
{
    [TextArea(3, 6)]
    public string textToSpeak = "Hello from Windows TTS via stream!";

    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (Input.GetKeyDown(KeyCode.Space) && !string.IsNullOrWhiteSpace(textToSpeak))
        {
            // synthesize on a worker thread, then play on main
            _ = SynthesizeAndPlay(textToSpeak);
        }
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    async Task SynthesizeAndPlay(string text)
    {
        try
        {
            byte[] wavBytes = await Task.Run(() =>
            {
                using (var ms = new MemoryStream())
                using (var localSynth = new SpeechSynthesizer())
                {
                    // Optional: choose a specific installed voice
                    // foreach (var v in localSynth.GetInstalledVoices()) Debug.Log(v.VoiceInfo.Name);
                    // localSynth.SelectVoice("Microsoft Zira Desktop");

                    // IMPORTANT: go straight to a stream (avoid default audio device)
                    localSynth.SetOutputToWaveStream(ms);
                    localSynth.Speak(text);            // blocking on this worker thread
                    localSynth.SetOutputToNull();      // detach
                    return ms.ToArray();
                }
            });

            if (WavUtility.TryToAudioClip(wavBytes, out var clip))
            {
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                Debug.LogError("Failed to parse WAV from TTS.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("TTS error: " + e);
        }
    }
#endif
}

/// <summary>Minimal WAV reader for PCM 16-bit mono/any sample rate.</summary>
public static class WavUtility
{
    public static bool TryToAudioClip(byte[] wavData, out AudioClip clip)
    {
        clip = null;
        try
        {
            int pos = 0;
            string riff = System.Text.Encoding.ASCII.GetString(wavData, pos, 4); pos += 4;
            if (riff != "RIFF") return false;
            pos += 4; // chunk size
            string wave = System.Text.Encoding.ASCII.GetString(wavData, pos, 4); pos += 4;
            if (wave != "WAVE") return false;

            string fmt = System.Text.Encoding.ASCII.GetString(wavData, pos, 4); pos += 4;
            if (fmt != "fmt ") return false;

            int sub1 = BitConverter.ToInt32(wavData, pos); pos += 4;
            short audioFmt = BitConverter.ToInt16(wavData, pos); pos += 2; // 1 = PCM
            short channels = BitConverter.ToInt16(wavData, pos); pos += 2;
            int sampleRate = BitConverter.ToInt32(wavData, pos); pos += 4;
            pos += 6; // byteRate + blockAlign
            short bps = BitConverter.ToInt16(wavData, pos); pos += 2;

            if (sub1 > 16) pos += (sub1 - 16);

            string hdr = System.Text.Encoding.ASCII.GetString(wavData, pos, 4); pos += 4;
            while (hdr != "data")
            {
                int size = BitConverter.ToInt32(wavData, pos); pos += 4 + size;
                hdr = System.Text.Encoding.ASCII.GetString(wavData, pos, 4); pos += 4;
            }
            int dataSize = BitConverter.ToInt32(wavData, pos); pos += 4;

            if (audioFmt != 1 || bps != 16) Debug.LogWarning($"Non-PCM16 WAV (fmt={audioFmt}, bps={bps}).");

            int bytesPerSample = bps / 8;
            int totalSamples = dataSize / bytesPerSample / Math.Max((int)channels, 1);
            float[] samples = new float[totalSamples];

            if (channels == 1)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short s = BitConverter.ToInt16(wavData, pos); pos += 2;
                    samples[i] = s / 32768f;
                }
            }
            else // simple stereo¡úmono downmix
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short l = BitConverter.ToInt16(wavData, pos); pos += 2;
                    short r = BitConverter.ToInt16(wavData, pos); pos += 2;
                    samples[i] = ((l + r) * 0.5f) / 32768f;
                }
            }

            clip = AudioClip.Create("TTS", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("WAV parse exception: " + e.Message);
            return false;
        }
    }
}
