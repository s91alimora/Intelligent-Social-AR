using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the training, separately from the study: open the Training scene and press Play. Each participant does it once,
/// before Session 1, to learn where everything is. It plays one conversation in Testbed iAT and then one in Testbed iAA -
/// the study's own scenes, so every element is where the study has it - with the training team and the training
/// manuscripts (Resources/Manuscripts/Training): a weather question, and avatars, names and voices the study never uses.
/// The participant moves on with X on the left controller (Space does the same for the experimenter); there is no Back
/// and nothing is logged. SessionController does the rest; see its IsTraining.
/// </summary>
public class TrainingController : MonoBehaviour
{
    public static TrainingController Instance { get; private set; }

    /// <summary>Resources folder of the training manuscripts, one per room (iAT.json, iAA.json).</summary>
    public const string ManuscriptFolder = "Manuscripts/Training";

    /// <summary>The rooms in order: the table first, then the grid, which adds the walking and the charts.</summary>
    public static readonly SessionController.StudyCondition[] Rooms =
        { SessionController.StudyCondition.iAT, SessionController.StudyCondition.iAA };

    [Tooltip("The four training avatars, made by Tools/iXR/Training/Build Training Avatars. Element 0 plays agent_1, etc.")]
    public List<GameObject> team = new();

    [Header("Scene Names")]
    public string iAT_SceneName = "Testbed iAT";
    public string iAA_SceneName = "Testbed iAA";

    [Header("Wall Prompts")]
    [Tooltip("On the wall before the avatars introduce themselves.")]
    public string introductionPrompt = "Press X to meet the group.";
    [Tooltip("On the wall before the question.")]
    public string readyPrompt = "Press X to see the question.";
    [Tooltip("Under the question until the discussion starts.")]
    public string questionPrompt = "Press X to start the discussion.";
    [Tooltip("Under the question while the participant looks around after the discussion.")]
    public string reviewPrompt = "Look around. Press X when you are done.";
    [Tooltip("On the wall after the conversation in the first room.")]
    public string nextRoomPrompt = "Press X to go to the next room.";
    [Tooltip("On the wall at the end, once the avatars and the grid are gone.")]
    [TextArea(2, 3)] public string endMessage = "End of Training.\nPlease wait.";

    [Header("Time")]
    [Tooltip("Minutes the training should take at most; the panel shows the time used against this.")]
    [Min(1f)] public float timeLimitMinutes = 15f;

    private int _room = -1; // index into Rooms; -1 until the experimenter presses Start
    private float _startedAt;
    private bool _teamValid;

    /// <summary>The condition of the room that is running.</summary>
    public SessionController.StudyCondition CurrentRoom => Rooms[Mathf.Clamp(_room, 0, Rooms.Length - 1)];

    public bool IsLastRoom => _room == Rooms.Length - 1;

    /// <summary>The training avatars in casting order (member i plays agent_{i+1}); null if the team is not set up.</summary>
    public List<GameObject> Team => _teamValid ? team : null;

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
        _teamValid = team != null && team.Count == 4 && team.All(m => m != null && m.GetComponent<ConversationalAgent>() != null);
        if (!_teamValid)
            Debug.LogError("[TrainingController] The training team needs 4 avatar prefabs. Open the Training scene and run " +
                           "Tools/iXR/Training/Build Training Avatars.");
    }

    /// <summary>Called by SessionController when the participant leaves a room that is not the last one.</summary>
    public void NextRoom()
    {
        if (!IsLastRoom) LoadRoom(_room + 1);
    }

    private void LoadRoom(int room)
    {
        _room = room;
        // As between study conditions: free the previous room's audio.
        CrossPlatformTTS.ClearCache();
        string scene = CurrentRoom == SessionController.StudyCondition.iAT ? iAT_SceneName : iAA_SceneName;
        Debug.Log($"[TrainingController] Loading training room {_room + 1}/{Rooms.Length}: {CurrentRoom} ('{scene}').");
        SceneManager.LoadScene(scene);
    }

    // --- Experimenter panel ---
    // Drawn with IMGUI on the PC monitor only, like the master's panel.

    private void OnGUI()
    {
        // IMGUI's default skin is tiny on large monitors; scale it with the window height.
        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (_room < 0) DrawStartPanel(Screen.width / scale, Screen.height / scale);
        else DrawStatus();
    }

    private void DrawStartPanel(float width, float height)
    {
        GUILayout.BeginArea(new Rect((width - 460f) / 2f, 30f, 460f, height - 60f), GUI.skin.box);
        GUILayout.Label("<b>iXR Study - Training</b>", RichLabel());
        GUILayout.Space(8);
        GUILayout.Label("One conversation at the table (iAT), then one on the grid (iAA), with avatars and a topic the study " +
                        "never uses. The participant presses X on the left controller to move on; nothing is logged. " +
                        "Run it once with each participant, before Session 1.", RichLabel());
        if (!_teamValid)
            GUILayout.Label("<color=#ff7070>The training team is not set up (see the Console).</color>", RichLabel());

        GUILayout.FlexibleSpace();
        GUI.enabled = _teamValid;
        bool start = GUILayout.Button("Start training", GUILayout.Height(40));
        GUI.enabled = true;
        GUILayout.EndArea();

        if (start)
        {
            _startedAt = Time.time;
            LoadRoom(0);
        }
    }

    private void DrawStatus()
    {
        int seconds = Mathf.FloorToInt(Time.time - _startedAt);
        string time = $"{seconds / 60}:{seconds % 60:00} of {timeLimitMinutes:0} min";
        if (seconds > timeLimitMinutes * 60f) time = $"<color=#ffcc40>{time}</color>";

        var session = SessionController.Instance;
        GUILayout.BeginArea(new Rect(10f, 10f, 560f, 160f + (session != null ? session.WarningsHeight : 0f)), GUI.skin.box);
        GUILayout.Label($"Training  |  Room {_room + 1}/{Rooms.Length}: {CurrentRoom}  |  {time}", RichLabel());
        GUILayout.Label("The participant presses X to move on; Space does the same here.");
        if (session != null) session.DrawWizardPanel();
        GUILayout.EndArea();
    }

    private static GUIStyle RichLabel() => new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
}
