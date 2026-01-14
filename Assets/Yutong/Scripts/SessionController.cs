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

[DisallowMultipleComponent]
public class SessionController : MonoBehaviour
{
    public static SessionController Instance { get; private set; }

    public enum SessionPhase { Setup, GridMove, Discussion, AR_UI }
    public SessionPhase CurrentPhase { get; private set; }

    public enum StudyCondition { iAA, ARR }
    
    [Header("Study State")]
    public StudyCondition studyCondition = StudyCondition.iAA;

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
    
    [Header("ARR Settings")]
    public Transform[] agentOrigins = new Transform[4];

    [Header("Augmentation UI")]
    //public Text wallQuestionText; // Assign the text on the wall
    public TextMeshProUGUI wallQuestionText;
    public GameObject augmentationPanel;
    public TextMeshProUGUI suggestionsText;
    public TextMeshProUGUI themesText;
    public Image speakingSumImage;
    public Image grpMoveImage;
    public Image grpSimMatImage;
    public TextMeshProUGUI turnTakingText;
    public TextMeshProUGUI dissonantOpinionsText;
    public AgentAugmentationInteracter interacter;

    // Internal State
    private Dictionary<string, GameObject> _agentMapping = new(); // "agent_1" -> Prefab
    private List<ConversationalAgent> _currentAgents = new(); // Spawned instances
    private List<(TextAsset script, int questionIndex)> _trialSequence = new();
    private int _currentTrialIndex = 0;
    
    // Current Trial Data
    private QuestionData _currentQuestionData;

    void Awake()
    {
        Instance = this;
        if (gridGenerator == null) gridGenerator = FindObjectOfType<GridGenerator>();
        if (manager == null) manager = FindObjectOfType<ConversationalAgentsManager>();
    }

    void Start()
    {
        StartCoroutine(SetupExperiment());
    }

    private IEnumerator SetupExperiment()
    {
        // 1. Select Avatars (2 Male, 2 Female)
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

        // Persistent mapping for "agent_1" to "agent_4"
        _agentMapping.Clear();
        for (int i = 0; i < 4; i++)
        {
            _agentMapping[$"agent_{i + 1}"] = selected[i];
        }

        // 2. Generate Trial Sequence (4 Trials)
        // Each script used once. Random question (1-4) from each script.
        _trialSequence.Clear();
        if (scriptFiles.Count < 4)
        {
            Debug.LogError("Need 4 Script JSON files assigned!");
            yield break;
        }

        var shuffledScripts = scriptFiles.OrderBy(x => UnityEngine.Random.value).Take(4).ToList();
        foreach (var script in shuffledScripts)
        {
            int qIndex = UnityEngine.Random.Range(1, 4 + 1); // 1 to 4
            _trialSequence.Add((script, qIndex));
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
            yield break;
        }

        // Parse Data
        var trial = _trialSequence[trialIndex];
        _currentQuestionData = ParseQuestionData(trial.script, trial.questionIndex);

        if (studyCondition == StudyCondition.iAA)
        {
            yield return StartTrial_iAA(trialIndex);
        }
        else if (studyCondition == StudyCondition.ARR)
        {
            yield return StartTrial_ARR(trialIndex);
        }
    }

    private IEnumerator StartTrial_iAA(int trialIndex)
    {
        var trial = _trialSequence[trialIndex];
        
        // Phase 1: Setup (Line Up)
        CurrentPhase = SessionPhase.Setup;
        SetupPhase1();

        // Wait for Space -> Phase 2
        yield return WaitForKey(KeyCode.Space);

        // Phase 2: Move to Grid
        CurrentPhase = SessionPhase.GridMove;
        yield return MovePhase2();

        // Wait for Space -> Phase 3
        yield return WaitForKey(KeyCode.Space);

        // Phase 3: Discussion
        CurrentPhase = SessionPhase.Discussion;
        yield return DiscussionPhase3();

        // Wait for Space -> Phase 4
        yield return WaitForKey(KeyCode.Space);

        // Phase 4: AR UI
        CurrentPhase = SessionPhase.AR_UI;
        yield return Phase_AR_UI(trial);

        // Wait for Space -> Next Trial
        Debug.Log($"SessionController: Trial {trialIndex} complete. Press Space for next.");
        yield return WaitForKey(KeyCode.Space);

        // Next
        FinishTrial();
    }

    private IEnumerator StartTrial_ARR(int trialIndex)
    {
        var trial = _trialSequence[trialIndex];
        
        // Phase 1: Setup (Origins)
        CurrentPhase = SessionPhase.Setup;
        SetupPhase1();

        // Wait for Space -> Phase 2 (Discussion)
        yield return WaitForKey(KeyCode.Space);

        // Phase 2: Discussion
        CurrentPhase = SessionPhase.Discussion;
        yield return DiscussionPhase3();

        // Wait for Space -> Phase 3 (AR UI)
        yield return WaitForKey(KeyCode.Space);

        // Phase 3: AR UI
        CurrentPhase = SessionPhase.AR_UI;
        yield return Phase_AR_UI(trial);

        // Wait for Space -> Next Trial
        Debug.Log($"SessionController: Trial {trialIndex} complete. Press Space for next.");
        yield return WaitForKey(KeyCode.Space);

        // Next
        FinishTrial();
    }

    private IEnumerator Phase_AR_UI((TextAsset script, int questionIndex) trial)
    {
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

    private void SetupPhase1()
    {
        // Hide Augmentations at start of trial
        if (augmentationPanel != null) augmentationPanel.SetActive(false);
        if (interacter != null) interacter.SetActive(false);

        // Clear previous
        foreach (var agent in _currentAgents) StopSpeakingAndDestroy(agent);
        _currentAgents.Clear();

        // iAA specific setup
        if (studyCondition == StudyCondition.iAA)
        {
            // Build Grid (Static 5x4)
            if (gridGenerator)
            {
                Debug.Log("SessionController: Building Grid 5 Rows x 4 Cols");
                gridGenerator.labelAlignment = GridGenerator.LabelAlignment.Left;
                gridGenerator.Build(5, 4); 
                gridGenerator.BuildLabels(new List<string> { "Very Bad", "Bad", "Neutral", "Good", "Very Good" });
            }
        }

        // Show Question
        if (wallQuestionText != null)
        {
            wallQuestionText.text = _currentQuestionData.questionText;
        }
        else
        {
            Debug.LogWarning("SessionController: Wall Question Text not assigned.");
        }

        // Spawn Avatars
        Debug.Log($"SessionController: Phase 1 Setup ({studyCondition}). Spawning avatars.");
        
        if (studyCondition == StudyCondition.iAA)
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
        else if (studyCondition == StudyCondition.ARR)
        {
            // Spawn at origins
            for (int i = 1; i <= 4; i++)
            {
                string id = $"agent_{i}";
                var prefab = _agentMapping[id];
                Transform origin = agentOrigins[i - 1];
                
                if (origin != null)
                {
                    SpawnAgent(id, prefab, origin.position, origin.rotation);
                }
                else
                {
                    Debug.LogWarning($"SessionController: Agent Origin {i} is missing!");
                    SpawnAgent(id, prefab, Vector3.zero, Quaternion.identity);
                }
            }
        }
    }

    private void SpawnAgent(string id, GameObject prefab, Vector3 pos, Quaternion rot)
    {
        var go = Instantiate(prefab, pos, rot); 
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
        
        var rankToRow = new Dictionary<string, int>
        {
            {"Very Bad", 0}, {"Bad", 1}, {"Neutral", 2}, {"Good", 3}, {"Very Good", 4}
        };

        var nextColInRow = new int[5]; // defaults to 0
        
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
            yield return MoveAgent(agent.transform, target, moveDuration);
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

        manager.agents = _currentAgents;
        manager.sequence = steps;
        manager.playOnStart = false; 

        manager.Play();

        while (manager.IsPlaying) 
        {
            yield return null;
        }
        
        Debug.Log("SessionController: Discussion Finished.");
        // FIX: Add small buffer to prevent accidental Space pass-through
        yield return new WaitForSeconds(0.5f);
        Debug.Log("SessionController: Ready for Phase 4. Press Space.");
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
            if (themesText != null) 
                themesText.text = FormatBulletinPoints(themes);
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

                if (speakingSumImage != null) LoadImageToUI(speakingPath, speakingSumImage);
                if (grpMoveImage != null) LoadImageToUI(movePath, grpMoveImage);
                if (grpSimMatImage != null) LoadImageToUI(simMatPath, grpSimMatImage);
            }
        }
        else if (studyCondition == StudyCondition.ARR)
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
            formatted += "■ " + parts[i].Trim() + (i < parts.Length - 1 ? "\n" : "");
        }
        return formatted;
    }

    private void LoadImageToUI(string relativePath, Image uiImage)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        // Convert relative path (Assets/...) to absolute path
        // Application.dataPath is C:/.../Assets
        // JSON sequence: Assets/Augmentation Images/...
        // So we need to handle the "Assets/" prefix carefully.
        
        string fullPath = "";
        if (relativePath.StartsWith("Assets/"))
        {
            fullPath = Path.Combine(Application.dataPath, relativePath.Substring(7));
        }
        else
        {
            fullPath = Path.Combine(Application.dataPath, relativePath);
        }

        if (File.Exists(fullPath))
        {
            byte[] fileData = File.ReadAllBytes(fullPath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                // Create sprite
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                uiImage.sprite = sprite;
            }
        }
        else
        {
            Debug.LogWarning($"SessionController: Image file not found at {fullPath}");
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
        
        // Initial safety buffer to prevent accidental pass-through
        yield return new WaitForSeconds(0.4f);

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
