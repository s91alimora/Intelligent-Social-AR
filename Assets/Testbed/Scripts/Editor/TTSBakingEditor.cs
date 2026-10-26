using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Pre-renders every line the study can speak into StreamingAssets/AudioCache, so CrossPlatformTTS plays
/// baked WAVs instead of running Piper during a session (no synthesis gap, the same audio for every
/// participant). Open "Master Level Control" first: its avatar teams decide which voices are baked.
///
/// Each team always plays one condition, and team member i plays agent_{i+1}. So each condition's agent_N lines
/// are baked once, with member N of that condition's team, plus one introduction per avatar. File names
/// hash voice model + prosody + text (CrossPlatformTTS.GetBakedFilename), so a clip can never play with the
/// wrong voice or text; a line changed after baking just falls back to live Piper.
/// </summary>
public class TTSBakingEditor : EditorWindow
{
    [MenuItem("Tools/iXR/Bake All Session Audio")]
    public static void ShowWindow()
    {
        GetWindow<TTSBakingEditor>("TTS Baker");
    }

    struct BakeJob
    {
        public CrossPlatformTTS voice; // the avatar prefab's TTS settings (model, lengthScale, noiseScale)
        public string text;
        public string fileName;
    }

    static string CacheFolder => Path.Combine(Application.streamingAssetsPath, "AudioCache");

    string _status = "";

    private void OnGUI()
    {
        GUILayout.Label("TTS Audio Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Open the 'Master Level Control' scene first: its avatar teams decide which voices are baked.", MessageType.Info);
        GUILayout.Space(10);

        if (GUILayout.Button("Check", GUILayout.Height(25))) Check();
        if (GUILayout.Button("Bake Missing Clips", GUILayout.Height(40))) BakeMissing();

        if (_status != "") EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    private void Check()
    {
        var jobs = CollectJobs();
        if (jobs == null) return;
        int baked = jobs.Count(j => File.Exists(Path.Combine(CacheFolder, j.fileName)));
        _status = baked == jobs.Count
            ? $"All {jobs.Count} clips are baked."
            : $"{baked} of {jobs.Count} clips are baked. Press 'Bake Missing Clips'.";
    }

    private void BakeMissing()
    {
        var jobs = CollectJobs();
        if (jobs == null) return;
        Directory.CreateDirectory(CacheFolder);

        var missing = jobs.Where(j => !File.Exists(Path.Combine(CacheFolder, j.fileName))).ToList();
        int failed = 0;
        try
        {
            for (int i = 0; i < missing.Count; i++)
            {
                var job = missing[i];
                string preview = job.text.Length > 40 ? job.text.Substring(0, 40) + "..." : job.text;
                if (EditorUtility.DisplayCancelableProgressBar("Baking TTS", $"{i + 1}/{missing.Count}  {job.voice.name}: {preview}", (float)i / missing.Count))
                    break;

                string path = Path.Combine(CacheFolder, job.fileName);
                job.voice.BakeToFile(job.text, path);
                if (!File.Exists(path))
                {
                    failed++;
                    Debug.LogError($"[TTS Baker] Piper wrote no file for {job.voice.name} ({job.voice.modelFileName}): \"{preview}\"");
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        Check();
        if (failed > 0) _status += $"\n{failed} clip(s) failed - see the Console.";
    }

    /// <summary>
    /// Every (voice, text) pair the study can play, without duplicates; null (after a dialog) if the open
    /// scene has no usable avatar teams.
    /// </summary>
    private static List<BakeJob> CollectJobs()
    {
        var master = FindObjectOfType<MasterLevelController>();
        if (master == null)
        {
            EditorUtility.DisplayDialog("TTS Baker",
                "No MasterLevelController found in the open scene.\nOpen 'Master Level Control' and try again.", "OK");
            return null;
        }

        var teams = master.avatarTeams;
        bool teamsValid = teams != null && teams.Count > 0 && teams.All(t =>
            t != null && t.members != null && t.members.Count == 4 &&
            t.members.All(m => m != null && m.GetComponent<CrossPlatformTTS>() != null)) &&
            System.Enum.GetValues(typeof(SessionController.StudyCondition)).Cast<SessionController.StudyCondition>()
                .All(c => teams.Count(t => t.condition == c) == 1);
        if (!teamsValid)
        {
            EditorUtility.DisplayDialog("TTS Baker",
                "Every avatar team needs exactly 4 avatar prefabs with a CrossPlatformTTS, and each condition exactly one team.\n" +
                "Run Tools/iXR/Assign Proposed Avatar Teams first.", "OK");
            return null;
        }

        var jobs = new List<BakeJob>();
        var seen = new HashSet<string>();
        void Add(GameObject avatar, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var voice = avatar.GetComponent<CrossPlatformTTS>();
            string fileName = voice.GetBakedFilename(text);
            if (seen.Add(fileName)) jobs.Add(new BakeJob { voice = voice, text = text, fileName = fileName });
        }

        // Introductions: one per avatar, worded exactly as SessionController.BuildIntroductionLine words them.
        foreach (var avatar in teams.SelectMany(t => t.members))
            Add(avatar, string.Format(SessionController.DefaultIntroductionLineFormat, avatar.name));

        // Discussion lines: each team speaks only its own condition's manuscript, agent_N's lines with member N-1.
        foreach (var team in teams)
        {
            var manuscript = Resources.Load<TextAsset>($"{SessionController.ManuscriptResourceFolder}/{team.condition}");
            if (manuscript == null)
            {
                Debug.LogWarning($"[TTS Baker] No manuscript at Resources/{SessionController.ManuscriptResourceFolder}/{team.condition}.json - skipped.");
                continue;
            }

            for (int q = 1; q <= 4; q++)
            {
                string responses = ManuscriptJson.GetObject(ManuscriptJson.GetQuestion(manuscript.text, q), "glb_Responses");
                foreach (var agentLines in ManuscriptJson.GetLinesByAgent(responses))
                {
                    int member = agentLines.Key - 1;
                    if (member < 0 || member >= 4) continue;
                    foreach (string text in agentLines.Value)
                        Add(team.members[member], text);
                }
            }
        }
        return jobs;
    }
}