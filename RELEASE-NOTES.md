# 4RTools Vanilla 0.6.2

This remains a patch release while the Vanilla recovery workflow is being calibrated against the live Vanilla/Gepard client.

- Made Vanilla the primary full-window workspace. The application now opens maximized with `Vanilla` as the first top-level tab and the original 4RTools interface preserved under a separate `Original 4RTools` tab.
- Restored the complete one-client diagnostic sequence as permanently visible controls: `1 GAME START`, `2 PROXY`, `3 FILL USER/PW`, `4 SUBMIT LOGIN`, `5 SERVER`, `6 CHARACTER`, and `7 RESUME HOTKEY`.
- Fixed the embedded recovery layout that could collapse the diagnostic group into unreadable vertical text at normal window sizes.
- Hardened Vanilla launcher startup. 4RTools now searches the visible `Vanilla Launcher` / `patcher` window, captures it read-only, looks for the yellow lower-center GAME START control, and clicks the detected normalized position using normal foreground input.
- Added a lower-center fallback sweep when visual capture is unavailable, with exact PID/coordinates/detection evidence written to the reconnect log for live diagnosis.
- Improved foreground mouse clicks with a realistic button-down/button-up hold rather than an immediate combined event.
- Increased the launcher retry interval so a successful click has time to start Gepard/Vanilla before another attempt, avoiding accidental duplicate launches.
- Cleaned up update-status text and sizing in the integrated Vanilla workspace.
- Persistent data and the verified GitHub self-updater from 0.6.1 remain unchanged; existing recovery accounts/settings stay under `%LOCALAPPDATA%\4RTools Vanilla\`.

CI validates Release compilation, offline regression tests, packaging, and portable smoke launch. Real Vanilla/Gepard screen interaction still requires local live validation; use the numbered step tests and `COPY LOG` if any live step fails.
