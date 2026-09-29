using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class SessionController : MonoBehaviour
{
    public static SessionController Instance { get; private set; }

    public enum SessionPhase { Setup, GridMove, Discussion, AR_UI }
    public SessionPhase CurrentPhase { get; private set; }

    public enum StudyCondition { iAA, iAT, nAA, nAT }
    public enum QuestionSelectionMode { Random, Manual, ConditionScript }

    // ConditionScript mode: each condition has ONE compiled manuscript named by its abbreviation
    // (iAA.json, iAT.json, nAA.json, nAT.json) under Assets/Resources/<this folder>/, holding
    // question_1..4_Data in final asking order (the Table 8 script/question mixing pre-applied).
    public const string ManuscriptResourceFolder = "Manuscripts";

    // Stance carried by each grid row, index = row number (0 = near row at z=0, 4 = far row).
    // Both the floor labels AND the seating destinations derive from this one array, so they can
    // never disagree - to reorder the grid, reorder here only. Must match the stance strings in
    // the manuscripts and the row order depicted in the pre-rendered augmentation charts.
    private static readonly string[] RowRanks = { "Very Good", "Good", "Neutral", "Bad", "Very Bad" };
    
    [Serializable]
    public struct ManualTrialSelection
    {
        public int scriptIndex; // index in scriptFiles
        public int questionIndex; // 1-based (1-4)
    }
    
    [Header("Study State")]
    public string participantID = "P1";
    public GazeDataRecorder gazeRecorder;
    public StudyCondition studyCondition = StudyCondition.iAA;
    public QuestionSelectionMode selectionMode = QuestionSelectionMode.Random;
    public List<ManualTrialSelection> manualTrials = new();

    [Header("Configuration")]
    public List<TextAsset> scriptFiles; // Assign Script_1 to Script_4
    public List<GameObject> availableMaleAvatars;
    public List<GameObject> availableFemaleAvatars;

    [Header("Scene References")]
    public GridGenerator gridGenerator;
    public ConversationalAgentsManager manager;
   

    [Header("Settings")]
    public float moveDuration = 2f;
    public float lineUpSpacing = 1.2f;
    [Tooltip("Enable to skip lengthy movement and discussion phases")]
    public bool isTestMode = false;

    [Header("Introduction Phase")]
    [Tooltip("If enabled, the avatars introduce themselves once per condition, at the start of the first trial - " +
             "identical across all four conditions and BEFORE any gaze recording starts.")]
    public bool enableIntroductionPhase = true;
    [Tooltip("Line spoken by each avatar during the introduction. {0} = avatar display name.")]
    public string introductionLineFormat = "Hi, I'm {0}. Nice to meet you.";
    
    [Header("Cursor Settings")]
    public Renderer sphereCursorRenderer;
    public Color conversationEndColor = Color.green;
    private Color _originalCursorColor = Color.white;
    private bool _hasSavedCursorColor = false;
    
    [Header("iAT Settings")]
    public Transform[] agentOrigins = new Transform[4];

    [Header("Augmentation UI")]
    //public Text wallQuestionText; // Assign the text on the wall
    public TextMeshProUGUI wallQuestionText;
    public GameObject augmentationPanel;
    public TextMeshProUGUI suggestionsText;
    public List<TextMeshProUGUI> themesTexts;
    public Image speakingSumImage;
    public Image grpMoveImage;
    public Image grpSimMatImage;
    public TextMeshProUGUI turnTakingText;
    public TextMeshProUGUI dissonantOpinionsText;
    public AgentAugmentationInteracter interacter;

    // Internal State
    public bool isExperimentFinished { get; private set; } = false;
    private Dictionary<string, GameObject> _agentMapping = new(); // "agent_1" -> Prefab
    private string _currentTeamName = ""; // Avatar team used in this condition (for logs/CSV)
    private List<ConversationalAgent> _currentAgents = new(); // Spawned instances
    private List<(TextAsset script, int questionIndex)> _trialSequence = new();
    private int _currentTrialIndex = 0;

    private QuestionData _currentQuestionData;

    void Awake()
    {
        Instance = this;
        if (gridGenerator == null) gridGenerator = FindObjectOfType<GridGenerator>();
        if (manager == null) manager = FindObjectOfType<ConversationalAgentsManager>();

        // Fix for "Head-Locked" scene: Force tracking origin to Floor Level
        // This ensures the virtual world stays static while you move physically
        StartCoroutine(SetTrackingOriginFloor());
    }

    private IEnumerator SetTrackingOriginFloor()
    {
        // Wait for XR to initialize
        yield return new WaitForSeconds(1.0f);
        var subsystems = new List<UnityEngine.XR.XRInputSubsystem>();
        SubsystemManager.GetInstances<UnityEngine.XR.XRInputSubsystem>(subsystems);
        foreach (var s in subsystems)
        {
            if (s.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Floor))
            {
                Debug.Log($"[SessionController] Successfully set tracking origin to Floor.");
            }
        }
    }

    void Start()
    {
        if (sphereCursorRenderer != null)
        {
            _originalCursorColor = sphereCursorRenderer.material.color;
            _hasSavedCursorColor = true;
        }
        StartCoroutine(SetupExperiment());
    }

    private void SaveGazeData()
    {
        if (gazeRecorder != null)
        {
            string pID = participantID;
            string seqName = "";

            if (MasterLevelController.Instance != null)
            {
                pID = MasterLevelController.Instance.participantID;
                seqName = MasterLevelController.Instance.GetCurrentSequenceName();
            }

            gazeRecorder.WriteDataToCSV(pID, seqName);
        }
    }

    void OnDestroy()
    {
        SaveGazeData();
    }

    void OnApplicationQuit()
    {
        SaveGazeData();
    }
    

    private IEnumerator SetupExperiment()
    {
        if (MasterLevelController.Instance != null)
        {
            // Guard 1: the loaded scene must actually be the condition the master intended -
            // scene names are free-text fields, so a paste error would otherwise run (and log)
            // the wrong condition with the wrong team, silently corrupting the counterbalance.
            var expected = MasterLevelController.Instance.GetExpectedCondition();
            if (studyCondition != expected)
            {
                Debug.LogError($"[SessionController] CONDITION MISMATCH: this scene is serialized as {studyCondition} " +
                               $"but MasterLevelController expected {expected} at trial position " +
                               $"{MasterLevelController.Instance.CurrentConditionIndex + 1}. " +
                               "Check the scene-name fields on MasterLevelController and this scene's Study Condition. Halting setup.");
                yield break;
            }

            // Guard 2: in a master-driven (real study) run, test mode skips the introduction phase
            // and allows Space-skipping of discussions. It is also surfaced on the wall text
            // (see SetupPhase1) so it cannot run unnoticed.
            if (isTestMode)
            {
                Debug.LogWarning("[SessionController] isTestMode is ON during a master-driven run - " +
                                 "introductions are skipped and discussions can be skipped with Space. " +
                                 "Turn it OFF in this scene before running a real participant!");
            }
        }

        // 1. Select Avatars
        // Primary path: deterministic team lookup from MasterLevelController.
        // The team for this condition scene is fixed by trial position (Team A = trial 1, ...),
        // and the member order IS the casting: member i plays agent_{i+1} for every participant.
        // This makes avatar identity a counterbalanced factor instead of random noise.
        var teamMembers = MasterLevelController.Instance != null
            ? MasterLevelController.Instance.GetTeamForCurrentCondition()
            : null;

        if (teamMembers != null)
        {
            _currentTeamName = MasterLevelController.Instance.GetCurrentTeamName();
            _agentMapping.Clear();
            for (int i = 0; i < 4; i++)
            {
                _agentMapping[$"agent_{i + 1}"] = teamMembers[i];
            }
            Debug.Log($"[SessionController] Avatar team '{_currentTeamName}' (trial position " +
                      $"{MasterLevelController.Instance.CurrentConditionIndex + 1}): " +
                      string.Join(", ", teamMembers.Select((m, i) => $"agent_{i + 1}={m.name}")));
        }
        else
        {
            // Fallback for standalone scene testing (no master / teams not configured): random 2M+2F draw
            _currentTeamName = "";
            if (availableMaleAvatars.Count < 2 || availableFemaleAvatars.Count < 2)
            {
                Debug.LogError("Not enough avatars assigned! Need at least 2 Male and 2 Female.");
                yield break;
            }

            var males = availableMaleAvatars.OrderBy(x => UnityEngine.Random.value).Take(2).ToList();
            var females = availableFemaleAvatars.OrderBy(x => UnityEngine.Random.value).Take(2).ToList();
            var selected = new List<GameObject>();
            selected.AddRange(males);
            selected.AddRange(females);

            // Randomly shuffle the 4 selected avatars
            selected = selected.OrderBy(x => UnityEngine.Random.value).ToList();

            Debug.LogWarning("[SessionController] No avatar team available - using RANDOM avatar draw (standalone testing only).");

            // Persistent mapping for "agent_1" to "agent_4"
            _agentMapping.Clear();
            for (int i = 0; i < 4; i++)
            {
                _agentMapping[$"agent_{i + 1}"] = selected[i];
            }
        }

        // 2. Generate Trial Sequence (4 Trials)
        _trialSequence.Clear();

        if (selectionMode == QuestionSelectionMode.ConditionScript)
        {
            // Load the per-condition compiled manuscript by the condition abbreviation itself -
            // no per-scene file wiring, so scenes can never silently point at a stale script.
            var conditionScript = Resources.Load<TextAsset>($"{ManuscriptResourceFolder}/{studyCondition}");
            if (conditionScript == null)
            {
                Debug.LogError($"[SessionController] No manuscript found at Resources/{ManuscriptResourceFolder}/{studyCondition}.json - " +
                               "add the compiled per-condition script or change Selection Mode. Halting setup.");
                yield break;
            }

            Debug.Log($"[SessionController] Loaded condition manuscript '{conditionScript.name}' for {studyCondition} (questions 1-4 in order).");
            for (int q = 1; q <= 4; q++)
            {
                _trialSequence.Add((conditionScript, q));
            }

            _currentTrialIndex = 0;
            yield return StartTrial(_currentTrialIndex);
            yield break;
        }

        // --- Legacy modes below (Manual piloting / master-generated blocks / random standalone) ---
        if (scriptFiles.Count < 4)
        {
            Debug.LogError("Need 4 Script JSON files assigned!");
            yield break;
        }

        if (selectionMode == QuestionSelectionMode.Manual)
        {
            foreach (var mt in manualTrials)
            {
                if (mt.scriptIndex >= 0 && mt.scriptIndex < scriptFiles.Count)
                {
                    _trialSequence.Add((scriptFiles[mt.scriptIndex], mt.questionIndex));
                }
            }
        }
        else if (MasterLevelController.Instance != null)
        {
            // Global study flow ensures perfect non-repeating cross-condition assignments
            var masterTrials = MasterLevelController.Instance.GetTrialsForCurrentCondition();
            foreach (var t in masterTrials)
            {
                if (t.scriptIndex >= 0 && t.scriptIndex < scriptFiles.Count)
                {
                    _trialSequence.Add((scriptFiles[t.scriptIndex], t.questionIndex));
                }
            }
        }
        else
        {
            // Fallback for standalone scene testing (random picking)
            var shuffledScripts = scriptFiles.OrderBy(x => UnityEngine.Random.value).Take(4).ToList();
            foreach (var script in shuffledScripts)
            {
                int qIndex = UnityEngine.Random.Range(1, 4 + 1); // 1 to 4
                _trialSequence.Add((script, qIndex));
            }
        }

        _currentTrialIndex = 0;
        
        // Start First Trial
        yield return StartTrial(_currentTrialIndex);
    }

    private IEnumerator StartTrial(int trialIndex)
    {
        if (trialIndex >= _trialSequence.Count)
        {
            Debug.Log("Experiment Finished");
            isExperimentFinished = true;
            SaveGazeData();
            yield break;
        }

        // Parse Data
        var trial = _trialSequence[trialIndex];
        _currentQuestionData = ParseQuestionData(trial.script, trial.questionIndex);

        if (studyCondition == StudyCondition.iAA || studyCondition == StudyCondition.nAA)
        {
            yield return StartTrial_iAA(trialIndex);
        }
        else if (studyCondition == StudyCondition.iAT || studyCondition == StudyCondition.nAT)
        {
            yield return StartTrial_iAT(trialIndex);
        }
    }

    private IEnumerator StartTrial_iAA(int trialIndex)
    {
        var trial = _trialSequence[trialIndex];

        // Phase 1: Setup (Line Up)
        CurrentPhase = SessionPhase.Setup;
        SetupPhase1();

        yield return RunIntroductionIfNeeded(trialIndex);

        // Wait for Space -> Phase 2
        yield return WaitForKey(KeyCode.Space);

        // Phase 2: Move to Grid
        CurrentPhase = SessionPhase.GridMove;
        yield return MovePhase2();

        // Wait for Space -> Phase 3
        yield return WaitForKey(KeyCode.Space);

        // Phase 3: Discussion
        CurrentPhase = SessionPhase.Discussion;
        if (gazeRecorder != null) gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Conversation", _currentTeamName);
        yield return DiscussionPhase3();
        if (studyCondition == StudyCondition.nAA) { if (gazeRecorder != null) gazeRecorder.StopRecording(); }

        // Wait for Space -> Phase 4
        yield return WaitForKey(KeyCode.Space);

        // Phase 4: AR UI
        CurrentPhase = SessionPhase.AR_UI;
        if (studyCondition == StudyCondition.iAA) { if (gazeRecorder != null) gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Augmentation", _currentTeamName); }
        yield return Phase_AR_UI(trial);

        // Wait for Space -> Next Trial
        Debug.Log($"SessionController: Trial {trialIndex} complete. Press Space for next.");
        yield return WaitForKey(KeyCode.Space);

        if (studyCondition == StudyCondition.iAA) { if (gazeRecorder != null) gazeRecorder.StopRecording(); }

        // Next
        FinishTrial();
    }

    private IEnumerator StartTrial_iAT(int trialIndex)
    {
        var trial = _trialSequence[trialIndex];

        // Phase 1: Setup (Origins)
        CurrentPhase = SessionPhase.Setup;
        SetupPhase1();

        yield return RunIntroductionIfNeeded(trialIndex);

        // Wait for Space -> Phase 2 (Discussion)
        yield return WaitForKey(KeyCode.Space);

        // Phase 2: Discussion
        CurrentPhase = SessionPhase.Discussion;
        if (gazeRecorder != null) gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Conversation", _currentTeamName);
        yield return DiscussionPhase3();
        if (studyCondition == StudyCondition.nAT) { if (gazeRecorder != null) gazeRecorder.StopRecording(); }

        // Wait for Space -> Phase 3 (AR UI)
        yield return WaitForKey(KeyCode.Space);

        // Phase 3: AR UI
        CurrentPhase = SessionPhase.AR_UI;
        if (studyCondition == StudyCondition.iAT) { if (gazeRecorder != null) gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Augmentation", _currentTeamName); }
        yield return Phase_AR_UI(trial);

        // Wait for Space -> Next Trial
        Debug.Log($"SessionController: Trial {trialIndex} complete. Press Space for next.");
        yield return WaitForKey(KeyCode.Space);

        if (studyCondition == StudyCondition.iAT) { if (gazeRecorder != null) gazeRecorder.StopRecording(); }

        // Next
        FinishTrial();
    }

    private IEnumerator Phase_AR_UI((TextAsset script, int questionIndex) trial)
    {
        if (studyCondition == StudyCondition.nAA || studyCondition == StudyCondition.nAT)
        {
            Debug.Log($"SessionController: {studyCondition} Mode - No AR UI.");
            yield return new WaitForSeconds(0.5f);
            yield break;
        }

        Debug.Log("SessionController: AR UI Enabled. Interact with agents.");
        
        // Show Augmentations
        ShowGroupAugmentations();
        
        // Phase 4: Enable Individual Augmentation Interacter
        if (interacter != null) 
        {
            interacter.PopulateAll(_currentAgents, trial.script.text, trial.questionIndex, studyCondition);
            interacter.PreSetupCanvases(_currentAgents);
            interacter.SetActive(true);
        }
        
        yield return new WaitForSeconds(0.5f); // Ensure UI/Phase state is clear
    }

    private void FinishTrial()
    {
        if (interacter != null) interacter.SetActive(false);
        _currentTrialIndex++;
        StartCoroutine(StartTrial(_currentTrialIndex));
    }

    // --- Phase Implementations ---

    /// <summary>
    /// One-time introduction gate: first trial of this condition only, identical across all four
    /// conditions, and before any gaze recording (which starts at Discussion). Kept in one place so
    /// the iAA- and iAT-family flows can never diverge procedurally.
    /// </summary>
    private IEnumerator RunIntroductionIfNeeded(int trialIndex)
    {
        if (!ShouldRunIntroduction(trialIndex)) yield break;
        yield return WaitForKey(KeyCode.Space);
        yield return IntroductionPhase();

        // Reveal the first question only after the avatars have introduced themselves.
        ShowWallQuestion();
    }

    private bool ShouldRunIntroduction(int trialIndex)
    {
        return trialIndex == 0 && enableIntroductionPhase && !isTestMode;
    }

    private void ShowWallQuestion()
    {
        if (wallQuestionText == null)
        {
            Debug.LogWarning("SessionController: Wall Question Text not assigned.");
            return;
        }
        // Test mode is surfaced on the wall itself so it can never run unnoticed in a real session.
        wallQuestionText.text = isTestMode
            ? "[TEST MODE] " + _currentQuestionData.questionText
            : _currentQuestionData.questionText;
    }

    /// <summary>
    /// Avatars introduce themselves in casting order (agent_1..agent_4). Runs once per condition,
    /// before the first discussion, so introductions never contaminate Conversation-phase gaze data.
    /// The content and structure are identical in all four conditions (a constant addition).
    /// </summary>
    private IEnumerator IntroductionPhase()
    {
        Debug.Log("SessionController: Introduction Phase.");

        foreach (var agt in _currentAgents) agt.sentences.Clear();

        var steps = new List<ConversationStep>();
        foreach (var agent in _currentAgents.OrderBy(a => a.agentName))
        {
            agent.sentences.Add(BuildIntroductionLine(agent.displayName));
            steps.Add(new ConversationStep { agent = agent, sentenceIndex = agent.sentences.Count - 1 });
        }

        yield return PlayStepsAndWait(steps, allowTestSkip: false);
        Debug.Log("SessionController: Introductions complete. Press Space to continue.");
    }

    private string BuildIntroductionLine(string displayName)
    {
        // The format string is inspector-editable; a stray brace must not kill the trial coroutine.
        try
        {
            return string.Format(introductionLineFormat, displayName);
        }
        catch (FormatException)
        {
            Debug.LogError($"[SessionController] Introduction Line Format '{introductionLineFormat}' is not a valid " +
                           "format string (use {0} for the avatar name). Falling back to a default introduction.");
            return $"Hi, I'm {displayName}.";
        }
    }

    /// <summary>
    /// Shared playback ritual for introductions and discussions: hand the steps to the manager,
    /// play, wait for completion (optionally allowing the test-mode Space skip), then a short
    /// buffer so the finishing frame's input cannot pass through to the next WaitForKey.
    /// </summary>
    private IEnumerator PlayStepsAndWait(List<ConversationStep> steps, bool allowTestSkip)
    {
        manager.agents = _currentAgents;
        manager.sequence = steps;
        manager.playOnStart = false;

        manager.Play();

        float startTime = Time.time;
        while (manager.IsPlaying)
        {
            // Only allow skip after a 0.5s grace period to avoid catching the initial Space press
            if (allowTestSkip && isTestMode && Time.time - startTime > 0.5f && Input.GetKeyDown(KeyCode.Space))
            {
                manager.Stop();
                foreach (var agent in _currentAgents)
                {
                    var tts = agent.GetComponent<CrossPlatformTTS>();
                    if (tts) tts.Stop();

                    // Manually force animation state to false since tts.Stop() might kill the callback
                    agent.SendMessage("SetTalkingState", false, SendMessageOptions.DontRequireReceiver);
                }
                Debug.Log("SessionController: Test Mode - Discussion Skipped.");
                break;
            }
            yield return null;
        }

        // Small buffer to prevent accidental Space pass-through
        yield return new WaitForSeconds(0.5f);
    }

    private void SetupPhase1()
    {
        // Restore cursor color to original for new trial
        if (sphereCursorRenderer != null && _hasSavedCursorColor)
        {
            sphereCursorRenderer.material.color = _originalCursorColor;
        }

        // Hide Augmentations at start of trial
        if (augmentationPanel != null) augmentationPanel.SetActive(false);
        if (interacter != null) interacter.SetActive(false);

        // Clear previous
        foreach (var agent in _currentAgents) StopSpeakingAndDestroy(agent);
        _currentAgents.Clear();

        // iAA specific setup
        if (studyCondition == StudyCondition.iAA || studyCondition == StudyCondition.nAA)
        {
            // Build Grid (Static 5x4)
            if (gridGenerator)
            {
                Debug.Log("SessionController: Building Grid 5 Rows x 4 Cols");
                gridGenerator.labelAlignment = GridGenerator.LabelAlignment.Left;
                gridGenerator.Build(RowRanks.Length, 4);
                gridGenerator.BuildLabels(new List<string>(RowRanks));
            }
        }

        // Show Question - except on a first trial with introductions pending: there the wall stays
        // blank until the avatars have introduced themselves (revealed in RunIntroductionIfNeeded).
        if (ShouldRunIntroduction(_currentTrialIndex))
        {
            if (wallQuestionText != null) wallQuestionText.text = "";
        }
        else
        {
            ShowWallQuestion();
        }

        // Spawn Avatars
        Debug.Log($"SessionController: Phase 1 Setup ({studyCondition}). Spawning avatars.");
        
        if (studyCondition == StudyCondition.iAA || studyCondition == StudyCondition.nAA)
        {
            // "Line up on the side". Let's say left of grid.
            Vector3 startPos = gridGenerator ? gridGenerator.GridToWorld(0, 4) : Vector3.zero; // 2 columns left
            for (int i = 1; i <= 4; i++)
            {
                string id = $"agent_{i}";
                var prefab = _agentMapping[id];
                Vector3 pos = startPos + new Vector3(0, 0, (i - 1) * lineUpSpacing); 
                SpawnAgent(id, prefab, pos, Quaternion.LookRotation(Vector3.left));
            }
        }
        else if (studyCondition == StudyCondition.iAT || studyCondition == StudyCondition.nAT)
        {
            // Spawn at origins
            for (int i = 1; i <= 4; i++)
            {
                string id = $"agent_{i}";
                var prefab = _agentMapping[id];
                Transform origin = agentOrigins[i - 1];
                
                if (origin != null)
                {
                    SpawnAgent(id, prefab, origin.position, origin.rotation, origin);
                }
                else
                {
                    Debug.LogWarning($"SessionController: Agent Origin {i} is missing!");
                    SpawnAgent(id, prefab, Vector3.zero, Quaternion.identity);
                }
            }
        }
    }

    private void SpawnAgent(string id, GameObject prefab, Vector3 pos, Quaternion rot, Transform parent = null)
    {
        var go = Instantiate(prefab, pos, rot, parent); 
        go.name = id;
        var agt = go.GetComponent<ConversationalAgent>();
        if (!agt) agt = go.AddComponent<ConversationalAgent>();
        agt.agentName = id;
        agt.displayName = prefab.name; // Preserve original prefab name (e.g. Tony)
        
        // Ensure TTS is ready
        var tts = go.GetComponent<CrossPlatformTTS>();
        if (!tts) tts = go.AddComponent<CrossPlatformTTS>();
        
        _currentAgents.Add(agt);

        // Hide individual augmentation canvas initially
        var canvas = go.GetComponentInChildren<Canvas>(true);
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    private IEnumerator MovePhase2()
    {
        Debug.Log("SessionController: Phase 2 Moving.");
        var posConfig = _currentQuestionData.apr_Positions;
        
        var rankToRow = new Dictionary<string, int>();
        for (int r = 0; r < RowRanks.Length; r++) rankToRow[RowRanks[r]] = r;

        var nextColInRow = new int[RowRanks.Length]; // defaults to 0
        
        foreach (int agentNum in posConfig.order_Seating)
        {
            string id = $"agent_{agentNum}";
            var agent = _currentAgents.FirstOrDefault(a => a.agentName == id);
            if (!agent) continue;

            string rank = GetRankForAgent(posConfig, agentNum);
            
            // DEBUG: Print positions to verify vs JSON
            Debug.Log($"[TRIAL DEBUG] Agent {agentNum} Target Rank: '{rank}'");

            if (string.IsNullOrEmpty(rank) || !rankToRow.ContainsKey(rank))
            {
                Debug.LogWarning($"SessionController: Unknown rank '{rank}' for agent {id}");
                continue;
            }

            int row = rankToRow[rank];
            int col = nextColInRow[row];
            nextColInRow[row]++; 

            Vector3 target = gridGenerator ? gridGenerator.GridToWorld(row, col) : agent.transform.position;
            float duration = isTestMode ? 0f : moveDuration;
            yield return MoveAgent(agent.transform, target, duration);
        }
    }
    
    private string GetRankForAgent(PositionConfig cfg, int num)
    {
        // Debug Log inside here or caller? Caller is better.
        return num switch {
            1 => cfg.agent_1_Pos,
            2 => cfg.agent_2_Pos,
            3 => cfg.agent_3_Pos,
            4 => cfg.agent_4_Pos,
            _ => null
        };
    }

    private IEnumerator DiscussionPhase3()
    {
        Debug.Log("SessionController: Phase 3 Discussion.");
        
        // We need to fetch the raw JSON segment for this question to parse duplicates manually
        // Since we don't have the raw segment easily isolated in _currentQuestionData, we'll re-extract it from the raw file text
        // or just rely on the fact that we can parse "glb_Responses" from the parsed object? 
        // No, the parsed object ALREADY lost the data (duplicates dropped).
        
        // Strategy: Re-parse the *specific* question block from the Full JSON Text
        // We need the raw text of the current trial's script.
        // We stored the script reference in _trialSequence[_currentTrialIndex].script
        
        var trial = _trialSequence[_currentTrialIndex];
        string fullJson = trial.script.text;
        
        // 1. Isolate the specific Question Data block (e.g. "question_1_Data" : { ... })
        // We know the qIndex.
        // Regex to find "question_X_Data" : { ... } is hard because of nested braces.
        // BUT, the file structure is consistent.
        // Let's rely on the order of "order_Speech" and a smart Regex over the relevant block.
        
        // Simpler: Just Regex search the WHOLE file for the block that starts with our question key.
        // Question key is dynamic: "question_1_Data", etc.
        string qKey = $"question_{trial.questionIndex}_Data";
        int startIdx = fullJson.IndexOf(qKey);
        if (startIdx == -1) 
        {
            Debug.LogError($"Could not find {qKey} in script.");
            yield break;
        }
        
        // Find "glb_Responses" after that startIdx
        int respIdx = fullJson.IndexOf("\"glb_Responses\"", startIdx);
        if (respIdx == -1)
        {
             Debug.LogError($"Could not find glb_Responses in {qKey}.");
             yield break;
        }
        
        // Extract the content of glb_Responses block (rough heuristics: until "glb_Augmentations" or end of object)
        // Actually, just find the "order_Speech" line, and then capture following lines?
        // Let's use Regex to find all `agent_\d+_Resp` matches starting from respIdx.
        // And stop when we hit the next main block (e.g. "glb_Augmentations" or closing brace).
        
        // Find end of responses block (roughly)
        int endIdx = fullJson.IndexOf("\"glb_Augmentations\"", respIdx);
        if (endIdx == -1) endIdx = fullJson.Length;
        
        string respBlock = fullJson.Substring(respIdx, endIdx - respIdx);
        
        // Regex to match: "agent_N_Resp" : "TEXT"
        // Handle escaped quotes in text if any: \"
        var matches = Regex.Matches(respBlock, "\"agent_\\d+_Resp\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        
        var speechOrder = _currentQuestionData.glb_Responses.order_Speech;
        if (speechOrder == null || speechOrder.Length == 0) yield break;

        // Validation
        // The number of regex matches SHOULD usually match the speech order length.
        // But let's trust the matches queue.
        
        var responseQueue = new Queue<string>();
        foreach (Match m in matches)
        {
            responseQueue.Enqueue(m.Groups[1].Value);
        }
        
        Debug.Log($"[TRIAL DEBUG] Found {matches.Count} response strings for {speechOrder.Length} speech turns.");

        var steps = new List<ConversationStep>();

        foreach (var agt in _currentAgents) agt.sentences.Clear();

        foreach (int agentNum in speechOrder)
        {
            string id = $"agent_{agentNum}";
            var agent = _currentAgents.FirstOrDefault(a => a.agentName == id);
            if (!agent) continue;

            string text = "";
            if (responseQueue.Count > 0)
            {
                text = responseQueue.Dequeue();
                // Unescape JSON string if needed (Regex captured raw inside quotes)
                text = Regex.Unescape(text);
            }
            else
            {
                Debug.LogWarning("Not enough response strings found in JSON!");
            }

            agent.sentences.Add(text);
            int idx = agent.sentences.Count - 1;
            steps.Add(new ConversationStep { agent = agent, sentenceIndex = idx });
        }

        yield return PlayStepsAndWait(steps, allowTestSkip: true);

        // Turn cursor green automatically when conversation ends
        if (sphereCursorRenderer != null)
        {
            sphereCursorRenderer.material.color = conversationEndColor;
        }

        Debug.Log("SessionController: Discussion Finished. Ready for Phase 4. Press Space.");
    }

    private void ShowGroupAugmentations()
    {
        if (augmentationPanel != null) augmentationPanel.SetActive(true);

        var trial = _trialSequence[_currentTrialIndex];
        string fullJson = trial.script.text;
        string qKey = $"question_{trial.questionIndex}_Data";
        int startIdx = fullJson.IndexOf(qKey);
        
        if (startIdx == -1) return;

        // 1. Extract Text Augmentations (glb_Grp_Sums)
        int grpSumsIdx = fullJson.IndexOf("\"glb_Grp_Sums\"", startIdx);
        if (grpSumsIdx != -1)
        {
            // Suggestions
            string suggestions = ExtractJsonString(fullJson, "glb_Grp_Suggestions", grpSumsIdx);
            if (suggestionsText != null) 
                suggestionsText.text = FormatBulletinPoints(suggestions);

            // Themes
            string themes = ExtractJsonString(fullJson, "glb_Emg_Themes", grpSumsIdx);
            if (themesTexts != null && themesTexts.Count > 0) 
            {
                // Split by ■ to get individual points
                string[] points = themes.Split(new char[] { '■' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Clear all first
                foreach (var tmp in themesTexts) if (tmp != null) tmp.text = "";

                // Distribute: 1-2 in first TMP, 3-4 in second TMP
                string firstHalf = "";
                string secondHalf = "";

                for (int i = 0; i < points.Length; i++)
                {
                    string formattedPoint = "■ <indent=1.2em>" + points[i].Trim() + "</indent>";
                    if (i < 2)
                    {
                        firstHalf += formattedPoint + (i == 0 && points.Length > 1 ? "\n" : "");
                    }
                    else if (i < 4)
                    {
                        secondHalf += formattedPoint + (i == 2 && points.Length > 3 ? "\n" : "");
                    }
                }

                if (themesTexts.Count > 0 && themesTexts[0] != null) themesTexts[0].text = firstHalf;
                if (themesTexts.Count > 1 && themesTexts[1] != null) themesTexts[1].text = secondHalf;
            }
        }

        // 2. Extract Image or Stat Augmentations based on condition
        if (studyCondition == StudyCondition.iAA)
        {
            // Toggle visibility
            if (speakingSumImage != null) speakingSumImage.gameObject.SetActive(true);
            if (grpMoveImage != null) grpMoveImage.gameObject.SetActive(true);
            if (grpSimMatImage != null) grpSimMatImage.gameObject.SetActive(true);
            if (turnTakingText != null) turnTakingText.gameObject.SetActive(false);
            if (dissonantOpinionsText != null) dissonantOpinionsText.gameObject.SetActive(false);

            int aprGrpSumsIdx = fullJson.IndexOf("\"apr_Grp_Sums\"", startIdx);
            if (aprGrpSumsIdx != -1)
            {
                string speakingPath = ExtractJsonString(fullJson, "speaking_Sum", aprGrpSumsIdx);
                string movePath = ExtractJsonString(fullJson, "grp_Move", aprGrpSumsIdx);
                string simMatPath = ExtractJsonString(fullJson, "grp_Sim_Mat", aprGrpSumsIdx);

                if (speakingSumImage != null) StartCoroutine(LoadImageToUI(speakingPath, speakingSumImage));
                if (grpMoveImage != null) StartCoroutine(LoadImageToUI(movePath, grpMoveImage));
                if (grpSimMatImage != null) StartCoroutine(LoadImageToUI(simMatPath, grpSimMatImage));
            }
        }
        else if (studyCondition == StudyCondition.iAT)
        {
            // Toggle visibility
            if (speakingSumImage != null) speakingSumImage.gameObject.SetActive(false);
            if (grpMoveImage != null) grpMoveImage.gameObject.SetActive(false);
            if (grpSimMatImage != null) grpSimMatImage.gameObject.SetActive(false);
            if (turnTakingText != null) turnTakingText.gameObject.SetActive(true);
            if (dissonantOpinionsText != null) dissonantOpinionsText.gameObject.SetActive(true);

            int trdGrpSumIdx = fullJson.IndexOf("\"trd_Grp_Sum\"", startIdx);
            if (trdGrpSumIdx != -1)
            {
                string turnTaking = ExtractJsonValue(fullJson, "turn_Taking", trdGrpSumIdx);
                string dissOps = ExtractJsonValue(fullJson, "no_Diss_Ops", trdGrpSumIdx);

                if (turnTakingText != null) turnTakingText.text = turnTaking;
                if (dissonantOpinionsText != null) dissonantOpinionsText.text = dissOps;
            }
        }
    }

    private string ExtractJsonString(string json, string key, int searchStart)
    {
        // Matches "key" : "value"
        var match = Regex.Match(json.Substring(searchStart), $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (match.Success)
        {
            return Regex.Unescape(match.Groups[1].Value);
        }
        return "";
    }

    private string ExtractJsonValue(string json, string key, int searchStart)
    {
        // Matches "key" : value (for numbers or booleans)
        var match = Regex.Match(json.Substring(searchStart), $"\"{key}\"\\s*:\\s*([^,\\s}}]+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return "";
    }

    private string FormatBulletinPoints(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        // Ensure that each ■ starts on a new line if it doesn't already
        // First, replace any existing \n ■ with just ■ to normalize, then replace all ■ with \n■
        // Then trim the leading newline if added.
        
        // User wants: "I want you to use reg exp to read how many bulletin points there are and each bulletin points use a line"
        // And "preserve the ■ in the text, put it in the beginning"
        
        // Simple strategy: Split by ■, then join with \n ■
        string[] parts = text.Split(new char[] { '■' }, StringSplitOptions.RemoveEmptyEntries);
        string formatted = "";
        for (int i = 0; i < parts.Length; i++)
        {
            formatted += "■ <indent=1.2em>" + parts[i].Trim() + "</indent>" + (i < parts.Length - 1 ? "\n" : "");
        }
        return formatted;
    }

    private IEnumerator LoadImageToUI(string relativePath, Image uiImage)
    {
        if (string.IsNullOrEmpty(relativePath)) yield break;

        // 1. Try local file path (Primary for Editor/PC)
        string localPath = "";
        if (relativePath.StartsWith("Assets/"))
            localPath = Path.Combine(Application.dataPath, relativePath.Substring(7));
        else
            localPath = Path.Combine(Application.dataPath, relativePath);

        if (Application.platform != RuntimePlatform.Android && File.Exists(localPath))
        {
            byte[] fileData = File.ReadAllBytes(localPath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                uiImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                yield break;
            }
        }

        // 2. Fallback to StreamingAssets (For Standalone Quest/Android)
        // Extract filename from the path (e.g., Augmentation Images/Group/.../img.jpg)
        // We assume the user duplicated the folder structure into StreamingAssets
        string streamingPath = Path.Combine(Application.streamingAssetsPath, relativePath);
        
        // On Android, StreamingAssets is inside the APK and must be read via UnityWebRequest
        if (streamingPath.Contains("://") || Application.platform == RuntimePlatform.Android)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(streamingPath))
            {
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    Texture2D tex = DownloadHandlerTexture.GetContent(uwr);
                    uiImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
                else
                {
                    Debug.LogWarning($"[SessionController] Failed to load from StreamingAssets: {uwr.error} at {streamingPath}");
                }
            }
        }
        else if (File.Exists(streamingPath))
        {
            byte[] fileData = File.ReadAllBytes(streamingPath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                uiImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }
    }

    private string GetResponseForAgent(Responses speech, int num)
    {
          return num switch {
            1 => speech.agent_1_Resp,
            2 => speech.agent_2_Resp,
            3 => speech.agent_3_Resp,
            4 => speech.agent_4_Resp,
            _ => ""
        };
    }

    // --- Legacy / Debug Support ---
    public void InitializeFromJsonPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        InitializeFromJsonText(System.IO.File.ReadAllText(path));
    }

    public void InitializeFromJsonText(string json)
    {
        // For debugging: Load this single script as a 1-trial experiment
        // Only if we are not already running a full experiment? 
        // Or just overwrite.
        StopAllCoroutines();
        _trialSequence.Clear();
        _currentTrialIndex = 0;
        
        // Wrap as text asset? No, we parsed strings.
        // We need to change _trialSequence to support raw strings or TextAssets.
        // But _trialSequence uses (TextAsset, int).
        
        // Hack: Create a dummy TextAsset or just parse immediately and injection?
        // Let's change the internal logic to store Data Objects instead of TextAssets?
        // Actually, ParseQuestionData takes TextAsset, but passing a string is cleaner if we duplicate logic.
        
        // Let's just ignore the "TextAsset" part and parse directly here?
        // But StartTrial needs to pull from _trialSequence.
        
        // Better fix: Make ParseQuestionData take string instead of TextAsset.
        // Then _trialSequence can be (string content, int qIndex).
        
        // See Step 2 refactor below. For now, I'll log that this is not fully supported 
        // OR I will refactor ParseQuestionData to take string.
        Debug.LogWarning("Runtime loading of single JSON is not fully supported in iAA mode yet. Ignored.");
    }

    // --- Helpers ---

    private QuestionData ParseQuestionData(TextAsset jsonFile, int qIndex)
    {
        return ParseQuestionDataString(jsonFile.text, qIndex);
    }
    
    private QuestionData ParseQuestionDataString(string jsonText, int qIndex)
    {
        // Normalize
        // 1. Replace qN_Txt with questionText
        jsonText = Regex.Replace(jsonText, "\"q[0-9]+_Txt\"", "\"questionText\"");
        
        var wrapper = JsonUtility.FromJson<ScriptJsonWrapper>(jsonText);
        return qIndex switch {
            1 => wrapper.question_1_Data,
            2 => wrapper.question_2_Data,
            3 => wrapper.question_3_Data,
            4 => wrapper.question_4_Data,
            _ => wrapper.question_1_Data
        };
    }

    private IEnumerator MoveAgent(Transform t, Vector3 dest, float duration)
    {
        Vector3 start = t.position;
        float elapsed = 0;
        while (elapsed < duration)
        {
            t.position = Vector3.Lerp(start, dest, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        t.position = dest;
    }

    private void StopSpeakingAndDestroy(ConversationalAgent agent)
    {
        if (agent)
        {
            var tts = agent.GetComponent<CrossPlatformTTS>();
            if (tts) tts.Stop(); // Ensure method exists or StopCoroutine
            Destroy(agent.gameObject);
        }
    }

    private IEnumerator WaitForKey(KeyCode key)
    {
        Debug.Log($"[SessionController] Waiting for {key} or Quest 'A' button...");
        
        // Safety buffer to prevent accidental pass-through from previous frame
        yield return new WaitForSeconds(0.5f);

        bool progressionTriggered = false;

        while (!progressionTriggered)
        {
            // 1. Check Keyboard
            if (Input.GetKeyDown(key))
            {
                Debug.Log($"[SessionController] Keyboard {key} detected.");
                progressionTriggered = true;
                break;
            }

            // 2. Check VR Controllers (A button / PrimaryButton)
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
            
            foreach (var device in devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool isPressed) && isPressed)
                {
                    Debug.Log("[SessionController] VR 'A' button detected. Waiting for release...");
                    
                    // Wait for release to prevent skipping next phase
                    while (isPressed)
                    {
                        device.TryGetFeatureValue(CommonUsages.primaryButton, out isPressed);
                        yield return null;
                    }
                    
                    Debug.Log("[SessionController] VR 'A' button released.");
                    progressionTriggered = true;
                    break;
                }
            }

            yield return null;
        }

        Debug.Log("[SessionController] Progressing to next step.");
    }
}
