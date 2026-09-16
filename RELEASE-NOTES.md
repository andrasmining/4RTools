# 4RTools Vanilla 0.6.42

## Post-login hotkey path hardened

Recovery/relogin no longer depends on the screenshot classifier reporting exactly
`Gameplay` before the configured Autobattle/slave hotkey can run. After character
selection, 4RTools waits for two fresh read-only samples proving the expected login
username/character plus valid X/Y/map/living HP. It then keeps the existing 7-second
settle and three-attempt hotkey verifier (10 seconds of X/Y observation per attempt).
Known login, modal, logging-out and disconnected screens still block input.

The reconnect session log and global Debug log now both record readiness, the 7-second
settle, every `Sending autobattle hotkey attempt X/3` event and every movement window.
The 30-second steady-state movement watchdog and three-restart terminal budget remain.

## Human-presence-aware minimization

A supervised game window is no longer minimized immediately after the user restores,
maximizes or otherwise exposes it. Automatic minimization requires BOTH at least 60
seconds continuously non-minimized and at least 60 seconds without machine cursor
movement. Any cursor movement restarts the idle grace. If cursor state cannot be read,
automatic minimization fails closed and waits another grace period. Explicit manual
minimize remains immediate.

Startup/recovery keeps ownership while waiting for the same safe-minimize condition,
so a later client does not steal the serialized startup/recovery gate while the user
is actively working in the current client.

## Slower single-action GAME START

The Vanilla launcher is allowed to settle for 2.5 seconds before any GAME START action.
Each attempt now performs exactly ONE activation: a semantic native button invocation
when available, otherwise one click only after two stable visual detections 750 ms
apart. The old fallback coordinate sweep and the previous native-plus-visual double
activation are removed. After a real activation, 4RTools waits at least 15 seconds for
Vanilla/Gepard startup before another attempt.

This reduces duplicate/too-fast launcher input associated with the observed generic
GDI+ launcher error while remaining resolution/DPI independent.

## Validation and limits

Release validation covers Debug/Release regression suites, post-login memory readiness,
visual input blocking, three-hotkey/three-restart recovery, 30-second movement watchdog,
60-second cursor/visibility minimization policy, launcher pacing/stable-candidate rules,
native test-owned process recovery, portable launch/package checks and native mock-data
UI checks. Public release assets, exact source identity and updater discovery are
verified after publication.

No live Vanilla/Gepard/RDP session was available to the build runner. The user-supplied
screenshots/log are live evidence for the defects; final automation behavior is validated
by Windows/runtime/state tests rather than an independent live-game login.
