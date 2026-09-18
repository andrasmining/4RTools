# 4RTools Vanilla 0.6.49

## Character-bound, X/Y-only Smart Teleport

Smart Teleport no longer depends on a manually selected process or on target/combat/casting state. It is configured directly on each saved username + character row in Recovery & relog. Every character has its own enable flag, stationary timeout (60 seconds by default) and live-captured teleport hotkey. The running PID is resolved automatically from the existing fresh verified username + character identity.

Only fresh verified read-only X/Y is used as the idle trigger. Coordinate changes and intermediate movement reset the timer. Stale, unavailable, unverified, session-replaced or map-replaced observations start a new baseline and cannot be interpreted as idle.

Teleport input is designed for background clients: after the per-client serialized input lease is acquired, the configured hotkey is posted directly to the verified Vanilla HWND without restoring/foregrounding it. 4RTools captures that same window and requires two consecutive structural detections of the **Select an Area to Warp** modal with the first selection row highlighted before sending Enter. A missing/ambiguous popup authorizes no Enter; an already-open popup before the hotkey authorizes no input; after Enter the popup must be observed gone. This prevents a blind Enter from falling through into chat or another UI.

Smart Teleport shares ownership with startup/recovery and Weight/Cart automation so input cannot overlap across clients. STOP, settings changes and PID/session/character replacement invalidate stale work. Lease contention defers the teleport without discarding the already-due idle condition.

The old profile-based Smart Teleport UI in the hosted Automation page is retired in favor of the character-row configuration; it cannot silently run a second process-selected teleport path.

## Validation limits

Release validation covers per-character persistence/validation, default 60-second timing, X/Y-only idle/reset semantics, rejection of unverified coordinate gaps, popup-positive and quantity/ordinary-UI negative recognition, hosted legacy-path disablement, Debug/Release regressions, native recovery checks, responsive UI, portable package/launch integrity, public release identity and updater discovery.

The engineering environment cannot run the user's live Vanilla/Gepard client or independently prove that this server/client accepts background PostMessage input or PrintWindow capture while minimized. The implementation therefore fails closed whenever background capture or popup recognition is unavailable; it does not foreground the client as a fallback and never sends Enter without positive popup evidence.
