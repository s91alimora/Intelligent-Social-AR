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

    [Header("Study Flow")]
    public string participantID = "test";
    public StudySequence studySequence = StudySequence.Sequence_1_nAT_nAA_iAT_iAA;

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
        GenerateTrialBlocks();
        LoadConditionScene(0);
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
            qList = qList.OrderBy(x => Random.value).ToList();
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
            block = block.OrderBy(x => Random.value).ToList();
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
