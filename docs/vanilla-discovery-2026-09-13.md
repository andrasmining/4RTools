# Vanilla discovery session, 2026-09-13

The live session reached a process-access boundary before any gameplay bytes
could be read. **No new field was verified or promoted.** The existing five
verified mappings remain unchanged. This record supersedes older statements
about current mapping coverage in the historical diagnostics log.

## Live observations and stopping point

The repository was clean on `main` at `b52e648`, then fast-forwarded to
`origin/main` at `aa9dd38` before implementation. Two processes named
`Vanilla MMO` were present, with PIDs `9928` and `23372`; both had the Vanilla
MMO / Gepard Shield window title. The running 4RTools window reported 0.6.11.
Names and window titles are metadata, not executable fingerprint verification.

The existing `ReadOnlyProcessMemory` adapter was invoked once for each client
under the observer's existing privileges. The x86 assembly was loaded in an
x86 host to match its assembly format; this did not change process access or
privileges. Both adapter construction attempts failed at their first native
operation:

| Client PID | Operation | Result |
| --- | --- | --- |
| 9928 | `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION)`, mask `0x1010`, inheritance disabled | Win32 5: Access is denied |
| 23372 | Same operation | Win32 5: Access is denied |

The exact adapter message was:

```text
OpenProcess failed for PID <pid>: Win32 5 (Access is denied). Session stopped; no retry or alternate access attempted.
```

No `ReadProcessMemory` or `VirtualQueryEx` call followed these failures. Neither
live executable fingerprint nor bitness/module base could be confirmed through
the adapter. The error alone does not identify whether Windows permissions,
process protection, or another component denied access; it is not proof that
Gepard caused the rejection.

No scanner, elevated observer, already-running application, alternate process
access path, or protection change was used to retry the blocked operation.
No game input, inventory action, relog, client restart, map transition, network
change, or email was performed for discovery. Without a readable baseline,
moving the characters would not supply the required memory correlation.

## Field status

The existing profile is restricted to SHA-256
`7eb420579690bd2f5c81b42fa69888cb3d144486d3c275a19073f5216698b3ef`,
PE machine `0x014c`, and image size `15839232`. All offsets below are relative
to `Vanilla MMO.exe`. “Previously verified” describes the evidence already
committed in the profile, not a successful read in this session.

| Field | Status | Type | Mapping |
| --- | --- | --- | --- |
| CurrentHP | Previously verified | UInt32 | `+0xD38DDC` |
| MaxHP | Previously verified | UInt32 | `+0xD38DE0` |
| CurrentSP | Previously verified | UInt32 | `+0xD38DE4` |
| MaxSP | Previously verified | UInt32 | `+0xD38DE8` |
| CharacterName | Previously verified | UTF-8, 40-byte bound | `+0xD3B7B0` |
| CurrentWeight | Blocked before baseline; unavailable | Unknown | None |
| MaxWeight / WeightLimit | Blocked before baseline; unavailable | Unknown | None |
| X | Blocked before baseline; unavailable | Unknown | None |
| Y | Blocked before baseline; unavailable | Unknown | None |
| Map | Blocked before baseline; unavailable | Unknown | None |
| ActionState / stance / movement | Blocked before baseline; unavailable | Unknown | None |
| CurrentTargetId / no-target value | Blocked before baseline; unavailable | Unknown | None |
| Player/selected/nearby actor identity and coordinates | Blocked before baseline; unavailable | Unknown | None |
| StatusEffects / Soul Link | Blocked before baseline; unavailable | Unknown | None |
| Learned skills / levels / shortcut slots | Blocked before baseline; unavailable | Unknown | None |
| Selected/queued/current skill, cast, cooldown, cast target | Blocked before baseline; unavailable | Unknown | None |
| ClientReady / connection state | Blocked before baseline; unavailable | Unknown | None |
| Loading | Blocked before baseline; unavailable | Unknown | None |
| AutobattleEnabled | Blocked before baseline; unavailable | Unknown | None |

No new candidate offset or pointer chain was produced. For every requested new
field, controlled reversals, second-client semantic confirmation, relog,
full client restart, and PC restart verification are **not performed**.

The retained profile evidence records changing HP/SP matching visible gameplay,
matching maxima, and a matching character name on 2026-09-13. It does not certify
this session's two clients or all of the requested restart checks. No new
evidence was added to `VerifiedFields`.

## Changes prompted by the investigation

Weight diagnostics and the alert consumer share one pair-validation contract:
both values must exist, maximum must be positive, current must not exceed
maximum, and both must fit the existing 1,000,000,000 raw-unit sanity ceiling.
Numeric plausibility alone never grants verification. Unmapped weight remains
unavailable, and the Alerts page cannot show a verified live percentage until
the exact build has proven mappings.

The Memory Finder stops after native observation failure, clears incomplete
samples, and retains the error. Candidate filters must complete before their
results are committed. An exhausted candidate set stays exhausted until an
explicit new baseline; it must not silently restart a broad comparison.
Candidate snippets must describe the scanned datatype accurately rather than
treating arbitrary bytes as booleans or pretending a 16-bit value is UInt32.
The writable-memory scope is explicitly bounded below 2 GiB and by the host's
reported application-address limit. Higher private allocations are excluded;
the report records the bounds. The known main-module scope remains separate.

Offline UI smoke validation must leave fleet polling and weight-alert polling
stopped. This prevents a smoke test from reopening clients or sending configured
alerts while merely checking application construction and rendering.

## Ordinary-input automation boundary

The current Temporary Actions implementation uses an explicitly configured
action key, a sit/stand key, and optional client-relative target coordinates.
No verified skill-to-shortcut-slot mapping, actor structure, or Soul Link status
source exists in the audited profile. Upstream status IDs do not prove a
Vanilla memory layout.

This session cannot establish that no-hotkey activation is impossible. It also
cannot safely implement automatic slot/actor selection from unobserved memory
or guessed button geometry. Those mappings and UI correspondences still need
independent evidence through permitted observation. No new input automation
was enabled; existing ordinary-input workflows retain their current settings.

## Validation

`scripts/build.ps1 -VanillaRelease` rebuilt Release and Debug successfully:
**148 offline tests passed per configuration, zero failures, zero build errors**.
This includes four added weight-validation groups and eleven added scanner
groups using fake memory. The six existing warnings remain: three MSB3277
assembly conflicts (`System.Runtime`, `System.Reflection`,
`System.Diagnostics.Tracing`), CS0168, CS0169, and CS0414. Build/test logs are
under `%LOCALAPPDATA%/4RTools-Engineering/builds/20260913-135932-385/`.

The Release application entry point passed the offline UI smoke check in an
as-invoker x86 STA harness, with executable and working directory both inside
the repository. The harness loaded a byte-identical copy of the built
`4RTools-Vanilla.exe` from a separate engineering folder, preserving legacy
profiles in `bin/Release`. It used isolated `FOURRTOOLS_DATA_ROOT` settings with
alerts enabled and synthetic `.invalid` SMTP addresses. The smoke process
exited with code 0 and rendered 14 feature forms. The report confirmed zero
fleet polls, no gameplay attachment/input, and stopped legacy polling, fleet
polling, recovery, alerts, updates, and automation. The release script accepted
the report under its strengthened validation contract.

The screenshot and report are retained under
`%LOCALAPPDATA%/4RTools-Engineering/discovery-20260913/`
(`ui-smoke-verified.png` and `ui-smoke-verified.json`). The temporary harness and
its copied payload remain in the ignored
`bin/Engineering/discovery-20260913-smoke/` folder because automatic approval
review rejected their cleanup as "blocked by policy". One-off helper sources
remain in the external engineering log folder. None are committed. This
validates application startup and UI construction at existing privileges; it
does not claim a normal UAC launch, a new portable release, or live gameplay
validation.

The live access-denial result above is the only gameplay-process validation
from this session. The audited build profile, `VerifiedFields`, and version
0.6.11 remain unchanged. Discovery remains blocked at the stated read-access
boundary; no relog/restart or new field evidence is claimed.

Implementation commits on `main`, each pushed and verified against `origin/main`:

- `cc6e44d`: consistent weight-pair validation.
- `23f45a2`: stopped discovery sessions and scan integrity.
- `464a4ca`: offline smoke isolation and report validation.
