using UnityEngine;
using System.IO;
using System;
using System.Threading.Tasks;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Speech.Synthesis; // System.Speech.dll in Assets/Plugins/Windows
#endif

[RequireComponent(typeof(AudioSource))]
public class TextToSpeech : MonoBehaviour
{
    [TextArea(3, 6)]
    public string textToSpeak = "Hello from System.Speech!";

    [Tooltip("Exact voice name from GetInstalledVoices(), e.g. 'Microsoft Zira Desktop' or 'Microsoft Huihui Desktop'")]
    public string voiceName = "Microsoft Zira Desktop";

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
            _ = SynthesizeAndPlay(textToSpeak, voiceName);
            Debug.Log("hhhh");
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            // List voices in Console
            using (var s = new SpeechSynthesizer())
                foreach (var v in s.GetInstalledVoices())
                    Debug.Log($"Voice: {v.VoiceInfo.Name} ({v.VoiceInfo.Culture})");
        }
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    async Task SynthesizeAndPlay(string text, string desiredVoice)
    {
        try
        {
            byte[] wavBytes = await Task.Run(() =>
            {
                using (var ms = new MemoryStream())
                using (var localSynth = new SpeechSynthesizer())
                {
                    if (!string.IsNullOrWhiteSpace(desiredVoice))
                        localSynth.SelectVoice(desiredVoice); // pick a specific voice

                    localSynth.SetOutputToWaveStream(ms); // no default device
                    localSynth.Speak(text);               // blocking on worker thread
                    localSynth.SetOutputToNull();
                    return ms.ToArray();
                }
            });

            if (WavToClip(wavBytes, out var clip))
            {
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                Debug.LogError("Failed to parse WAV data.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("TTS error: " + e);
        }
    }

    // Minimal WAV (PCM16 mono/stereo) ¡ú AudioClip
    static bool WavToClip(byte[] data, out AudioClip clip)
    {
        clip = null;
        try
        {
            int pos = 0;
            string chunkID = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            if (chunkID != "RIFF") return false;
            pos += 4; // chunk size
            string format = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            if (format != "WAVE") return false;

            // fmt
            string subchunk1ID = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            int subchunk1Size = BitConverter.ToInt32(data, pos); pos += 4;
            short audioFormat = BitConverter.ToInt16(data, pos); pos += 2; // 1=PCM
            short channels = BitConverter.ToInt16(data, pos); pos += 2;
            int sampleRate = BitConverter.ToInt32(data, pos); pos += 4;
            pos += 6; // byteRate + blockAlign
            short bitsPerSample = BitConverter.ToInt16(data, pos); pos += 2;
            if (subchunk1Size > 16) pos += (subchunk1Size - 16);
            // find data chunk
            string subchunk2ID = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            while (subchunk2ID != "data")
            {
                int skip = BitConverter.ToInt32(data, pos); pos += 4 + skip;
                subchunk2ID = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            }
            int subchunk2Size = BitConverter.ToInt32(data, pos); pos += 4;

            if (audioFormat != 1 || bitsPerSample != 16)
                Debug.LogWarning($"Non-PCM16 WAV: fmt={audioFormat}, bps={bitsPerSample}");

            int bytesPerSample = bitsPerSample / 8;
            int channelCount = Math.Max((int)channels, 1);
            int totalSamples = subchunk2Size / bytesPerSample / channelCount;

            float[] samples = new float[totalSamples];
            if (channelCount == 1)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short s = BitConverter.ToInt16(data, pos); pos += 2;
                    samples[i] = s / 32768f;
                }
            }
            else
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short l = BitConverter.ToInt16(data, pos); pos += 2;
                    short r = BitConverter.ToInt16(data, pos); pos += 2;
                    samples[i] = ((l + r) * 0.5f) / 32768f; // downmix stereo¡úmono
                }
            }

            clip = AudioClip.Create("TTS", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("WAV parse exception: " + ex.Message);
            return false;
        }
    }
#endif
}
