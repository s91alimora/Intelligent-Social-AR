# Running a study session

A checklist for the experimenter ("wizard") who runs a participant through all four conditions.

## Before the participant arrives

- [ ] Unity 2022.3.42f1 is open with the project, and `Tools\Setup-TTS.ps1` has been run on this computer.
- [ ] The Quest 3 is charged, connected with **Quest Link**, and Unity shows the headset as the active XR device.
- [ ] **Meta XR Simulator is deactivated** (*Meta → Meta XR Simulator → Deactivate*). While it is active, the app runs in the simulator instead of the headset.
- [ ] In each of the four `Testbed` scenes, **Is Test Mode** is off on the SessionController. The wall shows `[TEST MODE]` if it is on.
- [ ] The play area is clear. The APR grid needs about 5 × 6 m.

## Setting up the session

1. Open `Assets/Yutong/Scenes/Mater Level Control.unity`.
2. Select the **MasterLevelController** object and set:
   - **Participant ID**, for example `P07`
   - **Study Sequence**, the condition order assigned to this participant
3. Check the Console after pressing Play. The first condition logs the loaded manuscript and the avatar team, for example:
   `Avatar team 'Team A' (trial position 1): agent_1=Alan, ...`
   A red `CONDITION MISMATCH` or avatar-team error means the setup is wrong: stop and fix it before continuing.

| Sequence | Condition order |
|---|---|
| `Sequence_1_nAT_nAA_iAT_iAA` | nAT → nAA → iAT → iAA |
| `Sequence_2_nAA_iAT_iAA_nAT` | nAA → iAT → iAA → nAT |
| `Sequence_3_iAT_iAA_nAT_nAA` | iAT → iAA → nAT → nAA |
| `Sequence_4_iAA_nAT_nAA_iAT` | iAA → nAT → nAA → iAT |

Assign sequences in rotation so each is used equally often.

## During a condition

Advance each step with **Space** on the keyboard or **A** on the right controller.

1. **Setup.** The avatars appear. In the first question of each condition the wall stays blank.
2. **Introductions** (first question of each condition only). Press Space; each avatar introduces itself. The question then appears on the wall.
3. **Grid move** (iAA and nAA only). Press Space; the avatars walk to the rows matching their stances.
4. **Discussion.** Press Space; the avatars speak in turn. The gaze cursor turns green when the discussion ends. Gaze is recorded during this phase.
5. **Augmentations** (iAA and iAT only). Press Space; group information appears on the back wall, and each avatar's panel appears when the participant looks at it. Gaze is recorded during this phase too.
6. Press Space to move to the next question.

After the fourth question the Console logs `Experiment Finished`. Press **T** to load the next condition.

## After the session

- [ ] Gaze data is in `Assets/Logs/`: one CSV per condition, named `<Sequence>_<ParticipantID>_<timestamp>.csv`. Check that four files exist for the participant.
- [ ] Copy the CSVs to your study storage. They are ignored by git and must never be committed.
- [ ] Record the participant's sequence and any incidents in your study log.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| An avatar moves its mouth but makes no sound | Its voice model is missing; run `Tools\Setup-TTS.ps1`. Amy and Tony have no public voice file yet (see the README). |
| The headset stays black | Quest Link is not active, or the Meta XR Simulator is still activated. |
| `CONDITION MISMATCH` in the Console | A scene-name field on MasterLevelController points to the wrong scene, or a scene's Study Condition is wrong. |
| Pressing T does nothing | The current condition has not finished all four questions. |
| Random avatars instead of the team | Play was started from a `Testbed` scene instead of `Mater Level Control`. |
