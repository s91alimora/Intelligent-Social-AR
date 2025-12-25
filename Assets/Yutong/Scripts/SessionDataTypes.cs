using System;
using System.Collections.Generic;

[Serializable]
public class ScriptJsonWrapper
{
    // We will normalize keys like "q1_Txt" -> "questionText" before parsing
    public QuestionData question_1_Data;
    public QuestionData question_2_Data;
    public QuestionData question_3_Data;
    public QuestionData question_4_Data;
}

[Serializable]
public class QuestionData
{
    public string questionText; // Replaces q1_Txt, etc.
    public PositionConfig apr_Positions;
    public Responses glb_Responses;
    // We ignore augmentations for now as per instructions
}

[Serializable]
public class PositionConfig
{
    public int[] order_Seating; // e.g. [2, 1, 3, 4]
    public string agent_1_Pos;
    public string agent_2_Pos;
    public string agent_3_Pos;
    public string agent_4_Pos;
}

[Serializable]
public class Responses
{
    public int[] order_Speech; // e.g. [1, 2, 3, 4]
    public string agent_1_Resp;
    public string agent_2_Resp;
    public string agent_3_Resp;
    public string agent_4_Resp;
}
