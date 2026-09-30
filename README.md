# Intelligent-Social-AR

**A mixed-reality testbed for studying augmented focus group moderation.**

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE.md)
![Unity 2022.3.42f1](https://img.shields.io/badge/Unity-2022.3.42f1-black?logo=unity)
![Meta XR SDK 205](https://img.shields.io/badge/Meta_XR_SDK-205.0.0-0467DF?logo=meta)
![Platform: Meta Quest 3 via Link](https://img.shields.io/badge/Platform-Meta_Quest_3_(Link)-lightgrey)

Intelligent-Social-AR puts a human moderator in charge of a simulated focus group. Wearing a Meta Quest headset in passthrough mixed reality, the participant moderates a discussion among four embodied virtual agents, then formulates follow-up questions. The agents speak fully scripted dialogue with local neural text-to-speech and lip sync, so every participant hears identical content. In some conditions, an AR layer shows the moderator summaries, speaking statistics and group-dynamics charts.

The testbed was built for a controlled user study on how **Intelligent Augmented Moderation** and the **APR** seating approach affect moderators' cognitive load, situational awareness and performance.

> **Status:** research software under active development. The accompanying paper is in preparation.

---

## Study design

A 2×2 within-subjects design crosses two factors:

|  | **Traditional (T)**: agents seated around a table | **APR (A)**: agents stand on an opinion grid |
|---|---|---|
| **No augmentation (nA)** | `nAT` | `nAA` |
| **Intelligent augmentation (iA)** | `iAT` | `iAA` |

- **APR grid.** A 5 × 4 floor grid whose rows encode opinion, from *Very Good* (nearest row) to *Very Bad* (farthest). Before speaking, each agent walks to the row matching its stance on the question.
- **Augmentations (iA conditions).** Group-level information is shown on the back wall; per-agent information appears above an agent when the moderator looks at it (0.3 s head-gaze dwell). iAA shows seating patterns, speaking summaries, movement and similarity charts; iAT shows turn-taking, speaking-time and dissenting-opinion statistics.
- **Counterbalancing.** Four condition orders (a rotated Latin square) are assigned across participants. Each condition runs four questions from a compiled per-condition script, so script and question order are balanced across conditions.
- **Avatar teams.** The 16 avatars form four fixed teams (A–D) of two male and two female voices, matched on pitch, speaking rate, loudness and accent. Team *k* always appears in trial position *k*; because condition order rotates, every team meets every condition exactly once across the four sequences.

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
| **Git** | [Git LFS](https://git-lfs.com/), used for the voice models |
| **Optional** | Meta XR Simulator v205 for testing without a headset |

The study runs from the Unity Editor over Quest Link. Standalone (untethered) Quest builds are not supported yet: speech would need to be pre-baked and gaze data would need a writable path on the device.

## Getting started

```bash
git lfs install
git clone https://github.com/s91alimora/Intelligent-Social-AR.git
```

1. Open the project in Unity 2022.3.42f1. The first import takes a while.
2. Connect the Quest over Link and confirm Unity sees it (Meta XR SDK prompts in the Project Setup Tool).
3. Open `Assets/Yutong/Scenes/Mater Level Control.unity`, the entry scene.

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
├── Yutong/
│   ├── Scripts/             # Study logic: session flow, agents, TTS, gaze logging, grid, MRUK
│   │   └── Editor/          # Custom inspectors and editor tools
│   ├── Scenes/              # Mater Level Control (entry) + Testbed iAA / iAT / nAA / nAT
│   └── Prefabs/Avatars/     # The 16 study avatars (Male/, Female/)
├── Jasmine/                 # Avatar import, blinking and lip-sync setup
├── Avatars/                 # Microsoft Rocketbox avatars and animations
├── Resources/Manuscripts/   # Per-condition study scripts
└── StreamingAssets/
    ├── Augmentation Images/ # AR chart images
    └── tts/                 # Piper runtime and voice models
Packages/                    # Unity package manifest (Meta XR SDK 205)
ProjectSettings/             # Unity project and build settings
```

## Known limitations

- Windows only; the TTS engine runs as a local process.
- Standalone Quest builds are not supported (see [Requirements](#requirements)). *Tools → iXR → Bake All Session Audio* pre-renders speech for one voice only.
- Gaze is approximated by head orientation.
- Room alignment with MR Utility Kit is experimental and not used by the study scenes.

## Citation

If you use this software, please cite it using the metadata in [`CITATION.cff`](CITATION.cff) (GitHub's **Cite this repository** button provides APA and BibTeX). A paper describing the study is in preparation.

## License

The project's own code is released under the [Apache License 2.0](LICENSE.md). It bundles third-party software, models and assets under their own licenses; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Acknowledgements

Developed at Virginia Tech by **Saeid Alimoradi**, **Yutong** ([@ryutong](https://github.com/ryutong)) and **Jasmine Walker** ([@Jwalker055](https://github.com/Jwalker055)).

Built with [Unity](https://unity.com/), the [Meta XR SDK](https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/), [Piper](https://github.com/rhasspy/piper), [eSpeak NG](https://github.com/espeak-ng/espeak-ng), [ONNX Runtime](https://onnxruntime.ai/) and [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox).
