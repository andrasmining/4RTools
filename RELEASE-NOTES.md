# 4RTools Vanilla 0.6.57

## Autoattack + Smart Teleport recovery

This release hardens the stationary-character recovery path seen during the v0.6.56 live startup test.

The shared Autobattle resume verifier now performs at most three bounded recovery cycles:

1. Send the configured per-character Autobattle/Resume hotkey.
2. Observe fresh verified X/Y for up to 10 seconds.
3. **Only if the character is still stationary**, run the saved Smart Teleport hotkey through the existing verified background-teleport path.
4. Observe fresh verified X/Y for up to another 10 seconds.
5. Repeat only while the character is still stationary, for at most three cycles.

Any verified X or Y movement stops the current wait immediately and suppresses the teleport and every later recovery input. A teleport is therefore never sent simply because a timer elapsed after the character already started moving.

Recovery teleport keeps the existing safety gates: it targets the verified owned Vanilla window, sends Enter only after the expected warp popup is positively recognized, and verifies that the modal clears. An unconfigured teleport hotkey, missing popup, ambiguous capture or failed background input does not authorize blind Enter/input.

After the three action cycles are exhausted, 4RTools sends no more recovery input and passively watches fresh X/Y until the **180-second recovery deadline**. If there is still no verified movement at that point, the existing restart/relogin path takes over unchanged and starts the client again. Brief Loading state after a verified warp authorizes no new input until fresh gameplay state returns.

The same shared verifier is used after relog/restart and when Weight/Cart maintenance resumes Autobattle.

## Validation

Regression coverage now verifies that:

- movement during the Autobattle 10-second window suppresses teleport immediately;
- movement produced by teleport stops recovery without another wait/action;
- stationary clients receive exactly three Autobattle attempts and three teleport attempts;
- the action timing is Autobattle -> 10s -> teleport -> 10s for each cycle;
- after those cycles, recovery remains passive until exactly the 180-second deadline;
- cancellation, stale/unverified state, identity replacement and existing input-safety gates still fail closed.

The Windows validation pipeline passed Debug tests before versioning this release, including the complete offline diagnostics suite, Release packaging/smoke checks, native recovery checks and mock-data UI validation.

The engineering environment still cannot reproduce the user's live Vanilla/Gepard session, so the new recovery cadence is the next VPS live-validation boundary.
