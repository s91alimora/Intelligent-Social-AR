// SessionController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class SessionController : MonoBehaviour
{
    [Header("Screenplay (JSON)")]
    [Tooltip("Session manuscript JSON (as TextAsset).")]
    public TextAsset screenplayJson;

    [Header("Scene References")]
    public GridGenerator gridGenerator;
    public ConversationalAgentsManager manager;   // leave in scene; we will fill it
    public AgentGenerator agentGenerator;         // helper that spawns cubes + attaches components

    [Header("Header Question UI (TMP)")]
    public Graphic headerBackground;
    public Text headerTextTMP;

    // ===== Data containers parsed from JSON =====
    [Serializable]
    class Root
    {
        public string version = "1.0";
        public GridSpec grid = new GridSpec();
        public float defaultWaitSec = 1f;
        public float moveDurationSec = 2f;
        public string headerQuestion = "";
        public StagingSpec staging = new StagingSpec();
        public List<AgentSpec> agents = new List<AgentSpec>();
        public List<ScriptLine> script = new List<ScriptLine>();
    }

    [Serializable] class GridSpec { public int rows = 5; public int cols = 5; }

    [Serializable]
    class StagingSpec
    {
        public string axis = "x";         // "x" or "z"
        public float spacing = 1.6f;
        public float[] startWorld;        // optional: [x,y,z]
    }

    [Serializable]
    class AgentSpec
    {
        public string id = "agent";
        public string model = "en-us.onnx";
        public float lengthScale = 1.0f;
        public float noiseScale = 0.33f;
        public string extraArgs = "";
        public float[] color = new float[] { 1, 1, 1, 1 };
        public int[] gridPos = new int[] { 0, 0 }; // [row, col]
    }

    [Serializable]
    class ScriptLine
    {
        public string agent;
        public string text;
        // Use -1 to mean ¡°not specified¡± (JsonUtility supports float, not float?)
        public float waitSec = -1f;
    }

    Root _spec;

    // runtime mappings
    readonly Dictionary<string, ConversationalAgent> _idToAgent = new();
    readonly Dictionary<string, Vector3> _idToGridWorld = new();

    bool _built;
    int _stage = 0; // 0=not started, 1=header shown, 2=moved, 3=playing/played
    bool _playing;

    // ---------- Lifecycle ----------

    void Awake()
    {
        if (screenplayJson != null)
        {
            TryParseSpec(screenplayJson.text);
        }
        else
        {
            // You can still load later via InitializeFromJsonPath/Text
            Debug.Log("SessionController: No TextAsset assigned; waiting for runtime JSON.");
        }
    }

    IEnumerator Start()
    {
        if (_spec == null)
            yield break; // waiting for runtime load

        yield return BuildFromSpec();
    }

    void Update()
    {
        if (!_built) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (_stage == 0)
            {
                // Show header
                SetHeaderVisible(true);
                SetHeaderText(_spec.headerQuestion);
                _stage = 1;
            }
            else if (_stage == 1)
            {
                // Move to grid
                SetHeaderVisible(false);
                StartCoroutine(MoveAllToGrid(_spec.moveDurationSec));
                _stage = 2;
            }
            else if (_stage == 2 && !_playing)
            {
                // Start conversation
                StartCoroutine(StartConversation());
                _stage = 3;
            }
        }
    }

    // ---------- External runtime loading API ----------

    public void InitializeFromJsonPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError($"SessionController: JSON path invalid: {path}");
            return;
        }
        InitializeFromJsonText(File.ReadAllText(path));
    }

    public void InitializeFromJsonText(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("SessionController: JSON text empty.");
            return;
        }

        ResetSessionIfAny();

        if (!TryParseSpec(json))
            return;

        // Rebuild immediately
        StartCoroutine(BuildFromSpec());
    }

    // ---------- Build / Reset ----------

    bool TryParseSpec(string json)
    {
        try
        {
            _spec = JsonUtility.FromJson<Root>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("SessionController: JSON parse failed. " + e.Message);
            _spec = null;
        }

        if (_spec == null)
        {
            Debug.LogError("SessionController: JSON is empty/invalid.");
            return false;
        }
        return true;
    }

    IEnumerator BuildFromSpec()
    {
        if (_spec == null)
            yield break;

        // Clean UI state
        SetHeaderVisible(false);
        _stage = 0;
        _built = false;

        // 1) Grid
        if (gridGenerator == null) gridGenerator = FindObjectOfType<GridGenerator>();
        if (gridGenerator == null)
        {
            Debug.LogError("SessionController: GridGenerator not found/assigned.");
            yield break;
        }
        gridGenerator.Build(_spec.grid.rows, _spec.grid.cols);

        // 2) Agents lined up
        if (agentGenerator == null) agentGenerator = FindObjectOfType<AgentGenerator>();
        if (agentGenerator == null)
        {
            Debug.LogError("SessionController: AgentGenerator not found/assigned.");
            yield break;
        }

        Vector3 start = Vector3.zero;
        if (_spec.staging.startWorld != null && _spec.staging.startWorld.Length >= 3)
        {
            start = new Vector3(
                _spec.staging.startWorld[0],
                _spec.staging.startWorld[1],
                _spec.staging.startWorld[2]);
        }
        else
        {
            // default: one cell above the grid top-left
            start = gridGenerator.GridToWorld(-1, 0);
        }

        Vector3 step =
            (_spec.staging.axis != null && _spec.staging.axis.ToLowerInvariant() == "z")
                ? new Vector3(0, 0, _spec.staging.spacing)
                : new Vector3(_spec.staging.spacing, 0, 0);

        _idToAgent.Clear();
        _idToGridWorld.Clear();

        for (int i = 0; i < _spec.agents.Count; i++)
        {
            var a = _spec.agents[i];
            var go = agentGenerator.SpawnAgent(a.id, start + step * i, a);
            var comp = go.GetComponent<ConversationalAgent>();
            _idToAgent[a.id] = comp;

            // cache world target
            var rc = a.gridPos ?? new int[] { 0, 0 };
            _idToGridWorld[a.id] = gridGenerator.GridToWorld(rc[0], rc[1]);
        }

        _built = true;
        yield break;
    }

    void ResetSessionIfAny()
    {
        // stop any running coroutines here if desired
        StopAllCoroutines();

        // Destroy previously spawned agents
        foreach (var kv in _idToAgent)
        {
            if (kv.Value != null)
                Destroy(kv.Value.gameObject);
        }
        _idToAgent.Clear();
        _idToGridWorld.Clear();

        // Reset state/UI
        _playing = false;
        _stage = 0;
        _built = false;
        SetHeaderVisible(false);

        // Rebuild grid if your GridGenerator exposes a clear; otherwise next Build will overwrite.
        // Example:
        // gridGenerator.Clear();
    }

    // ---------- Movement & Conversation ----------

    IEnumerator MoveAllToGrid(float dur)
    {
        var items = new List<(Transform t, Vector3 from, Vector3 to)>();
        foreach (var kv in _idToAgent)
        {
            var tr = kv.Value.transform;
            items.Add((tr, tr.position, _idToGridWorld[kv.Key]));
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            foreach (var it in items)
            {
                // keep cubes on the ¡°surface¡± Y from AgentGenerator
                var from = new Vector3(it.from.x, agentGenerator.SurfaceY(), it.from.z);
                var to = new Vector3(it.to.x, agentGenerator.SurfaceY(), it.to.z);
                it.t.position = Vector3.Lerp(from, to, k);
            }

            yield return null;
        }
        foreach (var it in items)
        {
            it.t.position = new Vector3(it.to.x, agentGenerator.SurfaceY(), it.to.z);
        }
    }

    IEnumerator StartConversation()
    {
        _playing = true;

        // Build per-agent sentence lists and an index sequence for the manager
        var agentToLines = new Dictionary<string, List<string>>();
        foreach (var a in _spec.agents) agentToLines[a.id] = new List<string>();

        var steps = new List<ConversationStep>();
        bool anyCustomWait = false;

        foreach (var line in _spec.script)
        {
            var list = agentToLines[line.agent];
            int idx = list.Count;
            list.Add(line.text);

            if (_idToAgent.TryGetValue(line.agent, out var agentComp))
                steps.Add(new ConversationStep { agent = agentComp, sentenceIndex = idx });

            if (line.waitSec >= 0f) anyCustomWait = true;
        }

        // push sentences into agent components
        foreach (var kv in agentToLines)
        {
            if (_idToAgent.TryGetValue(kv.Key, out var comp))
            {
                comp.sentences.Clear();
                comp.sentences.AddRange(kv.Value);
            }
        }

        if (!anyCustomWait)
        {
            if (manager == null) manager = FindObjectOfType<ConversationalAgentsManager>();
            if (manager == null)
            {
                Debug.LogError("SessionController: ConversationalAgentsManager not assigned/present.");
            }
            else
            {
                manager.agents.Clear();
                manager.agents.AddRange(_idToAgent.Values);
                manager.sequence = steps;
                manager.pauseBetweenLines = Mathf.Max(0f, _spec.defaultWaitSec);
                manager.Play();

                // Wait until no agent is speaking
                while (true)
                {
                    bool anySpeaking = manager.agents.Any(a => a.GetComponent<CrossPlatformTTS>()?.IsSpeaking == true);
                    if (!anySpeaking) break;
                    yield return null;
                }
            }
        }
        else
        {
            // Drive conversation ourselves to respect per-step waits
            for (int i = 0; i < _spec.script.Count; i++)
            {
                var line = _spec.script[i];
                var comp = _idToAgent[line.agent];

                int sentenceIndex = steps[i].sentenceIndex;

                bool done = false;
                comp.Speak(sentenceIndex, () => done = true);
                while (!done) yield return null;

                float wait = (line.waitSec >= 0f) ? line.waitSec : Mathf.Max(0f, _spec.defaultWaitSec);
                if (wait > 0f && i < _spec.script.Count - 1)
                    yield return new WaitForSeconds(wait);


            }
        }

        _playing = false;
    }

    // ---------- UI helpers ----------

    void SetHeaderText(string txt)
    {
        if (headerTextTMP != null) headerTextTMP.text = txt;
    }

    void SetHeaderVisible(bool vis)
    {
        if (headerBackground != null) headerBackground.gameObject.SetActive(vis);
        if (headerTextTMP != null) headerTextTMP.gameObject.SetActive(vis);
    }
}
