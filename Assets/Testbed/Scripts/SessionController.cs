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

    // The steps of a trial (see RunCondition). Setup and Introduction run once per condition, Finished after the last trial.
    public enum SessionStep { Setup, Introduction, AwaitQuestion, Question, Discussion, Review, ReturnAgents, Finished }
    public SessionStep CurrentStep { get; private set; }

    public enum StudyCondition { iAA, iAT, nAA, nAT }

    // Each condition has ONE compiled manuscript named by its abbreviation
    // (iAA.json, iAT.json, nAA.json, nAT.json) under Assets/Resources/<this folder>/, holding
    // question_1..4_Data in final asking order (the Table 8 script/question mixing pre-applied).
    public const string ManuscriptResourceFolder = "Manuscripts";
    // Introduction wording. The TTS baker (Tools/iXR/Bake All Session Audio) bakes introductions with this
    // format, so a scene that overrides Introduction Line Format plays its introductions through live Piper.
    public const string DefaultIntroductionLineFormat = "Hi, I'm {0}. Nice to meet you.";

    // Stance carried by each grid row, index = row number (0 = near row at z=0, 4 = far row).
    // Both the floor labels AND the seating destinations derive from this one array, so they can
    // never disagree - to reorder the grid, reorder here only. Must match the stance strings in
    // the manuscripts and the row order depicted in the pre-rendered augmentation charts.
    private static readonly string[] RowRanks = { "Very Good", "Good", "Neutral", "Bad", "Very Bad" };

    [Header("Study State")]
    public string participantID = "P1";
    public GazeDataRecorder gazeRecorder;
    public StudyCondition studyCondition = StudyCondition.iAA;

    [Header("Configuration")]
    public List<GameObject> availableMaleAvatars;
    public List<GameObject> availableFemaleAvatars;

    [Header("Scene References")]
    public GridGenerator gridGenerator;
    public ConversationalAgentsManager manager;


    [Header("Settings")]
    public float moveDuration = 2f;
    [Tooltip("Walking pace (m/s) for the grid move: travel time = distance / pace, so long and short moves look natural. " +
         "0 = every move takes Move Duration.")]
    public float walkSpeed = 1.2f;
    public float lineUpSpacing = 1.2f;
    [Tooltip("Enable to skip lengthy movement and discussion phases")]
    public bool isTestMode = false;

    [Header("Introduction Phase")]
    [Tooltip("If enabled, the avatars introduce themselves once per condition, at the start of the first trial - " +
             "identical across all four conditions and BEFORE any gaze recording starts.")]
    public bool enableIntroductionPhase = true;
    [Tooltip("Line spoken by each avatar during the introduction. {0} = avatar display name.")]
    public string introductionLineFormat = DefaultIntroductionLineFormat;

    [Header("Back Wall")]
    [Tooltip("Put in front of the question on the back wall; {0} is the question's number (1-4). Empty = no number. " +
             "Rich text works: <b>...</b> is bold.")]
    public string questionNumberFormat = "<b>Q{0}:</b> ";
    [Tooltip("Shown large and centred on the back wall until the wizard reveals the next question.")]
    public string readyMessage = "Next question when ready";
    [Tooltip("Font size of the ready message. The question keeps the size and position set on the wall text in the scene.")]
    public float readyMessageFontSize = 20f;

    [Header("Between Conditions")]
    [Tooltip("Shown large and centred on the back wall once the wizard's first T has cleared the room.")]
    [TextArea(2, 3)] public string endMessage = "End of Conversation.\nNext Trial when ready.";
    [Tooltip("The end message in the last condition of the session, which has no next trial.")]
    [TextArea(2, 3)] public string lastConditionEndMessage = "End of Conversation.";
    [Tooltip("Font size of the end message. Up to about 28 each line of the default message fits on the wall; " +
             "larger sizes wrap to a third line, which is cut off.")]
    public float endMessageFontSize = 26f;
    [Tooltip("Table conditions: the table (Environment/Table Origin), hidden when the first T clears the room. " +
             "Leave empty in grid conditions; their grid is cleared instead.")]
    public GameObject tableSetting;
    [Tooltip("Seconds between the table or grid appearing and the avatars appearing when a condition starts.")]
    public float avatarAppearDelay = 2f;

    [Header("Gaze Recording")]
    [Tooltip("Fixations shorter than this (seconds) are not logged during the discussion. 0 = log every fixation.")]
    public float conversationMinFixation = 0f;
    [Tooltip("Fixations shorter than this (seconds) are not logged while the participant looks around after the discussion " +
             "(Review phase). 0 = log every fixation.")]
    public float reviewMinFixation = 2f;

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
    private List<ConversationalAgent> _currentAgents = new(); // Spawned once per condition, kept for all its trials
    private readonly Dictionary<ConversationalAgent, (Vector3 position, Quaternion rotation)> _home = new(); // spawn pose
    private List<(TextAsset script, int questionIndex)> _trialSequence = new();
    private int _currentTrialIndex = 0;
    private int _firstTrial; // trial this condition starts at: 0, or later in a session resumed after a crash

    private QuestionData _currentQuestionData;
    private Coroutine _audioPreload;
    private bool _audioPreloaded;
    private int _preloadTrial = -1; // trial whose lines _audioPreload loads

    // Review: time between the end of a discussion and the wizard's Space, summed over the stretches when the
    // wizard went Back into a review. Per trial, like the Back presses.
    private float[] _reviewSeconds = new float[0];
    private int[] _backPresses = new int[0];
    private float _reviewStartedAt;

    // Wizard input. Back is only accepted where a step allows it (_canGoBack) and until the data is saved.
    private enum WizardInput { None, Next, Back }
    private WizardInput _wizardInput;
    private bool _canGoBack;
    private bool _backRequested;
    private bool _interrupted; // set by RunInterruptible when Back stopped the step
    private bool _saved;
    private string _statusDetail = ""; // what a running step is doing, for the wizard panel

    // Passthrough (see CheckPassthrough): null while it runs or is still starting, otherwise why it is not running.
    private string _passthroughProblem;
    private const float PassthroughStartupSeconds = 3f; // passthrough is only checked once the scene has run this long

    // Where this condition's CSVs go and how they are named (see SetLogNames).
    private string _logFolder;
    private string _filePrefix = "";
    private string _logParticipant = "";
    private string _logSequence = "";
    private int _logTrial;

    // Training (TrainingController, a separate start to the master): the training team and manuscripts, nothing
    // logged, no Back, prompts on the wall, and the participant moves on with X on the left controller.
    private static TrainingController Training => TrainingController.Instance;
    private static bool IsTraining => Training != null;

    void Awake()
    {
        Instance = this;
        if (gridGenerator == null) gridGenerator = FindObjectOfType<GridGenerator>();
        if (manager == null) manager = FindObjectOfType<ConversationalAgentsManager>();
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

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Backspace)) RequestBack();
        CheckPassthrough();
    }

    /// <summary>True when passthrough is not running, so the wizard panel shows a warning and needs more room.</summary>
    public bool HasPassthroughProblem => _passthroughProblem != null;

    /// <summary>
    /// The participant sees the real room through passthrough: OVRManager starts it and the scene's
    /// [BuildingBlock] Passthrough layer draws it behind everything, where the camera clears to transparent black.
    /// If it does not start, the headset shows black everywhere except the avatars, wall and floor. Over Link that
    /// almost always means Passthrough over Link is off in the Meta Horizon Link app, which no project setting can
    /// change, so the wizard panel says so and the Console gets one error.
    /// </summary>
    private void CheckPassthrough()
    {
        if (Time.timeSinceLevelLoad < PassthroughStartupSeconds) return;

        string problem =
            OVRManager.instance == null ? "the scene has no OVRManager" :
            !OVRManager.instance.isInsightPassthroughEnabled ? "Insight Passthrough is off on the OVRManager" :
            OVRManager.HasInsightPassthroughInitFailed() ? "it failed to start" :
            OVRManager.IsInsightPassthroughInitialized() || OVRManager.IsInsightPassthroughInitPending() ? null :
            "it has not started";

        if (problem != null && _passthroughProblem == null)
            Debug.LogError($"[SessionController] Passthrough is not running ({problem}); the headset shows black around " +
                           "the room. In the Meta Horizon Link app on the PC, turn on Settings > Developer > Developer Runtime " +
                           "Features and Passthrough over Meta Horizon Link, then restart Unity.");
        _passthroughProblem = problem;
    }

    /// <summary>
    /// Writes the CSVs of every question of this condition. Each question is already written when its review ends
    /// (EndReview); this writes them once more when the wizard presses T (MasterLevelController) and when the scene
    /// closes or Play stops, which also keeps a question stopped part-way. Back is disabled once the data is saved.
    /// </summary>
    public void SaveData()
    {
        // T, quitting and leaving the scene all call this; only the first call writes.
        if (_saved) return;

        // Quitting in the middle of a review: keep the review time so far.
        if (CurrentStep == SessionStep.Review && _currentTrialIndex < _reviewSeconds.Length)
            ReportReviewTime(_reviewSeconds[_currentTrialIndex] + (Time.time - _reviewStartedAt));

        for (int t = 0; t < _trialSequence.Count; t++) SaveQuestion(t);
        _saved = true;
    }

    void OnDestroy()
    {
        SaveData();
    }

    void OnApplicationQuit()
    {
        SaveData();
    }

    /// <summary>
    /// Writes one question's gaze and review-time CSVs into this session's folder, e.g.
    /// Logs/P07_Seq3_S1_2026-10-13_14-05-22/P07_Seq3_S1_T2_iAT_Q1_Gaze.csv and P07_Seq3_S1_T2_iAT_Q1_ReviewTime.csv.
    /// Existing files are replaced: after Back the question is written again with all its rows. A failed write
    /// (e.g. the file is open in Excel) is logged and does not stop the session.
    /// </summary>
    private void SaveQuestion(int trialIndex)
    {
        if (gazeRecorder == null || string.IsNullOrEmpty(_logFolder)) return;
        int question = _trialSequence[trialIndex].questionIndex;
        try
        {
            gazeRecorder.WriteQuestionCSVs(_logFolder, $"{_filePrefix}_Q{question}", _logParticipant, _logSequence,
                                           _logTrial, question);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SessionController] Could not save the CSVs of question {question}: {e.Message}");
        }
    }

    /// <summary>
    /// Folder and file-name start of this condition's CSVs. With the master: the session's folder
    /// (Logs/P07_Seq3_S1_2026-10-13_14-05-22) and "P07_Seq3_S1_T2_iAT" = participant, sequence, session, trial (position
    /// of this condition in the sequence, 1-4) and condition. Without the master: Logs/test_Standalone_2026-10-13_14-05-22 and
    /// "test_Standalone_T0_iAA", with the scene's Participant ID.
    /// </summary>
    private void SetLogNames()
    {
        var master = MasterLevelController.Instance;
        if (master != null)
        {
            _logParticipant = master.participantID;
            _logSequence = master.GetCurrentSequenceName();
            _logTrial = master.CurrentConditionIndex + 1;
            _logFolder = master.SessionLogFolder;
            _filePrefix = $"{master.SessionName}_T{_logTrial}_{studyCondition}";
        }
        else
        {
            string session = $"{participantID}_Standalone";
            _logParticipant = participantID;
            _logSequence = "Standalone";
            _logTrial = 0;
            _logFolder = Path.Combine(GazeDataRecorder.LogsFolder, $"{session}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
            _filePrefix = $"{session}_T0_{studyCondition}";
        }
    }


    private IEnumerator SetupExperiment()
    {
        SetLogNames();

        if (IsTraining)
        {
            if (studyCondition != Training.CurrentRoom)
            {
                Debug.LogError($"[SessionController] CONDITION MISMATCH: this scene is serialized as {studyCondition} but " +
                               $"TrainingController expected {Training.CurrentRoom}. Check its scene-name fields. Halting setup.");
                yield break;
            }
            gazeRecorder = null; // nothing is recorded or written in training
        }
        else if (MasterLevelController.Instance != null)
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
            // (see ShowWallQuestion) so it cannot run unnoticed.
            if (isTestMode)
            {
                Debug.LogWarning("[SessionController] isTestMode is ON during a master-driven run - " +
                                 "introductions are skipped and discussions can be skipped with Space. " +
                                 "Turn it OFF in this scene before running a real participant!");
            }
        }

        // 1. Select Avatars
        // Primary path: deterministic team lookup from MasterLevelController.
        // Each condition has its own fixed team (Team A always plays nAT, ...), and the member order IS the
        // casting: member i plays agent_{i+1}, so every participant hears the same avatars say the same lines here.
        // Training uses its own team, in both rooms.
        var teamMembers = IsTraining ? Training.Team
            : MasterLevelController.Instance != null ? MasterLevelController.Instance.GetTeamForCurrentCondition()
            : null;

        if (teamMembers != null)
        {
            _currentTeamName = IsTraining ? "Training" : MasterLevelController.Instance.GetCurrentTeamName();
            _agentMapping.Clear();
            for (int i = 0; i < 4; i++)
            {
                _agentMapping[$"agent_{i + 1}"] = teamMembers[i];
            }
            Debug.Log($"[SessionController] Avatar team '{_currentTeamName}' for {studyCondition}" +
                      (IsTraining ? "" : $" (position {MasterLevelController.Instance.CurrentConditionIndex + 1})") + ": " +
                      string.Join(", ", teamMembers.Select((m, i) => $"agent_{i + 1}={m.name}")));
        }
        else if (IsTraining)
        {
            // Never fall back to the scene's study avatars in training.
            Debug.LogError("[SessionController] The training team is not set up; see TrainingController. Halting setup.");
            yield break;
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
        // Load the per-condition compiled manuscript by the condition abbreviation itself -
        // no per-scene file wiring, so scenes can never silently point at a stale script.
        _trialSequence.Clear();
        string manuscriptFolder = IsTraining ? TrainingController.ManuscriptFolder : ManuscriptResourceFolder;
        var conditionScript = Resources.Load<TextAsset>($"{manuscriptFolder}/{studyCondition}");
        if (conditionScript == null)
        {
            Debug.LogError($"[SessionController] No manuscript found at Resources/{manuscriptFolder}/{studyCondition}.json - " +
                           "add the compiled per-condition script. Halting setup.");
            yield break;
        }

        // The study's manuscripts have questions 1-4; the training's only question 1.
        for (int q = 1; q <= 4; q++)
        {
            if (ManuscriptJson.GetQuestion(conditionScript.text, q) != "") _trialSequence.Add((conditionScript, q));
        }
        if (_trialSequence.Count == 0)
        {
            Debug.LogError($"[SessionController] Resources/{manuscriptFolder}/{studyCondition}.json has no question_N_Data. Halting setup.");
            yield break;
        }
        Debug.Log($"[SessionController] Loaded condition manuscript '{manuscriptFolder}/{conditionScript.name}' for {studyCondition} " +
                  $"({_trialSequence.Count} questions in order).");

        _reviewSeconds = new float[_trialSequence.Count];
        _backPresses = new int[_trialSequence.Count];
        // A session resumed after a crash starts its first condition at the question chosen on the master's panel.
        _firstTrial = MasterLevelController.Instance != null
            ? Mathf.Clamp(MasterLevelController.Instance.StartQuestion - 1, 0, _trialSequence.Count - 1)
            : 0;
        yield return RunCondition();
    }

    // --- Trial flow ---

    private bool IsGridCondition => studyCondition == StudyCondition.iAA || studyCondition == StudyCondition.nAA;
    private bool HasAugmentations => studyCondition == StudyCondition.iAA || studyCondition == StudyCondition.iAT;
    private bool IntroductionEnabled => enableIntroductionPhase && !isTestMode;

    /// <summary>
    /// Runs the condition's trials as steps the wizard moves through with Space (or the Quest 'A' button) and Back
    /// (Backspace or the panel button). Per trial:
    ///   AwaitQuestion  wall shows the ready message                  Space: show the question
    ///                                                                Back: return to the previous trial's Review
    ///   Question       wall shows the question, read out loud        Space: avatars take their places and discuss
    ///                                                                Back: hide the question
    ///   Discussion     grid conditions: avatars walk to their rows,  (automatic)
    ///                  then the discussion plays                     Back: stop it and return to Question
    ///   Review         augmentations (i-conditions); review time     Space: end the review
    ///                  and Review-phase gaze recorded
    ///   ReturnAgents   avatars walk back, ready message shown        (automatic) Back: return to Review
    /// After the last trial the condition waits in Finished for the master's T; Back still returns to the last
    /// Review until then. Back always restores the earlier state at once (avatars do not walk to get there).
    /// The avatars are spawned once and stay for all trials of the condition.
    /// A condition resumed after a crash starts at _firstTrial, without introductions, and Back cannot go before it.
    /// In training the participant moves on with X, the wall says what X does next, there is no Back, and the room ends
    /// in FinishTrainingRoom instead of waiting for the master's T.
    /// </summary>
    private IEnumerator RunCondition()
    {
        CurrentStep = SessionStep.Setup;
        LoadTrial(_firstTrial);
        HideAugmentations();
        if (IsGridCondition) BuildGrid();
        ShowWallText(""); // the wall stays blank until the avatars have introduced themselves
        // The table or grid shows first, then the avatars appear.
        if (avatarAppearDelay > 0f) yield return new WaitForSeconds(avatarAppearDelay);
        SpawnTeam();

        // The avatars introduce themselves only when the condition starts at its first question; a condition resumed
        // part-way after a crash has already had its introductions.
        if (IntroductionEnabled && _firstTrial == 0)
        {
            StartAudioPreload(0, includeIntroductions: true);
            CurrentStep = SessionStep.Introduction;
            if (IsTraining) ShowWallText(Training.introductionPrompt);
            yield return WaitForWizard();
            if (IsTraining) ShowWallText("");
            _statusDetail = "Loading the introductions' audio...";
            yield return WaitForAudioPreload();
            yield return IntroductionPhase();
        }

        var step = SessionStep.AwaitQuestion;
        while (true)
        {
            CurrentStep = step;
            int t = _currentTrialIndex;
            switch (step)
            {
                case SessionStep.AwaitQuestion:
                    ShowWallText(ReadyMessage);
                    StartAudioPreload(t, includeIntroductions: false);
                    yield return WaitForWizard(allowBack: t > _firstTrial);
                    if (_wizardInput == WizardInput.Back)
                    {
                        LoadTrial(t - 1);
                        _backPresses[t - 1]++;
                        SnapAgentsToSeats();
                        step = SessionStep.Review;
                    }
                    else step = SessionStep.Question;
                    break;

                case SessionStep.Question:
                    ShowWallQuestion(IsTraining ? Training.questionPrompt : "");
                    yield return WaitForWizard(allowBack: true);
                    if (_wizardInput == WizardInput.Back)
                    {
                        _backPresses[t]++;
                        step = SessionStep.AwaitQuestion;
                    }
                    else step = SessionStep.Discussion;
                    break;

                case SessionStep.Discussion:
                    if (IsTraining) ShowWallQuestion(); // the prompt goes once the discussion starts
                    yield return RunInterruptible(DiscussionRoutine());
                    if (_interrupted)
                    {
                        // The discussion starts over from the question, so its partial gaze data is not kept.
                        if (gazeRecorder != null) gazeRecorder.CancelRecording();
                        StopAllAgents();
                        SnapAgentsHome();
                        _backPresses[t]++;
                        step = SessionStep.Question;
                    }
                    else step = SessionStep.Review;
                    break;

                case SessionStep.Review:
                    StartReview();
                    yield return WaitForWizard();
                    EndReview();
                    step = SessionStep.ReturnAgents;
                    break;

                case SessionStep.ReturnAgents:
                    bool lastTrial = t == _trialSequence.Count - 1;
                    ShowWallText(lastTrial ? "" : ReadyMessage);
                    yield return RunInterruptible(WalkHome());
                    if (_interrupted)
                    {
                        StopAllAgents();
                        SnapAgentsToSeats();
                        _backPresses[t]++;
                        step = SessionStep.Review;
                    }
                    else if (lastTrial) step = SessionStep.Finished;
                    else
                    {
                        LoadTrial(t + 1);
                        step = SessionStep.AwaitQuestion;
                    }
                    break;

                case SessionStep.Finished:
                    Debug.Log("Experiment Finished");
                    isExperimentFinished = true;
                    if (IsTraining)
                    {
                        yield return FinishTrainingRoom();
                        yield break;
                    }
                    // Only Back leaves this step; the master's T saves the data and loads the next condition.
                    yield return WaitForWizard(allowNext: false, allowBack: true);
                    isExperimentFinished = false;
                    _backPresses[t]++;
                    SnapAgentsToSeats();
                    step = SessionStep.Review;
                    break;
            }
        }
    }

    /// <summary>
    /// End of a training room. The participant's X takes them to the next room; after the last room the avatars and the
    /// grid go and the wall shows the training's end message, as after the master's first T.
    /// </summary>
    private IEnumerator FinishTrainingRoom()
    {
        if (Training.IsLastRoom)
        {
            ClearRoom();
            yield break;
        }
        ShowWallText(Training.nextRoomPrompt);
        yield return WaitForWizard();
        Training.NextRoom();
    }

    private void LoadTrial(int trialIndex)
    {
        _currentTrialIndex = trialIndex;
        var trial = _trialSequence[trialIndex];
        _currentQuestionData = ParseQuestionData(trial.script, trial.questionIndex);
    }

    /// <summary>
    /// Wizard's discussion step: wait for the trial's audio, walk the avatars to their grid rows (grid conditions),
    /// then play the discussion with Conversation-phase gaze recording.
    /// </summary>
    private IEnumerator DiscussionRoutine()
    {
        _statusDetail = "Loading this trial's audio...";
        yield return WaitForAudioPreload();

        if (IsGridCondition)
        {
            _statusDetail = "Avatars walking to their places on the grid...";
            yield return MoveToSeats();
        }

        _statusDetail = "Discussion in progress...";
        var trial = _trialSequence[_currentTrialIndex];
        if (gazeRecorder != null)
            gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Conversation",
                                        _currentTeamName, conversationMinFixation);
        yield return DiscussionPhase();
        // Stop at the end of the discussion, so the review that follows is logged as its own phase.
        if (gazeRecorder != null) gazeRecorder.StopRecording();
        _statusDetail = "";
    }

    /// <summary>
    /// The participant looks around after the discussion: augmentations in the i-conditions, the room in all.
    /// Review time and Review-phase gaze run until EndReview. Also used to resume a review after Back.
    /// </summary>
    private void StartReview()
    {
        int t = _currentTrialIndex;
        var trial = _trialSequence[t];
        ShowWallQuestion(IsTraining ? Training.reviewPrompt : "");

        if (HasAugmentations)
        {
            Debug.Log("SessionController: AR UI Enabled. Interact with agents.");
            ShowGroupAugmentations();
            if (interacter != null)
            {
                interacter.PopulateAll(_currentAgents, trial.script.text, trial.questionIndex, studyCondition);
                interacter.PreSetupCanvases(_currentAgents);
                interacter.SetActive(true);
            }
        }

        // Turn the cursor green: the discussion is over.
        if (sphereCursorRenderer != null) sphereCursorRenderer.material.color = conversationEndColor;

        _reviewStartedAt = Time.time;
        if (gazeRecorder != null)
            gazeRecorder.StartRecording(studyCondition.ToString(), trial.script.name, trial.questionIndex, "Review",
                                        _currentTeamName, reviewMinFixation, _reviewSeconds[t]);
    }

    private void EndReview()
    {
        int t = _currentTrialIndex;
        _reviewSeconds[t] += Time.time - _reviewStartedAt;
        if (gazeRecorder != null) gazeRecorder.StopRecording();
        ReportReviewTime(_reviewSeconds[t]);
        SaveQuestion(t); // this question's data is complete; written again if Back returns to its review
        Debug.Log($"SessionController: Trial {t + 1} review ended after {_reviewSeconds[t]:F1}s.");

        HideAugmentations();
        if (sphereCursorRenderer != null && _hasSavedCursorColor) sphereCursorRenderer.material.color = _originalCursorColor;
    }

    private void ReportReviewTime(float seconds)
    {
        if (gazeRecorder == null) return;
        var trial = _trialSequence[_currentTrialIndex];
        gazeRecorder.SetReviewTime(studyCondition.ToString(), trial.script.name, trial.questionIndex, _currentTeamName,
                                   seconds, _backPresses[_currentTrialIndex]);
    }

    private void HideAugmentations()
    {
        if (augmentationPanel != null) augmentationPanel.SetActive(false);
        if (interacter != null) interacter.SetActive(false);
    }

    /// <summary>True once the first T after the last question has cleared the room.</summary>
    public bool IsCleared { get; private set; }

    /// <summary>
    /// First T after the last question (MasterLevelController): the avatars, the table or grid and the augmentations
    /// disappear and the wall shows the end message. The data is already saved, so Back no longer works; the next T
    /// loads the next condition.
    /// </summary>
    public void ClearRoom()
    {
        IsCleared = true;
        foreach (var agent in _currentAgents) StopSpeakingAndDestroy(agent);
        _currentAgents.Clear();
        HideAugmentations();
        if (gridGenerator) gridGenerator.Clear();
        if (tableSetting) tableSetting.SetActive(false);
        ShowWallText(EndMessage, endMessageFontSize);
    }

    // --- Wizard input ---

    /// <summary>True while a Back press would undo something (and the condition's data is not saved yet). Never in training.</summary>
    public bool CanGoBack => _canGoBack && !_saved && !IsTraining;

    /// <summary>Back button on the experimenter panel; Backspace does the same (see Update).</summary>
    public void RequestBack()
    {
        if (CanGoBack) _backRequested = true;
    }

    private bool ConsumeBack()
    {
        if (!_backRequested) return false;
        _backRequested = false;
        return CanGoBack;
    }

    /// <summary>
    /// Waits for the wizard: Space or the Quest 'A' button (when allowNext), or Back (when allowBack). In training the
    /// participant's 'X' button counts as Next too. The answer is left in _wizardInput.
    /// </summary>
    private IEnumerator WaitForWizard(bool allowNext = true, bool allowBack = false)
    {
        _wizardInput = WizardInput.None;
        _backRequested = false;
        _canGoBack = allowBack;
        Debug.Log($"[SessionController] {CurrentStep}: waiting for the wizard...");

        // Safety buffer so the Space press that started this step cannot also end it. Back is accepted at once:
        // it is usually pressed right after a mistaken Space.
        float acceptNextAt = Time.time + 0.5f;

        while (_wizardInput == WizardInput.None)
        {
            if (ConsumeBack())
            {
                _wizardInput = WizardInput.Back;
                break;
            }

            if (allowNext && Time.time >= acceptNextAt)
            {
                // 1. Check Keyboard
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    _wizardInput = WizardInput.Next;
                    break;
                }

                // 2. Check VR Controllers (PrimaryButton): A on the right and, in training, the participant's X on the left
                if (TryGetPressedPrimaryButton(InputDeviceCharacteristics.Right, out InputDevice device) ||
                    (IsTraining && TryGetPressedPrimaryButton(InputDeviceCharacteristics.Left, out device)))
                {
                    // Wait for release to prevent skipping next phase
                    bool isPressed = true;
                    while (isPressed)
                    {
                        device.TryGetFeatureValue(CommonUsages.primaryButton, out isPressed);
                        yield return null;
                    }
                    _wizardInput = WizardInput.Next;
                    break;
                }
            }

            yield return null;
        }

        _canGoBack = false;
        Debug.Log($"[SessionController] {CurrentStep}: wizard pressed {(_wizardInput == WizardInput.Back ? "Back" : "Space")}.");
    }

    /// <summary>The primary button of the controller in that hand: A on the right controller, X on the left.</summary>
    private static bool TryGetPressedPrimaryButton(InputDeviceCharacteristics hand, out InputDevice pressed)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(hand | InputDeviceCharacteristics.Controller, devices);
        foreach (var device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool isPressed) && isPressed)
            {
                pressed = device;
                return true;
            }
        }
        pressed = default;
        return false;
    }

    /// <summary>
    /// Runs a step that Back can stop part-way; afterwards _interrupted says whether it was stopped.
    /// The routine and the IEnumerators it yields are stepped here, inside this coroutine, so Back drops the whole
    /// chain between two frames without relying on StopCoroutine; what the chain started elsewhere (speech in the
    /// manager and TTS, a walk's animation graph) is stopped by the caller with StopAllAgents.
    /// </summary>
    private IEnumerator RunInterruptible(IEnumerator routine)
    {
        _interrupted = false;
        _backRequested = false;
        _canGoBack = true;
        var chain = new Stack<IEnumerator>();
        chain.Push(routine);
        while (chain.Count > 0)
        {
            if (ConsumeBack())
            {
                _interrupted = true;
                foreach (var e in chain) (e as IDisposable)?.Dispose();
                break;
            }
            var top = chain.Peek();
            if (!top.MoveNext())
            {
                chain.Pop();
                continue;
            }
            if (top.Current is IEnumerator nested)
            {
                chain.Push(nested);
                continue;
            }
            yield return top.Current; // null or a YieldInstruction: let Unity do the waiting
        }
        _canGoBack = false;
        _statusDetail = "";
    }

    // --- Experimenter panel ---

    /// <summary>What is happening and what Space / Back do now (rich text), for the experimenter panel.</summary>
    public string WizardStatus
    {
        get
        {
            int n = _trialSequence.Count;
            int t = _currentTrialIndex;
            string trial = $"Trial {t + 1}/{n}";
            string back = CanGoBack ? "   Back: " : "";
            switch (CurrentStep)
            {
                case SessionStep.Introduction:
                    return _statusDetail != ""
                        ? $"<b>{_statusDetail}</b>"
                        : $"<b>Avatars in place, {(IsTraining ? $"wall shows \"{Training.introductionPrompt}\"" : "wall blank")}.</b>\n" +
                          "Space: the avatars introduce themselves.";
                case SessionStep.AwaitQuestion:
                    return $"<b>{trial} - wall shows \"{ReadyMessage}\".</b>\nSpace: show question {t + 1}." +
                           (CanGoBack ? $"{back}return to the trial {t} review." : "");
                case SessionStep.Question:
                    return $"<b>{trial} - question shown; the participant reads it out loud.</b>\n" +
                           $"Space: the avatars {(IsGridCondition ? "take their places and " : "")}discuss." +
                           (CanGoBack ? $"{back}hide the question." : "");
                case SessionStep.Discussion:
                    return $"<b>{trial} - {_statusDetail}</b>\n" +
                           (isTestMode ? "Space: skip the discussion (test mode).   " : "") +
                           (CanGoBack ? "Back: stop and return to the question." : "");
                case SessionStep.Review:
                    float seconds = _reviewSeconds[t] + (Time.time - _reviewStartedAt);
                    return $"<b>{trial} - participant reviewing: {seconds:F0} s</b>\n" +
                           $"Space: end the review{(IsGridCondition ? "; the avatars walk back" : "")}.";
                case SessionStep.ReturnAgents:
                    return $"<b>{trial} - avatars walking back...</b>\n" + (CanGoBack ? "Back: return to the review." : "");
                case SessionStep.Finished:
                    if (IsTraining)
                        return IsCleared ? "<b>Training complete; the wall shows the end message.</b>\nStop Play mode."
                                         : $"<b>Conversation done; the wall shows \"{Training.nextRoomPrompt}\".</b>\nSpace: go to the next room.";
                    if (IsCleared) return "<b>Room cleared; the wall shows the end message.</b>\nT: continue.";
                    return $"<b>All {n} trials done.</b>\n" +
                           (MasterLevelController.Instance != null ? "T: save the data and clear the room." : "Stop Play mode to save the data.") +
                           (CanGoBack ? $"{back}return to the trial {n} review." : "");
                default:
                    return "<b>Setting up...</b>";
            }
        }
    }

    private GUIStyle _panelLabelStyle;

    /// <summary>Extra panel height for the warnings DrawWizardPanel shows (passthrough, room calibration).</summary>
    public float WarningsHeight =>
        (HasPassthroughProblem ? 50f : 0f) +
        (RoomCalibration.Instance != null && RoomCalibration.Instance.Problem != null ? 50f : 0f);
    
    /// <summary>
    /// Draws the status and the Back button with GUILayout. Call from OnGUI: MasterLevelController's panel does, and
    /// this script's own OnGUI does when the scene runs without the master.
    /// </summary>
    public void DrawWizardPanel()
    {
        if (_panelLabelStyle == null) _panelLabelStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        if (HasPassthroughProblem)
            GUILayout.Label($"<color=#ff7070><b>Passthrough is not running</b> ({_passthroughProblem}). Link app: Settings > " +
                            "Developer > Developer Runtime Features and Passthrough over Meta Horizon Link on, then restart Unity.</color>",
                            _panelLabelStyle);
        if (RoomCalibration.Instance != null && RoomCalibration.Instance.Problem != null)
            GUILayout.Label($"<color=#ffcc40>{RoomCalibration.Instance.Problem}</color>", _panelLabelStyle);
        GUILayout.Label(WizardStatus, _panelLabelStyle);
        if (IsTraining) return; // no Back in training
        GUI.enabled = CanGoBack;
        if (GUILayout.Button("Back (Backspace)", GUILayout.Width(160f), GUILayout.Height(26f))) RequestBack();
        GUI.enabled = true;
    }

    void OnGUI()
    {
        if (MasterLevelController.Instance != null || IsTraining) return; // their panels show the same controls

        // Same scaling as the master's panel: IMGUI's default skin is tiny on large monitors.
        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUILayout.BeginArea(new Rect(10f, 10f, 560f, 110f + WarningsHeight), GUI.skin.box);
        DrawWizardPanel();
        GUILayout.EndArea();
    }

    // --- Phase Implementations ---

    /// <summary>
    /// The trial's question at the top of the back wall, in the wall text's layout from the scene. A prompt (training)
    /// goes on its own line underneath, smaller.
    /// </summary>
    private void ShowWallQuestion(string prompt = "")
    {
        if (wallQuestionText == null)
        {
            Debug.LogWarning("SessionController: Wall Question Text not assigned.");
            return;
        }
        UseWallLayout(centred: false);
        string question = QuestionNumberPrefix() + _currentQuestionData.questionText;
        if (!string.IsNullOrEmpty(prompt)) question += $"\n<size=75%><i>{prompt}</i></size>";
        // Test mode is surfaced on the wall itself so it can never run unnoticed in a real session.
        wallQuestionText.text = isTestMode ? "[TEST MODE] " + question : question;
    }

    /// <summary>"<b>Q2</b> " (a bold Q2) for the current trial, from Question Number Format; "" when that is empty.</summary>
    private string QuestionNumberPrefix()
    {
        if (string.IsNullOrEmpty(questionNumberFormat)) return "";
        int number = _trialSequence[_currentTrialIndex].questionIndex;
        // The format is inspector-editable; a stray brace must not stop the trial.
        try
        {
            return string.Format(questionNumberFormat, number);
        }
        catch (FormatException)
        {
            Debug.LogError($"[SessionController] Question Number Format '{questionNumberFormat}' is not a valid format " +
                           "string (use {0} for the number). Showing a bold Q and the number instead.");
            return $"<b>Q{number}</b> ";
        }
    }

    // The last condition of a session has no next trial, so its wall only says that the conversation is over. The
    // training has its own end message.
    private string EndMessage =>
        IsTraining ? Training.endMessage
        : MasterLevelController.Instance != null && MasterLevelController.Instance.IsLastConditionOfSession
            ? lastConditionEndMessage
            : endMessage;

    // In training the ready message tells the participant to press X.
    private string ReadyMessage => IsTraining ? Training.readyPrompt : readyMessage;

    /// <summary>The ready message, large and centred on the back wall; "" leaves the wall blank.</summary>
    private void ShowWallText(string message) => ShowWallText(message, readyMessageFontSize);

    /// <summary>A message centred on the back wall at the given font size; "" leaves the wall blank.</summary>
    private void ShowWallText(string message, float fontSize)
    {
        if (wallQuestionText == null) return;
        bool show = !string.IsNullOrEmpty(message);
        UseWallLayout(show, fontSize);
        wallQuestionText.text = show && isTestMode ? "[TEST MODE] " + message : message ?? "";
    }

    // The wall text's layout as set up in the scene, captured on first use so the centred message can switch back.
    private bool _wallLayoutSaved;
    private Vector2 _questionPosition, _questionSize;
    private float _questionFontSize;
    private TextAlignmentOptions _questionAlignment;

    private void UseWallLayout(bool centred, float fontSize = 0f)
    {
        var rect = wallQuestionText.rectTransform;
        if (!_wallLayoutSaved)
        {
            _questionPosition = rect.anchoredPosition;
            _questionSize = rect.sizeDelta;
            _questionFontSize = wallQuestionText.fontSize;
            _questionAlignment = wallQuestionText.alignment;
            _wallLayoutSaved = true;
        }

        if (centred)
        {
            // The text is anchored to the middle of the wall canvas, so position (0, 0) is the centre of the wall.
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(_questionSize.x, fontSize * 3f); // room for two lines
            wallQuestionText.fontSize = fontSize;
            wallQuestionText.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            rect.anchoredPosition = _questionPosition;
            rect.sizeDelta = _questionSize;
            wallQuestionText.fontSize = _questionFontSize;
            wallQuestionText.alignment = _questionAlignment;
        }
    }

    /// <summary>
    /// Avatars introduce themselves in casting order (agent_1..agent_4). Runs once per condition,
    /// before the first discussion, so introductions never contaminate Conversation-phase gaze data.
    /// The content and structure are identical in all four conditions (a constant addition).
    /// </summary>
    private IEnumerator IntroductionPhase()
    {
        Debug.Log("SessionController: Introduction Phase.");
        _statusDetail = "Avatars introducing themselves...";

        foreach (var agt in _currentAgents) agt.sentences.Clear();

        var steps = new List<ConversationStep>();
        foreach (var agent in _currentAgents.OrderBy(a => a.agentName))
        {
            agent.sentences.Add(BuildIntroductionLine(agent.displayName));
            steps.Add(new ConversationStep { agent = agent, sentenceIndex = agent.sentences.Count - 1 });
        }

        yield return PlayStepsAndWait(steps, allowTestSkip: false);
        _statusDetail = "";
        Debug.Log("SessionController: Introductions complete.");
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
    /// Shared playback for introductions and discussions: hand the steps to the manager, play, and wait for
    /// completion (optionally allowing the test-mode Space skip). The next WaitForWizard has its own 0.5 s guard
    /// against the finishing frame's input passing through.
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
                StopAllAgents();
                Debug.Log("SessionController: Test Mode - Discussion Skipped.");
                break;
            }
            yield return null;
        }
    }

    /// <summary>Silences the avatars and ends any walk mid-step (after Back stopped the step that started them).</summary>
    private void StopAllAgents()
    {
        if (manager != null) manager.Stop();
        foreach (var agent in _currentAgents)
        {
            if (agent == null) continue;
            var tts = agent.GetComponent<CrossPlatformTTS>();
            if (tts) tts.Stop();
            // Manually force animation state to false since tts.Stop() might kill the callback
            agent.SendMessage("SetTalkingState", false, SendMessageOptions.DontRequireReceiver);
            var locomotion = agent.GetComponent<AgentLocomotion>();
            if (locomotion) locomotion.StopWalk();
        }
    }

    private void BuildGrid()
    {
        if (!gridGenerator) return;
        Debug.Log("SessionController: Building Grid 5 Rows x 4 Cols");
        gridGenerator.labelAlignment = GridGenerator.LabelAlignment.Left;
        gridGenerator.Build(RowRanks.Length, 4);
        gridGenerator.BuildLabels(new List<string>(RowRanks));
    }

    /// <summary>Spawns the condition's four avatars once; they stay for all its trials and return to these spots.</summary>
    private void SpawnTeam()
    {
        foreach (var agent in _currentAgents) StopSpeakingAndDestroy(agent);
        _currentAgents.Clear();
        _home.Clear();

        Debug.Log($"SessionController: Setup ({studyCondition}). Spawning avatars.");

        if (IsGridCondition)
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
        else
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
        FitGazeTargetToTorso(go);
        // The avatars are moved only by script. Their prefabs carry a non-kinematic Rigidbody (no gravity, no drag),
        // so anything that touched them - the gaze cursor, the table, the floor - shoved and spun them for good.
        foreach (var body in go.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
        var agt = go.GetComponent<ConversationalAgent>();
        if (!agt) agt = go.AddComponent<ConversationalAgent>();
        agt.agentName = id;
        agt.displayName = prefab.name; // Preserve original prefab name (e.g. Tony)

        // Ensure TTS is ready
        var tts = go.GetComponent<CrossPlatformTTS>();
        if (!tts) tts = go.AddComponent<CrossPlatformTTS>();
        // Walk cycle for scripted moves (see MoveAgent)
        if (!go.GetComponent<AgentLocomotion>()) go.AddComponent<AgentLocomotion>();

        _currentAgents.Add(agt);
        _home[agt] = (go.transform.position, go.transform.rotation);

        // Hide individual augmentation canvas initially
        var canvas = go.GetComponentInChildren<Canvas>(true);
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

// --- Audio preloading ---

    /// <summary>
    /// Loads every line a trial will speak (and the introductions, before the first trial) into memory while the
    /// wizard is still waiting to show the question, so no turn waits for audio. A clip baked into
    /// StreamingAssets/AudioCache (Tools/iXR/Bake All Session Audio) only has to be read; a line without one
    /// is synthesized by Piper here instead of mid-discussion.
    /// </summary>
    private void StartAudioPreload(int trialIndex, bool includeIntroductions)
    {
        // Already loading (or loaded) this trial's lines, e.g. after Back: restarting would abandon a Piper run mid-line.
        if (trialIndex == _preloadTrial && !includeIntroductions) return;

        var lines = new List<(CrossPlatformTTS tts, string text)>();

        if (includeIntroductions)
        {
            foreach (var agent in _currentAgents)
                lines.Add((agent.GetComponent<CrossPlatformTTS>(), BuildIntroductionLine(agent.displayName)));
        }

        // Same parsing as DiscussionPhase, so the preloaded text is exactly the text that is spoken.
        var trial = _trialSequence[trialIndex];
        string responses = ManuscriptJson.GetObject(ManuscriptJson.GetQuestion(trial.script.text, trial.questionIndex), "glb_Responses");
        foreach (var agentLines in ManuscriptJson.GetLinesByAgent(responses))
        {
            var agent = _currentAgents.FirstOrDefault(a => a.agentName == $"agent_{agentLines.Key}");
            if (agent == null) continue;
            foreach (string text in agentLines.Value)
                lines.Add((agent.GetComponent<CrossPlatformTTS>(), text));
        }

        _preloadTrial = trialIndex;
        if (_audioPreload != null) StopCoroutine(_audioPreload);
        _audioPreloaded = false;
        _audioPreload = StartCoroutine(PreloadAudio(lines));
    }

    private IEnumerator PreloadAudio(List<(CrossPlatformTTS tts, string text)> lines)
    {
        float start = Time.realtimeSinceStartup;
        foreach (var (tts, text) in lines)
        {
            if (tts != null) yield return tts.PrepareRoutine(text);
        }
        _audioPreloaded = true;
        _audioPreload = null;
        Debug.Log($"[SessionController] Preloaded {lines.Count} lines in {Time.realtimeSinceStartup - start:F1}s.");
    }

    /// <summary>Holds until StartAudioPreload has finished, so every turn starts on the fixed pause between lines.</summary>
    private IEnumerator WaitForAudioPreload()
    {
        if (_audioPreloaded) yield break;
        Debug.Log("[SessionController] Waiting for this trial's audio to finish loading...");
        while (!_audioPreloaded) yield return null;
    }

    /// <summary>
    /// Grid cell of each seated avatar in this trial, in order_Seating (the order they walk in). Empty in the
    /// table conditions.
    /// </summary>
    private List<(ConversationalAgent agent, Vector3 seat)> GetSeats()
    {
        var seats = new List<(ConversationalAgent agent, Vector3 seat)>();
        if (!IsGridCondition || !gridGenerator) return seats;

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

            if (string.IsNullOrEmpty(rank) || !rankToRow.ContainsKey(rank))
            {
                Debug.LogWarning($"SessionController: Unknown rank '{rank}' for agent {id}");
                continue;
            }

            int row = rankToRow[rank];
            int col = nextColInRow[row];
            nextColInRow[row]++;

            seats.Add((agent, gridGenerator.GridToWorld(row, col)));
        }
        return seats;
    }

    private IEnumerator MoveToSeats()
    {
        Debug.Log("SessionController: Avatars walking to their seats.");
        foreach (var (agent, seat) in GetSeats())
        {
            Debug.Log($"[TRIAL DEBUG] {agent.agentName} -> {seat}");
            yield return MoveAgent(agent.transform, seat, TravelTime(agent.transform.position, seat));
        }
    }

    /// <summary>
    /// Walks the seated avatars back to where they spawned, in reverse seating order: the last to sit down leaves
    /// first, so each one retraces its walk in past the same avatars it passed then.
    /// </summary>
    private IEnumerator WalkHome()
    {
        var seats = GetSeats();
        seats.Reverse();
        foreach (var (agent, _) in seats)
        {
            if (!_home.TryGetValue(agent, out var home)) continue;
            yield return MoveAgent(agent.transform, home.position, TravelTime(agent.transform.position, home.position));
        }
    }

    private float TravelTime(Vector3 from, Vector3 to)
    {
        return isTestMode ? 0f :
            walkSpeed > 0f ? Vector3.Distance(from, to) / walkSpeed :
            moveDuration;
    }

    /// <summary>Puts every avatar back where it spawned at once (Back undoing a walk to the grid).</summary>
    private void SnapAgentsHome()
    {
        foreach (var agent in _currentAgents)
        {
            if (agent == null || !_home.TryGetValue(agent, out var home)) continue;
            agent.transform.position = home.position;
            agent.transform.rotation = home.rotation;
        }
    }

    /// <summary>Puts the avatars on this trial's grid seats at once (Back returning to a review).</summary>
    private void SnapAgentsToSeats()
    {
        // Seated avatars face the way they spawned (AgentLocomotion turns them back on arrival).
        SnapAgentsHome();
        foreach (var (agent, seat) in GetSeats()) agent.transform.position = seat;
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

    private IEnumerator DiscussionPhase()
    {
        Debug.Log("SessionController: Discussion.");

        // Read the lines from the raw text: JsonUtility (used for _currentQuestionData) drops duplicate
        // keys and does not know the "agent_N_Resp_K" keys of an agent's repeat lines.
        var trial = _trialSequence[_currentTrialIndex];
        string qKey = $"question_{trial.questionIndex}_Data";
        string responses = ManuscriptJson.GetObject(ManuscriptJson.GetQuestion(trial.script.text, trial.questionIndex), "glb_Responses");
        if (responses == "")
        {
            Debug.LogError($"[SessionController] Could not find {qKey}.glb_Responses in {trial.script.name}.");
            yield break;
        }

        var speechOrder = _currentQuestionData.glb_Responses.order_Speech;
        if (speechOrder == null || speechOrder.Length == 0) yield break;

        // Each turn in order_Speech takes that agent's next line, so a line is always spoken by the agent
        // named in its key (matching its summary and stats), and an agent who speaks twice
        // ("agent_1_Resp", later "agent_1_Resp_2") says both lines on its two turns.
        var linesByAgent = ManuscriptJson.GetLinesByAgent(responses);
        Debug.Log($"[TRIAL DEBUG] {qKey}: {linesByAgent.Values.Sum(q => q.Count)} lines for {speechOrder.Length} speech turns.");

        var steps = new List<ConversationStep>();

        foreach (var agt in _currentAgents) agt.sentences.Clear();

        foreach (int agentNum in speechOrder)
        {
            string id = $"agent_{agentNum}";
            var agent = _currentAgents.FirstOrDefault(a => a.agentName == id);
            if (!agent) continue;

            string text = "";
            if (linesByAgent.TryGetValue(agentNum, out var agentLines) && agentLines.Count > 0)
            {
                text = agentLines.Dequeue();
            }
            else
            {
                Debug.LogWarning($"[SessionController] {qKey}: order_Speech gives {id} more turns than it has lines; that turn is silent.");
            }

            agent.sentences.Add(text);
            int idx = agent.sentences.Count - 1;
            steps.Add(new ConversationStep { agent = agent, sentenceIndex = idx });
        }

        foreach (var unspoken in linesByAgent.Where(kv => kv.Value.Count > 0))
        {
            Debug.LogWarning($"[SessionController] {qKey}: agent_{unspoken.Key} has {unspoken.Value.Count} line(s) " +
                             "with no turn in order_Speech; they are not spoken.");
        }

        yield return PlayStepsAndWait(steps, allowTestSkip: true);

        Debug.Log("SessionController: Discussion Finished.");
    }

    private void ShowGroupAugmentations()
    {
        if (augmentationPanel != null) augmentationPanel.SetActive(true);

        var trial = _trialSequence[_currentTrialIndex];
        // All lookups are scoped to this question, so a null/missing value shows as empty
        // instead of the next question's value.
        string question = ManuscriptJson.GetQuestion(trial.script.text, trial.questionIndex);
        if (question == "") return;

        // 1. Extract Text Augmentations (glb_Grp_Sums)
        string grpSums = ManuscriptJson.GetObject(question, "glb_Grp_Sums");
        // Suggestions
        string suggestions = ManuscriptJson.GetString(grpSums, "glb_Grp_Suggestions");
        if (suggestionsText != null)
            suggestionsText.text = ManuscriptJson.FormatBullets(suggestions);

        // Themes
        string themes = ManuscriptJson.GetString(grpSums, "glb_Emg_Themes");
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

        // 2. Extract Image or Stat Augmentations based on condition
        if (studyCondition == StudyCondition.iAA)
        {
            // Toggle visibility
            if (speakingSumImage != null) speakingSumImage.gameObject.SetActive(true);
            if (grpMoveImage != null) grpMoveImage.gameObject.SetActive(true);
            if (grpSimMatImage != null) grpSimMatImage.gameObject.SetActive(true);
            if (turnTakingText != null) turnTakingText.gameObject.SetActive(false);
            if (dissonantOpinionsText != null) dissonantOpinionsText.gameObject.SetActive(false);

            string aprGrpSums = ManuscriptJson.GetObject(question, "apr_Grp_Sums");
            string speakingPath = ManuscriptJson.GetString(aprGrpSums, "speaking_Sum");
            string movePath = ManuscriptJson.GetString(aprGrpSums, "grp_Move");
            // The *_Grp_Sim_Mat image: "sim_Mat" in current manuscripts, "grp_Sim_Mat" in older ones.
            string simMatPath = ManuscriptJson.GetString(aprGrpSums, "sim_Mat");
            if (simMatPath == "") simMatPath = ManuscriptJson.GetString(aprGrpSums, "grp_Sim_Mat");

            if (speakingSumImage != null) StartCoroutine(ManuscriptJson.LoadImage(speakingPath, speakingSumImage));
            if (grpMoveImage != null) StartCoroutine(ManuscriptJson.LoadImage(movePath, grpMoveImage));
            if (grpSimMatImage != null) StartCoroutine(ManuscriptJson.LoadImage(simMatPath, grpSimMatImage));
        }
        else if (studyCondition == StudyCondition.iAT)
        {
            // Toggle visibility
            if (speakingSumImage != null) speakingSumImage.gameObject.SetActive(false);
            if (grpMoveImage != null) grpMoveImage.gameObject.SetActive(false);
            if (grpSimMatImage != null) grpSimMatImage.gameObject.SetActive(false);
            if (turnTakingText != null) turnTakingText.gameObject.SetActive(true);
            if (dissonantOpinionsText != null) dissonantOpinionsText.gameObject.SetActive(true);

            string trdGrpSum = ManuscriptJson.GetObject(question, "trd_Grp_Sum");
            if (turnTakingText != null) turnTakingText.text = ManuscriptJson.GetValue(trdGrpSum, "turn_Taking");
            if (dissonantOpinionsText != null) dissonantOpinionsText.text = ManuscriptJson.GetValue(trdGrpSum, "no_Diss_Ops");
        }
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
        // Walk (turn, step, turn back) when a walk clip is available; otherwise glide as before.
        var locomotion = t.GetComponent<AgentLocomotion>();
        if (locomotion != null && locomotion.CanWalk(dest, duration))
        {
            yield return locomotion.WalkTo(dest, duration);
            yield break;
        }
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

    // Front-to-back size (m) of each avatar's gaze target; the chest is about this deep.
    private const float TorsoDepth = 0.25f;

    /// <summary>
    /// Shrinks the avatar's gaze target - the BoxCollider on its root, which the cursor, the gaze log and the cards all
    /// hit - from the whole body to the upper torso: from the lower chest (Bip01 Spine1) to the base of the neck
    /// (Bip01 Neck), as wide as the shoulder joints, so the head, arms and legs are not part of it. Set once from the
    /// avatar's own skeleton, so it fits each avatar's height and build.
    /// </summary>
    private static void FitGazeTargetToTorso(GameObject avatar)
    {
        var box = avatar.GetComponent<BoxCollider>();
        Transform chest = FindBone(avatar.transform, "Bip01 Spine1");
        Transform neck = FindBone(avatar.transform, "Bip01 Neck");
        Transform leftArm = FindBone(avatar.transform, "Bip01 L UpperArm");
        Transform rightArm = FindBone(avatar.transform, "Bip01 R UpperArm");
        if (box == null || chest == null || neck == null || leftArm == null || rightArm == null)
        {
            Debug.LogWarning($"[SessionController] {avatar.name}: torso bones or BoxCollider not found; its gaze target stays the whole body.");
            return;
        }

        // In the avatar's own space (the collider's), so its scale and facing do not matter.
        Vector3 bottom = avatar.transform.InverseTransformPoint(chest.position);
        Vector3 top = avatar.transform.InverseTransformPoint(neck.position);
        Vector3 left = avatar.transform.InverseTransformPoint(leftArm.position);
        Vector3 right = avatar.transform.InverseTransformPoint(rightArm.position);

        Vector3 across = left - right;
        bool shouldersAlongX = Mathf.Abs(across.x) >= Mathf.Abs(across.z);
        float width = shouldersAlongX ? Mathf.Abs(across.x) : Mathf.Abs(across.z);
        float height = Mathf.Abs(top.y - bottom.y);
        Vector3 middle = (left + right) / 2f;

        box.center = new Vector3(middle.x, (bottom.y + top.y) / 2f, middle.z);
        box.size = shouldersAlongX ? new Vector3(width, height, TorsoDepth) : new Vector3(TorsoDepth, height, width);
    }

    private static Transform FindBone(Transform parent, string boneName)
    {
        if (parent.name == boneName) return parent;
        foreach (Transform child in parent)
        {
            var found = FindBone(child, boneName);
            if (found != null) return found;
        }
        return null;
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
}
