using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class MasterLevelController : MonoBehaviour
{
    public static MasterLevelController Instance { get; private set; }

    public enum StudySequence
    {
        Sequence_1_nAT_nAA_iAT_iAA,
        Sequence_2_nAA_iAT_iAA_nAT,
        Sequence_3_iAT_iAA_nAT_nAA,
        Sequence_4_iAA_nAT_nAA_iAT
    }

    [Serializable]
    public class AvatarTeam
    {
        public string teamName;
        [Tooltip("Exactly 4 prefabs (2M + 2F). List order is the fixed casting: element 0 -> agent_1, 1 -> agent_2, etc.")]
        public List<GameObject> members = new();
    }

    [Header("Study Flow")]
    public string participantID = "test";
    public StudySequence studySequence = StudySequence.Sequence_1_nAT_nAA_iAT_iAA;

    [Header("Avatar Teams (fixed per trial position)")]
    [Tooltip("Team at index k is used for trial position k+1 in EVERY sequence (Team A always first, B second, ...). " +
             "Because condition order rotates across the four sequences, each team meets each condition exactly once " +
             "across sequences - avatar identity is counterbalanced against condition by construction.")]
    public List<AvatarTeam> avatarTeams = new();

    [Header("Scene Names")]
    public string iAA_SceneName = "testbed iAA";
    public string iAT_SceneName = "testbed iAT";
    public string nAA_SceneName = "testbed nAA";
    public string nAT_SceneName = "testbed nAT";

    // Internal State
    private int _currentConditionIndex = 0;
    
    // Each condition block contains exactly 4 trials: (scriptIndex, questionIndex)
    // scriptIndex: 0-3 (corresponding to Script_1 to Script_4 assignments in SessionController)
    // questionIndex: 1-4
    private List<List<(int scriptIndex, int questionIndex)>> _conditionTrialBlocks = new();

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
        GenerateTrialBlocks();
        LoadConditionScene(0);
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

        _avatarTeamsValid = valid;
        if (!valid)
            Debug.LogError("[MasterLevelController] Avatar team configuration is INVALID - team lookup is disabled for ALL conditions " +
                           "and SessionController will fall back to random selection. Fix the teams before running a study.");
    }

    /// <summary>
    /// Fixed lookup: the team for the CURRENT trial position (condition block index).
    /// Team order does NOT rotate across sequences - Team A is always trial 1, Team B trial 2, etc.
    /// Condition order DOES rotate across the four sequences, so every team meets every condition
    /// exactly once across sequences (counterbalanced, never confounded with condition).
    /// Returns null if teams are not (fully) configured, so callers can fall back.
    /// </summary>
    public List<GameObject> GetTeamForCurrentCondition()
    {
        if (!_avatarTeamsValid) return null;
        if (_currentConditionIndex < 0 || _currentConditionIndex >= 4) return null;
        return avatarTeams[_currentConditionIndex].members;
    }

    public string GetCurrentTeamName()
    {
        if (!_avatarTeamsValid) return "";
        if (_currentConditionIndex < 0 || _currentConditionIndex >= 4) return "";
        return avatarTeams[_currentConditionIndex].teamName ?? "";
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
        // Jump to next condition if T is pressed and current condition is fully finished
        if (Input.GetKeyDown(KeyCode.T))
        {
            var session = SessionController.Instance;
            if (session != null && session.isExperimentFinished)
            {
                _currentConditionIndex++;
                if (_currentConditionIndex < 4)
                {
                    Debug.Log($"[MasterLevelController] Loading condition {_currentConditionIndex + 1}/4...");
                    LoadConditionScene(_currentConditionIndex);
                }
                else
                {
                    Debug.Log("[MasterLevelController] Entire User Study Finished! All 16 trials complete.");
                    // Optional: load a "Thank you" scene or quit
                }
            }
            else
            {
                Debug.LogWarning("[MasterLevelController] Cannot transition yet. Current session is not finished.");
            }
        }
    }

    /// <summary>
    /// Creates 4 blocks of trials such that:
    /// - Every block uses exactly one question from each of the 4 scripts.
    /// - Over the whole experiment (4 blocks), every script/question pairing is used exactly once.
    /// </summary>
    private void GenerateTrialBlocks()
    {
        _conditionTrialBlocks.Clear();

        // Step 1: Create a shuffled list of question numbers [1, 2, 3, 4] for each script [0, 1, 2, 3]
        List<List<int>> shuffledQuestionsPerScript = new();
        for (int scriptIndex = 0; scriptIndex < 4; scriptIndex++)
        {
            var qList = new List<int> { 1, 2, 3, 4 };
            qList = qList.OrderBy(x => UnityEngine.Random.value).ToList();
            shuffledQuestionsPerScript.Add(qList);
        }

        // Step 2: Assign these to the 4 condition blocks
        for (int conditionIndex = 0; conditionIndex < 4; conditionIndex++)
        {
            var block = new List<(int scriptIndex, int questionIndex)>();
            
            for (int scriptIndex = 0; scriptIndex < 4; scriptIndex++)
            {
                int qNum = shuffledQuestionsPerScript[scriptIndex][conditionIndex];
                block.Add((scriptIndex, qNum));
            }

            // Shuffle the order of the 4 trials inside this specific condition block
            block = block.OrderBy(x => UnityEngine.Random.value).ToList();
            _conditionTrialBlocks.Add(block);
        }
    }

    public List<(int scriptIndex, int questionIndex)> GetTrialsForCurrentCondition()
    {
        if (_currentConditionIndex >= 0 && _currentConditionIndex < _conditionTrialBlocks.Count)
        {
            return _conditionTrialBlocks[_currentConditionIndex];
        }
        return new List<(int, int)>();
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
        SessionController.StudyCondition[] seq = studySequence switch {
            StudySequence.Sequence_1_nAT_nAA_iAT_iAA => new[] { SessionController.StudyCondition.nAT, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT, SessionController.StudyCondition.iAA },
            StudySequence.Sequence_2_nAA_iAT_iAA_nAT => new[] { SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT, SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAT },
            StudySequence.Sequence_3_iAT_iAA_nAT_nAA => new[] { SessionController.StudyCondition.iAT, SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAT, SessionController.StudyCondition.nAA },
            StudySequence.Sequence_4_iAA_nAT_nAA_iAT => new[] { SessionController.StudyCondition.iAA, SessionController.StudyCondition.nAT, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT },
            _ => new[] { SessionController.StudyCondition.nAT, SessionController.StudyCondition.nAA, SessionController.StudyCondition.iAT, SessionController.StudyCondition.iAA }
        };
        return seq[index];
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
}
