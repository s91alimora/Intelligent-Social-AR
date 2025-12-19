// AgentGenerator.cs
using UnityEngine;

[DisallowMultipleComponent]
public class AgentGenerator : MonoBehaviour
{
    [Header("Cube Appearance")]
    public Vector3 cubeSize = new Vector3(0.5f, 0.5f, 0.5f);

    [Tooltip("World Y for the grid plane (LineRenderer grid is usually at 0).")]
    public float gridY = 0f;

    [Tooltip("Extra vertical clearance above the grid surface.")]
    public float yOffset = 0.0f;

    public float SurfaceY()
    {
        return (gridY + (cubeSize.y * 0.5f) + yOffset);
    }

    public GameObject SpawnAgent(string id, Vector3 gridPos, object agentSpecObj)
    {
        var wrapper = new AgentView(agentSpecObj);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = id;

        go.tag = "Agent";

        // place with correct Y so it ¡°sits¡± on the grid surface
        go.transform.position = new Vector3(gridPos.x, SurfaceY(), gridPos.z);
        go.transform.localScale = cubeSize;

        var tts = go.AddComponent<CrossPlatformTTS>();
        tts.modelFileName = wrapper.model;
        tts.lengthScale = wrapper.lengthScale;
        tts.noiseScale = wrapper.noiseScale;
        tts.extraArgs = wrapper.extraArgs;

        var agent = go.AddComponent<ConversationalAgent>();
        agent.agentName = id;
        //agent.speakingRenderer = go.GetComponent<Renderer>();
        //if (wrapper.color != null && wrapper.color.Length >= 3)
        //{
        //    var col = new Color(wrapper.color[0], wrapper.color[1], wrapper.color[2],
        //                        wrapper.color.Length > 3 ? wrapper.color[3] : 1f);
        //    agent.speakingColor = col;
        //}
        return go;
    }


    // Small helper to read the fields we need without hard dependency on the JSON class
    class AgentView
    {
        public string model;
        public float lengthScale;
        public float noiseScale;
        public string extraArgs;
        public float[] color;

        public AgentView(object o)
        {
            // Using reflection once at spawn time keeps this decoupled
            var t = o.GetType();
            model = (string)t.GetField("model").GetValue(o);
            lengthScale = (float)t.GetField("lengthScale").GetValue(o);
            noiseScale = (float)t.GetField("noiseScale").GetValue(o);
            extraArgs = (string)t.GetField("extraArgs").GetValue(o);
            color = (float[])t.GetField("color").GetValue(o);
        }
    }
}
