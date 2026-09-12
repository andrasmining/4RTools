# 4RTools Vanilla 0.3.0

4RTools Vanilla is an independent MIT-licensed fork of 4RTools focused on Vanilla MMO. Vanilla's own documentation lists **4R Tools Supported**, **Gepard 3.0 Protection**, and its 24/7 Auto-Attack system. This fork does not disable, patch, hide from, bypass, or otherwise interfere with Gepard. Vanilla-specific automation uses ordinary targeted window input and, for existing diagnostic features, read-only client observation.

## New in 0.3.0: overnight restart and relog recovery

The primary new feature is a **Vanilla Restart & Relog** supervisor designed for the two failure modes observed on Vanilla: the client process can close during a server restart, or a lag/disconnect can leave the client at a login/service screen. The supervisor can be configured for up to two Vanilla clients on one PC and can run continuously from the same portable 4RTools Vanilla executable.

For each local account profile it supports:

- automatic relaunch of the configured Vanilla executable when its assigned client exits;
- waiting for the Gepard/client startup path rather than trying to bypass it;
- configurable proxy selection, with **Tokyo** as the default;
- username/password login;
- selection of the first Vanilla server entry;
- configurable character slot (1–15 across the visible 5x3 character grid);
- configurable post-login Autobattle hotkey, defaulting to **Ctrl+2**;
- recovery after returning to the login shell without restarting the process;
- acknowledgement of the observed in-game disconnect/logout modal using ordinary Enter input;
- separate status and reconnect logging for each managed account;
- manual one-shot relog for the selected account;
- start/stop controls plus an option to start the supervisor with 4RTools.

The relogger does not use fixed screen pixels. UI interaction is based on normalized client coordinates so the same configuration can work across different client window sizes/resolutions as long as Vanilla uses the same UI layout. Login/service/gameplay/modal detection uses a low-resolution normalized visual probe rather than OCR or game-memory modification.

The supplied screenshots were used as offline visual evidence. The classifier distinguishes the bright Vanilla login/service/character shell from normal gameplay, and its modal heuristic was adjusted so the supplied **Now Logging Out.** gameplay popup is recognized separately. Gepard's splash remains an unknown/waiting state and receives no input.

## Account secrets and portability

Passwords are never committed to the repository or written to logs. The reconnect manager stores them with Windows DPAPI, bound to the current Windows user/machine. That means a copied portable folder does **not** carry a decryptable password to another PC; enter each account password once on each PC. This is intentional.

The rest of the configuration is application-relative and portable. The release does not hard-code a Windows username, PID, Vanilla install directory, or monitor resolution. A running client can be used to populate the local Vanilla executable path.

## Recovery safety

The supervisor will not automatically press the Autobattle hotkey merely because it adopted a client that was already running when 4RTools started. The one-shot resume hotkey is armed by a fresh launch/relogin path instead, avoiding an accidental Autobattle toggle-off on an already-online client.

Freshly launched clients retain the information that proxy/service selection is still required; after an in-process disconnect, the recovery path can resume from the login shell without unnecessarily replaying the initial proxy startup flow. Actions are targeted at the selected Vanilla window rather than sent globally.

The supervisor backs off after failures, stops targeting exited processes, and does not log credentials. It never writes HP, SP, coordinates, targets, inventory, or other game state to process memory.

## Existing Vanilla companion functionality

Version 0.2.0 already integrated Vanilla into the original 4RTools window with read-only diagnostics, profiles, guarded automation rules, Smart Teleport infrastructure, SP-recovery sequences, and the existing 4RTools features. The currently known Vanilla build has candidate HP/SP/name mappings, but those gameplay fields remain deliberately unverified until controlled live changes prove their semantics. Unverified fields do not enable dependent Smart Teleport or SP logic.

This is separate from the 0.3.0 relogger, which is based on ordinary window/process lifecycle and UI-state observation and therefore does not require those game-memory offsets.

## Build and automated validation

The repository now has a Windows GitHub Actions workflow using the repository's MSBuild-discovery/build tooling. It restores dependencies, builds the x86 Release, runs the diagnostics/automation test executable, creates the portable package, runs the package's existing smoke validation, and uploads the ZIP/checksum as a workflow artifact.

The first workflow run exposed an MSBuild PATH assumption in CI; the workflow was corrected to use the repository's Visual Studio/MSBuild discovery logic. The corrected pre-0.3.0 run then completed build, tests, portable packaging, and artifact upload successfully. The 0.3.0 workflow validates the hardened reconnect source and packages it as `4RTools-Vanilla-v0.3.0-portable.zip`.

## What still requires the user's local Vanilla test

GitHub Actions cannot run Vanilla MMO or Gepard, and this ChatGPT workspace cannot attach to the user's Windows game process. Therefore the complete real sequence still needs one local end-to-end validation on the user's machine:

1. launch/relaunch through the actual Vanilla executable and Gepard;
2. select Tokyo;
3. fill credentials;
4. select the Vanilla server;
5. select the configured character slot;
6. enter gameplay;
7. send Ctrl+2 (or the configured hotkey);
8. recover from an actual lag/login-screen disconnect;
9. recover from an actual process-closing server restart;
10. confirm Gepard accepts the custom fork's ordinary input behavior.

If Gepard blocks the custom executable or an input path, that is a hard boundary for this project: do not bypass or disguise the program to evade Gepard. Report the exact blocked operation and keep the protection intact.

## Portable release

The intended 0.3.0 output is:

- `dist/4RTools-Vanilla-v0.3.0/`
- `dist/4RTools-Vanilla-v0.3.0-portable.zip`
- `dist/4RTools-Vanilla-v0.3.0-portable.zip.sha256`

The portable package targets x86 and .NET Framework 4.7.2 or later 4.x. It does not require Visual Studio, Git, NuGet, source code, or manual offset entry to run. The upstream administrator manifest is retained.

The original 4RTools MIT license and attribution remain included. This fork is not an official Vanilla MMO or upstream 4RTools release.
