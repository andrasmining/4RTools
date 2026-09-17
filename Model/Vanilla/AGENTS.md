# Vanilla UI policy

Applies to files in `Model/Vanilla/` in addition to the repository-root `AGENTS.md`.

The Vanilla workspace is functionally dense, so keep its visible UI deliberately minimal and responsive.

- Optimize first for ordinary Full-HD / RDP work areas around 1920x1080 and 1980x1020, while retaining graceful scrolling below that size. Do not design around 2K/4K space.
- Prefer adaptive layout based on current client width/height instead of large fixed panels or fixed whitespace.
- Spend vertical space on interactive data and controls, especially the account table and log. Keep the always-visible fleet strip compact without clipping location, activity or resource values.
- Do not show explanatory paragraphs, security implementation notes, or other documentation inline when a tooltip/hover description is sufficient. Normal-state helper prose should be hidden; visible text should be state, data, action, warning, or error information.
- Keep related controls on one compact row at Full-HD when practical and allow wrapping only as a responsive fallback.
- Launcher input should be only as wide as useful; do not let it consume the whole row.
- On normal desktop widths, the Recovery & relog tab should use approximately two thirds of its width for launcher/actions/accounts and one third for the reconnect log. Stack only on genuinely narrow windows or when enlarged text cannot fit both panes readably.
- Do not keep a separate large recovery-status panel when the same state can be expressed beside the corresponding account. Put compact PID/state information in the account table and keep verbose detail in hover text/logging.
- The account table is the primary recovery-management surface. Reserve room for at least four account rows plus roughly one empty-row worth of breathing room. Let it consume available vertical space and show all saved profiles that fit; use an internal vertical scrollbar only when the profile list exceeds the available pane.
- All account columns must remain inside the visible table. Measure compact fields and share remaining width among Description, Username, Character name and Status. Do not retain stale fill widths from the larger legacy layout.
- Any number of account profiles may be saved persistently, but at most two profiles may be enabled/actively supervised at once. Preserve the existing two-active-client recovery/runtime limit.
- Account-specific settings belong in the account row/edit dialog. Proxy is account-specific and visible in the table; edit it through the account editor. Do not add a second selected-account proxy control below the table.
- Do not expose redundant one-off actions such as a separate `Run login now` button when the normal supervisor/test flow already owns login/recovery behavior.
- Preserve hover documentation for controls and panels so functionality remains discoverable without making the default workspace visually busy.
- UI-only refactors must reuse existing behavior/event wiring where possible and must not change sequential startup, recovery, memory observation, or automation semantics unless the task explicitly requires that behavior change.

## Required native UI validation

Use the existing standard Windows GitHub Actions runners for build, test and rendering validation. Do not introduce paid runners, testing services or extra infrastructure. Before publishing a UI release, run `scripts/test-ui-layout.ps1`, fix its failures and inspect the generated screenshots. A successful build alone is not UI validation.

Preserve the native mock-data harness's checks for full-width embedding, column visibility, pane ratio, toolbar spacing, live-card fields, large profile lists, scrolling, tab selection and resize/text-size transitions. Add a regression assertion when a screenshot reveals a missed defect. Do not silently skip or weaken a failing check just to publish. Keep the release gated on these tests.

Report actual Windows mock-data UI results separately from live gameplay/RDP testing. The harness must keep game observation, input, automatic startup, email and network update services inactive; test data must never be real credentials or user profiles.

The table is a character roster: Description, Username, Slot, Character name,
then the remaining fields. Multiple characters may share one username; at most
two enabled rows. Unknown slots remain blank, not slot 1. Auto-discovered rows
stay disabled. Native UI checks must cover discovery and the character editor.

## User-presence-aware minimization and launcher pacing

Automatic minimization must respect active human use. If a managed Vanilla window
is restored/maximized/otherwise visible, do not minimize it immediately. Require
at least 60 seconds continuously visible AND at least 60 seconds without machine
cursor movement. Any cursor movement restarts the idle grace; unavailable cursor
state fails closed and defers minimization. Manual explicit minimize remains
immediate. Startup/recovery that owns a serialized gate may wait for this grace
rather than stealing a window from an active user.

Launcher GAME START automation must never double-activate a control in one attempt.
Wait for the launcher window to remain present for at least 2.5 seconds, then use
one semantic native-control invocation when available; otherwise require two stable
visual detections before one click. Never use blind/fallback GAME START coordinates.
Wait at least 15 seconds after an actual activation before another attempt unless a
new Vanilla process appears first.

Post-login Autobattle/slave activation is memory-state-first. Fresh verified login
username + character identity + X/Y/map/living HP may establish readiness even when
the visual classifier returns Unknown. Known login/modal/logout/disconnect states
still block input. Every readiness transition, 7-second settle, hotkey attempt and
10-second movement window must also be written to the global debug log, not only
the reconnect session log.

## Existing-client startup evidence

- For an already-running, identity-matched Vanilla client, fresh verified read-only gameplay memory is the primary startup evidence: expected username + character, X/Y, map and living HP.
- Visual recognition is supplemental for existing-client adoption. `Unknown` must not fail adoption, trigger hotkey input, or repeatedly restore/focus a client solely to prove gameplay. Explicit login/logout/disconnected evidence may still fail closed.
- `COPY DEBUG LOG` must retain enough host/session/display/top-level-window telemetry to diagnose machine-specific visual/focus differences without requiring the user to reconstruct the environment manually.

## Launcher and helper-window ownership

- Never treat `GDI+ Hook Window Class` / `GDI+ Window (...)`, IME helpers, Gepard splash windows, or other tiny bootstrap/helper surfaces as an interactive Vanilla game window. In particular, never call ShowWindow/restore/focus on the 1x1 GDI+ helper just because its title contains `Vanilla MMO`.
- Launcher GAME START must be bound to a verified launcher HWND/PID. Foreground acquisition must be ownership-checked, bounded, and fail closed; never use taskbar/desktop coordinates to work around Windows foreground lock.
- When GAME START has been detected twice at a stable launcher-client position, prefer one direct owned-window client message to that verified launcher HWND over a global cursor/SendInput click. No blind coordinate fallback is authorized.
