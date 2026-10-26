using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Puts the virtual room on the real one. With the Stage tracking origin the headset keeps tracking space fixed to the
/// room's boundary through recentering, taking the headset off and restarts. A one-time calibration records where two
/// tape marks on the real floor are in tracking space; every testbed scene then moves its camera rig so those points
/// land on Marker A and Marker B. The environment never moves, so world coordinates (grid, seats, the gaze CSVs'
/// HitX/Y/Z) are the same in every session.
/// </summary>
public class RoomCalibration : MonoBehaviour
{
    public static RoomCalibration Instance { get; private set; }

    [Tooltip("Virtual floor point that stands for tape mark A. Same world position in every testbed scene.")]
    public Transform markerA;
    [Tooltip("Virtual floor point that stands for tape mark B, at least 2 m from A.")]
    public Transform markerB;
    [Tooltip("Discs on the markers (no colliders), shown while calibrating or with the toggle key.")]
    public GameObject[] markerVisuals;
    [Tooltip("Controller held on the tape marks while calibrating.")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public KeyCode calibrateKey = KeyCode.C;
    public KeyCode showMarkersKey = KeyCode.V;
    [Tooltip("Largest accepted difference between the measured and the virtual A-B distance, in metres.")]
    public float maxDistanceError = 0.05f;

    [Serializable]
    private class Data { public Vector3 a; public Vector3 b; public string savedAt; }

    /// <summary>Kept outside Assets: it belongs to this PC and this headset's boundary, not to the project.</summary>
    public static string FilePath => Path.Combine(Application.persistentDataPath, "RoomCalibration.json");

    /// <summary>Null when the rig is placed from a saved calibration, otherwise what the experimenter must do.</summary>
    public string Problem { get; private set; }

    private int _step; // 0 idle, 1 waiting for mark A, 2 waiting for mark B
    private Vector3 _a;
    private bool _markersVisible;

    void Awake()
    {
        Instance = this;
        SetMarkersVisible(false);
    }

    void Start()
    {
        var master = MasterLevelController.Instance;
        if (master != null && !master.AnchorToRoom)
        {
            // Pilot run outside the lab: the room follows the headset as before.
            OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            enabled = false;
            return;
        }
        Load();
    }

    void Update()
    {
        if (Input.GetKeyDown(showMarkersKey)) SetMarkersVisible(!_markersVisible);
        if (Input.GetKeyDown(calibrateKey))
        {
            _step = 1;
            SetMarkersVisible(true);
            Problem = "Calibrating: controller upright on tape mark A, press the trigger.";
        }
        if (_step == 0 || !OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller)) return;

        Vector3 p = OVRInput.GetLocalControllerPosition(controller); // tracking (Stage) space
        if (_step == 1)
        {
            _a = p;
            _step = 2;
            Problem = "Calibrating: now on tape mark B, press the trigger.";
            return;
        }
        _step = 0;
        float measured = Vector3.Distance(Flat(_a), Flat(p));
        float expected = Vector3.Distance(Flat(markerA.position), Flat(markerB.position));
        if (Mathf.Abs(measured - expected) > maxDistanceError)
        {
            Problem = $"Calibration rejected: A to B measured {measured:F2} m, the scene has {expected:F2} m. Press {calibrateKey} to retry.";
            return;
        }
        var data = new Data { a = _a, b = p, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        Apply(data);
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
        {
            Problem = $"Room not calibrated: the virtual room is not on the real one. Press {calibrateKey} to calibrate.";
            return;
        }
        Apply(JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)));
    }

    /// <summary>Moves and turns the rig about the vertical axis so the two measured points land on the markers.</summary>
    private void Apply(Data d)
    {
        Vector3 measured = Flat(d.b - d.a);
        Vector3 scene = Flat(markerB.position - markerA.position);
        Quaternion rotation = Quaternion.AngleAxis(Vector3.SignedAngle(measured, scene, Vector3.up), Vector3.up);
        Vector3 position = markerA.position - rotation * Flat(d.a);
        position.y = markerA.position.y; // Stage puts the real floor at y = 0 of tracking space
        transform.SetPositionAndRotation(position, rotation);
        Problem = null;
        Debug.Log($"[RoomCalibration] Rig placed from the calibration of {d.savedAt} ({FilePath}).");
    }

    private void SetMarkersVisible(bool visible)
    {
        _markersVisible = visible;
        foreach (var v in markerVisuals) if (v != null) v.SetActive(visible);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}