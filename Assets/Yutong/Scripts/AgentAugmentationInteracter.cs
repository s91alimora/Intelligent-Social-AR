using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;

public class AgentAugmentationInteracter : MonoBehaviour
{
    [Header("Raycast Settings")]
    public Camera mainCamera;
    public Transform raySource;
    public float maxDistance = 10f;
    public LayerMask layerMask = ~0;

    [Header("Thresholds")]
    [Tooltip("How long to stay on an agent to show the canvas.")]
    public float saccadeThreshold = 0.3f;
    
    [Tooltip("Grace period to keep canvas visible if ray leaves briefly (must be < saccadeThreshold).")]
    public float jitterBuffer = 0.25f;

    private GameObject activeAgent;
    private GameObject pendingAgent;
    private float dwellTimer = 0f;
    private float jitterTimer = 0f;
    private bool isInteracterActive = false;

    private Dictionary<GameObject, Canvas> agentCanvasCache = new Dictionary<GameObject, Canvas>();

    private void Start()
    {
        // Ensure jitterBuffer is always smaller than saccadeThreshold
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold * 0.8f;
            Debug.LogWarning($"[AgentAugmentationInteracter] jitterBuffer was too large. Adjusted to {jitterBuffer}");
        }
    }

    public void SetActive(bool active)
    {
        isInteracterActive = active;
        if (!active)
        {
            HideActiveCanvas();
            ResetStates();
        }
    }

    public void PreSetupCanvases(List<ConversationalAgent> agents)
    {
        foreach (var agent in agents)
        {
            Canvas canvas = GetAgentCanvas(agent.gameObject);
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = mainCamera;
            }
        }
    }

    public void PopulateAll(List<ConversationalAgent> agents, string fullJson, int questionIndex)
    {
        string qKey = $"question_{questionIndex}_Data";
        int startIdx = fullJson.IndexOf(qKey);
        if (startIdx == -1) return;

        foreach (var agent in agents)
        {
            PopulateAgentUI(agent.gameObject, fullJson, startIdx);
        }
    }

    private void PopulateAgentUI(GameObject agent, string json, int startIdx)
    {
        // 1. Identify agent index (e.g. agent_1 -> 1)
        string agentName = agent.name; // agent_1, agent_2, etc.
        Match numMatch = Regex.Match(agentName, @"\d+");
        if (!numMatch.Success) return;
        string agentIdx = numMatch.Value;

        // 2. Find UI Components recursively
        Canvas canvas = GetAgentCanvas(agent);
        if (canvas == null) return;

        Transform root = canvas.transform.Find("Individual AR Augmentation");
        if (root == null) return;

        // 3. Populate Avatar ID & Name
        TextMeshProUGUI idText = FindComponentByName<TextMeshProUGUI>(root, "Avatar ID");
        if (idText != null) idText.text = agentIdx;

        // Extract name from ConversationalAgent component
        var agtComp = agent.GetComponent<ConversationalAgent>();
        string displayName = agtComp != null ? agtComp.displayName : agent.name;
        
        TextMeshProUGUI nameText = FindComponentByName<TextMeshProUGUI>(root, "Avatar Name");
        if (nameText != null) nameText.text = displayName;

        // 4. Populate Individual Summary
        TextMeshProUGUI summaryText = FindComponentByName<TextMeshProUGUI>(root, "Individual Summary");
        if (summaryText != null)
        {
            string summaryData = ExtractJsonString(json, $"{agentName}_Sum", startIdx);
            summaryText.text = FormatBulletinPoints(summaryData);
        }

        // 5. Populate Stats
        TextMeshProUGUI spokenText = FindComponentByName<TextMeshProUGUI>(root, "Times Spoken First");
        TextMeshProUGUI seatedText = FindComponentByName<TextMeshProUGUI>(root, "Times Seated First");
        
        // Find the stats block for this agent
        string statsKey = $"{agentName}_apr_Stats";
        int statsIdx = json.IndexOf($"\"{statsKey}\"", startIdx);
        if (statsIdx != -1)
        {
            if (spokenText != null) spokenText.text = ExtractJsonValue(json, "times_Spoken_First", statsIdx);
            if (seatedText != null) seatedText.text = ExtractJsonValue(json, "times_Seated_First", statsIdx);

            // 6. Populate Seating Pattern Image
            Image seatingImage = FindComponentByName<Image>(root, "Individual Seating Pattern");
            if (seatingImage != null)
            {
                string imagePath = ExtractJsonString(json, "ind_Seating", statsIdx);
                LoadImageToUI(imagePath, seatingImage);
            }
        }
    }

    private T FindComponentByName<T>(Transform root, string name) where T : Component
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
            {
                T comp = child.GetComponent<T>();
                if (comp != null) return comp;
            }
            T found = FindComponentByName<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private string ExtractJsonString(string json, string key, int searchStart)
    {
        var match = Regex.Match(json.Substring(searchStart), $"\"{key}\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : "";
    }

    private string ExtractJsonValue(string json, string key, int searchStart)
    {
        var match = Regex.Match(json.Substring(searchStart), $"\"{key}\"\\s*:\\s*(\\d+)");
        return match.Success ? match.Groups[1].Value : "0";
    }

    private string FormatBulletinPoints(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] parts = text.Split(new char[] { '■' }, System.StringSplitOptions.RemoveEmptyEntries);
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
        string fullPath = relativePath.StartsWith("Assets/") 
            ? Path.Combine(Application.dataPath, relativePath.Substring(7)) 
            : Path.Combine(Application.dataPath, relativePath);

        if (File.Exists(fullPath))
        {
            byte[] fileData = File.ReadAllBytes(fullPath);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                uiImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }
        else
        {
            Debug.LogWarning($"[AgentAugmentationInteracter] Image not found: {fullPath}");
        }
    }

    private void Update()
    {
        if (!isInteracterActive) return;

        UpdateRaycast();
        UpdateTimers();
    }

    private void UpdateRaycast()
    {
        Ray ray = new Ray(raySource.position, raySource.forward);
        RaycastHit hit;

        GameObject hitAgent = null;
        if (Physics.Raycast(ray, out hit, maxDistance, layerMask))
        {
            if (hit.collider.CompareTag("Agent"))
            {
                hitAgent = hit.collider.gameObject;
            }
        }

        // 1. Handle Active Agent Jitter
        if (activeAgent != null)
        {
            if (hitAgent == activeAgent)
            {
                jitterTimer = 0f; // Reset jitter buffer
            }
            else
            {
                // Ray is away from active agent (either hitting nothing or another agent)
                jitterTimer += Time.deltaTime;
                if (jitterTimer >= jitterBuffer)
                {
                    HideActiveCanvas();
                }
            }
        }

        // 2. Handle Pending Agent Saccade
        if (hitAgent != null && hitAgent != activeAgent)
        {
            if (hitAgent == pendingAgent)
            {
                dwellTimer += Time.deltaTime;
                if (dwellTimer >= saccadeThreshold)
                {
                    SwitchToAgent(hitAgent);
                }
            }
            else
            {
                pendingAgent = hitAgent;
                dwellTimer = 0f;
            }
        }
        else
        {
            pendingAgent = null;
            dwellTimer = 0f;
        }
    }

    private void UpdateTimers()
    {
        // Timers are updated within UpdateRaycast for simplicity in this logic
    }

    private void SwitchToAgent(GameObject agent)
    {
        if (activeAgent != null) HideActiveCanvas();
        
        ShowCanvas(agent);
        activeAgent = agent;
        jitterTimer = 0f;
        pendingAgent = null;
        dwellTimer = 0f;
    }

    private void ShowCanvas(GameObject agent)
    {
        Canvas canvas = GetAgentCanvas(agent);
        GameObject rotationOrigin = canvas.transform.parent.gameObject;
        if (canvas != null)
        {
            // Face the camera before showing (Y-axis only billboard)
            FaceCameraYOnly(rotationOrigin.transform);

            canvas.gameObject.SetActive(true);
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = mainCamera;
            }
        }
    }

    private void FaceCameraYOnly(Transform target)
    {
        if (mainCamera == null) return;

        Vector3 directionToCamera = mainCamera.transform.position - target.position;
        directionToCamera.y = 0; // Flatten the vector to keep rotation on Y-axis only

        if (directionToCamera != Vector3.zero)
        {
            // LookRotation expects a forward vector. 
            // Since UI usually faces 'forward', we might need to adjust based on UI orientation.
            // In most Unity UI setups, the 'back' of the canvas faces the camera to be readable.
            target.rotation = Quaternion.LookRotation(directionToCamera); 
        }
    }

    private void HideActiveCanvas()
    {
        if (activeAgent != null)
        {
            Canvas canvas = GetAgentCanvas(activeAgent);
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
            }
            activeAgent = null;
        }
    }

    private void ResetStates()
    {
        activeAgent = null;
        pendingAgent = null;
        dwellTimer = 0f;
        jitterTimer = 0f;
    }

    private Canvas GetAgentCanvas(GameObject agent)
    {
        if (agentCanvasCache.TryGetValue(agent, out Canvas cachedCanvas))
        {
            return cachedCanvas;
        }

        Canvas canvas = agent.GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            agentCanvasCache[agent] = canvas;
        }
        return canvas;
    }

    private void OnValidate()
    {
        if (jitterBuffer >= saccadeThreshold)
        {
            jitterBuffer = saccadeThreshold - 0.05f;
        }
    }
}
