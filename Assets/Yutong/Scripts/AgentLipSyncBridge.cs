using UnityEngine;

/// <summary>
/// Bridges Yutong's CrossPlatformTTS with Jasmine's Oculus LipSync pipeline.
/// Attach this to the same GameObject as CrossPlatformTTS.
/// </summary>
[RequireComponent(typeof(CrossPlatformTTS))]
public class AgentLipSyncBridge : MonoBehaviour
{
    [Header("LipSync Components")]
    public OVRLipSyncContext lipSyncContext;
    public OVRLipSyncContextMorphTarget morphTargetHandler;

    private CrossPlatformTTS _tts;

    void Awake()
    {
        _tts = GetComponent<CrossPlatformTTS>();

        // Auto-find components if not assigned
        if (lipSyncContext == null) lipSyncContext = GetComponent<OVRLipSyncContext>();
        if (morphTargetHandler == null) morphTargetHandler = GetComponent<OVRLipSyncContextMorphTarget>();

        // Validation
        if (lipSyncContext == null)
        {
            Debug.LogWarning($"[LipSyncBridge] OVRLipSyncContext not found on {gameObject.name}. Adding one.");
            lipSyncContext = gameObject.AddComponent<OVRLipSyncContext>();
        }

        // Ensure LipSyncContext is configured to "listen" to the AudioSource
        // and allow the sound to pass through (audioLoopback = true)
        lipSyncContext.skipAudioSource = false; 
        lipSyncContext.audioLoopback = true;
    }

    /// <summary>
    /// This script is mostly a "wiring" helper. 
    /// Because CrossPlatformTTS plays audio via an AudioSource,
    /// and OVRLipSyncContext uses OnAudioFilterRead to listen to that same AudioSource,
    /// they will work together automatically as long as they are on the same GameObject
    /// and audioLoopback is enabled on the LipSyncContext.
    /// </summary>
    void Start()
    {
        Debug.Log($"[LipSyncBridge] Initialized for {gameObject.name}. Ready to sync TTS with Morph Targets.");
    }
}
