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

    //// --- Speaking visual -----------------------------------------------------
    //[Header("Speaking Visual")]
    //[Tooltip("Renderer whose material will be tinted while this agent speaks. " +
    //         "If left empty, the first Renderer in children will be used.")]
    //public Renderer speakingRenderer;

    //[Tooltip("Which sub-material on the renderer to tint (0 = first).")]
    //public int materialIndex = 0;

    //[Tooltip("Shader color property to change (usually _Color).")]
    //public string colorProperty = "_Color";

    //[Tooltip("Tint color to apply while speaking.")]
    //public Color speakingColor = new Color(1f, 0.85f, 0.2f); // warm yellow

    // ------------------------------------------------------------------------

    CrossPlatformTTS _tts;
    //MaterialPropertyBlock _mpb;
    //int _colorPropId;
    //Color _originalColor;
    //bool _hasColorProp; // true only if material + property exist

    void Awake()
    {
        _tts = GetComponent<CrossPlatformTTS>();
        if (string.IsNullOrWhiteSpace(agentName))
            agentName = gameObject.name;

        //if (speakingRenderer == null)
        //    speakingRenderer = GetComponentInChildren<Renderer>();

        //_mpb = new MaterialPropertyBlock();
        //_colorPropId = Shader.PropertyToID(colorProperty);

        // Validate renderer/material/property and cache original color
        //if (speakingRenderer != null)
        //{
        //    var sharedMats = speakingRenderer.sharedMaterials;
        //    if (sharedMats != null && materialIndex >= 0 && materialIndex < sharedMats.Length)
        //    {
        //        var mat = sharedMats[materialIndex];
        //        if (mat != null && mat.HasProperty(_colorPropId))
        //        {
        //            _originalColor = mat.GetColor(_colorPropId);
        //            _hasColorProp = true;
        //        }
        //    }
        //}
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
        if (string.IsNullOrWhiteSpace(line))
        {
            onComplete?.Invoke();
            return;
        }

        //SetSpeakingVisual(true);
        _tts.Speak(line, () =>
        {
            //SetSpeakingVisual(false);
            onComplete?.Invoke();
        });
    }

    //void SetSpeakingVisual(bool on)
    //{
    //    if (!_hasColorProp || speakingRenderer == null) return;

    //    // Read current block, override color, reapply (per-material index)
    //    speakingRenderer.GetPropertyBlock(_mpb, materialIndex);
    //    _mpb.SetColor(_colorPropId, on ? speakingColor : _originalColor);
    //    speakingRenderer.SetPropertyBlock(_mpb, materialIndex);
    //}
}
