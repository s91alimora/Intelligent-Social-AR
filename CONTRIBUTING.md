# Contributing

Thanks for helping improve Intelligent-Social-AR. This project is research software for a user study, so changes that affect what participants see or hear need extra care.

## Setting up

1. Install **Unity 2022.3.42f1** with Android Build Support (OpenJDK, Android SDK & NDK Tools). Use exactly this version; other versions re-serialize assets and create noisy diffs.
2. Clone the repository and run `Tools\Setup-TTS.ps1` to download the text-to-speech runtime and voices.
3. Open the project and start from `Assets/Yutong/Scenes/Mater Level Control.unity`.

See the [README](README.md) for requirements and the [docs](docs/) for how the testbed works.

## Making changes

- Create a branch from `main` named `<your-name>/<short-topic>`, for example `saeid/grid-labels`.
- Keep commits focused, with a short imperative subject line ("Fix grid label order", not "fixed stuff").
- Open a pull request into `main` and describe what changed and how you tested it.
- Merge with a merge commit; don't squash, so the history stays traceable.

## What not to commit

`.gitignore` already covers these, but please double-check before committing:

- **Participant data.** Anything in `Assets/Logs/` (gaze CSVs). Never commit study data.
- **Personal editor state.** `UserSettings/`, `Library/`, `Logs/`, `Temp/`.
- **Downloaded components.** The Piper runtime and voice models; they come from `Tools\Setup-TTS.ps1`.
- **Assets without a clear license.** Add any new third-party asset, model or library to [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md), and only if its license allows redistribution.

## Changes that affect the study

These change the stimulus participants experience. Coordinate with the study lead before merging, and note them in the pull request:

- Manuscripts (`Assets/Resources/Manuscripts/`) and augmentation images
- Avatar teams, avatar models, voices or voice settings (`lengthScale`, loudness)
- Phase timing, grid layout, or what the augmentations show
- The gaze CSV format (analysis scripts depend on its columns)

Data collected before and after such a change should not be pooled without noting it.

## Unity tips

- Rename and move assets **inside Unity**, so their `.meta` files (and the references to them) move too.
- Keep scene and prefab edits in separate commits from code changes where possible; they are hard to review together.
- Test in the Editor with Quest Link before opening a pull request. The Meta XR Simulator is fine for quick checks.

## Reporting problems

Open a GitHub issue with what you expected, what happened, the Unity Console output, and whether it happened with the headset or the simulator.
