# Intelligent-Social-AR

**Intelligent Augmented Reality as a Cognitive Assistant for Facilitators of Collocated Group Interaction**

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE.md)
![Unity 2022.3.42f1](https://img.shields.io/badge/Unity-2022.3.42f1-black?logo=unity)
![Meta XR SDK 205](https://img.shields.io/badge/Meta_XR_SDK-205.0.0-0467DF?logo=meta)
![Platform: Meta Quest 3 via Link](https://img.shields.io/badge/Platform-Meta_Quest_3_(Link)-lightgrey)

Leading or moderating a group discussion splits attention across tasks that compete for the same mental resources: the leader/moderator watches the group, listens to the content, and plans the next steps at the same time. Multiple Resource Theory predicts that such competing demands degrade performance, and Cognitive Load Theory predicts that extra processing reduces the capacity left for the main task. Intelligent-Social-AR is a context-aware AR that collects data on a social situation such as group moderaltion, and augments information onto the user reality to reduce the cognitive load. To test this, we built this testbed that puts a human moderator in charge of a simulated focus group. Wearing a Meta Quest headset in passthrough mixed reality, the participant moderates a discussion among four embodied virtual agents, then formulates follow-up questions. The agents speak fully scripted dialogue with local neural text-to-speech and lip sync, so every participant hears identical content. In some conditions, an AR layer shows the moderator summaries, speaking statistics and group-dynamics charts. This version

The testbed was built for a controlled user study. This version is an initial design that incorporate the AI part in a Wizard-of-Oz fashion where the experimenter is in charge of the session entirely. This means that all the augmentations are pre-defined and set for each experimental conditions.

The testbed has been developed in modules and each module can be refined and customized.

> **Status:** research software under active development. The accompanying paper is in preparation.

---

## Study design

A 2×2 within-subjects design crosses two factors:

|  | **Traditional (T)**: agents seated around a table | **APR (A)**: agents stand on an opinion grid |
|---|---|---|
| **No augmentation (nA)** | `nAT` | `nAA` |
| **Intelligent augmentation (iA)** | `iAT` | `iAA` |

- **APR** Anchored Positional Responding or APR is a novel approach in running focus groups introduced by [Alimoradi et al. (2025)](https://doi.org/10.1177/10711813251361005). In the approach, participants are first required to physically position themselves within a room-sized replicated Likert Scale (as external anchors) in response to a given question prompt before responding verbally. For this testbed, a 5 × 4 floor grid whose rows encode opinion, from *Very Good* (nearest row) to *Very Bad* (farthest). Before speaking, each agent walks to the row matching its stance on the question.
- **Augmentations** Pre-defined information and graphs derived from scripted transcripts. The augmentations are supposably information that an LLM/VLM infers from collected context. In the testbed, two type of information is augmented; group-level and individual level. Group-level contains information summarizing group interaction that are shown on the back. Individual-level contains per-agent information appears in-front of an agent when the moderator looks at it (0.3 s head-gaze dwell). iAA shows information that is extracted through APR approach; iAT shows information that corresponds with traditional focus group.

Each condition begins with the agents introducing themselves. Every question then runs through the phases **Setup → (Grid move) → Discussion → Augmentations**, advanced by the experimenter.

## Features

- Scripted multi-agent conversations with sequential turn-taking, driven by JSON manuscripts
- Local neural text-to-speech with [Piper](https://github.com/rhasspy/piper): per-agent voices, calibrated speaking rate and normalized loudness; no cloud services
- Audio-driven lip sync (Oculus LipSync) and automatic blinking on [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox) avatars
- Head-gaze fixation logging per trial and phase, written to CSV
- Study-integrity checks: the scene's condition must match the planned sequence, avatar teams are validated before a session, and test mode is visibly marked
- Experimental room alignment using Meta's MR Utility Kit (separate prototype scene)

## Requirements

| | |
|---|---|
| **Unity** | 2022.3.42f1 with **Android Build Support** (incl. OpenJDK and Android SDK & NDK Tools) |
| **Headset** | Meta Quest 3, connected to the PC with **Quest Link** (USB-C or Air Link) |
| **PC** | Windows 10/11; the bundled Piper TTS runtime is Windows-only |
| **Packages** | Meta XR All-in-One SDK 205.0.0 (resolved automatically from `Packages/manifest.json`) |
| **Internet** | Needed once, to download the text-to-speech runtime and voices (about 1.1 GB) |
| **Optional** | Meta XR Simulator v205 for testing without a headset |

The study runs from the Unity Editor over Quest Link. Standalone (untethered) Quest builds are not supported yet: speech would need to be pre-baked and gaze data would need a writable path on the device.

## Getting started

1. Clone the repository:

   ```bash
   git clone https://github.com/s91alimora/Intelligent-Social-AR.git
   ```

2. Download the text-to-speech runtime and voices. From the repository folder, in PowerShell:

   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools\Setup-TTS.ps1
   ```

   The script fetches [Piper](https://github.com/rhasspy/piper) and the study's voice models from their official sources and verifies every file's checksum. They are not stored in this repository because several voices' licenses don't allow redistribution (see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)). It is safe to run again.

3. Open the project in Unity 2022.3.42f1. The first import takes a while.
4. Connect the Quest over Link and confirm Unity sees it (Meta XR SDK prompts in the Project Setup Tool).
5. Open `Assets/Testbed/Scenes/Master Level Control.unity`, the entry scene.

## Running a session

1. Select the **MasterLevelController** object and set:
   - **Participant ID**, used in the CSV file names
   - **Study Sequence**, one of the four condition orders (`Sequence_1_nAT_nAA_iAT_iAA`, `Sequence_2_nAA_iAT_iAA_nAT`, `Sequence_3_iAT_iAA_nAT_nAA`, `Sequence_4_iAA_nAT_nAA_iAT`)
2. Make sure **Is Test Mode** is off in the four `Testbed` scenes. In test mode, introductions are skipped, discussions can be skipped, and the wall shows `[TEST MODE]`.
3. Press **Play**. The master scene loads the first condition.

| Control | Action |
|---|---|
| **Space** (or right controller **A**) | Advance to the next phase |
| **T** | Load the next condition, once all four questions are finished |

## Data output

Gaze fixations are written as CSV files to `Assets/Logs/` (ignored by git), one file per condition:

`<Sequence>_<ParticipantID>_<yyyy-MM-dd_HH-mm-ss>.csv`

| Column | Meaning |
|---|---|
| `ParticipantID` | Participant identifier from the master scene |
| `Sequence` | Condition order used for this participant |
| `Condition` | `iAA`, `iAT`, `nAA` or `nAT` |
| `Team` | Avatar team shown in this condition (`Team A`–`Team D`) |
| `TrialScript` | Manuscript the question came from |
| `QuestionNum` | Question number within the condition (1–4) |
| `Phase` | `Conversation` or `Augmentation` |
| `TargetType` | `Avatar` or `Wall` |
| `TargetID` | Agent slot (`agent_1`–`agent_4`) or `Wall` |
| `TargetName` | Avatar name (e.g. `Tony`) |
| `FixationDuration` | Fixation length in seconds |

Gaze is **head gaze** (a ray from the headset's forward direction), not eye tracking.

## Study content

- **Manuscripts:** `Assets/Resources/Manuscripts/{iAA,iAT,nAA,nAT}.json`. Each file holds four questions in asking order, with the agents' stances, spoken responses, speaking order and the data for the augmentations. Scenes load the file matching their condition automatically.
- **Augmentation charts:** `Assets/StreamingAssets/Augmentation Images/`, 112 pre-rendered images named `<condition>_Q<n>_[A<agent>_]<chart>.jpg`.
- **Avatar teams:** set on MasterLevelController; *Tools → iXR → Assign Proposed Avatar Teams* restores the documented partition.

## Project structure

```
Assets/
├── Testbed/
│   ├── Scripts/             # Study logic: session flow, agents, TTS, gaze logging, grid, blinking
│   │   └── Editor/          # Custom inspectors, avatar-team and import tools
│   ├── Scenes/              # Master Level Control (entry) + Testbed iAA / iAT / nAA / nAT
│   │   └── Prototypes/      # Development scenes (lip sync, head-ray interaction, MRUK)
│   ├── Prefabs/Avatars/     # The 16 study avatars (Male/, Female/)
│   ├── Animation/           # Talking and idle animator controllers
│   ├── Models/              # Table model; Rocketbox avatars with custom import settings
│   ├── Materials/, Shaders/, Audio/
├── Avatars/                 # Microsoft Rocketbox avatars and animations
├── Resources/Manuscripts/   # Per-condition study scripts
└── StreamingAssets/
    ├── Augmentation Images/ # AR chart images
    └── tts/                 # Voice configs (tracked); Piper runtime and models (downloaded)
Packages/                    # Unity package manifest (Meta XR SDK 205)
ProjectSettings/             # Unity project and build settings
Tools/                       # Setup-TTS.ps1: downloads the text-to-speech runtime and voices
```

## Documentation

- [Running a study session](docs/running-a-session.md): step-by-step checklist for the experimenter
- [Manuscript format](docs/manuscript-format.md): how to write or edit study content
- [Architecture](docs/architecture.md): how the components fit together
- [Contributing](CONTRIBUTING.md): setup, branching and what not to commit

## Known limitations

- **Two voices are not yet distributable.** The Amy and Tony voices used in the study have no verified official source, so the setup script cannot download them, and those two avatars stay silent in a fresh clone. They will be replaced or documented in a future update.
- Some voices are licensed for non-commercial or research use only; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md#voice-models) before reusing them.
- Windows only; the TTS engine runs as a local process.
- Standalone Quest builds are not supported (see [Requirements](#requirements)). *Tools → iXR → Bake All Session Audio* pre-renders speech for one voice only.
- Gaze is approximated by head orientation.
- Room alignment with MR Utility Kit is experimental and not used by the study scenes.

## Citation

If you use this software, please cite it using the metadata in [`CITATION.cff`](CITATION.cff) (GitHub's **Cite this repository** button provides APA and BibTeX). A paper describing the study is in preparation.

## License

The project's own code is released under the [Apache License 2.0](LICENSE.md). It bundles third-party software, models and assets under their own licenses; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Acknowledgements

Developed at Virginia Tech by **Saeid Alimoradi**, **Yutong Ren** ([@ryutong](https://github.com/ryutong)) and **Jasmine Walker** ([@Jwalker055](https://github.com/Jwalker055)).

Built with [Unity](https://unity.com/), the [Meta XR SDK](https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/), [Piper](https://github.com/rhasspy/piper), [eSpeak NG](https://github.com/espeak-ng/espeak-ng), [ONNX Runtime](https://onnxruntime.ai/) and [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox).
