# 4RTools Vanilla 0.6.3

This is another focused patch release while the live Vanilla/Gepard relog flow is being calibrated step by step.

- Fixed the real GAME START blocker observed on the user's PC: Windows could see the launcher window but refused `SetForegroundWindow`, and the previous input layer treated that as a fatal condition before any mouse click was sent. Launcher mouse clicks now use best-effort focus and still perform the real screen-coordinate click when Windows refuses to grant foreground keyboard focus.
- Kept strict foreground-focus requirements for keyboard-driven Vanilla steps, where typing or hotkeys must not be sent to the wrong window.
- Preserved the visual yellow GAME START detector and normalized launcher-coordinate targeting from 0.6.2.
- Changed the numbered one-client diagnostics so a newer step can supersede a previous diagnostic wait instead of reporting `Another test is already running`.
- If exactly one Vanilla client is already running, pressing `2 PROXY` through `7 RESUME HOTKEY` now tests only that selected stage against the existing client.
- If no Vanilla client is running, any numbered diagnostic can start from the beginning and run the prerequisite numbered stages up to the requested step. This makes each button independently usable for debugging.
- If step 1 is pressed while a Vanilla client is already running, GAME START is treated as already satisfied instead of forcing the user to close the client again.
- One-client diagnostics still refuse to guess when multiple Vanilla MMO clients are running; leave one test client open for deterministic step testing.
- Existing persistent recovery/account configuration and the GitHub self-updater remain unchanged.

The exact source changes were built, tested, packaged and portable-smoke-tested in Windows GitHub Actions before being committed. Real Vanilla/Gepard interaction still requires local validation, so use the numbered steps and `COPY LOG` if a live stage fails.
