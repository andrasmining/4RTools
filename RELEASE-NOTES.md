# 4RTools Vanilla 0.6.44

## Existing running clients: verified memory is authoritative

A cross-machine regression could correctly match running clients by username + character name,
while START SUPERVISOR still failed because the screenshot classifier remained `Unknown`.
The supplied debug bundle proved that both PIDs had read-only access, correct build profiles and
verified character identity, but the old existing-client gate repeatedly restored the window and
waited 15 seconds for a visual `Gameplay` classification.

Existing clients are now adopted from two fresh verified read-only gameplay-memory samples:
expected username + character, valid X/Y, map and living HP. A visual `Unknown` result is
supplemental and no longer blocks adoption or causes the client to be repeatedly restored just to
prove gameplay. Explicit LoginShell, LoggingOut and Disconnected visual states still fail closed.
No resume/autobattle hotkey is sent when merely adopting an already-running client.

Freshly restarted/relogged clients retain the 0.6.43 policy: after verified login readiness they
wait a full 10 seconds and use the same TESTS -> Resume hotkey verifier, with at most three sends
and a 10-second fresh X/Y movement window after each send.

## Better cross-machine debug bundles

COPY DEBUG LOG now adds host/session/display telemetry: real Windows product/build from the
registry, process/OS architecture, CLR/culture/timezone, terminal/RDP session information,
virtual desktop and every monitor's bounds/working area, system DPI, and every running Vanilla
PID plus all of its top-level windows (handle, visibility, minimized state, client size, class and
title). A compact copy is also written once to debug.log at application startup. Clipboard copy
uses an extended retry window to tolerate transient RDP clipboard contention.

## Validation limits

Release validation covers Debug/Release regression suites, existing-client memory/visual policy,
host diagnostics generation, native test-owned process recovery, portable package/launch checks,
mock-data UI checks, public release assets/source identity and updater discovery. No independent
live Vanilla/Gepard/RDP session is available to the build runner; the user-provided 0.6.43 debug
bundle is the live evidence for this machine-specific failure.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.6.44. SHA256:

- `4RTools-Vanilla-v0.6.44-portable.zip`: `4503f9fed9ba73ee013559f3726214c54ceae68014b4a6cb64242ab4f7d381d0`
- `4RTools-Vanilla.exe`: `7511b58b9a020ce272b356f4ab0ee68486ff6b5cad66bb8781ea2f77491bffe3`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
