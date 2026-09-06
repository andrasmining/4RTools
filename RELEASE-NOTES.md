# 4RTools Vanilla 0.2.0

This independent fork integrates Vanilla into the original 4RTools application.
The original window, Ragnarok Client selector, profile selector, and feature
tabs are the main interface. Vanilla uses the read-only state integration;
additional automation and diagnostics are available from its Vanilla tab.
The fork retains the upstream MIT license,
including `Copyright (c) 2022 4RTools`, and does not imply affiliation with
Vanilla MMO or the upstream maintainers.

**Live gameplay automation remains unverified.** The original window detects
and selects Vanilla and displays its observed character name and HP/SP values.
One unique HP/SP quartet and one unique name candidate match the visible
character, and repeated stable reads have succeeded. Controlled changes,
character switches, and lifecycle checks still need validation. Candidate
values remain marked Unverified and do not enable actions; users are not asked
to discover offsets or edit configuration.

## Application and profiles

- Normal startup opens the original 4RTools `Container` window. Autopot/Ygg,
  AHK spammer, macro chains/songs, Auto Refresh timers, buffs, status recovery,
  settings, and profiles retain their existing controls.
- Vanilla instances appear in the original Ragnarok Client list, with explicit
  selection when multiple clients exist. No process ID or installation path is
  hard-coded. Selection uses Vanilla's read-only state provider instead of the
  legacy reader that requests memory-write access.
- The **Vanilla** tab provides state observations and two entry points:
  **Smart Teleport and extra rules** opens an owned settings window sharing the
  main application's selected read-only session, and **Open diagnostics** opens
  detailed observations. Extra rules retain dry-run and a configurable emergency
  stop key (Pause by default).
- Profiles are edited, saved, imported, and exported through the UI. Automation
  preferences contain no process ID or resolved runtime address. A settings or
  profile change switches automation OFF.
- Available candidates, including the character name, can be displayed without
  granting permission to use them for input. Unknown, stale, or failed state is
  invalidated, and a missing value never means zero HP, no target, or idle combat.
- Original and extra automation are mutually exclusive. The existing feature
  workers stop cooperatively; obsolete thread suspension has been removed.
- Buffered status readouts and updates only to changed cells remove repeated
  redraws and visible flicker while polling.

Version 0.1.0 opened a separate companion window first. Version 0.2.0 makes the
original 4RTools application primary and reuses its feature implementations.
The existing 0.1.0 folder and ZIP are preserved as separate release artifacts.
The upstream executable self-updater remains excluded so it cannot overwrite
this fork with an unrelated upstream executable.

## Client identity and state resolution

The state layer fingerprints the executable and matches local `VanillaBuilds`
definitions. A client build definition separates portable address recipes and
semantic validation from user automation preferences. Existing diagnostics,
snapshot export, module-relative offsets, checked pointer resolution, field
timestamps, and read-failure reporting remain available.

A known executable identity alone does not establish a trusted gameplay map.
Required field semantics and client readiness must also be validated before the
automation engine accepts observations. Definitions for known builds are local
and can be packaged for offline use; the companion does not depend on the
upstream remote supported-server list to identify Vanilla.

Original Autopot and Ygg logic can consume verified HP/max HP/SP/max SP through
the existing `Client` read methods. Name display may show the raw observed
candidate; enabling Vanilla input separately requires verified HP/max HP and
character-name state, plus the specific feature's prerequisites. A matching HP
quartet does not verify the upstream
`HP base + 0x474` status-buffer assumption: autobuff and status recovery require
their own verified status data and cannot treat unknown statuses as absent.

## How teleport decisions work

Smart Idle requires verified live HP/readiness, target state, and combat/casting
state. Farming must be enabled explicitly. The no-target and no-combat timers
must both reach their configured durations before the configured teleport
hotkey can run. A target, combat activity, or casting prevents teleportation.

Optional stuck recovery additionally requires verified coordinates and sustained
absence of movement, a target, and combat. Movement clears that stationary timer.
The teleport cooldown and post-teleport grace period prevent repeated actions.
Initial connection, map/loading transitions, client changes, and automation
restart reset the relevant timers. A failed required observation stops automation.

Fixed Interval is an explicit fallback mode. It still requires a verified build,
fresh HP/readiness, farming, cooldown, and grace checks. It respects target or
combat activity when those observations are available. Without verified combat
signals, it cannot establish the same combat avoidance guarantee as Smart Idle.
It is not enabled against the currently unverified gameplay state.

Dry-run uses the same rule decisions and timers while logging intended inputs.
It does not create trusted gameplay state or bypass state validation.

## SP recovery and action scheduling

SP recovery starts when validated current SP is strictly below the configured
percentage of maximum SP. Its configurable sequence supports ordinary key
presses, paired key down/up, delays, and client-window clicks. Recovery has its
own cooldown and an optional out-of-combat requirement.

Only one sequence executes at a time. Delays are scheduled without blocking the
UI, and delayed timer ticks do not produce catch-up bursts. OFF, disconnect,
client changes, invalid observations, and relevant combat transitions cancel
pending execution. Held inputs are released only while the original live window
can still be validated. A failed read or rejected input stops that path; cleanup
is reported as unavailable if it cannot be performed without retrying a failed
operation. Test Once uses the same state and sequence safety checks.

The additional-rule editor exposes timed, HP/SP threshold, status present/missing,
no-target, no-combat, and stationary conditions. Each rule has a name, enable
switch, condition settings, cooldown, optional out-of-combat requirement, and
an ordered key/wait/click sequence. Changes are edited on a private copy and
saved with the selected profile; Cancel preserves the prior rules. Conditions
depending on unknown or unverified state remain unavailable.

## Actual live observation

The observed executable is `Vanilla MMO.exe`, with this SHA256:

`7eb420579690bd2f5c81b42fa69888cb3d144486d3c275a19073f5216698b3ef`

The observed process is 32-bit, its module base was `0x00400000`, and its image
size was 15,839,232 bytes. These are build/session observations; the companion
discovers the process and module location at runtime.

| Check | Actual result |
| --- | --- |
| Ordinary read-only process handle | Succeeded |
| Executable header read | Succeeded; `MZ` header observed |
| Bounded main-module `.data` read | Succeeded; 3,989,092 bytes read |
| Production session selection | Connected through the normal companion session and automatically selected the matching local identity profile |
| Original 4RTools client selector | Selected the live Vanilla instance through Ragnarok Client and displayed its observed name and HP/SP without enabling automation |
| Client restart identity | Same executable SHA256 recognized after a client restart with a new PID |
| Production automation gate | An ON request using dry-run was refused with `client readiness is not verified`; no input was sent |
| HP / max HP / SP / max SP | One exact quartet candidate repeatedly returned 456/456 HP and 113/113 SP, matching visible state; controlled-change validation remains required |
| Character name | One exact name candidate repeatedly matched the visible character and displayed in the original window; character-switch/lifecycle validation remains required |
| X/Y, target/no-target, combat/casting | Not verified |
| Map/loading, Autobattle, status effects | Not verified |
| Ordinary `PrintWindow` capture | Returned a black image; no alternate capture path was attempted |
| Real companion keyboard/mouse actions | None sent to Vanilla during this validation |
| Smart Idle, Fixed Interval, SP recovery live effects | Not validated; dependent actions remain gated |
| Relog, map change, gameplay resolution after client restart, PC restart | Gameplay resolution not validated |
| Windows 10 x64 | Local development/validation environment |
| Windows 11 x64 / another PC | Not available for actual validation |

The candidate definitions are packaged as offsets relative to the fingerprinted
main executable, not today's absolute addresses:

| Candidate field | Module-relative offset | Observation status |
| --- | --- | --- |
| Current HP | `0xD38DDC` | Repeated stable match, unverified |
| Maximum HP | `0xD38DE0` | Repeated stable match, unverified |
| Current SP | `0xD38DE4` | Repeated stable match, unverified |
| Maximum SP | `0xD38DE8` | Repeated stable match, unverified |
| Character name | `0xD3B7B0` | Exact 40-byte bounded string candidate, unverified |

The build definition deliberately keeps `VerifiedFields` empty. Stable reads
of unchanged values do not prove semantics across damage, SP use, character
switches, map changes, client restarts, or another PC. Target, combat, position,
readiness, loading, and status fields remain unmapped.

Vanilla remained running during the read-only checks. No Gepard internals were
inspected or modified, and no protection bypass, code injection, packet
manipulation, game-memory write, or security-setting change was performed.
Successful reads do not establish that Gepard accepts custom automated inputs;
that part of coexistence is untested. Any blocked read or action ends that line
of investigation without stronger privileges or alternative access paths.

## Validation history and 0.2.0 checks

The current **0.2.0** x86 Release and Debug configurations both rebuilt with
**zero errors and five existing warnings**, passing **101 offline test groups
per configuration**. The original-window smoke verified **13 feature forms**,
portable settings, embedded resources, and automation OFF. A separate live
original-window check selected Vanilla through its normal dropdown and displayed
the observed name and HP/SP candidates without sending input.

The five remaining warnings comprise three assembly-version conflicts
(`System.Runtime`, `System.IO`, and `System.Diagnostics.Tracing`), an unused
updater exception variable, and an unused ATK/DEF field. The earlier
`Thread.Suspend` warning was eliminated by replacing thread suspension with
cooperative stop and interruption. The test suite additionally covers the
read-only bridge for original features, candidate display versus action
validation, unverified-state rejection, and failure stopping. Cancellation tests
cover ordinary OFF without a failure latch and stopped callbacks that must not
send into a later ON interval. The owned extra window's emergency stop covers
both sets of controls, with conflicting hotkeys rejected.

The **0.2.0** portable ZIP was extracted into a fresh repository directory and
its executable passed the original-window launch check: `Container`, 13 original
feature forms, automation OFF, no game attachment, and no input. All packaged
payload checksums matched after launch. The tests and live display checks above
do not establish Autopot, macro, teleport, or recovery effects in the game, and
no real input was sent during these checks.

The prior **0.1.0** x86 Release and Debug configurations rebuilt with
**zero errors and the same six baseline warnings**, passing **88 offline test
groups in each configuration**. These cover the read-only memory layer,
build-profile parsing and semantic gates, module/pointer addressing, state
validation, teleport timers and transitions, SP conditions, scheduling and
cancellation, lost observation continuity, profile serialization/storage, and
blocking Vanilla from the legacy memory-access path. They use synthetic state and
do not establish live gameplay semantics.

That earlier warning count included the obsolete `Thread.Suspend` usage now
removed. The initial diagnostics baseline passed 24 offline test groups per
configuration; that count is retained in the diagnostics report.

The prior 0.1.0 companion UI was launched, displayed, and closed successfully;
its screenshot was visually reviewed. The smoke route verified embedded JSON
and stock resources, portable settings, UI construction/display/closure, and
automation OFF without attaching to a game. A separate production-session live
check confirmed automatic fingerprint/profile selection and refusal to enable
automation on unverified gameplay state. The portable ZIP was extracted into a
fresh directory inside the repository and its EXE passed the same startup test
with the repository as its working directory. All packaged payload hashes were
verified after that launch. This demonstrates independence from the executable's
original build folder; it does not substitute for a test on another PC.

These 0.1.0 results are historical and are separate from the current original
window checks above.

The release script always restores and rebuilds Release, runs the offline tests,
checks explicit x86 PE/CLR flags and file-version consistency, verifies required
dependency embedding, and packages only the selected EXE/config and release
assets. It extracts the ZIP into a new location inside the repository and runs
the packaged original-window smoke test. The report must identify `Container`,
verify at least ten original feature forms, and explicitly show automation OFF,
no game attachment, and no input sent. The isolated test suppresses external
telemetry and global input-hook registration while exercising the original
forms and profile controls. Payload checksums are verified before publishing
the final folder/ZIP. Build and smoke-test logs stay outside source control.

The packaging helpers were exercised on Windows PowerShell 5.1: syntax parsing,
repository path containment, AnyCPU/Prefer32Bit rejection, genuine x86 detection,
identical-input ZIP hashes, empty profile directories, and Unicode ZIP names
passed. These checks are separate from launching the completed application.

Static distribution review confirmed that stock form/icon/audio resources remain
embedded and that referenced non-framework managed dependencies are embedded.
No additional non-Windows native DLL is required by the reviewed input code.
This dependency review is separate from in-game regression tests of upstream
AHK, macros, Autopot, and autobuff effects, which have not been performed.

## Portable distribution

The 0.2.0 release workflow targets these separate output paths:

- `dist/4RTools-Vanilla-v0.2.0/`
- `dist/4RTools-Vanilla-v0.2.0-portable.zip`
- `dist/4RTools-Vanilla-v0.2.0-portable.zip.sha256`

The folder contains `4RTools-Vanilla.exe`, its `.config`, empty `Profile` and
`Profiles` directories,
README, release notes, version metadata, SHA256 manifest, the unchanged upstream
license, third-party notices, and local build definitions when supplied. Managed
dependencies are embedded through Costura/Fody. The unused upstream RAR updater
and its Aspose dependency are excluded from this fork's compiled application.
Original profiles live in `Profile`; additional automation settings live in
`Profiles`. No existing user settings, test profiles, logs, or memory dumps are
copied into the distribution. Version 0.1.0 remains in its existing folder.

The package targets x86 and requires Microsoft .NET Framework 4.7.2 or a later
4.x runtime. No Visual Studio, SDK, NuGet, Git, or source tree is needed to run it.
The runtime configuration lets Windows/.NET report a missing framework before
startup. The upstream administrator manifest is retained; a comparison proving
that elevation is unnecessary has not been completed. This work does not change
antivirus or game security settings.

The source can be rebuilt with `scripts/build.ps1 -VanillaRelease`; release
engineering uses `scripts/build-release.ps1`. The latter's `-Replace` option
preserves existing packages and any profiles under `dist/.previous/` before
publishing a replacement. These scripts are for maintainers, not required steps
for application users.

ZIP and executable hashes are generated below only after a successful complete
packaging run. The packaged notes omit that generated block to avoid a circular
ZIP checksum; `SHA256SUMS.txt` inside the package covers its original payload.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.2.0. SHA256:

- `4RTools-Vanilla-v0.2.0-portable.zip`: `d6bdcc4efb7455d59fc0f2aab0f81445143c096c73e80b58ca06e5b4dc75c2f1`
- `4RTools-Vanilla.exe`: `4ae0abdef0357c3d5f80146c497cfebd8f76aec9e7a587bca75f2315945ed695`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
