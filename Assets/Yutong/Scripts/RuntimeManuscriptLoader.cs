// RuntimeManuscriptLoader.cs
using UnityEngine;
using System.IO;

#if UNITY_EDITOR
using UnityEditor; // EditorUtility.OpenFilePanel
#endif

#if !UNITY_EDITOR
// Only needed in player builds if you imported StandaloneFileBrowser
using SFB;
#endif

public class RuntimeManuscriptLoader : MonoBehaviour
{
    [Header("Hook up your SessionController here")]
    public SessionController sessionController;

    [Header("Optional simple on-screen UI")]
    public bool showOverlayUI = false;

    string _lastPath = "";
    string _status = "No file loaded.";

    void Awake()
    {
        showOverlayUI = false; // Force hide UI as requested
        if (sessionController == null)
            sessionController = FindObjectOfType<SessionController>();
    }

    void OnGUI()
    {
        if (!showOverlayUI) return;

        const int w = 520;
        const int h = 140;
        var rect = new Rect(12, 12, w, h);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label("<b>Load Session Manuscript (JSON)</b>", Rich());

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Pick JSON…", GUILayout.Width(120)))
        {
            string path = PickJsonPath();
            if (!string.IsNullOrEmpty(path))
            {
                _lastPath = path;
                TryLoad(path);
            }
        }

        GUILayout.Label(string.IsNullOrEmpty(_lastPath) ? "(none)" : _lastPath, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        // Fallback manual path entry
        GUILayout.BeginHorizontal();
        GUILayout.Label("Path:", GUILayout.Width(40));
        _lastPath = GUILayout.TextField(_lastPath);
        if (GUILayout.Button("Load", GUILayout.Width(80)))
        {
            TryLoad(_lastPath);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label(_status);
        GUILayout.EndArea();
    }

    GUIStyle Rich()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.richText = true;
        return s;
    }

    void TryLoad(string path)
    {
        if (sessionController == null)
        {
            _status = "<color=#f66>Missing SessionController reference.</color>";
            Debug.LogError(_status);
            return;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _status = "<color=#f66>Invalid path.</color>";
            Debug.LogError(_status);
            return;
        }

        _status = "Loading…";
        // Call the compatibility method
        sessionController.InitializeFromJsonPath(path);
        _status = "<color=#6c6>Loaded.</color>";
    }

    string PickJsonPath()
    {
#if UNITY_EDITOR
        return EditorUtility.OpenFilePanel("Select Session JSON", "", "json");
#else
        // Standalone build
        try
        {
#if SFB_FOUND 
            var extensions = new[] { new ExtensionFilter("JSON", "json") };
            var paths = StandaloneFileBrowser.OpenFilePanel("Select Session JSON", "", extensions, false);
            if (paths != null && paths.Length > 0) return paths[0];
            return null;
#else
            return null;
#endif
        }
        catch
        {
            return null;
        }
#endif
    }
}
