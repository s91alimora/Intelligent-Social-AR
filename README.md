# Intelligent-Social-AR

**Intelligent Augmented Reality as a Cognitive Assistant for Facilitators of Collocated Group Interaction**

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE.md)
![Unity 2022.3.42f1](https://img.shields.io/badge/Unity-2022.3.42f1-black?logo=unity)
![Meta XR SDK 205](https://img.shields.io/badge/Meta_XR_SDK-205.0.0-0467DF?logo=meta)
![Platform: Meta Quest 3 via Link](https://img.shields.io/badge/Platform-Meta_Quest_3_(Link)-lightgrey)

Leading or moderating a group discussion splits attention across tasks that compete for the same mental resources: the leader/moderator watches the group, listens to the content, and plans the next steps at the same time. Multiple Resource Theory predicts that such competing demands degrade performance, and Cognitive Load Theory predicts that extra processing reduces the capacity left for the main task. Intelligent-Social-AR is a context-aware AR that collects data on a social situation such as group moderation, and augments information onto the user's reality to reduce the cognitive load. To test this, we built this testbed that puts a human moderator in charge of a simulated focus group. Wearing a Meta Quest headset in passthrough mixed reality, the participant moderates a discussion among four embodied virtual agents, then formulates follow-up questions. The agents speak fully scripted dialogue with local neural text-to-speech and lip sync, so every participant hears identical content. In some conditions, an AR layer shows the moderator summaries, speaking statistics and group-dynamics charts.

The testbed was built for a controlled user study. This version is an initial design that incorporates the AI part in a Wizard-of-Oz fashion, where the experimenter is in charge of the session entirely. This means that all the augmentations are pre-defined and set for each experimental condition.

The testbed has been developed in modules, and each module can be refined and customized (see [Modular design](#modular-design) and [Customizing the testbed](#customizing-the-testbed)).

> **Status:** research software under active development. The accompanying paper is in preparation.

> **Study materials are not included.** The conversation scripts, augmentation images and voice selection used in our study are original research materials and are not part of this repository. The testbed runs with any content you provide in the formats described below.

---

## Study design

A 2×2 within-subjects design crosses two factors:

|  | **Traditional (T)**: agents seated around a table | **APR (A)**: agents stand on an opinion grid |
|---|---|---|
| **No augmentation (nA)** | `nAT` | `nAA` |
| **Intelligent augmentation (iA)** | `iAT` | `iAA` |

- **APR.** Anchored Positional Responding (APR) is a novel approach to running focus groups introduced by [Alimoradi et al. (2025)](https://doi.org/10.1177/10711813251361005). In this approach, participants first physically position themselves within a room-sized replicated Likert scale (as external anchors) in response to a given question prompt, before responding verbally. In this testbed, APR is a 5 × 4 floor grid whose rows encode opinion, from *Very Good* (nearest row) to *Very Bad* (farthest). Before speaking, each agent walks to the row matching its stance on the question.
- **Augmentations.** Pre-defined information and graphs derived from scripted transcripts. The augmentations stand in for information that an LLM/VLM would infer from the collected context. Two types of information are augmented: group-level and individual-level. Group-level information summarizes the group interaction and is shown on the back wall. Individual-level information is shown in front of an agent when the moderator looks at it (0.3 s head-gaze dwell). iAA shows information extracted through the APR approach; iAT shows information that corresponds to a traditional focus group.

Each condition begins with the agents introducing themselves. Every question then runs through the phases **Setup → (Grid move) → Discussion → Augmentations**, advanced by the experimenter.

## Features

- Scripted multi-agent conversations with sequential turn-taking, driven by JSON manuscripts
- Local neural text-to-speech with [Piper](https://github.com/rhasspy/piper): per-agent voices, adjustable speaking rate and normalized loudness; no cloud services
- Audio-driven lip sync (Oculus LipSync) and automatic blinking on [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox) avatars
- Head-gaze fixation logging per trial and phase, written to CSV
- Counterbalancing: four rotated condition orders and fixed avatar teams per trial position
- Study-integrity checks: the scene's condition must match the planned sequence, avatar teams are validated before a session, and test mode is visibly marked
- Experimental room alignment using Meta's MR Utility Kit (separate prototype scene)

## Requirements

| | |
|---|---|
| **Unity** | 2022.3.42f1 with **Android Build Support** (incl. OpenJDK and Android SDK & NDK Tools) |
| **Headset** | Meta Quest 3, connected to the PC with **Quest Link** (USB-C or Air Link) |
| **PC** | Windows 10/11; the Piper TTS runtime used here is Windows-only |
| **Packages** | Meta XR All-in-One SDK 205.0.0 (resolved automatically from `Packages/manifest.json`) |
| **Internet** | Needed during setup, to download the text-to-speech runtime and voices |
| **Optional** | [Meta XR Simulator](https://developers.meta.com/horizon/documentation/unity/xrsim-intro/) v205, to test without a headset |

The testbed runs from the Unity Editor over Quest Link. Standalone (untethered) Quest builds are not supported yet: speech would need to be pre-baked and gaze data would need a writable path on the device.

## Setting up the environment

### 1. Install Unity

1. Install [Unity Hub](https://unity.com/download).
2. In Unity Hub, go to **Installs → Install Editor → Archive** and install **2022.3.42f1**. Use exactly this version; other versions re-serialize the project's assets.
3. When asked for modules, select **Android Build Support** together with **OpenJDK** and **Android SDK & NDK Tools**. No other platform is needed.

### 2. Get the project

```bash
git clone https://github.com/s91alimora/Intelligent-Social-AR.git
```

### 3. Install text-to-speech

From the repository folder, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\Setup-TTS.ps1
```

This downloads [Piper](https://github.com/rhasspy/piper) 2023.11.14-2 from its official release into `Assets/StreamingAssets/tts/piper/win/` and verifies it, then downloads the voices listed in `Tools/voices.json` (or the example list in `Tools/voices.example.json` if you haven't created your own). See [Adding voices](#adding-voices). Running it again is safe.

### 4. Open the project

1. In Unity Hub, choose **Add → Add project from disk** and select the cloned folder. The first import takes a while.
2. Connect the Quest with **Quest Link** and confirm that Unity uses it (the Meta XR SDK's *Project Setup Tool* reports any missing setting).
3. Open `Assets/Testbed/Scenes/Master Level Control.unity`, the entry scene.

### 5. Add your content

Before a session can run, add the content the testbed plays: [manuscripts](#adding-conversation-scripts-manuscripts), [augmentation images](#adding-augmentation-images) and [voices](#adding-voices). Without a manuscript, a condition scene stops at startup with an error naming the file it expected.

### 6. Optional: test without a headset

Install the Meta XR Simulator v205, activate it with **Meta → Meta XR Simulator → Activate**, and press Play. Deactivate it again before running sessions on the headset.

## Modular design

The testbed is built from independent modules. Each can be changed or replaced without touching the others:

| Module | What it does | Where it lives | Customize by |
|---|---|---|---|
| **Study flow** | Condition order, participant ID, avatar teams, loading one scene per condition | `MasterLevelController` in `Master Level Control.unity` | Editing sequences and teams in the Inspector |
| **Session** | Runs the phases of each question in a condition | `SessionController` in each `Testbed` scene | Inspector settings (intro line, test mode, timing) |
| **Content** | What the agents say and what the augmentations show | `Assets/Resources/Manuscripts/<condition>.json` | Writing manuscripts ([format](docs/manuscript-format.md)) |
| **Speech** | Text-to-speech, voices, speaking rate, loudness | `CrossPlatformTTS` on each avatar; `Tools/Setup-TTS.ps1` | Adding voices and setting them per avatar |
| **Avatars** | Bodies, animation, lip sync, blinking | `Assets/Testbed/Prefabs/Avatars/` | Adding or editing avatar prefabs |
| **Environment** | Opinion grid (APR) or table seating | `GridGenerator`; the `Testbed` scenes | Grid size, row labels, seat positions |
| **Augmentations** | Back-wall panel and per-agent panels | `AgentAugmentationInteracter`; wall UI in the scenes | Manuscript data and images |
| **Data logging** | Head-gaze fixations to CSV | `GazeDataRecorder` | Targets, thresholds, CSV columns |

See [Architecture](docs/architecture.md) for how the modules interact.

## Customizing the testbed

### Adding conversation scripts (manuscripts)

Each condition loads one manuscript from `Assets/Resources/Manuscripts/`, named after the condition: `iAA.json`, `iAT.json`, `nAA.json` and `nAT.json`. A manuscript holds four questions in asking order; for each question it defines the agents' stances (grid rows), what each agent says and in which order, and the data shown in the augmentations.

1. Create the folder `Assets/Resources/Manuscripts/`.
2. Copy [`docs/examples/manuscript-example.json`](docs/examples/manuscript-example.json) into it and rename it after a condition, for example `iAA.json`.
3. Replace the example text with your own questions and responses, following [Manuscript format](docs/manuscript-format.md). Write the responses in the same order as the speaking order.
4. Repeat for the other conditions. Conditions can share content, but each needs its own file.

### Adding augmentation images

The iAA condition shows chart images (seating patterns, speaking summaries, movement and similarity charts).

1. Put your images (`.jpg` or `.png`) under `Assets/StreamingAssets/`, for example `Assets/StreamingAssets/Augmentation Images/Group/my_chart.jpg`.
2. In the manuscript, reference each image by its path relative to `Assets/StreamingAssets/`, in the fields `ind_Seating`, `speaking_Sum`, `grp_Move` and `grp_Sim_Mat`.

Images are loaded while the testbed runs, so they don't need to be imported or assigned in Unity.

### Adding voices

Voices are [Piper](https://github.com/rhasspy/piper) models (`.onnx` plus a `.onnx.json` configuration). Browse the available voices and their licenses at [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices).

1. Create `Tools/voices.json` (use `Tools/voices.example.json` as a template). For each voice, give the file name the avatars will use and the download URL of the `.onnx` file; a SHA-256 checksum is optional:

   ```json
   [
     {
       "file": "narrator.onnx",
       "url": "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/ljspeech/medium/en_US-ljspeech-medium.onnx"
     }
   ]
   ```

2. Run `Tools\Setup-TTS.ps1` again. It downloads each voice and its configuration into `Assets/StreamingAssets/tts/piper/win/voices/`. You can also copy voice files there by hand.
3. In Unity, select an avatar prefab and set **Model File Name** on its `CrossPlatformTTS` component to the voice's file name.
4. Adjust **Length Scale** to match speaking rates across voices (below 1 is faster, above 1 slower). Loudness is normalized automatically.

`Tools/voices.json` and the downloaded voices are ignored by git, so your voice selection stays on your machine. Check each voice's license before using it; several Piper voices are restricted to non-commercial or research use.

### Adding or changing avatars and teams

- Avatar prefabs are in `Assets/Testbed/Prefabs/Avatars/`. Each combines a [Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox) body with a voice, lip sync and blinking. To add one, duplicate an existing prefab and change its body model and voice.
- Teams are set on **MasterLevelController → Avatar Teams**: four teams of four avatars, one team per trial position. The list order within a team decides which avatar plays `agent_1` to `agent_4`.

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

The full experimenter checklist is in [Running a study session](docs/running-a-session.md).

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

## Project structure

```
Assets/
├── Testbed/
│   ├── Scripts/             # Study logic: session flow, agents, TTS, gaze logging, grid, blinking
│   │   └── Editor/          # Custom inspectors, avatar-team and import tools
│   ├── Scenes/              # Master Level Control (entry) + Testbed iAA / iAT / nAA / nAT
│   │   └── Prototypes/      # Development scenes (lip sync, head-ray interaction, MRUK)
│   ├── Prefabs/Avatars/     # Avatar prefabs (Male/, Female/)
│   ├── Animation/           # Talking and idle animator controllers
│   ├── Models/              # Table model; Rocketbox avatars with custom import settings
│   └── Materials/, Shaders/
├── Avatars/                 # Microsoft Rocketbox avatars and animations
├── Resources/Manuscripts/   # Your manuscripts (not included)
└── StreamingAssets/
    ├── Augmentation Images/ # Your augmentation images (not included)
    └── tts/                 # Piper runtime and voices (downloaded by Tools/Setup-TTS.ps1)
docs/                        # Session checklist, manuscript format, architecture, examples
Packages/                    # Unity package manifest (Meta XR SDK 205)
ProjectSettings/             # Unity project and build settings
Tools/                       # Setup-TTS.ps1 and the example voice list
```

## Documentation

- [Running a study session](docs/running-a-session.md): step-by-step checklist for the experimenter
- [Manuscript format](docs/manuscript-format.md): how to write conversation scripts and augmentation data
- [Architecture](docs/architecture.md): how the modules fit together
- [Contributing](CONTRIBUTING.md): setup, branching and what not to commit

## Known limitations

- Windows only; the TTS engine runs as a local process.
- Standalone Quest builds are not supported (see [Requirements](#requirements)). *Tools → iXR → Bake All Session Audio* pre-renders speech for one voice only.
- Gaze is approximated by head orientation.
- Room alignment with MR Utility Kit is experimental and not used by the study scenes.

## Citation

If you use this software, please cite it using the metadata in [`CITATION.cff`](CITATION.cff) (GitHub's **Cite this repository** button provides APA and BibTeX). A paper describing the study is in preparation.

## License

The project's own code is released under the [Apache License 2.0](LICENSE.md). It includes and downloads third-party software, models and assets under their own licenses; see [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Acknowledgements

Developed at Virginia Tech by **Saeid Alimoradi**, **Yutong Ren** ([@ryutong](https://github.com/ryutong)) and **Jasmine Walker** ([@Jwalker055](https://github.com/Jwalker055)).

Built with [Unity](https://unity.com/), the [Meta XR SDK](https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/), [Piper](https://github.com/rhasspy/piper), [eSpeak NG](https://github.com/espeak-ng/espeak-ng), [ONNX Runtime](https://onnxruntime.ai/) and [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox).
