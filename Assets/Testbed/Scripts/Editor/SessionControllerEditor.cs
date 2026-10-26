using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(SessionController))]
public class SessionControllerEditor : Editor
{
    private SerializedProperty participantID;
    private SerializedProperty gazeRecorder;
    private SerializedProperty studyCondition;
    private SerializedProperty availableMaleAvatars;
    private SerializedProperty availableFemaleAvatars;
    private SerializedProperty gridGenerator;
    private SerializedProperty manager;
    private SerializedProperty moveDuration;
    private SerializedProperty walkSpeed;
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
    private SerializedProperty enableIntroductionPhase;
    private SerializedProperty introductionLineFormat;
    private SerializedProperty sphereCursorRenderer;
    private SerializedProperty conversationEndColor;
    private SerializedProperty questionNumberFormat;
    private SerializedProperty readyMessage;
    private SerializedProperty readyMessageFontSize;
    private SerializedProperty endMessage;
    private SerializedProperty lastConditionEndMessage;
    private SerializedProperty endMessageFontSize;
    private SerializedProperty tableSetting;
    private SerializedProperty avatarAppearDelay;
    private SerializedProperty conversationMinFixation;
    private SerializedProperty reviewMinFixation;

    private void OnEnable()
    {
        participantID = serializedObject.FindProperty("participantID");
        gazeRecorder = serializedObject.FindProperty("gazeRecorder");
        studyCondition = serializedObject.FindProperty("studyCondition");
        availableMaleAvatars = serializedObject.FindProperty("availableMaleAvatars");
        availableFemaleAvatars = serializedObject.FindProperty("availableFemaleAvatars");
        gridGenerator = serializedObject.FindProperty("gridGenerator");
        manager = serializedObject.FindProperty("manager");
        moveDuration = serializedObject.FindProperty("moveDuration");
        walkSpeed = serializedObject.FindProperty("walkSpeed");
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
        enableIntroductionPhase = serializedObject.FindProperty("enableIntroductionPhase");
        introductionLineFormat = serializedObject.FindProperty("introductionLineFormat");
        sphereCursorRenderer = serializedObject.FindProperty("sphereCursorRenderer");
        conversationEndColor = serializedObject.FindProperty("conversationEndColor");
        questionNumberFormat = serializedObject.FindProperty("questionNumberFormat");
        readyMessage = serializedObject.FindProperty("readyMessage");
        readyMessageFontSize = serializedObject.FindProperty("readyMessageFontSize");
        endMessage = serializedObject.FindProperty("endMessage");
        lastConditionEndMessage = serializedObject.FindProperty("lastConditionEndMessage");
        endMessageFontSize = serializedObject.FindProperty("endMessageFontSize");
        tableSetting = serializedObject.FindProperty("tableSetting");
        avatarAppearDelay = serializedObject.FindProperty("avatarAppearDelay");
        conversationMinFixation = serializedObject.FindProperty("conversationMinFixation");
        reviewMinFixation = serializedObject.FindProperty("reviewMinFixation");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Study Setup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(participantID);
        EditorGUILayout.PropertyField(gazeRecorder);
        EditorGUILayout.PropertyField(studyCondition);

        var cond = (SessionController.StudyCondition)studyCondition.enumValueIndex;
        EditorGUILayout.HelpBox(
            $"Loads Resources/{SessionController.ManuscriptResourceFolder}/{cond}.json - the compiled per-condition " +
            "manuscript with questions 1-4 in final asking order. No script wiring needed.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Base Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(availableMaleAvatars, true);
        EditorGUILayout.PropertyField(availableFemaleAvatars, true);
        EditorGUILayout.PropertyField(manager);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(isTestMode);
        EditorGUILayout.PropertyField(avatarAppearDelay);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Introduction Phase", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(enableIntroductionPhase);
        if (enableIntroductionPhase.boolValue)
        {
            EditorGUILayout.PropertyField(introductionLineFormat);
        }
        EditorGUILayout.LabelField("Cursor Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sphereCursorRenderer);
        EditorGUILayout.PropertyField(conversationEndColor);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Back Wall", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(questionNumberFormat);
        EditorGUILayout.PropertyField(readyMessage);
        EditorGUILayout.PropertyField(readyMessageFontSize);
        EditorGUILayout.PropertyField(endMessage);
        EditorGUILayout.PropertyField(lastConditionEndMessage);
        EditorGUILayout.PropertyField(endMessageFontSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Gaze Recording", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(conversationMinFixation);
        EditorGUILayout.PropertyField(reviewMinFixation);

        EditorGUILayout.Space();
        
        SessionController.StudyCondition condition = (SessionController.StudyCondition)studyCondition.enumValueIndex;

        if (condition == SessionController.StudyCondition.iAA || condition == SessionController.StudyCondition.nAA)
        {
            EditorGUILayout.LabelField("iAA/nAA (Grid) Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gridGenerator);
            EditorGUILayout.PropertyField(moveDuration);
            EditorGUILayout.PropertyField(walkSpeed);
            EditorGUILayout.PropertyField(lineUpSpacing);
        }
        else if (condition == SessionController.StudyCondition.iAT || condition == SessionController.StudyCondition.nAT)
        {
            EditorGUILayout.LabelField("iAT/nAT (Table/Origins) Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(agentOrigins, true);
            EditorGUILayout.PropertyField(tableSetting);
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
