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
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Study Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(studyCondition);
        
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
