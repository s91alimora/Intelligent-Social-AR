using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class ConversationStep
{
    public ConversationalAgent agent;
    public int sentenceIndex;
}

public class ConversationalAgentsManager : MonoBehaviour
{
    [Header("Registered agents (drag your agent objects here)")]
    public List<ConversationalAgent> agents = new List<ConversationalAgent>();

    [Header("Conversation sequence")]
    public List<ConversationStep> sequence = new List<ConversationStep>();

    [Header("Playback")]
    [Tooltip("Start automatically when the scene runs. If off, press Space to start.")]
    public bool playOnStart = false;

    [Min(0f)]
    [Tooltip("Seconds to wait between each spoken line.")]
    public float pauseBetweenLines = 0.5f;

    Coroutine _running;
    bool _isPlaying;

    IEnumerator Start()
    {
        if (playOnStart)
            _running = StartCoroutine(PlayConversation());
        yield break;
    }

    void Update()
    {
        // Manual kick-off with Space (only if not already playing)
        if (!playOnStart && !_isPlaying && Input.GetKeyDown(KeyCode.Space))
        {
            _running = StartCoroutine(PlayConversation());
        }
    }

    public void Play()
    {
        if (_isPlaying) return;
        _running = StartCoroutine(PlayConversation());
    }

    public void Stop()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }
        _isPlaying = false;
    }

    public bool IsPlaying => _isPlaying;

    public IEnumerator PlayConversation()
    {
        if (_isPlaying) yield break;
        _isPlaying = true;

        for (int i = 0; i < sequence.Count; i++)
        {
            var step = sequence[i];
            if (step == null || step.agent == null) continue;

            bool done = false;
            step.agent.Speak(step.sentenceIndex, () => done = true);
            while (!done) yield return null;

            if (pauseBetweenLines > 0f && i < sequence.Count - 1)
                yield return new WaitForSeconds(pauseBetweenLines);
        }

        _isPlaying = false;
        _running = null;
    }
}
