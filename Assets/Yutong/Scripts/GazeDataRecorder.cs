using UnityEngine;
using System.Collections.Generic;
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
        public string Phase;
        public string TrialScript;
        public int QuestionNum;
        public string TargetType;
        public string TargetID;
        public string TargetName;
        public float FixationDuration;
    }

    private List<GazeDataEntry> _recordedData = new List<GazeDataEntry>();
    private bool _isRecording = false;

    // Current Trial Info
    private string _currentCondition = "";
    private string _currentScript = "";
    private int _currentQuestion = 0;
    private string _currentPhase = "";

    // Internal State for Gaze
    private GameObject _activeTarget;
    private GameObject _pendingTarget;
    private float _dwellTimer = 0f;
    private float _jitterTimer = 0f;
    private float _currentFixationDuration = 0f;

    void Start()
    {
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold * 0.8f;
        }
        if (mainCamera == null) mainCamera = Camera.main;
        if (raySource == null && mainCamera != null) raySource = mainCamera.transform;
    }

    public void StartRecording(string condition, string scriptName, int questionNum, string phase)
    {
        ForceEndCurrentFixation();

        _currentCondition = condition;
        _currentScript = scriptName;
        _currentQuestion = questionNum;
        _currentPhase = phase;
        
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

    void Update()
    {
        if (!_isRecording || raySource == null) return;

        UpdateRaycast();
    }

    private void UpdateRaycast()
    {
        Ray ray = new Ray(raySource.position, raySource.forward);
        RaycastHit hit;
        GameObject hitTarget = null;

        if (Physics.Raycast(ray, out hit, maxDistance, layerMask))
        {
            if (hit.collider.CompareTag(agentTag) || hit.collider.CompareTag(wallTag))
            {
                hitTarget = hit.collider.gameObject;
            }
        }

        // 1. Handle Active Target
        if (_activeTarget != null)
        {
            if (hitTarget == _activeTarget)
            {
                _jitterTimer = 0f;
                _currentFixationDuration += Time.deltaTime;
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
                    SwitchToTarget(hitTarget);
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

    private void SwitchToTarget(GameObject target)
    {
        ForceEndCurrentFixation();
        
        _activeTarget = target;
        _jitterTimer = 0f;
        _pendingTarget = null;
        _dwellTimer = 0f;
        // The saccade duration counts towards the fixation
        _currentFixationDuration = saccadeThreshold; 
    }

    private void EndCurrentFixation()
    {
        if (_activeTarget != null && _currentFixationDuration > 0)
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
        string tType = "Unknown";
        string tID = target.name;
        string tName = target.name;

        if (target.CompareTag(agentTag))
        {
            tType = "Avatar";
            // Check ConversationalAgent component
            var agt = target.GetComponentInParent<ConversationalAgent>();
            if (agt == null) agt = target.GetComponentInChildren<ConversationalAgent>();

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
            Sequence = "", // Will be filled perfectly during WriteDataToCSV
            Condition = _currentCondition,
            Phase = _currentPhase,
            TrialScript = _currentScript,
            QuestionNum = _currentQuestion,
            TargetType = tType,
            TargetID = tID,
            TargetName = tName,
            FixationDuration = duration
        };

        _recordedData.Add(entry);
        Debug.Log($"[GazeDataRecorder] Logged fixation on {tName} for {duration:F2}s (Phase: {_currentPhase})");
    }

    public void WriteDataToCSV(string participantID, string sequenceName)
    {
        ForceEndCurrentFixation();
        if (_recordedData.Count == 0) return;

        string folderPath = Path.Combine(Application.dataPath, "Logs");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string fileName = "";
        if (!string.IsNullOrEmpty(sequenceName))
        {
            fileName = $"{sequenceName}_{participantID}.csv";
        }
        else
        {
            fileName = $"{_currentCondition}_{participantID}.csv";
        }

        string fullPath = Path.Combine(folderPath, fileName);
        bool fileExists = File.Exists(fullPath);

        // Build CSV content
        using (StreamWriter writer = new StreamWriter(fullPath, true)) // Append mode ensures we don't overwrite if multiple writes happen
        {
            if (!fileExists)
            {
                // Write Header
                writer.WriteLine("ParticipantID,Sequence,Condition,TrialScript,QuestionNum,Phase,TargetType,TargetID,TargetName,FixationDuration");
            }

            foreach (var entry in _recordedData)
            {
                // Fallback sequence if needed
                string seq = string.IsNullOrEmpty(sequenceName) ? "Standalone" : sequenceName;
                
                string line = $"{participantID},{seq},{entry.Condition},{entry.TrialScript},{entry.QuestionNum},{entry.Phase},{entry.TargetType},{entry.TargetID},{entry.TargetName},{entry.FixationDuration:F3}";
                writer.WriteLine(line);
            }
        }

        Debug.Log($"[GazeDataRecorder] Wrote {_recordedData.Count} records to {fullPath}");
        _recordedData.Clear(); // Clear after writing to avoid duplicates if called multiple times
    }

    void OnValidate()
    {
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold - 0.05f;
        }
    }
}
