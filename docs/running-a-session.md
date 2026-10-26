# Running a study session

A checklist for the experimenter ("wizard"). Each participant comes for two sessions of two conditions, and does a short training before session 1.

## Once per PC and play area

- [ ] `Tools\Setup-TTS.ps1` has been run, with the study and training voices in `Tools/voices.json`.
- [ ] The training content has been generated (`python Tools\Training\make_training_content.py`).
- [ ] Two tape marks, **A** and **B**, are on the floor at least 2 m apart, at the scenes' *Marker A* and *Marker B* positions.
- [ ] The room is calibrated (see [Calibrating the room](#calibrating-the-room)). Redo it if the PC, the headset or its boundary changes.

## Before the participant arrives

- [ ] Unity 2022.3.42f1 is open with the project.
- [ ] The four manuscripts (`Assets/Resources/Manuscripts/iAA.json`, `iAT.json`, `nAA.json`, `nAT.json`) and their augmentation images are in place.
- [ ] The Quest 3 is charged, connected with **Quest Link**, and Unity shows the headset as the active XR device.
- [ ] **Passthrough over Link** is on in the Meta Horizon Link app.
- [ ] **Meta XR Simulator is deactivated** (*Meta → Meta XR Simulator → Deactivate*). While it is active, the app runs in the simulator instead of the headset.
- [ ] In each of the four `Testbed` scenes, **Is Test Mode** is off on the SessionController. The wall shows `[TEST MODE]` if it is on.
- [ ] The play area is clear. The APR grid needs about 5 × 6 m.

## Calibrating the room

1. Start any testbed (for example by starting a pilot session), put on the headset or hand it to an assistant.
2. Press **C** on the keyboard.
3. Hold the right controller upright on mark **A** and pull the trigger.
4. Do the same on mark **B**.

If the measured distance between the marks differs from the scene's by more than 5 cm, the calibration is rejected:
check the tape marks and try again. Press **V** to show the marker discs and check that they sit on the tape.
The experimenter panel shows a warning while the room is not calibrated.

## Training (before session 1)

1. Open `Assets/Testbed/Scenes/Training.unity` and press **Play**.
2. Tell the participant to press **X** on the left controller to move on whenever the wall asks.
3. The training plays one conversation at the table (`iAT`), then one on the grid (`iAA`). Space works for you if the participant gets stuck.
4. The panel shows the time used against 15 minutes. Nothing is logged.

## Setting up the session

1. Open `Assets/Testbed/Scenes/Master Level Control.unity` and press **Play**.
2. In the setup panel on the PC monitor, enter the **participant number** and choose **Session 1** or **Session 2**. The panel shows the condition order the number selects.
3. Read any warnings before starting:
   - the session already has a log folder for this participant;
   - session 2 is chosen but there is no session 1 folder;
   - passthrough is not running;
   - the room is not calibrated.
4. Start. Check the Console: the first condition logs the loaded manuscript and the avatar team. A red `CONDITION MISMATCH` or avatar-team error means the setup is wrong: stop and fix it before continuing.

For a pilot, tick the pilot option: the data is saved as `PILOT_<ID>`, and you may turn off room anchoring to test
outside the lab (the room then follows the headset, and the log says `room NOT anchored`).

## During a condition

Advance each step with **Space** on the keyboard or **A** on the right controller. **Backspace** (or the panel's
Back button) undoes the last step. The panel shows what is happening and what Space does next.

1. **Introductions** (start of each condition). The avatars appear and introduce themselves.
2. **Ready.** A ready message is on the wall.
3. **Question.** The question appears on the wall, with its number in bold.
4. **Discussion.** In `iAA` and `nAA` the avatars first walk to the rows matching their stances. They then speak in turn. The gaze cursor turns green when the discussion ends.
5. **Review.** The participant looks around. In `iAA` and `iAT` group information is on the back wall, and each avatar's card opens when the participant looks at it.
6. **Return.** The avatars walk back, and the next question begins.

Gaze is recorded during the discussion and the review. Each question's files are saved when its review ends.

After the last question, press **T** once: the data is saved and the room is cleared. Press **T** again to load
the session's next condition, or to end the session after its second condition.

## If something crashes

1. Press Play again in `Master Level Control`.
2. Enter the same participant number and session, and choose **Resume**.
3. Pick the condition and question that was running. The session restarts there, without introductions; Back cannot go before that question.

The resumed data goes into a new folder ending in `_resumed-C<c>Q<q>`. Keep both folders.

## After the session

- [ ] The session's folder is in `Assets/Logs/`: `<ID>_Seq<n>_S<session>_<timestamp>/`, with a `_Gaze.csv` and a `_ReviewTime.csv` for each of the 8 questions (2 conditions × 4).
- [ ] Copy the folder to your study storage. Logs are ignored by git and must never be committed.
- [ ] Record the session, any resume, and any incidents in your study log.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| The participant sees black around the virtual objects | Passthrough over Link is off in the Meta Horizon Link app. Turn it on and restart Unity. The panel also warns about this. |
| The grid or table is in the wrong place in the room | The room is not calibrated, or the calibration is from another PC or boundary. Recalibrate with **C**. |
| Calibration is rejected | The tape marks are not where the scene's markers are, or the controller was not held on the marks. |
| An avatar moves its mouth but makes no sound | Its voice model is missing: add it to `Tools/voices.json` and run `Tools\Setup-TTS.ps1`, and check the avatar's **Model File Name** (see the README, *Adding voices*). |
| Training stops at startup with a missing-file error | The training content has not been generated; run `Tools\Training\make_training_content.py`. |
| The headset stays black | Quest Link is not active, or the Meta XR Simulator is still activated. |
| `CONDITION MISMATCH` in the Console | A scene-name field on MasterLevelController points to the wrong scene, or a scene's Study Condition is wrong. |
| Pressing T does nothing | The current condition has not finished its last question. |
| Random avatars instead of the team | Play was started from a `Testbed` scene instead of `Master Level Control`. |
| Unity or the headset crashed mid-session | Use **Resume** (see [If something crashes](#if-something-crashes)). |