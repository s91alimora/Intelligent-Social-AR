using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Reads values out of the per-condition manuscripts (Resources/Manuscripts/*.json) and loads their
/// augmentation images. Shared by SessionController and AgentAugmentationInteracter.
///
/// Every lookup searches only inside the block it is given (a question, or one object inside it).
/// A null or missing value therefore reads as "" instead of falling through to the next question.
/// Some nulls are deliberate (e.g. an agent with no summary is a nested bias) and must show as empty.
/// </summary>
public static class ManuscriptJson
{
    /// <summary>Folder under StreamingAssets that holds the pre-rendered augmentation charts.</summary>
    public const string AugmentationImagesFolder = "Augmentation Images";

    const string StringValuePattern = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    /// <summary>"question_{n}_Data" object of a manuscript, or "" if missing.</summary>
    public static string GetQuestion(string manuscriptJson, int questionIndex)
    {
        return GetObject(manuscriptJson, $"question_{questionIndex}_Data");
    }

    /// <summary>
    /// The JSON object stored under <paramref name="key"/> inside <paramref name="json"/>, from its '{'
    /// to the matching '}', or "" if the key is missing or its value is not an object (e.g. null).
    /// </summary>
    public static string GetObject(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\\{{");
        if (!m.Success) return "";

        int open = m.Index + m.Length - 1;
        int depth = 0;
        bool inString = false;
        for (int i = open; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (c == '\\') i++; // skip the escaped character
                else if (c == '"') inString = false;
            }
            else if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return json.Substring(open, i - open + 1);
        }
        return "";
    }

    /// <summary>String value of "key" inside <paramref name="json"/>; "" if missing, null or not a string.</summary>
    public static string GetString(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*{StringValuePattern}");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
    }

    /// <summary>
    /// Scalar value of "key" inside <paramref name="json"/> as text (a number, a bool, or a string
    /// without its quotes); "" if missing or null.
    /// </summary>
    public static string GetValue(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(?:{StringValuePattern}|([^,\\s}}\\]]+))");
        if (!m.Success) return "";
        if (m.Groups[1].Success) return Regex.Unescape(m.Groups[1].Value);
        string raw = m.Groups[2].Value;
        return raw == "null" ? "" : raw;
    }

    // "agent_2_Resp" is an agent's first line; "agent_2_Resp_2", "agent_2_Resp_3", ... are its later
    // lines when it speaks more than once in a question. Null lines do not match and are skipped.
    static readonly Regex ResponseLine = new Regex($"\"agent_(\\d+)_Resp(?:_(\\d+))?\"\\s*:\\s*{StringValuePattern}");

    /// <summary>
    /// The discussion lines in a "glb_Responses" block, queued per agent number in the order that agent
    /// says them (by the _K suffix, then by position in the file). The caller dequeues one line per
    /// turn in order_Speech, so each line is spoken by the agent named in its key.
    /// </summary>
    public static Dictionary<int, Queue<string>> GetLinesByAgent(string responsesJson)
    {
        var lines = new List<(int agent, int nth, int position, string text)>();
        if (!string.IsNullOrEmpty(responsesJson))
        {
            foreach (Match m in ResponseLine.Matches(responsesJson))
            {
                int nth = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                lines.Add((int.Parse(m.Groups[1].Value), nth, lines.Count, Regex.Unescape(m.Groups[3].Value)));
            }
        }

        var byAgent = new Dictionary<int, Queue<string>>();
        foreach (var line in lines.OrderBy(l => l.agent).ThenBy(l => l.nth).ThenBy(l => l.position))
        {
            if (!byAgent.TryGetValue(line.agent, out var queue)) byAgent[line.agent] = queue = new Queue<string>();
            queue.Enqueue(line.text);
        }
        return byAgent;
    }

    /// <summary>"■ a ■ b" -> one indented bullet per line, keeping the ■ markers.</summary>
    public static string FormatBullets(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] parts = text.Split(new[] { '■' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", parts.Select(p => "■ <indent=1.2em>" + p.Trim() + "</indent>"));
    }

    static bool IsAndroid => Application.platform == RuntimePlatform.Android;

    /// <summary>
    /// Loads a manuscript image into <paramref name="target"/>. The Image is hidden when there is no
    /// image (empty path, or file not found), so a previous trial's chart is never left on screen.
    /// </summary>
    public static IEnumerator LoadImage(string manuscriptPath, Image target)
    {
        if (target == null) yield break;
        if (string.IsNullOrWhiteSpace(manuscriptPath))
        {
            target.enabled = false;
            yield break;
        }

        string path = ResolveImagePath(manuscriptPath);
        if (path == null)
        {
            Debug.LogWarning($"[ManuscriptJson] Image not found: '{manuscriptPath}'. Put it under " +
                             $"StreamingAssets/{AugmentationImagesFolder}/ and use a path relative to StreamingAssets.");
            target.enabled = false;
            yield break;
        }

        Texture2D tex = null;
        if (path.Contains("://") || IsAndroid)
        {
            // On Android, StreamingAssets is inside the APK and can only be read through UnityWebRequest.
            using (var req = UnityWebRequestTexture.GetTexture(path))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) tex = DownloadHandlerTexture.GetContent(req);
                else Debug.LogWarning($"[ManuscriptJson] Failed to load {path}: {req.error}");
            }
        }
        else
        {
            tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Debug.LogWarning($"[ManuscriptJson] {path} is not a readable image.");
                UnityEngine.Object.Destroy(tex);
                tex = null;
            }
        }

        if (tex == null)
        {
            target.enabled = false;
            yield break;
        }
        target.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        target.enabled = true;
    }

    /// <summary>
    /// Full path of a manuscript image. Paths are meant to be relative to StreamingAssets
    /// (e.g. "Augmentation Images/Group/Grp_Sim_Mat/iAA_Q1_Grp_Sim_Mat.jpg"), so they work wherever the
    /// repository is cloned. If that file does not exist - for example a path copied from a design folder
    /// on another computer - the image is found by its file name anywhere under
    /// StreamingAssets/Augmentation Images. (That fallback is desktop-only: Android cannot list
    /// StreamingAssets, so Quest builds need the relative paths.) Returns null if not found.
    /// </summary>
    static string ResolveImagePath(string manuscriptPath)
    {
        string path = manuscriptPath.Trim().Replace('\\', '/');
        const string projectPrefix = "Assets/StreamingAssets/";
        if (path.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)) path = path.Substring(projectPrefix.Length);

        if (!Path.IsPathRooted(path))
        {
            string relative = Path.Combine(Application.streamingAssetsPath, path);
            if (IsAndroid || File.Exists(relative)) return relative;
        }
        if (IsAndroid) return null;

        string fileName = path.Substring(path.LastIndexOf('/') + 1);
        if (_imagesByFileName == null || !_imagesByFileName.ContainsKey(fileName)) IndexImages();
        return _imagesByFileName.TryGetValue(fileName, out var found) ? found : null;
    }

    static Dictionary<string, string> _imagesByFileName;

    static void IndexImages()
    {
        _imagesByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string root = Path.Combine(Application.streamingAssetsPath, AugmentationImagesFolder);
        if (!Directory.Exists(root)) return;

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(file);
            if (_imagesByFileName.ContainsKey(name))
                Debug.LogWarning($"[ManuscriptJson] Two images are named '{name}'; using {_imagesByFileName[name]}.");
            else
                _imagesByFileName[name] = file;
        }
    }
}
