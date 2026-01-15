using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

public class TTSBakingEditor : EditorWindow
{
    [MenuItem("Tools/iXR/Bake All Session Audio")]
    public static void ShowWindow()
    {
        GetWindow<TTSBakingEditor>("TTS Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("TTS Audio Baker", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        if (GUILayout.Button("Bake All Audio from SessionController", GUILayout.Height(40)))
        {
            BakeAll();
        }
    }

    private void BakeAll()
    {
        SessionController sc = FindObjectOfType<SessionController>();
        if (sc == null)
        {
            EditorUtility.DisplayDialog("Error", "No SessionController found in scene!", "OK");
            return;
        }

        string cachePath = Path.Combine(Application.streamingAssetsPath, "AudioCache");
        if (!Directory.Exists(cachePath)) Directory.CreateDirectory(cachePath);

        int bakedCount = 0;
        int skippedCount = 0;

        // Get a temp CrossPlatformTTS to use its settings
        // We'll look for any agent in the scene to get a reference
        ConversationalAgent sampleAgent = FindObjectOfType<ConversationalAgent>();
        if (sampleAgent == null)
        {
            EditorUtility.DisplayDialog("Error", "No ConversationalAgent found in scene to use for baking settings!", "OK");
            return;
        }

        CrossPlatformTTS tts = sampleAgent.GetComponent<CrossPlatformTTS>();

        foreach (var script in sc.scriptFiles)
        {
            if (script == null) continue;
            
            Debug.Log($"Baking script: {script.name}");
            
            // Extract all agent response texts
            // Regex to match: "agent_N_Resp" : "TEXT"
            var matches = Regex.Matches(script.text, "\"agent_\\d+_Resp\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            
            foreach (Match m in matches)
            {
                string rawText = m.Groups[1].Value;
                string unescaped = Regex.Unescape(rawText);
                
                if (string.IsNullOrWhiteSpace(unescaped)) continue;

                string filename = tts.GetBakedFilename(unescaped);
                string fullPath = Path.Combine(cachePath, filename);

                if (File.Exists(fullPath))
                {
                    skippedCount++;
                    continue;
                }

                EditorUtility.DisplayProgressBar("Baking TTS", $"Baking: {unescaped.Substring(0, Mathf.Min(30, unescaped.Length))}...", (float)bakedCount / matches.Count);
                
                tts.BakeToFile(unescaped, fullPath);
                bakedCount++;
            }
        }

        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Baking Complete", $"Baked {bakedCount} new files.\nSkipped {skippedCount} existing files.", "OK");
        AssetDatabase.Refresh();
    }
}
