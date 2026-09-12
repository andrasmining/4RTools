# 4RTools Vanilla 0.6.7

This patch fixes proxy-route selection and makes reconnect logging bounded and session-oriented.

- Replaced the old blind proxy click (`50% / 60%` plus `Home`/`Down`) with visual recognition of the actual four-row proxy list before any selection input is sent. The previous implementation could land on Singapore and then fail to reset the selection to Global.
- Proxy recognition is based on the current Vanilla client bitmap, proportional search regions, row spacing and dark-text structure rather than fixed desktop pixels. It is therefore designed to tolerate different client resolutions, Windows DPI/scaling, and moderate font/image softening.
- Selection fails closed: if four plausible, evenly spaced proxy rows cannot be identified confidently, 4RTools sends no proxy-selection input and reports the detector evidence instead of guessing.
- The configured route remains `Global / Singapore / Tokyo / Los Angeles` from top to bottom. 4RTools computes a conservative safe interior rectangle common to the detected row content, chooses the actual click point only inside the configured row's central interior, then clamps keyboard selection to the first row with repeated Up presses and advances exactly to the configured route before Enter.
- The same recognition path is used by normal unattended relog recovery and by `2 PROXY`, so diagnostics exercise the production logic rather than a separate approximation.
- The most recent proxy recognition capture is written to `Logs/proxy-screen-last.png` to make a live failure diagnosable without exposing credentials.
- Added offline regression tests for proxy-row recognition at 800x600, 1280x720, 1920x1080 and 2560x1440 plus softened gray text, as well as fail-closed behavior on a blank screen.
- Reconnect logs now start a fresh timestamped session file on every 4RTools process startup instead of appending forever to one `reconnect.log`.
- Each session log part is capped at 10 MB and automatically rotates to another part. The reconnect-log directory is also pruned to a bounded history (up to 20 reconnect log files / approximately 100 MB).
- An existing legacy `reconnect.log` is archived once rather than discarded. `OPEN LOG` and `COPY LOG` now target the current session log part.
- Persistent recovery/account configuration, DPAPI-protected passwords, the two-client limit, the working `TThorForm` GAME START fix from 0.6.6, and the GitHub self-updater remain unchanged.

The source wiring, Release/Debug builds, automated tests, packaging and portable launch smoke test were validated in Windows GitHub Actions before the implementation commit was pushed. Live proxy recognition still needs validation against the user's actual Vanilla login screen; when recognition is uncertain, the new behavior is deliberately to stop rather than select an unverified route.
