using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(SessionController))]
public class SessionControllerEditor : Editor
{
    private SerializedProperty studyCondition;
    private SerializedProperty scriptFiles;
    private SerializedProperty availableMaleAvatars;
    private SerializedProperty availableFemaleAvatars;
    private SerializedProperty gridGenerator;
    private SerializedProperty manager;
    private SerializedProperty moveDuration;
    private SerializedProperty lineUpSpacing;
    private SerializedProperty agentOrigins;
    private SerializedProperty wallQuestionText;
    private SerializedProperty augmentationPanel;
    private SerializedProperty suggestionsText;
    private SerializedProperty themesTexts;
    private SerializedProperty speakingSumImage;
    private SerializedProperty grpMoveImage;
    private SerializedProperty grpSimMatImage;
    private SerializedProperty turnTakingText;
    private SerializedProperty dissonantOpinionsText;
    private SerializedProperty interacter;
    private SerializedProperty isTestMode;
    private SerializedProperty selectionMode;
    private SerializedProperty manualTrials;

    private void OnEnable()
    {
        studyCondition = serializedObject.FindProperty("studyCondition");
        scriptFiles = serializedObject.FindProperty("scriptFiles");
        availableMaleAvatars = serializedObject.FindProperty("availableMaleAvatars");
        availableFemaleAvatars = serializedObject.FindProperty("availableFemaleAvatars");
        gridGenerator = serializedObject.FindProperty("gridGenerator");
        manager = serializedObject.FindProperty("manager");
        moveDuration = serializedObject.FindProperty("moveDuration");
        lineUpSpacing = serializedObject.FindProperty("lineUpSpacing");
        agentOrigins = serializedObject.FindProperty("agentOrigins");
        wallQuestionText = serializedObject.FindProperty("wallQuestionText");
        augmentationPanel = serializedObject.FindProperty("augmentationPanel");
        suggestionsText = serializedObject.FindProperty("suggestionsText");
        themesTexts = serializedObject.FindProperty("themesTexts");
        speakingSumImage = serializedObject.FindProperty("speakingSumImage");
        grpMoveImage = serializedObject.FindProperty("grpMoveImage");
        grpSimMatImage = serializedObject.FindProperty("grpSimMatImage");
        turnTakingText = serializedObject.FindProperty("turnTakingText");
        dissonantOpinionsText = serializedObject.FindProperty("dissonantOpinionsText");
        interacter = serializedObject.FindProperty("interacter");
        isTestMode = serializedObject.FindProperty("isTestMode");
        selectionMode = serializedObject.FindProperty("selectionMode");
        manualTrials = serializedObject.FindProperty("manualTrials");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Study Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(studyCondition);
        EditorGUILayout.PropertyField(selectionMode);

        if ((SessionController.QuestionSelectionMode)selectionMode.enumValueIndex == SessionController.QuestionSelectionMode.Manual)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual Question Selection", EditorStyles.boldLabel);
            
            // Draw list size
            int listSize = EditorGUILayout.IntField("Number of Trials", manualTrials.arraySize);
            if (listSize != manualTrials.arraySize) manualTrials.arraySize = listSize;

            // Generate options based on available scripts
            List<string> optionsList = new List<string>();
            int numScripts = scriptFiles.arraySize;
            for (int s = 0; s < numScripts; s++)
            {
                for (int q = 1; q <= 4; q++)
                {
                    optionsList.Add($"S_{s + 1}_Q_{q}");
                }
            }
            string[] options = optionsList.ToArray();

            // Draw each trial
            for (int i = 0; i < manualTrials.arraySize; i++)
            {
                SerializedProperty trialProp = manualTrials.GetArrayElementAtIndex(i);
                SerializedProperty sIdxProp = trialProp.FindPropertyRelative("scriptIndex");
                SerializedProperty qIdxProp = trialProp.FindPropertyRelative("questionIndex");

                int currentSelection = (sIdxProp.intValue * 4) + (qIdxProp.intValue - 1);
                // Clamp to avoid out of range if scripts list changed
                if (options.Length > 0)
                {
                    currentSelection = Mathf.Clamp(currentSelection, 0, options.Length - 1);
                    int newSelection = EditorGUILayout.Popup($"Trial {i + 1}", currentSelection, options);
                    sIdxProp.intValue = newSelection / 4;
                    qIdxProp.intValue = (newSelection % 4) + 1;
                }
                else
                {
                    EditorGUILayout.HelpBox("Add script files to generate question options.", MessageType.Info);
                    break;
                }
            }
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Base Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(scriptFiles, true);
        EditorGUILayout.PropertyField(availableMaleAvatars, true);
        EditorGUILayout.PropertyField(availableFemaleAvatars, true);
        EditorGUILayout.PropertyField(manager);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(isTestMode);

        EditorGUILayout.Space();
        
        SessionController.StudyCondition condition = (SessionController.StudyCondition)studyCondition.enumValueIndex;

        if (condition == SessionController.StudyCondition.iAA || condition == SessionController.StudyCondition.nAA)
        {
            EditorGUILayout.LabelField("iAA/nAA (Grid) Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gridGenerator);
            EditorGUILayout.PropertyField(moveDuration);
            EditorGUILayout.PropertyField(lineUpSpacing);
        }
        else if (condition == SessionController.StudyCondition.iAT || condition == SessionController.StudyCondition.nAT)
        {
            EditorGUILayout.LabelField("iAT/nAT (Table/Origins) Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(agentOrigins, true);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Environment UI", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(wallQuestionText);

        if (condition != SessionController.StudyCondition.nAA && condition != SessionController.StudyCondition.nAT)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Augmentation UI", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(augmentationPanel);
            EditorGUILayout.PropertyField(suggestionsText);
            EditorGUILayout.PropertyField(themesTexts, true);
            
            if (condition == SessionController.StudyCondition.iAA)
            {
                EditorGUILayout.PropertyField(speakingSumImage);
                EditorGUILayout.PropertyField(grpMoveImage);
                EditorGUILayout.PropertyField(grpSimMatImage);
            }
            else if (condition == SessionController.StudyCondition.iAT)
            {
                EditorGUILayout.PropertyField(turnTakingText);
                EditorGUILayout.PropertyField(dissonantOpinionsText);
            }

            EditorGUILayout.PropertyField(interacter);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
