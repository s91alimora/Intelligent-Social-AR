using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(CrossPlatformTTS))]
public class ConversationalAgent : MonoBehaviour
{
    [Tooltip("Optional display name; defaults to GameObject name")]
    public string agentName;

    [TextArea(2, 5)]
    public List<string> sentences = new List<string>();

    CrossPlatformTTS _tts;

    void Awake()
    {
        _tts = GetComponent<CrossPlatformTTS>();
        if (string.IsNullOrWhiteSpace(agentName))
            agentName = gameObject.name;
    }

    public int SentenceCount => sentences?.Count ?? 0;

    public string GetSentence(int index)
    {
        if (index < 0 || index >= SentenceCount) return null;
        return sentences[index];
    }

    public void Speak(int index, Action onComplete = null)
    {
        var line = GetSentence(index);
        if (string.IsNullOrWhiteSpace(line)) { onComplete?.Invoke(); return; }
        _tts.Speak(line, onComplete);
    }
}
