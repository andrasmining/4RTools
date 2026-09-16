# 4RTools Vanilla

An independent fork of 4RTools for Vanilla MMO. The **Vanilla** workspace is the
primary interface; the **Original 4RTools** tab remains available for compatibility.
Vanilla's own Autobattle controls movement and combat. This fork adds read-only
state observation and ordinary, client-targeted keyboard/mouse input around it.

## Portable application

Use the portable ZIP attached to the latest GitHub Release. Extract the entire
`4RTools-Vanilla-v<version>` folder and run **4RTools-Vanilla.exe**, keeping its
configuration and `VanillaBuilds` folder together. The application targets x86
and requires Windows with Microsoft .NET Framework 4.7.2 or a later 4.x runtime.
Visual Studio, Git, NuGet and the source tree are not required on the user's PC.
The application requests administrator privileges to match elevated clients.

Minimizing keeps the application on the Windows taskbar; it does not hide the
main window exclusively in the system tray. Closing the main window exits it.

User data lives under `%LOCALAPPDATA%\4RTools Vanilla`, outside versioned release
folders. Upgrades preserve compatible profiles and recovery settings. Passwords
use Windows DPAPI and must be entered separately for each Windows user/machine.
Release packages contain no personal profiles, passwords or mutable user-data
folders. See the included `README.txt`, `VERSION.txt` and `RELEASE-NOTES.md`.

## Workspace and recovery

The compact top area shows up to two observed clients, including character name,
HP/SP, location and activity when the corresponding state is valid. Recovery,
automation, temporary actions, alerts, memory finding, diagnostics and data/update
settings share the same workspace. Debug controls stay on the left of the header;
update controls and version status stay on the right.

Any number of account profiles can be saved, with at most **two enabled clients**
at once. Recovery settings auto-save. Proxy selection belongs to each account,
including cold startup, recovery and diagnostic input. Startup and recovery are
serialized; a healthy client is not restarted or toggled merely because another
client needs recovery.

### Terminal disconnect recovery

The visual watchdog explicitly recognizes the reported **Now Logging Out.** and
**Disconnected from Server.** Message dialogs. Two fresh matching captures are
required. Only the affected client is closed; its actual process exit must be
confirmed before replacement. If both clients fail, one complete close/restart/
login/movement-verification/minimize sequence finishes before the other begins.
The same terminal check is performed for an assigned existing client during START.
Unknown popups are not dismissed with Enter or used as automatic close evidence.
Failed close/launch attempts retain diagnostics and use bounded retry backoff;
STOP, configuration changes or replaced client ownership cancel pending actions.
This depends on a readable supported dialog capture, not merely frozen HP or X/Y.

### Autofarming health watchdog

The primary health signal is fresh verified read-only X/Y movement. **30 seconds
without verified movement** starts bounded hotkey recovery; unchanged, unavailable,
unreadable, unverified or stale coordinates do not reset the deadline and are never
converted to zero. The watchdog is inactive during startup/recovery and after STOP.

When the watchdog fires, the configured Autobattle/slave hotkey is tried before a
restart. Each hotkey attempt gets a 10-second X/Y verification window and there are
three attempts total. Only after all three fail is that client restarted. Replacement
clients repeat login, post-login settle, hotkey and movement verification. The same
movement incident permits at most three client restarts; failure after the third
replacement stops the supervisor and logs a terminal error. Verified movement resets
the restart budget.

Known logout/disconnect dialogs can trigger recovery sooner. Recovery remains globally
serialized from close through relaunch, login, verified movement and minimization;
a healthy sibling is not restarted or toggled. Unknown popups receive no blind input.


### Autobattle movement verification

After every actual login/relog, gameplay must first be stably detected. The client
then settles for **10 seconds** and the configured resume hotkey is always sent. Fresh,
verified X/Y is observed for **10 seconds**. Movement on either axis succeeds. Without
movement, the intended client is revalidated/focused and the hotkey is sent again.
There are **three total hotkey attempts**.

Every settle, send and verification attempt is logged. If all three hotkeys fail,
the affected client enters the bounded restart path rather than silently remaining
online. Each replacement repeats the same procedure, for at most three client restarts
for one movement incident. Failure after that stops automatic recovery completely
until a manual Start. Already-running healthy clients adopted by 4RTools are not
blindly toggled; if one later remains stationary for 30 seconds, the watchdog invokes
the same bounded hotkey-first path.

STOP, settings changes, replaced PIDs/sessions/characters, map changes, death, failed
reads, stale observations and lost input ownership prevent further automated input.
Movement confirms only movement, not combat; a deliberately stationary character can
therefore enter recovery by design.


## State validity and boundaries

Shipped build profiles are matched to the executable fingerprint. The current
profile records verified HP/SP, character name, carried weight, X/Y and map
observations, plus the user-supplied corroborated username mappings. Unsupported or unknown fields are not treated as valid
zero, idle or no-target states. Target/combat/status-dependent rules remain gated
by their required evidence; enabling a UI option does not verify its game effect.

Vanilla memory access is read-only. The fork does not write game memory, inject
code, manipulate packets, modify game files or bypass Gepard. A blocked read or
action stops that operation rather than using an invasive alternate access path.
Stock 4RTools support must not be interpreted as blanket approval of every fork
feature. Current release notes distinguish implementation, automated validation
and actual live-game validation.

## Engineering and release validation

The existing Windows build scripts restore dependencies, build Debug/Release and
run the offline regression suite. Portable packaging checks x86/version metadata,
licenses, payload checksums and a relocated inert executable launch. The native
mock-data UI harness covers responsive layouts, enlarged text, large saved-account
lists, character discovery/editor behavior, legacy username migration and diagnostic
username/address rendering without observing live clients.

The release workflow runs these gates before publication, then downloads the
public release assets and verifies their hashes and source identity against the
tested package. Automated tests and mock UI rendering are not a live Vanilla or
Gepard gameplay test. See `RELEASE-NOTES.md` for the precise validation limits.

## Attribution and license

This fork is independent of upstream 4RTools and Vanilla MMO. The MIT license
retains `Copyright (c) 2022 4RTools`. Distributed third-party notices are included
in `packaging/THIRD-PARTY-NOTICES.txt` and every portable release.

Character selection is keyboard-driven from a clamped grid origin; fixed character-slot and GAME START coordinates are not used.
