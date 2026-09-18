# 4RTools Vanilla 0.6.50

## Stabilization review after the recent recovery/automation changes

This release is a repo-wide stabilization pass focused on the paths changed most heavily in the recent releases: recovery ownership, steady-state movement monitoring, Smart Teleport, Weight/Cart maintenance, character identity, cancellation, UI lifecycle, debug history and updater/release delivery.

### Teleport-first steady-state recovery

Smart Teleport remains the first-line stationary self-heal and keeps its per-character 60-second default. Normal Online supervision does not send Autobattle wakeup hotkeys.

Recovery & relog now exposes a persisted **no-movement restart threshold**, defaulting to **180 seconds** and configurable from 60 to 3600 seconds. Smart Teleport attempts do not postpone that longer deadline unless verified X/Y movement actually occurs. At the threshold, only the affected client is restarted.

Freshly confirmed **Now Logging Out.** / **Disconnected from Server.** dialogs no longer wait for a long movement stall: two fresh matching captures can start sequential replacement immediately. A stable return to the login/service shell after confirmed gameplay is also treated as a recovery event.

Failed close/launch/login/restart cycles continue indefinitely with exponential backoff capped at one hour. Replacement login still uses the shared 10-second settle and up to three ResumeHotkey/X-Y verification attempts before that recovery attempt fails into backoff.

### Manual production-path diagnostics

The Recovery **TESTS** menu now includes:

- **Smart Teleport now (selected)**
- **Weight/Cart clean now (selected)**

Both use the selected character's normal verified username+character/PID binding and the same serialized input lease, cancellation, popup/quantity recognition and ownership guards as automatic operation. The manual commands bypass only the automatic idle/weight trigger.

The redundant Vanilla **Automation** tab is removed. Smart Teleport is configured on the character row in Recovery & relog. Original 4RTools remains available as the compatibility workspace.

### Weight/Cart safety fixes

Cart alert/arming state is now bound to the stable managed character/account row rather than PID or character-name-only state, so relogs/PID replacement cannot silently create a fresh automation state for the same character.

Manual Cart holds survive unrelated recovery settings changes and supervisor STOP/START cycles. They are removed only by the explicit clear-hold action, and clearing Cart holds no longer turns unrelated Error states back to Online.

If Cart maintenance is cancelled after Autobattle may already have been toggled OFF, the character is retained on an explicit manual hold instead of being reported as a harmless deferred retry.

### Overnight debug history

Automatic and manual Smart Teleport and Weight/Cart actions now emit structured timestamped action events to the global debug log. Cart events also record individual transfer progress and completion item counts.

**COPY DEBUG LOG** includes a **last-24-hours action summary** with Smart Teleport attempts/completions/failures/cancellations and Weight/Cart attempts/completions/items moved/failures/cancellations/manual holds, followed by the detailed logs.

### Review cleanup

The repo-wide review also removes three dead-code compiler warnings and updates the two framework NuGet packages that were producing high-severity advisory warnings (`System.Net.Http` 4.3.4 and `System.Text.RegularExpressions` 4.3.1), without broad dependency churn.

## Validation scope and limits

Automated validation covers recovery timing/state transitions, configurable thresholds, immediate two-sample terminal recovery, global serialization, backoff, Cart-hold persistence/isolation, Smart Teleport X/Y and popup guards, Weight/Cart vision guards, 24-hour action-summary counting, character/profile persistence, native mock-data UI layout, removal of the Automation tab, presence of both manual TESTS actions, Debug/Release builds, portable package launch, public release identity/checksums and updater discovery.

The engineering environment cannot run the user's live Vanilla/Gepard client. Background Smart Teleport message delivery/PrintWindow capture and live Inventory/Cart geometry therefore still require live user-side observation. Both features continue to fail closed when the required window/visual evidence is unavailable; no game-memory writes, injection, packet manipulation or Gepard bypass is used.
