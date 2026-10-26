using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays a walk cycle while an agent is moved by script (SessionController.MoveAgent), so avatars
/// step to their grid seat instead of gliding there.
///
/// No Animator Controller has to be edited: for the duration of a walk, a small PlayableGraph blends
/// the agent's own controller (idle) with a walk clip and drives the Animator; it is destroyed on
/// arrival and the controller (idle/talk) takes back control.
///
/// The Rocketbox walk clips (Resources/Locomotion, MIT, github.com/microsoft/Microsoft-Rocketbox)
/// carry their forward travel on Bip01, so that bone's horizontal drift is cancelled in LateUpdate:
/// the script moves the agent, the clip only moves the limbs.
/// </summary>
[DisallowMultipleComponent]
public class AgentLocomotion : MonoBehaviour
{
    public const string WalkClipResourceFolder = "Locomotion";
    public const string DefaultMaleWalkClip = "m_walk_neutral.max";
    public const string DefaultFemaleWalkClip = "f_walk_neutral.max";

    [Header("Walk Clip")]
    [Tooltip("Looping walk cycle for the Rocketbox (Bip01) Generic rig. If empty, a male or female default is " +
             "loaded from Resources/Locomotion, picked by the Animator Controller name (Male_* / Female_*).")]
    public AnimationClip walkClip;

    [Tooltip("Ground speed (m/s) at which the clip's feet do not slide at 1x playback. " +
             "0 = measure it from the clip's root travel (falls back to 1.3 m/s for in-place clips).")]
    public float clipGroundSpeed = 0f;

    [Tooltip("Skeleton root bone whose horizontal travel in the clip is cancelled.")]
    public string rootBoneName = "Bip01";

    [Header("Motion")]
    [Tooltip("Turning speed (deg/s) when facing the destination and when turning back on arrival.")]
    public float turnSpeed = 180f;

    [Tooltip("Seconds to speed up from standstill to walking pace (and to slow down before arriving).")]
    public float accelerationTime = 0.4f;

    [Tooltip("Minimum seconds for the idle <-> walk blend at the start and the end of a walk.")]
    public float blendTime = 0.25f;

    [Tooltip("Step-rate multiplier while turning on the spot.")]
    public float turnCadence = 0.7f;

    [Tooltip("On arrival, turn back to the facing the agent had before walking (e.g. towards the participant).")]
    public bool restoreFacingOnArrival = true;

    public bool IsWalking { get; private set; }

    private const float MinCadence = 0.5f;
    private const float MaxCadence = 1.6f;

    private Animator _animator;
    private Transform _rootBone;

    private PlayableGraph _graph;
    private AnimationMixerPlayable _mixer;
    private AnimatorControllerPlayable _controller;
    private AnimationClipPlayable _walk;
    private float _phase; // walk clip time, advanced manually so the cycle loops regardless of import settings

    private bool _calibrated;
    private float _groundSpeed = 1.3f;
    private Quaternion _walkYawOffset = Quaternion.identity; // turns the agent so the clip's travel points along the path
    private Vector3 _rootBoneRest; // root bone position in agent space at walk start

    void Awake()
    {
        var agent = GetComponent<ConversationalAgent>();
        _animator = agent != null && agent.animator != null ? agent.animator : GetComponentInChildren<Animator>();
        if (_animator != null) _rootBone = FindDeep(_animator.transform, rootBoneName);
    }

    void OnDisable() => EndWalk();
    void OnDestroy() => EndWalk();

    /// <summary>
    /// Ends a walk at once and hands the Animator back to its controller. For a walk whose coroutine was stopped
    /// (the wizard pressed Back); the caller then puts the agent where it belongs.
    /// </summary>
    public void StopWalk() => EndWalk();

    /// <summary>True if WalkTo will animate this move; otherwise the caller should move the agent itself.</summary>
    public bool CanWalk(Vector3 destination, float travelTime)
    {
        if (travelTime <= 0f) return false;
        if (Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).magnitude < 0.05f) return false;
        return EnsureReady();
    }

    /// <summary>
    /// Turn towards the destination, walk there in travelTime seconds (trapezoidal speed profile,
    /// step rate matched to ground speed), then turn back to the original facing.
    /// </summary>
    public IEnumerator WalkTo(Vector3 destination, float travelTime)
    {
        if (!CanWalk(destination, travelTime))
        {
            transform.position = destination;
            yield break;
        }

        Vector3 start = transform.position;
        Vector3 delta = destination - start;
        Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
        float distance = flat.magnitude;
        Quaternion startRot = transform.rotation;
        Quaternion walkRot = Quaternion.LookRotation(flat / distance, Vector3.up) * _walkYawOffset;

        BeginWalk();

        // 1) Turn on the spot towards the destination while starting to step.
        yield return TurnAndBlend(walkRot, 0f, 1f);

        // 2) Travel. Normalised progress s(t) follows a trapezoidal speed profile v(t).
        float ta = Mathf.Min(accelerationTime, travelTime / 3f);
        float vPeak = 1f / (travelTime - ta);
        for (float t = 0f; t < travelTime; t += Time.deltaTime)
        {
            float s, v;
            if (t < ta) { v = vPeak * t / ta; s = 0.5f * v * t; }
            else if (t > travelTime - ta) { float r = travelTime - t; v = vPeak * r / ta; s = 1f - 0.5f * v * r; }
            else { v = vPeak; s = vPeak * (t - 0.5f * ta); }

            transform.position = start + delta * s;
            ApplyPose(1f, Mathf.Clamp(v * distance / _groundSpeed, MinCadence, MaxCadence));
            yield return null;
        }
        transform.position = destination;

        // 3) Turn back to the original facing while settling into idle.
        yield return TurnAndBlend(restoreFacingOnArrival ? startRot : transform.rotation, 1f, 0f);

        EndWalk();
    }

    private IEnumerator TurnAndBlend(Quaternion target, float fromWeight, float toWeight)
    {
        Quaternion from = transform.rotation;
        float duration = Mathf.Max(blendTime, Quaternion.Angle(from, target) / Mathf.Max(1f, turnSpeed));
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float u = t / duration;
            transform.rotation = Quaternion.Slerp(from, target, Mathf.SmoothStep(0f, 1f, u));
            ApplyPose(Mathf.Lerp(fromWeight, toWeight, u), turnCadence);
            yield return null;
        }
        transform.rotation = target;
        ApplyPose(toWeight, turnCadence);
    }

    // --- Graph ---

    private void BeginWalk()
    {
        _rootBoneRest = transform.InverseTransformPoint(_rootBone.position);

        _graph = PlayableGraph.Create($"{name}.Walk");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        _controller = AnimatorControllerPlayable.Create(_graph, _animator.runtimeAnimatorController);
        _walk = AnimationClipPlayable.Create(_graph, walkClip);
        _walk.SetSpeed(0); // time is set explicitly in ApplyPose
        _phase = 0f;

        _mixer = AnimationMixerPlayable.Create(_graph, 2);
        _graph.Connect(_controller, 0, _mixer, 0);
        _graph.Connect(_walk, 0, _mixer, 1);
        _mixer.SetInputWeight(0, 1f);
        _mixer.SetInputWeight(1, 0f);

        var output = AnimationPlayableOutput.Create(_graph, "Walk", _animator);
        output.SetSourcePlayable(_mixer);

        // Continue the idle where the Animator's own controller is, so taking over does not pop.
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        _controller.Play(state.fullPathHash, 0, state.normalizedTime);

        _graph.Play();
        IsWalking = true;
    }

    private void EndWalk()
    {
        if (!_graph.IsValid()) { IsWalking = false; return; }

        // Hand the idle back at the same point it reached inside the graph.
        var state = _controller.GetCurrentAnimatorStateInfo(0);
        _graph.Destroy();
        IsWalking = false;
        if (_animator != null && _animator.isActiveAndEnabled)
            _animator.Play(state.fullPathHash, 0, state.normalizedTime);
    }

    private void ApplyPose(float walkWeight, float cadence)
    {
        if (!_graph.IsValid()) return;

        _mixer.SetInputWeight(0, 1f - walkWeight);
        _mixer.SetInputWeight(1, walkWeight);

        _phase = (_phase + Time.deltaTime * cadence) % walkClip.length;
        _walk.SetTime(_phase);
    }

    void LateUpdate()
    {
        if (!IsWalking) return;

        // Pin the root bone's horizontal position to where it stood at idle; keep the clip's vertical bob.
        Vector3 p = transform.InverseTransformPoint(_rootBone.position);
        p.x = _rootBoneRest.x;
        p.z = _rootBoneRest.z;
        _rootBone.position = transform.TransformPoint(p);
    }

    // --- Setup ---

    private bool EnsureReady()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null || _rootBone == null) return false;
        if (walkClip == null) walkClip = LoadDefaultClip();
        if (walkClip == null || walkClip.length <= 0f) return false;
        if (!_calibrated) Calibrate();
        return true;
    }

    private AnimationClip LoadDefaultClip()
    {
        bool female = _animator.runtimeAnimatorController.name.ToLowerInvariant().Contains("female");
        string path = $"{WalkClipResourceFolder}/{(female ? DefaultFemaleWalkClip : DefaultMaleWalkClip)}";

        // An FBX can also hold a hidden "__preview__" take; skip it.
        var clip = Resources.LoadAll<AnimationClip>(path).FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null)
            Debug.LogWarning($"[AgentLocomotion] No walk clip at Resources/{path} - {name} will glide without a walk animation.");
        return clip;
    }

    /// <summary>
    /// Measures the clip's ground speed and travel direction from the root bone's displacement over
    /// one cycle. Sampled on a bare probe hierarchy (Generic curves bind by path), so the live
    /// avatar's pose is never touched.
    /// </summary>
    private void Calibrate()
    {
        _calibrated = true;
        _groundSpeed = clipGroundSpeed > 0f ? clipGroundSpeed : 1.3f;
        _walkYawOffset = Quaternion.identity;

        var probe = new GameObject("WalkClipProbe") { hideFlags = HideFlags.HideAndDontSave };
        Transform probeBone = probe.transform;
        foreach (string part in RelativePath(_animator.transform, _rootBone).Split('/'))
        {
            var child = new GameObject(part).transform;
            child.SetParent(probeBone, false);
            probeBone = child;
        }

        walkClip.SampleAnimation(probe, 0f);
        Vector3 p0 = probeBone.localPosition;
        walkClip.SampleAnimation(probe, walkClip.length);
        Vector3 p1 = probeBone.localPosition;
        Destroy(probe);

        // Displacement in the real rig's parent space -> world -> agent space.
        Vector3 travelWorld = _rootBone.parent.TransformVector(p1 - p0);
        Vector3 travelLocal = Vector3.ProjectOnPlane(transform.InverseTransformDirection(travelWorld), Vector3.up);
        float travel = Vector3.ProjectOnPlane(travelWorld, transform.up).magnitude;

        if (travel > 0.05f)
        {
            if (clipGroundSpeed <= 0f) _groundSpeed = travel / walkClip.length;
            _walkYawOffset = Quaternion.Inverse(Quaternion.LookRotation(travelLocal.normalized, Vector3.up));
        }

        Debug.Log($"[AgentLocomotion] {name}: walk clip '{walkClip.name}' ({walkClip.length:F2}s), " +
                  $"ground speed {_groundSpeed:F2} m/s, travel yaw offset {_walkYawOffset.eulerAngles.y:F0} deg.");
    }

    private static string RelativePath(Transform root, Transform target)
    {
        string path = target.name;
        for (Transform t = target.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
        return path;
    }

    private static Transform FindDeep(Transform parent, string boneName)
    {
        if (parent.name == boneName) return parent;
        foreach (Transform child in parent)
        {
            var found = FindDeep(child, boneName);
            if (found != null) return found;
        }
        return null;
    }
}
