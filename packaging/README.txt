4RTools Vanilla
==============

Independent fork of 4RTools, not an official upstream or Vanilla MMO release.
Copyright (c) 2022 4RTools. See LICENSE and THIRD-PARTY-NOTICES.txt.

Starting the application
-----------------------
Extract the entire release folder and run 4RTools-Vanilla.exe. Keep its .config
and VanillaBuilds folder beside it. Windows with Microsoft .NET Framework 4.7.2
or a later 4.x runtime is required. The application targets x86, supports x64
Windows and requests administrator privileges to match elevated game clients.
No Visual Studio, Git, NuGet, SDK or source tree is needed to run the package.

Vanilla is the primary workspace. Original 4RTools remains a compatibility tab.
Minimizing keeps the main window on the Windows taskbar; it does not hide it
exclusively in the system tray. Closing the main window exits the application.

Recovery and Autobattle
-----------------------
Set the Launcher path, then add account profiles with credentials, character
slot, per-account proxy and the resume hotkey configured for Vanilla Autobattle.
Settings auto-save; there is no separate Save button. Any number of profiles may
be stored, but at most two accounts may be enabled and managed simultaneously.

START completes one client's startup before advancing to the next. After the
resume hotkey, fresh verified X/Y readings are checked for movement for 10
seconds. Movement on either axis succeeds. Otherwise the client is focused and
checked again before retrying. The limit is THREE TOTAL hotkey attempts: the
initial press and two retries, each with its own 10-second observation window.

After the final unsuccessful window, the account reports failure rather than
retrying forever. Later accounts do not start after a failed cold startup. A
failed or interrupted client is not silently treated as healthy. The explicit
Resume hotkey diagnostic performs a new bounded verification; success clears
the failed state. Healthy already-running clients are adopted without toggling.

STOP and changes to the active configuration cancel pending input. Unknown or
stale coordinates, failed reads, loss of input ownership, client/session changes,
map transitions and dead characters stop the verification safely. Detailed
reasons and attempt progress are available in status and logs. A character that
fights without moving can fail this movement-only test; motion is not proof of
combat or proof of what caused it.

Normal recovery minimizes a successfully verified client and leaves other
healthy clients untouched. Proxy/login/startup/recovery input is serialized.
Vanilla's own Autobattle remains responsible for movement and combat.

Persistent configuration and updates
------------------------------------
User data is stored outside the versioned release folder under:

  %LOCALAPPDATA%\4RTools Vanilla\

Compatible old profile/settings data is migrated without deleting the original
copy. The release ZIP contains no personal profiles, recovery credentials or
mutable user-data folders. Passwords are protected with Windows DPAPI, are not
logged and must be entered separately on each Windows user/machine.

CHECK FOR UPDATES and version status are at the right of the Vanilla header.
The updater verifies the downloaded ZIP checksum and its payload manifest before
applying an update. The Data & updates page displays the actual paths in use.

Validation and integrity
------------------------
VERSION.txt records the version, architecture, source commit and build status.
RELEASE-NOTES.md distinguishes automated Windows build, regression, package and
mock-data UI validation from actual live Vanilla/Gepard gameplay testing.
SHA256SUMS.txt lists the payload hashes; the release ZIP has an adjacent .sha256
file. A passing build or mock UI test does not prove live-game behavior.

Vanilla observation remains read-only. No game-memory writes, injections,
packet manipulation, game-file changes or Gepard bypasses are performed.
Unknown observations are never reinterpreted as valid gameplay state.

Autofarming health watchdog (0.6.36)
----------------------------------
With automatic recovery enabled, an online managed client is restarted after
120 seconds without fresh verified X/Y movement. Unchanged, unreadable, missing
and stale coordinates all count as lack of verified movement, never as (0,0).
A popup is not required. Both known disconnect/logout messages are also detected
and logged. Missing processes use the existing immediate sequential restart path.
The watchdog is inactive during startup/recovery and after STOP. Both failed
clients recover sequentially; another healthy client is left running.

Character roster
----------------
One row represents one character, not one account. Several rows may share a
username; at most two may be enabled. Description, Username, Slot and Character
name precede the existing fields. Running characters are discovered automatically
from fresh verified memory and added once, disabled. Existing secrets/proxies stay
unchanged. The unique key is username plus character name, not name alone.
The shared reader includes both supplied username addresses; agreeing values fill
the username automatically. Both values/addresses are visible in Diagnostics as
UserName and UserNameMirror. Missing/conflicting usernames do not create new rows.
A unique configured legacy username row learns its matching character name in
place, keeping its slot, password, proxy, enabled state and ID. Ambiguous matches
are not guessed. Slots remain unmapped; configured slots are preserved and unknown
slots stay blank. Discovery never guesses slot 1, passwords or proxies.

Character selection is keyboard-driven from a clamped grid origin; fixed character-slot and GAME START coordinates are not used.
