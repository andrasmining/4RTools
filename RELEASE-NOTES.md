# 4RTools Vanilla 0.6.4

This remains a focused patch release while the live Vanilla/Gepard relog path is being verified one stage at a time.

- Added an explicit `STOP TEST` control beside the numbered one-client diagnostics. It cancels the active diagnostic worker and stops any supervisor test instead of leaving a background launcher attempt running until timeout.
- Closing the Vanilla launcher manually during `1 GAME START` now ends that launch/test attempt automatically after a short disappearance grace period instead of continuing to retry for two minutes.
- Closing the diagnostic Vanilla client is treated as a stopped test rather than a generic failure when the active step observes the process exit.
- A newer numbered diagnostic now cancels the previous diagnostic worker as well as replacing its UI wait, so manually advancing the client and then pressing the next numbered step no longer leaves the earlier test running behind it.
- Hardened GAME START targeting again using two independent ordinary Windows UI-input strategies: physical screen-coordinate `SendInput` and a targeted launcher-window mouse message on alternate retries.
- Added a visible-screen capture fallback when `PrintWindow` cannot capture the skinned/web-style launcher. This lets the existing yellow GAME START detector inspect what is actually visible on screen and use the detected button center when possible.
- Increased launcher retry spacing so each click has time to start Gepard/Vanilla before another strategy is attempted.
- Logs now identify which GAME START click strategy and visual-detection path was used, making the next live failure unambiguous.
- Persistent account settings, encrypted passwords, profiles and the GitHub self-updater remain unchanged.

The source changes were validated by the Windows Release build/test/package/portable-smoke pipeline before being committed. GitHub Actions cannot prove the live Vanilla launcher accepts a click, so GAME START still requires the next local test on the user's PC; `COPY LOG` remains the diagnostic source if it does not.
