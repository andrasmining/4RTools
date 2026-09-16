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

The main health check uses the existing verified, read-only X/Y observations for
each managed client. **120 seconds without verified movement** triggers recovery,
including coordinates that remain unchanged, unavailable, unreadable or stale.
A missing process is queued for restart immediately. The watchdog does not require
a popup or a successful screenshot. It runs only while automatic recovery is ON,
after startup/recovery; STOP and client/configuration changes cancel/reset it.

Both known terminal dialogs can be recognized sooner and are logged separately.
Recovery keeps one account's lease through close, confirmed exit, relaunch, login,
verified movement and minimization. Another affected account remains queued; a
healthy sibling is not restarted or sent another Autobattle toggle. A failed
attempt uses exponential backoff rather than repeatedly closing or launching.
This is an autofarming policy: a deliberately stationary character can also reach
the timeout. It is not a diagnosis of why the client stopped moving.

### Autobattle movement verification

For a freshly started or recovered client, the configured resume hotkey is followed
by a **10-second observation window** using fresh, verified X/Y readings. A change
on either axis confirms movement immediately. Without movement, the intended
client is focused again and movement is rechecked before another hotkey is sent.
There are **three total attempts: the first press and at most two retries**.

After three unsuccessful windows the account enters an explicit failed state;
later observations do not silently restart the same budget. Cold startup does not
advance to the next account after failure. A failed or interrupted startup is not
subsequently adopted as a healthy client. The explicit Resume hotkey diagnostic
can perform a new bounded verification; only success clears the failed latch.
Already-running healthy clients are adopted without toggling Autobattle.

STOP, settings changes, replaced clients/sessions, map changes, death, failed reads,
stale observations and lost input ownership prevent further automated input.
Progress and failures appear in account status and logs. Movement confirms only
movement: it is not proof of combat, and a character fighting without moving can
fail this deliberately position-based check.

## Character roster and automatic discovery

The recovery table is one row per **character**, not per login account. Columns
are Enabled, **Description, Username, Slot, Character name**, then hotkey,
password, proxy, PID and status. Multiple characters on one username remain
independent saved profiles; at most two may be enabled.

The logical unique key is **username + character name**, consistently used for
discovery, validation and process matching. Several characters may share a username;
the same character name on different usernames does not collide. Persistent row
IDs continue to retain passwords and proxy preferences across upgrades.

The existing reader now includes the supplied username offsets `0xD343F8` and
`0xD39159`, relative to the fingerprinted main module. Both bounded, NUL-terminated
UTF-8 values appear in diagnostics and exports as `UserName` and `UserNameMirror`.
They must agree before the username is used for identity. No password is read.
Missing or conflicting usernames defer discovery instead of creating incomplete rows.

A unique legacy row with a configured username can learn the uniquely observed
character's name without a memory-read slot. It keeps its description, slot,
password, proxy, enabled flag and ID. Ambiguous rows/characters are not guessed.
Only untouched empty name-only discovery duplicates from 0.6.37 may be folded into
that configured row; user-edited profiles are preserved. New complete pairs are
added once, disabled. Discovery never overwrites configured values.

**Remaining mapping limit:** one-based character slots are not mapped. Existing
configured slots are kept; a new row's slot stays unknown/editable until configured
or supplied by a verified mapping. The supplied username screenshot establishes
the current addresses, not independent live validation across client restarts.

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
