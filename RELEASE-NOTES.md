# 4RTools Vanilla Companion 0.1.0

This independent fork adds a Vanilla companion UI, read-only client observation,
and guarded automation rules to 4RTools. It retains the upstream MIT license,
including `Copyright (c) 2022 4RTools`, and does not imply affiliation with
Vanilla MMO or the upstream maintainers.

**Live gameplay automation is not validated in this release.** The current
Vanilla client can be identified and read through ordinary read-only APIs, but
no gameplay field has yet been verified against visible character state. The
application therefore keeps rules that depend on that state disabled. This is
an actual remaining limitation, not a request for users to discover offsets or
edit configuration.

## Application and profiles

- Normal startup opens the companion without downloading or replacing its
  executable through the upstream updater. Original 4RTools functionality remains
  available separately in the application.
- Vanilla instances are enumerated by process name, with explicit selection when
  multiple clients exist. No process ID or installation path is hard-coded.
- The companion UI exposes connection status, character/state observations,
  client identity, teleport settings, SP recovery sequences, an additional-rule
  editor, dry-run, a global ON/OFF switch, and a configurable emergency stop key
  (Pause by default).
- Profiles are edited, saved, imported, and exported through the UI. Automation
  preferences contain no process ID or resolved runtime address. A settings or
  profile change switches automation OFF.
- Unknown, stale, failed, and unverified observations remain unavailable. A
  missing value is never interpreted as zero HP, no target, or idle combat.

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
| Client restart identity | Same executable SHA256 recognized after a client restart with a new PID |
| Production automation gate | An ON request using dry-run was refused with `client readiness is not verified`; no input was sent |
| HP / max HP / SP / max SP | Not verified |
| Character name | Not verified |
| X/Y, target/no-target, combat/casting | Not verified |
| Map/loading, Autobattle, status effects | Not verified |
| Ordinary `PrintWindow` capture | Returned a black image; no alternate capture path was attempted |
| Real companion keyboard/mouse actions | None sent to Vanilla during this validation |
| Smart Idle, Fixed Interval, SP recovery live effects | Not validated; dependent actions remain gated |
| Relog, map change, gameplay resolution after client restart, PC restart | Gameplay resolution not validated |
| Windows 10 x64 | Local development/validation environment |
| Windows 11 x64 / another PC | Not available for actual validation |

Vanilla remained running during the read-only checks. No Gepard internals were
inspected or modified, and no protection bypass, code injection, packet
manipulation, game-memory write, or security-setting change was performed.
Successful reads do not establish that Gepard accepts custom automated inputs;
that part of coexistence is untested. Any blocked read or action ends that line
of investigation without stronger privileges or alternative access paths.

## Build and package validation

The companion's explicit x86 Release and Debug configurations rebuilt with
**zero errors and the same six baseline warnings**, passing **88 offline test
groups in each configuration**. These cover the read-only memory layer,
build-profile parsing and semantic gates, module/pointer addressing, state
validation, teleport timers and transitions, SP conditions, scheduling and
cancellation, lost observation continuity, profile serialization/storage, and
blocking Vanilla from the legacy memory-access path. They use synthetic state and
do not establish live gameplay semantics.

The six warnings predate the companion: three assembly-version conflict
warnings (`System.Runtime`, `System.IO`, and `System.Diagnostics.Tracing`),
obsolete `Thread.Suspend`, an unused updater exception variable, and an unused
ATK/DEF field. The earlier diagnostics baseline passed 24 offline test groups per
configuration; that earlier count is retained in the diagnostics report.

The normal x86 companion UI was launched, displayed, and closed successfully;
its screenshot was visually reviewed. The smoke route verified embedded JSON
and stock resources, portable settings, UI construction/display/closure, and
automation OFF without attaching to a game. A separate production-session live
check confirmed automatic fingerprint/profile selection and refusal to enable
automation on unverified gameplay state. The portable ZIP was extracted into a
fresh directory inside the repository and its EXE passed the same startup test
with the repository as its working directory. All packaged payload hashes were
verified after that launch. This demonstrates independence from the executable's
original build folder; it does not substitute for a test on another PC.

The release script always restores and rebuilds Release, runs the offline tests,
checks explicit x86 PE/CLR flags and file-version consistency, verifies required
dependency embedding, and packages only the selected EXE/config and release
assets. It extracts the ZIP into a new location inside the repository, launches
the packaged smoke-test route, and verifies payload checksums before publishing
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

Local outputs from the validated release script are:

- `dist/4RTools-Vanilla-v0.1.0/`
- `dist/4RTools-Vanilla-v0.1.0-portable.zip`
- `dist/4RTools-Vanilla-v0.1.0-portable.zip.sha256`

The folder contains `4RTools-Vanilla.exe`, its `.config`, a `Profiles` directory,
README, release notes, version metadata, SHA256 manifest, the unchanged upstream
license, third-party notices, and local build definitions when supplied. Managed
dependencies are embedded through Costura/Fody. The unused upstream RAR updater
and its Aspose dependency are excluded from this fork's compiled application.

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

Release version: 0.1.0. SHA256:

- `4RTools-Vanilla-v0.1.0-portable.zip`: `0f6a15b1b308fcdc1dcdefddf66d4c32e98898d6aa08de64eef31c3087745ae7`
- `4RTools-Vanilla.exe`: `b60c2c7937d6747b96ec367388c865cba09faa8b5b027a6f9cb18abf2945e803`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
