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
    public GroupAugmentations glb_Augmentations;
    public AppraisalAugmentations apr_Augmentations;
}

[Serializable]
public class GroupAugmentations
{
    public Dictionary<string, string> glb_ind_Sums; // Note: JsonUtility doesn't support Dictionary, but we can keep it for structure or skip.
    public GroupSummaries glb_Grp_Sums;
}

[Serializable]
public class GroupSummaries
{
    public string glb_Grp_Suggestions;
    public string glb_Emg_Themes;
}

[Serializable]
public class AppraisalAugmentations
{
    public GroupAppraisalSummaries apr_Grp_Sums;
}

[Serializable]
public class GroupAppraisalSummaries
{
    public string speaking_Sum;
    public string grp_Move;
    public string grp_Sim_Mat;
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
