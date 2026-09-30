# Manuscript format

A manuscript is the script for one study condition: the questions, where each agent stands, what each agent says, and the data shown in the augmentations. Each condition loads one manuscript from `Assets/Resources/Manuscripts/`, named after the condition: `iAA.json`, `iAT.json`, `nAA.json`, `nAT.json`. A scene loads the file matching its **Study Condition** automatically.

Manuscripts are not included in this repository. Start from [`examples/manuscript-example.json`](examples/manuscript-example.json), which contains one fully worked question and placeholders for the other three.

A manuscript contains four questions, in the order they are asked:

```json
{
  "question_1_Data": { ... },
  "question_2_Data": { ... },
  "question_3_Data": { ... },
  "question_4_Data": { ... }
}
```

Agents are numbered 1–4 (`agent_1` … `agent_4`). Which avatar plays each number is decided by the avatar team, not by the manuscript.

## Question block

```json
"question_1_Data": {
  "q1_Txt": "How useful are the library's extended evening hours?",
  "apr_Positions":     { ... },
  "glb_Responses":     { ... },
  "glb_Augmentations": { ... },
  "apr_Augmentations": { ... },
  "trd_Augmentations": { ... }
}
```

| Key | Used in | Purpose |
|---|---|---|
| `qN_Txt` | all conditions | The question shown on the wall. `N` matches the question number. |
| `apr_Positions` | iAA, nAA | Where each agent stands on the opinion grid |
| `glb_Responses` | all conditions | What the agents say, and in which order |
| `glb_Augmentations` | iAA, iAT | Text summaries: per agent, and for the group |
| `apr_Augmentations` | iAA | Grid-condition statistics and chart images |
| `trd_Augmentations` | iAT | Table-condition statistics |

Blocks a condition doesn't use are ignored, so every manuscript can keep the full structure.

## `apr_Positions`: grid seating

```json
"apr_Positions": {
  "order_Seating": [2, 1, 4, 3],
  "agent_1_Pos": "Good",
  "agent_2_Pos": "Very Good",
  "agent_3_Pos": "Neutral",
  "agent_4_Pos": "Good"
}
```

- `order_Seating`: the order in which agents walk to the grid.
- `agent_N_Pos`: the agent's stance, which decides its grid row. Must be exactly one of `Very Good`, `Good`, `Neutral`, `Bad`, `Very Bad`. Row 0 (nearest) is *Very Good* and row 4 (farthest) is *Very Bad*. Agents with the same stance stand side by side in walking order.

## `glb_Responses`: the conversation

```json
"glb_Responses": {
  "order_Speech": [2, 1, 4, 3],
  "agent_2_Resp": "I use them almost every week...",
  "agent_1_Resp": "I agree they help, especially before exams...",
  "agent_4_Resp": "They are useful, but the building feels a bit empty...",
  "agent_3_Resp": "I rarely go in the evening..."
}
```

- `order_Speech`: who speaks each turn. An agent may appear more than once.
- `agent_N_Resp`: the spoken lines, one per turn.

> **Important:** responses are read **in the order they appear in the file** and paired with `order_Speech` turn by turn; the agent number in the key is not used for matching. Write the `agent_N_Resp` entries in exactly the same order as `order_Speech`. When an agent speaks twice, repeat its key (`"agent_2_Resp"` twice); standard JSON parsers keep only one of the duplicates, but the testbed reads all of them.

The text is synthesized with the avatar's voice. Plain sentences work best; avoid symbols and abbreviations the voice would read literally.

## `glb_Augmentations`: text summaries (iAA, iAT)

```json
"glb_Augmentations": {
  "glb_ind_Sums": {
    "agent_1_Sum": "■ Finds the hours helpful before exams. \n ■ Does not visit weekly."
  },
  "glb_Grp_Sums": {
    "glb_Grp_Suggestions": "■ What would make late visits feel more comfortable? \n ■ Who is not using the extended hours, and why?",
    "glb_Emg_Themes": "■ Focus and fewer interruptions \n ■ Exam-time demand"
  }
}
```

- `agent_N_Sum`: bullet summary shown in front of an agent when the moderator looks at it.
- `glb_Grp_Suggestions` and `glb_Emg_Themes`: follow-up suggestions and emerging themes on the back wall. Up to four themes are displayed.
- Start every bullet with `■`; the testbed splits on this character and puts each bullet on its own line.

## `apr_Augmentations`: grid-condition data (iAA)

```json
"apr_Augmentations": {
  "apr_Ind_Stats": {
    "agent_1_apr_Stats": {
      "times_Spoken_First": 0,
      "times_Seated_First": 0,
      "ind_Seating": "Augmentation Images/Individual/example_Q1_A1_Ind_Seating.jpg"
    }
  },
  "apr_Grp_Sums": {
    "speaking_Sum": "Augmentation Images/Group/example_Q1_Speaking_Sum.jpg",
    "grp_Move":     "Augmentation Images/Group/example_Q1_Group_Move.jpg",
    "grp_Sim_Mat":  "Augmentation Images/Group/example_Q1_Sim_Mat.jpg"
  }
}
```

Image paths are relative to `Assets/StreamingAssets/`. Keep one entry per agent in `apr_Ind_Stats`.

## `trd_Augmentations`: table-condition data (iAT)

```json
"trd_Augmentations": {
  "trd_Ind_Stats": {
    "agent_1_trd_Stats": { "dur_Spoken": "<1", "times_Spoken": 1, "words_Spoken": 18 }
  },
  "trd_Grp_Sum": { "turn_Taking": 3, "no_Diss_Ops": 1 }
}
```

- `dur_Spoken` is displayed as written (for example `"<1"` minutes).
- `turn_Taking` and `no_Diss_Ops` (number of dissenting opinions) are shown on the back wall.

## Augmentation images

Put images anywhere under `Assets/StreamingAssets/` and reference them by their path relative to that folder. Images are loaded while the testbed runs, so they don't need to be imported or assigned in Unity. Charts that show grid positions should use the same row order as the testbed: *Very Good* nearest, *Very Bad* farthest.

## Checklist for a new manuscript

- [ ] Four questions, `question_1_Data` … `question_4_Data`, each with its own `qN_Txt`
- [ ] Every `agent_N_Pos` is one of the five stance labels, spelled exactly
- [ ] `agent_N_Resp` entries are in the same order as `order_Speech`
- [ ] Augmentation blocks filled in for every question used in an iA condition
- [ ] Every image path exists under `Assets/StreamingAssets/`
- [ ] Bullets start with `■`
- [ ] The file is valid JSON apart from intentionally repeated `agent_N_Resp` keys
