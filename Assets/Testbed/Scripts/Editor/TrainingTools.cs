using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools/iXR/Training/Build Training Avatars makes the training team's four avatar prefabs in
/// Assets/Testbed/Prefabs/Training Avatars and assigns them to the TrainingController in the open scene (open "Training"
/// first). Running it again rebuilds the prefabs in place.
///
/// None of them can be seen in the study: each has a Rocketbox body that no study avatar uses and a voice that no study
/// avatar uses - kusal, reza, and two of the twelve speakers in aru_female, chosen by pitch (speakers 6 and 5, about
/// 200 Hz and 180 Hz, are female). Their lengthScale gives them the study voices' speaking rate: measured with piper-tts
/// on the training lines, Tony and Layla speak about 4.7 syllables a second, and so do these four.
///
/// Each prefab is built like a study avatar: a Prefab Variant of its body's FBX, with everything a study avatar adds to
/// its body - the components on the root (Animator, AudioSource, lip sync, TTS, ConversationalAgent, collider,
/// Rigidbody), any it adds to the face mesh (AutoBlink, which only Amy and Tony have), and the card (Rotation Origin >
/// Canvas) - copied from a study avatar of the same gender, with every reference moved over to the new body.
/// </summary>
public static class TrainingTools
{
    private const string StudyAvatarRoot = "Assets/Testbed/Prefabs/Avatars/";
    private const string PrefabsFolder = "Assets/Testbed/Prefabs";
    private const string TrainingAvatarFolderName = "Training Avatars";
    private const string Title = "Build Training Avatars";

    // Casting order (element i -> agent_{i+1}) is M, F, M, F, as in study teams A and C. The template is the study
    // avatar whose components and card are copied (only its setup is used, not its body or voice).
    private static readonly (string name, string body, string template, string voice, float lengthScale, int speaker)[] Team =
    {
        ("Daniel", "Assets/Avatars/Male_Adult_05/Export/Male_Adult_05_facial.fbx",     "Male/Alan",      "kusal.onnx",      1.20f, -1),
        ("Grace",  "Assets/Avatars/Female_Adult_05/Export/Female_Adult_05_facial.fbx", "Female/Layla",   "aru_female.onnx", 0.77f,  6),
        ("Marcus", "Assets/Avatars/Male_Adult_07/Export/Male_Adult_07_facial.fbx",     "Male/Norman",    "reza.onnx",       0.71f, -1),
        ("Hannah", "Assets/Avatars/Female_Adult_08/Export/Female_Adult_08_facial.fbx", "Female/Kristin", "aru_female.onnx", 0.93f,  5),
    };

    [MenuItem("Tools/iXR/Training/Build Training Avatars")]
    public static void BuildTrainingAvatars()
    {
        var training = Object.FindObjectOfType<TrainingController>();
        if (training == null)
        {
            EditorUtility.DisplayDialog(Title, "No TrainingController found in the open scene.\nOpen 'Training' and try again.", "OK");
            return;
        }

        string folder = $"{PrefabsFolder}/{TrainingAvatarFolderName}";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(PrefabsFolder, TrainingAvatarFolderName);

        var team = new List<GameObject>();
        var problems = new List<string>();
        try
        {
            for (int i = 0; i < Team.Length; i++)
            {
                EditorUtility.DisplayProgressBar(Title, Team[i].name, (float)i / Team.Length);
                team.Add(BuildAvatar(Team[i], $"{folder}/{Team[i].name}.prefab", problems));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string report = string.Join("\n", problems);
        if (team.Any(m => m == null))
        {
            Debug.LogError($"[TrainingTools] Training avatars NOT built:\n{report}");
            EditorUtility.DisplayDialog(Title, "Some avatars could not be built; the team was not changed.\n\n" + report, "OK");
            return;
        }

        Undo.RecordObject(training, Title);
        training.team = team;
        EditorUtility.SetDirty(training);
        EditorSceneManager.MarkSceneDirty(training.gameObject.scene);
        EditorSceneManager.SaveScene(training.gameObject.scene);

        string summary = "Built and assigned: " + string.Join(", ", team.Select(m => m.name)) +
                         (problems.Count > 0 ? "\n\nCheck these:\n" + report : "");
        Debug.Log($"[TrainingTools] {summary}");
        EditorUtility.DisplayDialog(Title, summary, "OK");
    }

    /// <summary>One training avatar, saved as a Prefab Variant of its body; null (with the reason in problems) if it failed.</summary>
    private static GameObject BuildAvatar((string name, string body, string template, string voice, float lengthScale, int speaker) spec,
                                          string path, List<string> problems)
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(spec.body);
        string templatePath = StudyAvatarRoot + spec.template + ".prefab";
        if (body == null || AssetDatabase.LoadAssetAtPath<GameObject>(templatePath) == null)
        {
            problems.Add($"{spec.name}: missing {(body == null ? spec.body : templatePath)}");
            return null;
        }

        var template = PrefabUtility.LoadPrefabContents(templatePath);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var avatar = (GameObject)PrefabUtility.InstantiatePrefab(body, preview);
            avatar.name = spec.name;
            avatar.tag = template.tag;
            var map = new BodyMap(template.transform, avatar.transform);

            // 1. Objects the study avatar adds to its body: the card. Instantiate keeps the references inside the copy
            //    pointing into the copy.
            foreach (var added in template.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAddedGameObjectOverride(added.gameObject) || map.IsInsideCopy(added)) continue;
                Transform parent = map.Find(added.parent);
                if (parent == null)
                {
                    problems.Add($"{spec.name}: no place for '{added.name}' (its parent '{added.parent.name}' is not on the new body)");
                    continue;
                }
                var copy = Object.Instantiate(added.gameObject, parent, false);
                copy.name = added.name;
                map.AddCopy(added, copy.transform);
            }

            // 2. Components the study avatar adds to its body (the root's, and AutoBlink on the face mesh if it has one).
            var copies = new List<Component>();
            foreach (var source in template.GetComponentsInChildren<Component>(true))
            {
                if (source == null || source is Transform || map.IsInsideCopy(source.transform) ||
                    !PrefabUtility.IsAddedComponentOverride(source)) continue;
                Transform target = map.Find(source.transform);
                if (target == null)
                {
                    problems.Add($"{spec.name}: no place for {source.GetType().Name} (on '{source.name}')");
                    continue;
                }
                // Get or add: adding ConversationalAgent also adds the CrossPlatformTTS and AudioSource it requires.
                var copy = target.GetComponent(source.GetType());
                if (copy == null) copy = target.gameObject.AddComponent(source.GetType());
                EditorUtility.CopySerialized(source, copy);
                copies.Add(copy);
            }

            // 3. References into the study avatar (its Animator, AudioSource, face mesh, ...) -> the same on the new body.
            foreach (var copy in copies) map.Retarget(copy, spec.name, problems);

            // 4. This avatar's own name and voice.
            var agent = avatar.GetComponent<ConversationalAgent>();
            var tts = avatar.GetComponent<CrossPlatformTTS>();
            if (agent == null || tts == null)
            {
                problems.Add($"{spec.name}: nothing was copied from {spec.template} (no ConversationalAgent or CrossPlatformTTS on the root)");
                return null;
            }
            agent.agentName = spec.name;
            agent.displayName = "";
            agent.sentences.Clear();
            tts.modelFileName = spec.voice;
            tts.lengthScale = spec.lengthScale;
            tts.extraArgs = spec.speaker >= 0 ? $"--speaker {spec.speaker}" : "";
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;

            var saved = PrefabUtility.SaveAsPrefabAsset(avatar, path, out bool ok);
            if (!ok) problems.Add($"{spec.name}: could not save {path}");
            return ok ? saved : null;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(template);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    /// <summary>
    /// Finds the new body's counterpart of an object on the study avatar: the same path (the root and the Bip01 bones
    /// have the same names on every Rocketbox body), the copy of a copied object, or for a skinned mesh the new face mesh.
    /// </summary>
    private class BodyMap
    {
        private readonly Transform _from, _to, _face;
        private readonly Dictionary<Transform, Transform> _copies = new();

        public BodyMap(Transform from, Transform to)
        {
            _from = from;
            _to = to;
            // The face: the skinned mesh with blend shapes that is switched on (FixRocketboxMaxImport hides all but hipoly).
            _face = to.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                .OrderByDescending(r => r.gameObject.activeSelf)
                .Select(r => r.transform)
                .FirstOrDefault();
        }

        public void AddCopy(Transform source, Transform copy) => _copies[source] = copy;

        /// <summary>True if the transform is, or is inside, an object that was copied whole.</summary>
        public bool IsInsideCopy(Transform t)
        {
            for (; t != null && t != _from; t = t.parent)
                if (_copies.ContainsKey(t)) return true;
            return false;
        }

        public Transform Find(Transform t)
        {
            if (t == _from) return _to;
            for (var a = t; a != null && a != _from; a = a.parent)
                if (_copies.TryGetValue(a, out var copy)) return a == t ? copy : copy.Find(RelativePath(a, t));
            var found = _to.Find(RelativePath(_from, t));
            if (found != null) return found;
            return t.GetComponent<SkinnedMeshRenderer>() != null ? _face : null;
        }

        /// <summary>Points every reference of <paramref name="copy"/> that is into the study avatar at the new body.</summary>
        public void Retarget(Component copy, string avatarName, List<string> problems)
        {
            var so = new SerializedObject(copy);
            var p = so.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.name == "m_GameObject") continue;
                var value = p.objectReferenceValue;
                Transform t = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
                if (t == null || !t.IsChildOf(_from)) continue; // empty, or an asset (controller, materials, ...)

                Transform target = Find(t);
                Object mapped = target == null ? null
                    : value is GameObject ? target.gameObject
                    : SameComponent((Component)value, target);
                if (mapped == null)
                    problems.Add($"{avatarName}: {copy.GetType().Name}.{p.displayName} pointed at '{t.name}', which the new body has no match for");
                p.objectReferenceValue = mapped;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The component on <paramref name="target"/> of the same type (and the same index among that type).</summary>
        private static Component SameComponent(Component source, Transform target)
        {
            var type = source.GetType();
            int index = Array.IndexOf(source.GetComponents(type), source);
            var candidates = target.GetComponents(type);
            return index >= 0 && index < candidates.Length ? candidates[index] : null;
        }

        private static string RelativePath(Transform root, Transform t)
        {
            string path = t.name;
            for (var a = t.parent; a != null && a != root; a = a.parent) path = a.name + "/" + path;
            return path;
        }
    }
}
