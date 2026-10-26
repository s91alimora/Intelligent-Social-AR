using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

public class GazeDataRecorder : MonoBehaviour
{
    [Header("Raycast Settings")]
    public Camera mainCamera;
    public Transform raySource;
    public float maxDistance = 10f;
    public LayerMask layerMask = ~0;

    [Header("Tags")]
    public string agentTag = "Agent";
    public string wallTag = "WallUI";

    [Header("Thresholds")]
    [Tooltip("How long to stay on an object (saccade) to count as a fixation.")]
    public float saccadeThreshold = 0.3f;
    [Tooltip("Grace period to keep fixation counting if ray leaves briefly.")]
    public float jitterBuffer = 0.25f;

    // Data Structure
    [System.Serializable]
    public class GazeDataEntry
    {
        public string Sequence;
        public string Condition;
        public string Team;
        public string Phase;
        public string TrialScript;
        public int QuestionNum;
        public string TargetType;
        public string TargetID;
        public string TargetName;
        public float FixationDuration;
        public float FixationStart; // seconds into the phase (see StartRecording's phaseTimeOffset)
        public Vector3 HitPoint;    // mean world-space point the head ray hit on the target during the fixation
    }

    private List<GazeDataEntry> _recordedData = new List<GazeDataEntry>();
    private bool _isRecording = false;

    // Current Trial Info
    private string _currentCondition = "";
    private string _currentTeam = "";
    private string _currentScript = "";
    private int _currentQuestion = 0;
    private string _currentPhase = "";
    private float _minFixation;    // fixations shorter than this are not logged (0 = log every fixation)
    private float _phaseStartTime; // Time.time at which the phase clock reads 0
    private int _phaseStartIndex;  // first entry of the current phase; CancelRecording removes from here

    // Internal State for Gaze
    private GameObject _activeTarget;
    private GameObject _pendingTarget;
    private float _dwellTimer = 0f;
    private float _jitterTimer = 0f;
    private float _currentFixationDuration = 0f;
    private float _fixationStart;
    private Vector3 _hitSum;
    private int _hitCount;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    // One row per trial: how long the participant looked around after the discussion (set by SessionController).
    private class ReviewTimeEntry
    {
        public string Condition, Team, TrialScript;
        public int QuestionNum;
        public float Seconds;
        public int BackPresses;
    }
    private readonly List<ReviewTimeEntry> _reviewTimes = new List<ReviewTimeEntry>();

    void Start()
    {
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold * 0.8f;
        }
        if (mainCamera == null) mainCamera = Camera.main;
        if (raySource == null && mainCamera != null) raySource = mainCamera.transform;
    }

    /// <param name="minFixationSeconds">Fixations shorter than this are not logged (0 = log every fixation).</param>
    /// <param name="phaseTimeOffset">Value of the phase clock (FixationStart) at this call; a phase resumed after
    /// the wizard pressed Back passes the time it had already run, so FixationStart keeps counting from there.</param>
    public void StartRecording(string condition, string scriptName, int questionNum, string phase, string team = "",
                               float minFixationSeconds = 0f, float phaseTimeOffset = 0f)
    {
        ForceEndCurrentFixation();

        _currentCondition = condition;
        _currentTeam = team;
        _currentScript = scriptName;
        _currentQuestion = questionNum;
        _currentPhase = phase;
        _minFixation = minFixationSeconds;
        _phaseStartTime = Time.time - phaseTimeOffset;
        _phaseStartIndex = _recordedData.Count;

        _isRecording = true;
        _dwellTimer = 0f;
        _jitterTimer = 0f;
        _currentFixationDuration = 0f;
        _activeTarget = null;
        _pendingTarget = null;
    }

    public void StopRecording()
    {
        ForceEndCurrentFixation();
        _isRecording = false;
    }

    /// <summary>
    /// Stops recording and throws away everything logged since the last StartRecording, including the fixation in
    /// progress. Used when the wizard presses Back during a discussion: the discussion is replayed, so its partial
    /// gaze data is not kept.
    /// </summary>
    public void CancelRecording()
    {
        if (!_isRecording) return;
        _activeTarget = null;
        _pendingTarget = null;
        _dwellTimer = 0f;
        _jitterTimer = 0f;
        _currentFixationDuration = 0f;
        int from = Mathf.Min(_phaseStartIndex, _recordedData.Count);
        _recordedData.RemoveRange(from, _recordedData.Count - from);
        _isRecording = false;
    }

    /// <summary>Sets (or, after the wizard went Back, updates) the review time of one trial.</summary>
    public void SetReviewTime(string condition, string scriptName, int questionNum, string team, float seconds, int backPresses)
    {
        var entry = _reviewTimes.Find(e => e.TrialScript == scriptName && e.QuestionNum == questionNum);
        if (entry == null) _reviewTimes.Add(entry = new ReviewTimeEntry { TrialScript = scriptName, QuestionNum = questionNum });
        entry.Condition = condition;
        entry.Team = team;
        entry.Seconds = seconds;
        entry.BackPresses = backPresses;
    }

    void Update()
    {
        if (!_isRecording || raySource == null) return;

        UpdateRaycast();
    }

    private float PhaseTime => Time.time - _phaseStartTime;

    private void UpdateRaycast()
    {
        GameObject hitTarget = null;
        Vector3 hitPoint = Vector3.zero;

        if (TryGetGazeHit(out RaycastHit hit))
        {
            hitPoint = hit.point;
            if (hit.collider.CompareTag(wallTag))
            {
                hitTarget = hit.collider.gameObject;
            }
            else
            {
                var agentComp = hit.collider.GetComponentInParent<ConversationalAgent>();
                // Anything else with a collider (the floor, a table, ...) is logged too, as Environment.
                hitTarget = agentComp != null ? agentComp.gameObject : hit.collider.gameObject;
            }
        }

        // 1. Handle Active Target
        if (_activeTarget != null)
        {
            if (hitTarget == _activeTarget)
            {
                _jitterTimer = 0f;
                _currentFixationDuration += Time.deltaTime;
                _hitSum += hitPoint;
                _hitCount++;
            }
            else
            {
                _jitterTimer += Time.deltaTime;
                // We keep adding to duration during the jitter period,
                // but if it breaks the buffer, we stop the fixation.
                _currentFixationDuration += Time.deltaTime;

                if (_jitterTimer >= jitterBuffer)
                {
                    // Subtract the jitter time that we inaccurately added from the end
                    _currentFixationDuration -= _jitterTimer;
                    EndCurrentFixation();
                }
            }
        }

        // 2. Handle Pending Target (Saccade Logic)
        if (hitTarget != null && hitTarget != _activeTarget)
        {
            if (hitTarget == _pendingTarget)
            {
                _dwellTimer += Time.deltaTime;
                if (_dwellTimer >= saccadeThreshold)
                {
                    SwitchToTarget(hitTarget, hitPoint);
                }
            }
            else
            {
                _pendingTarget = hitTarget;
                _dwellTimer = 0f;
            }
        }
        else
        {
            _pendingTarget = null;
            _dwellTimer = 0f;
        }
    }

    /// <summary>
    /// Nearest collider on the head ray, skipping trigger colliders and anything attached to the ray source itself.
    /// The Sphere Cursor is a child of CenterEyeAnchor with a collider 0.2 m straight ahead, so a plain raycast hits
    /// it every frame and never reaches the agents or the wall.
    /// </summary>
    private bool TryGetGazeHit(out RaycastHit nearest)
    {
        nearest = default;
        bool found = false;
        var ray = new Ray(raySource.position, raySource.forward);
        int count = Physics.RaycastNonAlloc(ray, _hits, maxDistance, layerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_hits[i].collider.transform.IsChildOf(raySource)) continue;
            if (!found || _hits[i].distance < nearest.distance)
            {
                nearest = _hits[i];
                found = true;
            }
        }
        return found;
    }

    private void SwitchToTarget(GameObject target, Vector3 hitPoint)
    {
        ForceEndCurrentFixation();

        _activeTarget = target;
        _jitterTimer = 0f;
        _pendingTarget = null;
        _dwellTimer = 0f;
        // The saccade duration counts towards the fixation
        _currentFixationDuration = saccadeThreshold;
        _fixationStart = Mathf.Max(0f, PhaseTime - saccadeThreshold);
        _hitSum = hitPoint;
        _hitCount = 1;
    }

    private void EndCurrentFixation()
    {
        if (_activeTarget != null && _currentFixationDuration > 0 && _currentFixationDuration >= _minFixation)
        {
            LogFixationData(_activeTarget, _currentFixationDuration);
        }

        _activeTarget = null;
        _currentFixationDuration = 0f;
        _jitterTimer = 0f;
    }

    private void ForceEndCurrentFixation()
    {
        if (_activeTarget != null)
        {
            // If we're mid-jitter, subtract it
            if (_jitterTimer > 0 && _jitterTimer < jitterBuffer)
            {
                _currentFixationDuration -= _jitterTimer;
            }
            EndCurrentFixation();
        }
    }

    private void LogFixationData(GameObject target, float duration)
    {
        string tType = "Environment";
        string tID = target.name;
        string tName = target.name;

        var agt = target.GetComponent<ConversationalAgent>();
        if (agt == null) agt = target.GetComponentInParent<ConversationalAgent>();
        if (agt == null) agt = target.GetComponentInChildren<ConversationalAgent>();

        if (agt != null || target.CompareTag(agentTag))
        {
            tType = "Avatar";

            if (agt != null)
            {
                tID = agt.agentName; // agent_1, etc.
                tName = agt.displayName; // Tony, etc.
            }
            else
            {
                // Fallback basic parsing
                Match m = Regex.Match(target.name, @"\d+");
                if (m.Success) tID = $"agent_{m.Value}";
            }
        }
        else if (target.CompareTag(wallTag))
        {
            tType = "Wall";
            tID = "Wall";
            tName = "Group Augmentation Wall";
        }

        GazeDataEntry entry = new GazeDataEntry()
        {
            Sequence = "",
            Condition = _currentCondition,
            Team = _currentTeam,
            Phase = _currentPhase,
            TrialScript = _currentScript,
            QuestionNum = _currentQuestion,
            TargetType = tType,
            TargetID = tID,
            TargetName = tName,
            FixationDuration = duration,
            FixationStart = _fixationStart,
            HitPoint = _hitCount > 0 ? _hitSum / _hitCount : Vector3.zero
        };

        _recordedData.Add(entry);
        Debug.Log($"[GazeDataRecorder] Logged fixation on {tName} for {duration:F2}s (Phase: {_currentPhase})");
    }

    // The column layout is defined ONCE here: the header is joined from CsvColumnNames and every row
    // comes from CsvRow in the same order. If you add a column, change both together - they are
    // adjacent precisely so they cannot drift apart unnoticed.
    private static readonly string[] CsvColumnNames =
    {
        "ParticipantID", "Sequence", "Trial", "Condition", "Team", "TrialScript", "QuestionNum",
        "Phase", "TargetType", "TargetID", "TargetName", "FixationDuration",
        "FixationStart", "HitX", "HitY", "HitZ"
    };

    private static string CsvHeader => string.Join(",", CsvColumnNames);

    private static string[] CsvRow(string participantID, string seq, int trial, GazeDataEntry e) => new[]
    {
        participantID, seq, trial.ToString(), e.Condition, e.Team, e.TrialScript, e.QuestionNum.ToString(),
        e.Phase, e.TargetType, e.TargetID, e.TargetName,
        F3(e.FixationDuration), F3(e.FixationStart), F3(e.HitPoint.x), F3(e.HitPoint.y), F3(e.HitPoint.z)
    };

    // One row per question, written next to its gaze CSV as <same name>_ReviewTime.csv.
    private static readonly string[] ReviewCsvColumnNames =
    {
        "ParticipantID", "Sequence", "Trial", "Condition", "Team", "TrialScript", "QuestionNum", "ReviewDuration", "BackPresses"
    };

    // Invariant culture: a comma-decimal locale would otherwise write "0,300" and split the column.
    private static string F3(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Every session writes into its own folder in here; MasterLevelController checks it for an ID's earlier sessions.</summary>
    public static string LogsFolder => Path.Combine(Application.dataPath, "Logs");

    /// <summary>
    /// Writes one question's gaze rows to <paramref name="folder"/>/<paramref name="baseName"/>_Gaze.csv and its
    /// review time to <paramref name="baseName"/>_ReviewTime.csv, replacing files of the same name. The rows stay in
    /// memory, so a question written again after the wizard went Back into its review gets all its rows.
    /// </summary>
    public void WriteQuestionCSVs(string folder, string baseName, string participantID, string sequence, int trial,
                                  int questionNum)
    {
        ForceEndCurrentFixation();
        var rows = _recordedData.FindAll(e => e.QuestionNum == questionNum);
        var review = _reviewTimes.Find(r => r.QuestionNum == questionNum);
        if (rows.Count == 0 && review == null) return;

        Directory.CreateDirectory(folder);

        if (rows.Count > 0)
        {
            string path = Path.Combine(folder, baseName + "_Gaze.csv");
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(CsvHeader);
                foreach (var entry in rows)
                    writer.WriteLine(string.Join(",", CsvRow(participantID, sequence, trial, entry)));
            }
            Debug.Log($"[GazeDataRecorder] Wrote {rows.Count} fixations to {path}");
        }

        if (review != null)
        {
            string path = Path.Combine(folder, baseName + "_ReviewTime.csv");
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(string.Join(",", ReviewCsvColumnNames));
                writer.WriteLine(string.Join(",", new[]
                {
                    participantID, sequence, trial.ToString(), review.Condition, review.Team, review.TrialScript,
                    review.QuestionNum.ToString(), F3(review.Seconds), review.BackPresses.ToString()
                }));
            }
            Debug.Log($"[GazeDataRecorder] Wrote the review time to {path}");
        }
    }

    void OnValidate()
    {
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold - 0.05f;
        }
    }
}
