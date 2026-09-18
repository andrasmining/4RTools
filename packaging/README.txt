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

STOP and changes to the active configuration cancel pending input.

Automatic minimization is user-presence aware. A restored/maximized managed client
is left alone until it has remained visible for at least 60 seconds AND the machine
cursor has not moved for at least 60 seconds. Cursor movement restarts the grace.
 Unknown or
stale coordinates, failed reads, loss of input ownership, client/session changes,
map transitions and dead characters stop the verification safely. Detailed
reasons and attempt progress are available in status and logs. A character that
fights without moving can fail this movement-only test; motion is not proof of
combat or proof of what caused it.

Normal recovery minimizes a successfully verified client and leaves other
healthy clients untouched. Proxy/login/startup/recovery input is serialized.
Vanilla's own Autobattle remains responsible for movement and combat.


Smart Teleport
--------------
Configure Smart Teleport in each character row. It is keyed by username + character
name and automatically follows the verified running PID; no process selector is used.
Each character stores its own enable flag, live-captured teleport hotkey and idle X/Y
timeout (60 seconds by default). Fresh verified X/Y movement resets the timer; target,
combat and casting state are not required.

At timeout, 4RTools sends the configured hotkey to that owned Vanilla window using
ordinary background Windows messages, then positively detects the Select an Area to
Warp popup before sending Enter to the selected first option. If the popup is not
recognized, Enter is never sent. Unknown/stale coordinates and ownership changes fail
closed.

Weight / Cart management
------------------------
The Weight tab can trigger ordinary UI-only Cart maintenance from verified read-only
CurrentWeight/MaxWeight. Each character row has its own Weight switch; shared Weight-tab settings apply only to rows whose Weight switch is enabled. The default trigger is 50% and is configurable. Use, Equip
and Etc inventory categories are selectable, as are the Inventory and Cart hotkeys.
4RTools pauses the configured Autobattle toggle, detects the opened panels/slots,
drags items to Cart, then resumes through verified X/Y movement and minimizes.

For stack transfers Enter is pressed only after a quantity dialog is positively
detected. A quantity-one item has no dialog and receives no Enter. If the UI cannot
be identified safely, the Cart rejects a transfer, or progress cannot be verified,
input stops and only that character is held for manual Cart emptying with Autobattle
left OFF. Memory access remains read-only; inventory state is never read/written from
game memory.

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

Autofarming health watchdog (0.6.41)
----------------------------------
With automatic recovery enabled, 30 seconds without fresh verified X/Y movement
starts hotkey recovery. Unchanged, unreadable, missing, unverified and stale
coordinates never count as movement and are never treated as (0,0).

After every login/relog, stable gameplay is followed by a 10-second settle, then
the configured Autobattle/slave hotkey is sent. X/Y is checked for 10 seconds.
Without movement the hotkey is tried again, for three total attempts. The same
three-attempt hotkey sequence is used when the 30-second online watchdog fires.

Only after all three hotkeys fail is the affected client restarted. A replacement
repeats the full login/settle/hotkey/movement sequence. At most three client
restarts are allowed for one movement failure incident. If the third replacement
still cannot establish verified movement, the supervisor stops and records a
terminal failure. Manual Start begins a new bounded budget. Any verified movement
resets the restart count.

Both known disconnect/logout messages can trigger replacement sooner. If both
clients fail, recovery stays sequential through close, exit confirmation, relaunch,
login, movement verification and minimization. A healthy sibling remains untouched.
STOP/configuration/client replacement cancels delayed work.


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
