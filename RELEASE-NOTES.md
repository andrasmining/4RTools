# 4RTools Vanilla 0.6.43

## Restart-only Autobattle/slave hotkeys

Automatic resume hotkeys are no longer a steady-state wakeup mechanism. Restoring,
maximizing, focusing or merely observing an already-running client does not send the
configured hotkey. Automatic hotkey input is authorized only after 4RTools itself
freshly launches/restarts/relogs that character.

After verified username/character/X/Y/map/living-HP readiness, 4RTools waits a full
10 seconds and then invokes the same shared ResumeHotkey verifier used by
TESTS -> Resume hotkey. That verifier allows three total sends, each followed by its
own 10-second fresh X/Y movement window, and stops immediately when either axis moves.

## Five/ten-minute steady-state movement recovery

Normal supervision now watches verified X/Y only and sends no wakeup hotkey. At five
minutes without verified movement, a freshly confirmed exact `Now Logging Out.` or
`Disconnected from Server.` popup can trigger an early restart. If neither popup is
present, 4RTools keeps waiting. At ten minutes without verified X/Y movement, the
affected client is restarted regardless of visual classification. Unknown, stale,
unreadable and unverified coordinates remain invalid rather than becoming `(0,0)`.

Recovery stays per-client and globally serialized. A healthy sibling is not disturbed.
Failed restart/login cycles use the existing exponential retry delay, doubling from the
configured base interval and capping at one hour between attempts. There is no longer a
three-client-restart permanent-stop budget; retries continue until recovery succeeds or
the user presses STOP/changes ownership or configuration.

## Existing 0.6.42 safeguards retained

Cursor-aware minimization remains: automatic minimize requires at least 60 seconds with
the client non-minimized and 60 seconds without machine cursor movement. Launcher GAME
START remains single-action and deliberately paced with stable semantic/visual evidence.
Character selection remains keyboard/state based rather than screen-coordinate based.

## Validation limits

Release validation covers Debug/Release regression suites, the shared three-attempt
resume verifier, restart-only ownership gating, five/ten-minute watchdog boundaries,
terminal-popup confirmation, exponential backoff to one hour, dual-client serialization,
native test-owned process recovery, portable package/launch checks and mock-data UI
checks. Public release assets, source identity and updater discovery are verified after
publication. No independent live Vanilla/Gepard/RDP session is available to the build
runner; the user-provided screenshots/logs are the live evidence for the corrected bug.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.6.43. SHA256:

- `4RTools-Vanilla-v0.6.43-portable.zip`: `6cc09c4c806929fdbdcf0355cefe3fc899cf6039463a2ea9a4f1b18873c78c19`
- `4RTools-Vanilla.exe`: `0f3d1852533d54b098067ea5b7a6a2b49d5cffa2dc869aaee01cc39987f94e46`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
