using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class MasterLevelController : MonoBehaviour
{
    public static MasterLevelController Instance { get; private set; }

    // Each session runs two of the four conditions: one with augmentation (i) and one without (n), one on the grid
    // (AA) and one at the table (AT), so neither factor changes with the session. Across the four sequences every
    // condition is in every position once, and each pair comes first in two of them.
    public enum StudySequence
    {
        Sequence_1_nAT_iAA_nAA_iAT,
        Sequence_2_iAA_nAT_iAT_nAA,
        Sequence_3_nAA_iAT_nAT_iAA,
        Sequence_4_iAT_nAA_iAA_nAT
    }

    [Serializable]
    public class AvatarTeam
    {
        public string teamName;
        [Tooltip("The condition this team always plays, in every sequence. Each condition needs exactly one team.")]
        public SessionController.StudyCondition condition;
        [Tooltip("Exactly 4 prefabs (2M + 2F). List order is the fixed casting: element 0 -> agent_1, 1 -> agent_2, etc.")]
        public List<GameObject> members = new();
    }

    // Entered on the experimenter panel when Play starts (see OnGUI), never in the Inspector, so a participant's
    // ID and sequence are not saved into the scene and cannot carry over to the next participant.
    public string participantID { get; private set; } = "";
    public StudySequence studySequence { get; private set; } = StudySequence.Sequence_1_nAT_iAA_nAA_iAT;

    /// <summary>Which visit this is: 1 runs the sequence's first two conditions, 2 its last two.</summary>
    public int sessionNumber { get; private set; } = 1;

    /// <summary>Conditions run in one session; each participant comes for two sessions.</summary>
    public const int ConditionsPerSession = 2;

    // Positions in the sequence (0-3) this session runs: from SessionStart up to, not including, SessionEnd.
    private int SessionStart => (sessionNumber - 1) * ConditionsPerSession;
    private int SessionEnd => SessionStart + ConditionsPerSession;

    /// <summary>True while the last condition of this session is running.</summary>
    public bool IsLastConditionOfSession => _currentConditionIndex == SessionEnd - 1;
    
    /// <summary>
    /// Question (1-4) the running condition starts at: 1, except in the first condition of a session resumed after a
    /// crash, which starts at the question chosen on the panel.
    /// </summary>
    public int StartQuestion { get; private set; } = 1;

    /// <summary>"P07_Seq3_S1": participant, sequence and session number, the start of every CSV name of this session.</summary>
    public string SessionName => $"{participantID}_Seq{(int)studySequence + 1}_S{sessionNumber}";

    /// <summary>This session's CSV folder, named when Start is pressed: Logs/P07_Seq3_S1_2026-10-13_14-05-22.</summary>
    public string SessionLogFolder { get; private set; } = "";

    /// <summary>False only for pilot runs outside the lab: the room then follows the headset as before (Floor Level).</summary>
    public bool AnchorToRoom { get; private set; } = true;
    
    [Header("Participants")]
    [Tooltip("Valid participant numbers are 1 to this. The number alone decides the sequence: " +
             "P01, P05, P09 -> Sequence 1; P02, P06, P10 -> 2; P03, P07, P11 -> 3; P04, P08, P12 -> 4.")]
    [Min(1)] public int participantCount = 12;

    [Header("Avatar Teams (fixed per condition)")]
    [Tooltip("Each team always plays its own condition, wherever that condition falls in the sequence. " +
             "Condition order rotates across the four sequences, so each team appears once in each position across sequences.")]
    public List<AvatarTeam> avatarTeams = new();

    [Header("Scene Names")]
    public string iAA_SceneName = "Testbed iAA";
    public string iAT_SceneName = "Testbed iAT";
    public string nAA_SceneName = "Testbed nAA";
    public string nAT_SceneName = "Testbed nAT";

    // Internal State
    private int _currentConditionIndex = 0;
    private bool _studyStarted; // set by the panel's Start button

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        ValidateAvatarTeams();
        // The first condition loads when the experimenter presses Start on the panel (BeginStudy).
    }

    // Set once by ValidateAvatarTeams; team lookup is all-or-nothing so a single bad team can never
    // produce a mixed team/random session (which would break the each-avatar-seen-once guarantee).
    private bool _avatarTeamsValid;

    private void ValidateAvatarTeams()
    {
        _avatarTeamsValid = false;

        if (avatarTeams == null || avatarTeams.Count == 0)
        {
            Debug.LogWarning("[MasterLevelController] No avatar teams assigned - SessionController will fall back to random avatar selection. " +
                             "Use Tools/iXR/Assign Proposed Avatar Teams to populate them.");
            return;
        }

        bool valid = avatarTeams.Count == 4;
        if (!valid)
            Debug.LogError($"[MasterLevelController] Expected 4 avatar teams, found {avatarTeams.Count}.");

        var seen = new HashSet<GameObject>();
        for (int i = 0; i < avatarTeams.Count; i++)
        {
            var team = avatarTeams[i];
            if (team == null || team.members == null || team.members.Count != 4 || team.members.Any(m => m == null))
            {
                Debug.LogError($"[MasterLevelController] Avatar team {i} ('{team?.teamName}') must have exactly 4 non-null members.");
                valid = false;
                continue;
            }
            foreach (var m in team.members)
            {
                if (!seen.Add(m))
                {
                    Debug.LogError($"[MasterLevelController] Avatar '{m.name}' appears in more than one team - each avatar must belong to exactly one team.");
                    valid = false;
                }
            }
        }

        foreach (SessionController.StudyCondition c in Enum.GetValues(typeof(SessionController.StudyCondition)))
        {
            int n = avatarTeams.Count(t => t != null && t.condition == c);
            if (n != 1)
            {
                Debug.LogError($"[MasterLevelController] Condition {c} has {n} avatar teams; each condition needs exactly one.");
                valid = false;
            }
        }

        _avatarTeamsValid = valid;
        if (!valid)
            Debug.LogError("[MasterLevelController] Avatar team configuration is INVALID - team lookup is disabled for ALL conditions " +
                           "and SessionController will fall back to random selection. Fix the teams before running a study.");
    }

    /// <summary>The team that always plays this condition; null if the teams are not set up correctly.</summary>
    private AvatarTeam GetTeam(SessionController.StudyCondition condition)
    {
        return _avatarTeamsValid ? avatarTeams.First(t => t.condition == condition) : null;
    }

    /// <summary>
    /// Members of the running condition's team, in casting order (member i plays agent_{i+1}).
    /// Teams are tied to conditions, so every participant sees the same avatars in a given condition.
    /// Returns null if teams are not (fully) configured, so callers can fall back.
    /// </summary>
    public List<GameObject> GetTeamForCurrentCondition()
    {
        if (_currentConditionIndex < 0 || _currentConditionIndex >= 4) return null;
        return GetTeam(GetConditionForIndex(_currentConditionIndex))?.members;
    }

    public string GetCurrentTeamName()
    {
        if (_currentConditionIndex < 0 || _currentConditionIndex >= 4) return "";
        return GetTeam(GetConditionForIndex(_currentConditionIndex))?.teamName ?? "";
    }

    public int CurrentConditionIndex => _currentConditionIndex;

    /// <summary>
    /// The condition this master intends for the currently loaded scene. SessionController verifies
    /// its own serialized studyCondition against this so a wrong scene name or wrong enum fails loudly
    /// instead of silently running (and logging) the wrong condition.
    /// </summary>
    public SessionController.StudyCondition GetExpectedCondition()
    {
        return GetConditionForIndex(Mathf.Clamp(_currentConditionIndex, 0, 3));
    }

    private void Update()
    {
        // T only counts once the study is running (until then it may be typed into the ID field) and
        // until the last condition of this session is done.
        if (!_studyStarted || _currentConditionIndex >= SessionEnd) return;

        // Jump to next condition if T is pressed and current condition is fully finished
        if (Input.GetKeyDown(KeyCode.T))
        {
            var session = SessionController.Instance;
            if (session != null && session.isExperimentFinished)
            {
                // First T: save this condition's CSVs and clear the room (avatars, table or grid gone, end message
                // on the wall). Back is disabled from here on.
                if (!session.IsCleared)
                {
                    session.SaveData();
                    session.ClearRoom();
                    return;
                }

                // Second T: load the next condition of this session.
                _currentConditionIndex++;
                if (_currentConditionIndex < SessionEnd)
                {
                    StartQuestion = 1; // only the first condition of a resumed session starts part-way
                    Debug.Log($"[MasterLevelController] Loading condition {_currentConditionIndex + 1}/4...");
                    LoadConditionScene(_currentConditionIndex);
                }
                else
                {
                    Debug.Log($"[MasterLevelController] Session {sessionNumber} finished.");
                }
            }
            else
            {
                Debug.LogWarning("[MasterLevelController] Cannot transition yet. Current session is not finished.");
            }
        }
    }

    public string GetCurrentSequenceName()
    {
        return studySequence.ToString();
    }

    private void LoadConditionScene(int index)
    {
        SessionController.StudyCondition targetCondition = GetConditionForIndex(index);
        string sceneToLoad = GetSceneName(targetCondition);

        if (string.IsNullOrEmpty(sceneToLoad))
        {
            Debug.LogError($"[MasterLevelController] Scene name for {targetCondition} is empty!");
            return;
        }

        // Free all cached TTS audio from the previous condition (scripts differ per block, so
        // nothing carries over; without this the static cache pins 100+ MB of PCM by session end).
        CrossPlatformTTS.ClearCache();

        Debug.Log($"[MasterLevelController] -> Loading target condition {targetCondition} at scene '{sceneToLoad}'");
        SceneManager.LoadScene(sceneToLoad);
    }

    private SessionController.StudyCondition GetConditionForIndex(int index)
    {
        return ConditionOrder(studySequence)[index];
    }

    /// <summary>The four conditions of a sequence, in the order they are run.</summary>
    private static SessionController.StudyCondition[] ConditionOrder(StudySequence sequence)
    {
        return sequence switch {
            StudySequence.Sequence_1_nAT_iAA_nAA_iAT => new[] { SessionController.StudyCondition.nAT, SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT },
            StudySequence.Sequence_2_iAA_nAT_iAT_nAA => new[] { SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAT, SessionController.StudyCondition.iAT, SessionController.StudyCondition.nAA },
            StudySequence.Sequence_3_nAA_iAT_nAT_iAA => new[] { SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT, SessionController.StudyCondition.nAT, SessionController.StudyCondition.iAA },
            StudySequence.Sequence_4_iAT_nAA_iAA_nAT => new[] { SessionController.StudyCondition.iAT, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAT },
            _ => new[] { SessionController.StudyCondition.nAT, SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT }
        };
    }

    private string GetSceneName(SessionController.StudyCondition cond)
    {
        return cond switch {
            SessionController.StudyCondition.iAA => iAA_SceneName,
            SessionController.StudyCondition.iAT => iAT_SceneName,
            SessionController.StudyCondition.nAA => nAA_SceneName,
            SessionController.StudyCondition.nAT => nAT_SceneName,
            _ => ""
        };
    }

    // --- Experimenter panel ---
    // Drawn with IMGUI on the PC monitor (the Game view in the Editor); it never appears in the headset.
    // Before the study starts the experimenter only types the participant number; the sequence, condition order
    // and teams all follow from it. Afterwards the panel shows what is running.

    private string _idInput = "";
    private bool _isPilot;
    private bool _testAnywhere; // "Test outside the lab" ticked; only offered on pilot runs
    private int _sessionInput; // 0 until the experimenter picks Session 1 or 2
    private bool _isResume;      // "Resume after a crash" ticked
    private int _resumeCondition; // 1 or 2 within the session; 0 until picked
    private int _resumeQuestion;  // 1-4; 0 until picked
    private readonly Dictionary<string, int> _logCounts = new(); // folder pattern -> folders found

    /// <summary>
    /// Called by the panel's Start button: fixes the participant, sequence and session, then loads the session's first
    /// condition. After a crash the session starts at <paramref name="startCondition"/> (1 or 2 within the session) and
    /// <paramref name="startQuestion"/>, in a new folder whose name ends in _resumed-C<c>Q<q>.
    /// </summary>
    private void BeginStudy(string id, StudySequence sequence, int session, int startCondition, int startQuestion)
    {
        participantID = id;
        studySequence = sequence;
        sessionNumber = session;
        _currentConditionIndex = SessionStart + startCondition - 1;
        StartQuestion = startQuestion;
        AnchorToRoom = !(_isPilot && _testAnywhere);
        bool resumed = startCondition > 1 || startQuestion > 1;
        SessionLogFolder = Path.Combine(GazeDataRecorder.LogsFolder, $"{SessionName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}" +
                                        (resumed ? $"_resumed-C{startCondition}Q{startQuestion}" : ""));
        _studyStarted = true;
        Debug.Log($"[MasterLevelController] Study started: participant '{participantID}', {studySequence}, session {sessionNumber}" +
                  (resumed ? $", resumed at condition {startCondition} question {startQuestion}" : "") +
                  (AnchorToRoom ? "." : ", room NOT anchored (testing outside the lab)."));
        LoadConditionScene(_currentConditionIndex);
    }

    private void OnGUI()
    {
        // IMGUI's default skin is tiny on large monitors; scale it with the window height.
        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (_studyStarted) DrawStatus();
        else DrawSetupPanel(Screen.width / scale, Screen.height / scale);
    }

    private void DrawSetupPanel(float width, float height)
    {
        // Everything shown is worked out from the state at the start of this event and edits are applied at
        // the end, so IMGUI's layout and repaint passes always see the same set of controls.
        string input = _idInput.Trim();
        int? number = ParseParticipantNumber(input);
        string problem = CheckParticipant(input, number);
        string sessionId = "";
        string plan = "";
        StudySequence sequence = default;
        int earlierLogs = 0;
        bool noSession1Logs = false;
        string resumeAt = "";
        if (problem == null)
        {
            sessionId = (_isPilot ? "PILOT_" : "") + ParticipantId(number.Value);
            sequence = SequenceForParticipant(number.Value);
            plan = DescribeOrder(sequence, _sessionInput);
            earlierLogs = CountEarlierLogs(sessionId, _sessionInput);
            noSession1Logs = _sessionInput == 2 && CountEarlierLogs(sessionId, 1) == 0;
            if (_isResume)
            {
                var condition = ConditionOrder(sequence)[(_sessionInput - 1) * ConditionsPerSession + _resumeCondition - 1];
                resumeAt = $"{condition}, question {_resumeQuestion}";
            }
        }

        GUILayout.BeginArea(new Rect((width - 460f) / 2f, 30f, 460f, height - 60f), GUI.skin.box);
        GUILayout.Label("<b>iXR Study - Session Setup</b>", RichLabel());
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Participant", GUILayout.Width(100));
        string newInput = GUILayout.TextField(_idInput, 8);
        GUILayout.EndHorizontal();
        // No session is selected at first, so the experimenter always picks one.
        int newSession = GUILayout.Toolbar(_sessionInput - 1, new[] { "Session 1", "Session 2" }) + 1;
        bool newPilot = GUILayout.Toggle(_isPilot, " Pilot run (saved as PILOT_<ID>)");
        // Pilot runs only: test somewhere other than the lab; the room then follows the headset as before.
        bool newAnywhere = _isPilot && GUILayout.Toggle(_testAnywhere, " Test outside the lab (room not anchored)");
        // After a crash: start the session at the condition and question that was running, in a new log folder.
        bool newResume = GUILayout.Toggle(_isResume, " Resume after a crash");
        int newResumeCondition = _resumeCondition;
        int newResumeQuestion = _resumeQuestion;
        if (_isResume)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Condition", GUILayout.Width(100));
            newResumeCondition = GUILayout.Toolbar(_resumeCondition - 1, new[] { "1st of session", "2nd of session" }) + 1;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Question", GUILayout.Width(100));
            newResumeQuestion = GUILayout.Toolbar(_resumeQuestion - 1, new[] { "1", "2", "3", "4" }) + 1;
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(8);

        if (problem != null)
        {
            GUILayout.Label($"<color=#ff7070>{problem}</color>", RichLabel());
        }
        else
        {
            GUILayout.Label($"<b>{sessionId}</b>  ->  {sequence.ToString().Replace('_', ' ')}", RichLabel());
            GUILayout.Label(plan, RichLabel());
            if (_isResume)
            {
                GUILayout.Label($"<b>Resumes at {resumeAt}</b>, in a new log folder. The earlier folder keeps every " +
                                "question whose review ended before the crash.", RichLabel());
                if (earlierLogs == 0)
                    GUILayout.Label($"<color=#ffcc40>Logs has no Session {_sessionInput} folder for {sessionId}, so there is " +
                                    "nothing to resume. Check the participant and the session.</color>", RichLabel());
            }
            else if (earlierLogs > 0)
                GUILayout.Label($"<color=#ffcc40>Logs already has {earlierLogs} Session {_sessionInput} folder(s) for {sessionId}. " +
                                "Make sure this session has not been run already.</color>", RichLabel());
            if (noSession1Logs)
                GUILayout.Label($"<color=#ffcc40>Logs has no Session 1 folder for {sessionId}. " +
                                "Make sure this participant has done Session 1.</color>", RichLabel());
        }

        GUILayout.FlexibleSpace();
        GUI.enabled = problem == null;
        bool start = GUILayout.Button(problem == null ? $"{(_isResume ? "Resume" : "Start")} {sessionId}, Session {_sessionInput}" : "Start",
                                      GUILayout.Height(40));
        GUI.enabled = true;
        GUILayout.EndArea();

        _idInput = newInput;
        _sessionInput = newSession;
        _isPilot = newPilot;
        _isResume = newResume;
        _resumeCondition = newResumeCondition;
        _resumeQuestion = newResumeQuestion;
        _testAnywhere = newAnywhere;
        if (start) BeginStudy(sessionId, sequence, _sessionInput, _isResume ? _resumeCondition : 1, _isResume ? _resumeQuestion : 1);
    }

    private void DrawStatus()
    {
        string condition;
        if (_currentConditionIndex >= SessionEnd)
        {
            condition = $"Session {sessionNumber} complete.";
        }
        else
        {
            string team = GetCurrentTeamName();
            int inSession = _currentConditionIndex - SessionStart + 1;
            condition = $"Condition {inSession}/{ConditionsPerSession} (position {_currentConditionIndex + 1}/4): " +
                        $"{GetConditionForIndex(_currentConditionIndex)}, " + (team != "" ? team : "random avatars");
            var session = SessionController.Instance;
            if (session != null && session.isExperimentFinished)
                condition += !session.IsCleared ? " - finished, press T to clear the room"
                           : !IsLastConditionOfSession ? " - room cleared, press T for the next condition"
                                                       : " - room cleared, press T to end the session";
        }

        // What the trial is doing, what Space does next, and the Back button; taller for each warning it shows.
        var current = SessionController.Instance;
        GUILayout.BeginArea(new Rect(10f, 10f, 560f, 160f + (current != null ? current.WarningsHeight : 0f)), GUI.skin.box);
        GUILayout.Label($"Participant {participantID}  |  {studySequence}  |  Session {sessionNumber}");
        GUILayout.Label(condition);
        if (current != null && _currentConditionIndex < SessionEnd) current.DrawWizardPanel();
        GUILayout.EndArea();
    }

    /// <summary>Why a session cannot start with this ID or session, or null if it can.</summary>
    private string CheckParticipant(string input, int? number)
    {
        if (input.Length == 0)
            return $"Enter the participant number (1-{participantCount}), e.g. 7 or P07.";
        if (number == null || number < 1 || number > participantCount)
            return $"Participant IDs are {ParticipantId(1)} to {ParticipantId(participantCount)}. Type the number (e.g. 7) or the ID (P07).";
        if (_sessionInput == 0)
            return "Choose Session 1 or Session 2.";
        if (_isResume && (_resumeCondition == 0 || _resumeQuestion == 0))
            return "Choose the condition and question to resume from.";
        if (!_isPilot && !_avatarTeamsValid)
            return "The avatar teams are not set up correctly (see the Console), so the avatars would be random. " +
                   "Fix the teams, or tick Pilot run.";
        return null;
    }

    /// <summary>
    /// Folders already in the Logs folder for this ID and session; every session writes into its own folder named
    /// "<ID>_Seq<n>_S<session>_<start time>" (see SessionLogFolder). Each pattern is counted once per Play.
    /// </summary>
    private int CountEarlierLogs(string id, int session)
    {
        string pattern = $"{id}_Seq*_S{session}_*";
        if (_logCounts.TryGetValue(pattern, out int count)) return count;
        count = Directory.Exists(GazeDataRecorder.LogsFolder)
            ? Directory.GetDirectories(GazeDataRecorder.LogsFolder, pattern).Length
            : 0;
        _logCounts[pattern] = count;
        return count;
    }

    /// <summary>Participant number from "7", "07", "P7" or "p07"; null for anything else.</summary>
    private static int? ParseParticipantNumber(string input)
    {
        var m = Regex.Match(input, @"^[Pp]?(\d{1,3})$");
        return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
    }

    /// <summary>The one spelling used in file names and CSVs: 7 -> "P07".</summary>
    private static string ParticipantId(int number) => $"P{number:00}";

    /// <summary>
    /// Participant n runs Sequence ((n - 1) % 4) + 1, so participants 1, 5, 9 get Sequence 1; 2, 6, 10 get 2;
    /// 3, 7, 11 get 3; and 4, 8, 12 get 4. Each sequence is run by the same number of participants.
    /// </summary>
    private static StudySequence SequenceForParticipant(int number) => (StudySequence)((number - 1) % 4);

    /// <summary>
    /// The sequence's conditions with their teams, one line per session, the chosen session in bold, e.g.
    /// "<b>Session 1: nAT (Team A) -> iAA (Team D)</b>" and "Session 2: nAA (Team B) -> iAT (Team C)".
    /// </summary>
    private string DescribeOrder(StudySequence sequence, int session)
    {
        var order = ConditionOrder(sequence).Select(condition =>
            _avatarTeamsValid ? $"{condition} ({GetTeam(condition).teamName})" : condition.ToString()).ToList();
        var lines = new List<string>();
        for (int s = 1; s <= 2; s++)
        {
            string line = $"Session {s}: " + string.Join("  ->  ", order.Skip((s - 1) * ConditionsPerSession).Take(ConditionsPerSession));
            lines.Add(s == session ? $"<b>{line}</b>" : line);
        }
        return string.Join("\n", lines);
    }

    private static GUIStyle RichLabel() => new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
}
