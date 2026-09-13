# Actual-product access follow-up, 2026-09-13

The previous external-host denial was insufficient to characterize the working
4RTools session. The user reported both clients' HP/SP/name updating in the
existing 0.6.11 UI. This follow-up tested the real product executable without
elevation; it did not use a scanner helper or take another process's handles.

## Actual observations

At the beginning of the follow-up, no 4RTools process remained. Window-title
matches belonged to a browser and an editor, not the product. The earlier
instance therefore could not be sampled or closed by the agent, and its token,
handles, executable path, and update history could not be established.

Both Vanilla processes remained running throughout the tests:

| PID | Process start, local time | State during product test |
| --- | --- | --- |
| 9928 | 2026-09-13 08:33:50 | Kept running; not restarted or sent input |
| 23372 | 2026-09-13 10:26:48 | Kept running; not restarted or sent input |

1. Starting `bin/Release/4RTools-Vanilla.exe` with the repository working
   directory and `UseShellExecute=false` failed with **Win32 740: The requested
   operation requires elevation**. The active product manifest specified
   `requireAdministrator`. No UAC prompt was accepted or elevation requested.
2. The product manifest was changed to `asInvoker` and the Release application
   rebuilt. This lowers the application's requested privilege; it changes no
   Windows, game, or protection setting.
3. The actual rebuilt application launched normally as **PID 16208**, retaining
   version 0.6.11. Its dashboard did **not** reconnect HP/SP/name; instead it
   displayed "No Vanilla clients running." Both game PIDs still existed.
4. Code inspection identified a misleading lifecycle path: fleet discovery
   silently discarded `Process.HasExited` / `Process.MainWindowHandle`
   exceptions, making inaccessible metadata look like absent clients. The
   original UI did not retain which of those metadata checks failed, so the
   screenshot alone cannot identify that exact operation.
5. The real product's existing `--vanilla-snapshot` entry point was run once
   for each game PID, using only the five already-verified mappings. These
   were normal launches of `4RTools-Vanilla.exe`, not a PowerShell memory reader,
   substitute scanner, reflection host, or injected component. They ran at the
   caller's existing permissions and sent no input.

| Product observer PID | Target PID | Result |
| --- | --- | --- |
| 27428 | 9928 | `OpenProcess(0x1010)` failed: Win32 5, Access is denied; exit 1 |
| 2804 | 23372 | `OpenProcess(0x1010)` failed: Win32 5, Access is denied; exit 1 |

Both failures occurred before module identification or gameplay reads. No
scanner `OpenProcess(0x0410)`, `VirtualQueryEx`, or `ReadProcessMemory` followed.
The normal main application was subsequently closed through its own window;
both Vanilla clients were left running. No game relog, launch-order experiment,
credential entry, inventory manipulation, or email was performed.

Dashboard capture and product-native failure JSON are retained in
`%LOCALAPPDATA%/4RTools-Engineering/product-access-20260913/`:
`product-reopened-dashboard.png`, `product-snapshot-9928.json`, and
`product-snapshot-23372.json`. No gameplay memory dump is present.

## What the results do and do not establish

| Explanation | Evidence status |
| --- | --- |
| A: A fresh normal product can attach while the external helper cannot | Not observed: both newly launched product snapshot processes were denied |
| B: Only the earlier instance's retained handles work | Undetermined: the earlier instance was already gone; its context was not measured |
| C: Client/product launch order determines access | Untested; clients were deliberately preserved |
| D: Product lifecycle or launch context differs | Confirmed manifest elevation requirement and misleading metadata-error handling; neither by itself proves why the earlier live instance could read |

In particular, a failed non-elevated relaunch does not logically prove B unless
the previous instance's permissions and lifecycle are also known. No assertion
is made that Gepard caused any particular failure. No inference of no target,
idle, zero weight, or disconnected gameplay follows from denied observation.

## Scanner permission boundary

The current scanner opens `0x1010` for its initial normal reader metadata, then
opens `0x0410` for region enumeration and reads. These are separate operations.
Microsoft documents `PROCESS_QUERY_INFORMATION` (`0x0400`) as required for
[`VirtualQueryEx`](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex).
Substituting limited-query rights is not a supported fix for that enumerator.
The scanner's masks remain unchanged, and neither scanner stage was tried
after the product-native `0x1010` denial.

## Product changes and remaining discovery work

- Normal product startup now uses `asInvoker`.
- Memory-attach errors identify the requested mask; native observation errors
  identify the operation and the observer's own PID, bitness, start UTC,
  elevation, and integrity. Unknown self metadata remains
  explicitly unknown. The context helper reads only its own pseudo-handle and
  query-only token; it never opens another process or changes privileges.
- Fleet metadata failures remain visible for the affected PID and stop that
  observation instead of becoming "No clients running." Healthy sibling
  observations are preserved. If process enumeration itself fails, the monitor
  closes its observations, retains the error, and stops further enumeration.

The actual product also passed an isolated offline UI startup check as PID
26468. Its own token report recorded **32-bit, elevated no, integrity medium
(`0x2000`)**, started at `2026-09-13T12:29:12.6436931+00:00`. This validates the
new self-context capture; it does not retrospectively measure the earlier
working instance or the two snapshot observers. The report is retained as
`product-offline-smoke.json` in the evidence directory above. It confirmed all
14 original feature forms, zero fleet polls, and inactive gameplay attachment,
input, automation, recovery, weight alerts, and update checks. The release
script's smoke-report assertions also passed. This check used a copy of the real
Release executable inside `bin/Engineering/product-access-smoke-20260913/`, the
repository working directory, and an isolated engineering user-data directory.

The final `scripts/build.ps1 -VanillaRelease` run rebuilt both Release and Debug
with **158 passing offline tests per configuration, zero errors, and the same
six baseline warnings**. Logs are retained under
`%LOCALAPPDATA%/4RTools-Engineering/builds/20260913-143120-227/`. New regression
coverage uses fake process metadata and fake readers to check native-error
identity, stale-value invalidation, healthy sibling continuity, failure retention,
handle disposal, and absence of retries after per-client or enumeration failures.
The revised dashboard failure presentation was validated in code and offline
tests; no additional live attachment was attempted after the measured denials.

The startup change was committed as
`007b75589680b8af1239ef5c01d96d943c43ddbf`; observation-context and fleet failure
handling were committed as `c6183334ac1c9f6773fbbc72dd2ca1031cfa166a`.
Both were pushed to `origin/main` and checked against the remote branch SHA.

No new field was verified. The weight, coordinate/map, action/target/actor,
status/skill, readiness/loading, and Autobattle priorities remain unavailable,
with no new two-client semantic, relog, or restart evidence. The existing five
module-relative mappings and version 0.6.11 remain unchanged. Continued
discovery requires a demonstrably permitted normal product observation session;
neither elevation nor a bypass is part of this work.
