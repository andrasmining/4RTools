# 4RTools Vanilla 0.6.35

## Autobattle startup and recovery

Startup, recovery and the Resume hotkey diagnostic share one bounded movement
verifier. After the configured hotkey, fresh verified X/Y readings are observed
throughout a 10-second window. A change on either axis succeeds immediately.
Without movement, the intended client is focused and checked again before a
retry. There are **three total attempts: the initial press and two retries**.
The third unsuccessful window produces an explicit failure; later gameplay
polls do not restart the same budget.

Cold startup holds the current client's recovery lease until movement verification
and minimization are complete. A failed cold start blocks later accounts. Healthy
already-running clients are adopted without toggling Autobattle. Failed or
interrupted startup is not silently adopted as healthy; a successful explicit
Resume hotkey diagnostic clears its failed latch.

Compact account status displays the current verification/retry attempt. Detailed
failure reasons remain available in hover text and logs. A verifying client is
not automatically minimized before verification finishes.

## Lifecycle and recovery repairs

- STOP and configuration changes invalidate pending startup, recovery and diagnostic
  workers. Old callbacks cannot revive an operation after STOP/START or overwrite
  a replacement client's status. A cancelled login cannot close the replacement
  client as a side effect of its failure cleanup.
- A rejected diagnostic request no longer invalidates the running diagnostic while
  leaving its input lease occupied. Explicit resume diagnostics record their real
  success/failure instead of leaving an obsolete failed latch behind.
- Final client minimization checks the exact runtime/PID under the ownership lock.
  Foreground input is serialized; cancellation and target ownership are checked
  before clicks, key presses and each typed character. Chord cleanup releases held
  keys after cancellation or input failure.
- Recovery and diagnostic proxy selection use the account's proxy, as cold startup
  already did. Shipped read-only build profiles are loaded beside the executable,
  not from the mutable user-data directory.
- Unknown/stale observations, failed reads, map/session/character changes, dead
  characters and lost input ownership stop verification safely. Existing trusted
  X/Y/map mappings are reused; no memory writes, offset guesses, packet actions,
  injection or protection bypasses are introduced.

## Repository and packaging

The outdated README and packaged quick start now describe the actual Vanilla-first
workspace, auto-saved account settings, taskbar minimization, persistent data and
movement verification. Repository policy explicitly requires verified release
publication, integration into main and removal of completed task branches.
Temporary recovery/patch infrastructure is removed after validation.

The release workflow verifies the published tag against its tested source commit,
downloads the public ZIP and checksum, and compares the downloaded bytes with the
Windows-tested package. It exports the verification report and source identity.
Licenses, notices, existing user configuration and the two-active-client limit
are preserved.

## Validation and limitations

Publication is gated on shipped-profile validation, Debug and Release compilation,
the complete offline regression suite, portable-package checksums and x86/version
checks, an inert Windows executable launch, and 18 native mock-data UI scenarios.
The autobattle suite includes 47 deterministic cases covering movement, timing,
three-attempt exhaustion, state validity, cancellation, client/operation isolation,
failure latching, diagnostic lifecycle, safe adoption and held-key cleanup. The
new lifecycle regressions are reproduced before applying their implementation
fixes, then required to pass with the full suite.

These automated Windows build, package and mock-data UI checks are **not live
Vanilla/Gepard gameplay or RDP testing**. This release has not been exercised
against a live game client in this engineering session. Movement proves only
movement, not combat or its cause. Conversely, a character fighting while
stationary for all three windows can fail this deliberately movement-based check.
Legacy dependency/compiler warnings remain visible; they are not suppressed.
