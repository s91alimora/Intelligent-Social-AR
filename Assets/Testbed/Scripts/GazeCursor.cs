using UnityEngine;

/// <summary>
/// Keeps the head-gaze cursor on whatever the head ray hits (an avatar, the wall, the floor) instead of at a fixed
/// distance. The eyes then converge on the cursor at the same depth as what the participant is looking at, so it is
/// seen single; a cursor much nearer than the target is seen double. Its size follows the distance, so it always looks
/// the same size. Put it on the Sphere Cursor, a child of CenterEyeAnchor.
/// </summary>
public class GazeCursor : MonoBehaviour
{
    [Tooltip("Cursor diameter in degrees of visual angle.")]
    public float angularSize = 1.5f;
    [Tooltip("Distance (m) used when the head ray hits nothing.")]
    public float defaultDistance = 2f;
    [Tooltip("The recorder whose Layer Mask and Max Distance the cursor's ray uses, so the cursor always shows what is " +
             "logged. Empty = the scene's Gaze Data Recorder.")]
    public GazeDataRecorder recorder;
    [Tooltip("The cursor sits this fraction of the distance in front of the surface, so it is not hidden inside it.")]
    public float surfaceOffset = 0.03f;

    private readonly RaycastHit[] _hits = new RaycastHit[16];

    void Awake()
    {
        if (recorder == null) recorder = FindObjectOfType<GazeDataRecorder>();
    }

    void LateUpdate()
    {
        Transform head = transform.parent;
        if (head == null) return;

        // Same ray as GazeDataRecorder (its Layer Mask and Max Distance): the nearest non-trigger collider, skipping
        // anything attached to the head (including this cursor's own collider).
        float range = recorder != null ? recorder.maxDistance : 10f;
        int mask = recorder != null ? (int)recorder.layerMask : ~0;
        int count = Physics.RaycastNonAlloc(head.position, head.forward, _hits, range, mask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (_hits[i].collider.transform.IsChildOf(head)) continue;
            nearest = Mathf.Min(nearest, _hits[i].distance);
        }
        float distance = nearest < float.MaxValue ? nearest * (1f - surfaceOffset) : defaultDistance;

        // Set along the head's forward axis in its own space, so the cursor stays centred when the headset updates the
        // head pose again just before rendering.
        float scale = head.lossyScale.z;
        transform.localPosition = new Vector3(0f, 0f, distance / scale);
        transform.localScale = Vector3.one * (2f * distance * Mathf.Tan(angularSize * 0.5f * Mathf.Deg2Rad) / scale);
    }
}