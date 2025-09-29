#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(ConversationalAgentsManager))]
public class ConversationalAgentsManagerEditor : Editor
{
    SerializedProperty agentsProp;
    SerializedProperty sequenceProp;
    SerializedProperty playOnStartProp;
    SerializedProperty pauseBetweenLinesProp;

    ReorderableList seqList;

    void OnEnable()
    {
        agentsProp = serializedObject.FindProperty("agents");
        sequenceProp = serializedObject.FindProperty("sequence");
        playOnStartProp = serializedObject.FindProperty("playOnStart");
        pauseBetweenLinesProp = serializedObject.FindProperty("pauseBetweenLines");

        // Build the reorderable list for the "sequence" field
        seqList = new ReorderableList(serializedObject, sequenceProp, true, true, true, true);

        seqList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "Conversation Sequence");
        };

        seqList.elementHeight = EditorGUIUtility.singleLineHeight * 3f + 12f;

        seqList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var stepProp = sequenceProp.GetArrayElementAtIndex(index);
            var agentProp = stepProp.FindPropertyRelative("agent");
            var idxProp = stepProp.FindPropertyRelative("sentenceIndex");

            rect.y += 2;
            var line = EditorGUIUtility.singleLineHeight;

            // Title: Step #
            EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width, line),
                                 $"Step {index + 1}", EditorStyles.boldLabel);

            // Fetch registered agents for popups
            var mgr = (ConversationalAgentsManager)target;
            var registered = new List<ConversationalAgent>();
            for (int i = 0; i < agentsProp.arraySize; i++)
            {
                var a = agentsProp.GetArrayElementAtIndex(i).objectReferenceValue as ConversationalAgent;
                if (a != null) registered.Add(a);
            }

            // Prep names
            string[] agentNames = registered.Select(a => a.agentName).ToArray();

            // Row: Agent popup
            rect.y += line + 2;
            var left = new Rect(rect.x, rect.y, 80, line);
            var right = new Rect(rect.x + 85, rect.y, rect.width - 85, line);
            EditorGUI.LabelField(left, "Agent");

            ConversationalAgent currentAgent = agentProp.objectReferenceValue as ConversationalAgent;
            int currentAgentIdx = Mathf.Max(0, registered.IndexOf(currentAgent));
            int newAgentIdx = currentAgentIdx;

            if (registered.Count > 0)
            {
                newAgentIdx = EditorGUI.Popup(right, currentAgentIdx, agentNames);
                var chosen = registered[Mathf.Clamp(newAgentIdx, 0, registered.Count - 1)];
                if (chosen != currentAgent)
                {
                    agentProp.objectReferenceValue = chosen;
                    idxProp.intValue = 0; // reset sentence index when agent changes
                    currentAgent = chosen;
                }
            }
            else
            {
                EditorGUI.HelpBox(right, "Add agents above to use in steps.", MessageType.Info);
            }

            // Row: Sentence popup (filtered by already-used ones for this agent in prior steps)
            rect.y += line + 2;
            left = new Rect(rect.x, rect.y, 80, line);
            right = new Rect(rect.x + 85, rect.y, rect.width - 85, line);
            EditorGUI.LabelField(left, "Sentence");

            if (currentAgent != null)
            {
                // Build "used" set for this agent up to (but not including) this index
                var used = new HashSet<int>();
                for (int i = 0; i < index; i++)
                {
                    var prev = sequenceProp.GetArrayElementAtIndex(i);
                    var prevAgent = prev.FindPropertyRelative("agent").objectReferenceValue as ConversationalAgent;
                    var prevIdx = prev.FindPropertyRelative("sentenceIndex").intValue;
                    if (prevAgent == currentAgent) used.Add(prevIdx);
                }

                // Available options for this element (exclude used)
                var options = new List<(int idx, string label)>();
                for (int s = 0; s < currentAgent.SentenceCount; s++)
                {
                    if (used.Contains(s)) continue;
                    string preview = currentAgent.GetSentence(s) ?? "";
                    preview = preview.Replace("\n", " ");
                    if (preview.Length > 48) preview = preview[..48] + "бн";
                    options.Add((s, $"{s}: {preview}"));
                }

                if (options.Count == 0)
                {
                    EditorGUI.HelpBox(right, $"No remaining sentences on '{currentAgent.agentName}'.", MessageType.Warning);
                }
                else
                {
                    // Map current sentence index to current filtered list position
                    int filteredCurrent = Mathf.Max(0, options.FindIndex(o => o.idx == idxProp.intValue));
                    if (filteredCurrent < 0) filteredCurrent = 0;
                    var labels = options.Select(o => o.label).ToArray();
                    int chosenFiltered = EditorGUI.Popup(right, filteredCurrent, labels);
                    idxProp.intValue = options[chosenFiltered].idx;
                }
            }
            else
            {
                EditorGUI.HelpBox(right, "Select an agent first.", MessageType.Info);
            }
        };

        seqList.onAddCallback = (ReorderableList list) =>
        {
            sequenceProp.InsertArrayElementAtIndex(sequenceProp.arraySize);
            var newStep = sequenceProp.GetArrayElementAtIndex(sequenceProp.arraySize - 1);
            newStep.FindPropertyRelative("agent").objectReferenceValue = null;
            newStep.FindPropertyRelative("sentenceIndex").intValue = 0;
        };

        // onRemove already provided by list; default behavior is fine
        // reordering is on by default (drag handle at left)
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(playOnStartProp);
        EditorGUILayout.PropertyField(pauseBetweenLinesProp);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Registered Agents", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(agentsProp, includeChildren: true);

        EditorGUILayout.Space(8);
        seqList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();

        // Runtime play button
        if (Application.isPlaying)
        {
            var mgr = (ConversationalAgentsManager)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Play Conversation")) mgr.Play();
            if (GUILayout.Button("Stop")) mgr.Stop();
        }
    }
}
#endif
