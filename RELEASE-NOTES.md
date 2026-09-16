# 4RTools Vanilla 0.6.36

## Primary X/Y autofarming watchdog

The existing verified, read-only fleet observations now also drive a per-client
movement watchdog. After startup/recovery, 120 seconds without verified movement
queues that client for restart. This includes unchanged, missing, unreadable,
unverified and stale X/Y readings. Unknown is never converted to coordinate zero.
Neither an unrecognized popup nor a failed screenshot blocks this recovery path.
Missing processes keep the existing immediate sequential relaunch behavior.
Process presence comes from the process-list snapshot: a denied metadata query
cannot silently discard a live PID or authorize a duplicate replacement.

Movement on either axis resets the deadline. The reader's movement timestamp also
preserves intermediate movement when a character returns to the same position
between supervisor polls. Deadlines use monotonic time. Stale/cached timestamps,
a first late baseline and temporary read loss do not masquerade as movement.
Valid session/map changes start a new baseline; unknown map fluctuations do not.

STOP, configuration/client replacement and disposal cancel pending work. The
watchdog does not accrue inactivity during startup, login or recovery. Automatic
recovery must be ON; the visual-watchdog switch controls image observation, not
the primary coordinate watchdog. No additional game-memory reader is created and
no failed reader is reopened for the same live process. A confirmed process exit
clears only that client's old reader so a recycled PID cannot inherit it.

## Terminal dialogs and sequential recovery

The two reported messages, **Now Logging Out.** and **Disconnected from Server.**,
are separately recognized from cropped dialog references, with two fresh matching
observations before early recovery. They are also handled when already present
on an assigned client before START. Unknown modals are not blindly acknowledged
with Enter. Uniform failed captures remain unknown rather than gameplay.

One account owns the entire close -> confirmed exit -> relaunch -> login ->
verified Autobattle movement -> minimization sequence. If both clients fail, the
second remains queued until the first completes or its attempt fails/backoffs.
A healthy sibling is neither closed, restarted nor toggled. The recovery lease
is retained across the polling handoff between close and relaunch.

Closing uses ordinary Windows lifecycle control with a process-identity-pinned,
restricted handle (no game-memory write, injection or privilege changes). Normal
window close waits up to three seconds; one termination request may then wait
another three seconds. Unconfirmed exit, denied access or changed identity does
not authorize a replacement launch. Cancellation is checked at every native
operation boundary. Failed attempts retain the PID and exponential backoff.

## Validation

The existing Windows Debug/Release builds, full offline regression suite,
shipped-profile validation, x86/version and package checksum checks, inert portable
launch, and 18 native mock-data UI scenarios remain release gates. Additional
regressions cover the exact 120-second boundary, axis/intermediate movement,
unavailable/stale/unverified state, map/session/PID isolation, STOP/configuration
cancellation, one/both/mixed client failures, close-to-relaunch lease continuity,
backoff, initially present dialogs, and both supplied message images at resampled
sizes. Native process control is tested separately using an inert test-owned child,
not a game client.

The published ZIP/checksum and clean source identity are verified by downloading
release assets after publication. Temporary patch/workflow infrastructure and the
completed working branch are removed after integration and successful release.

## Limits

This engineering session has no live Vanilla/Gepard client or user RDP desktop.
Automated state-machine, Windows package/UI and cropped-image checks do not prove
live game recovery or minimized-client capture across every DPI/skin. The X/Y
watchdog does not depend on such capture. A deliberately stationary character can
also reach the timeout: this is the requested autofarming recovery rule, not proof
of the underlying cause. Existing dependency/compiler warnings remain visible.
The ten-second, three-total-attempt startup Autobattle verification is unchanged.
