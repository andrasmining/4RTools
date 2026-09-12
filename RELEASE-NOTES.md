# 4RTools Vanilla 0.6.8

This patch fixes the two remaining live relog stages exposed by the 0.6.7 step-by-step test: username/password field targeting and the one-entry game-server selection dialog.

- Credential filling no longer assumes a fixed login coordinate and no longer relies on `Tab` to move from username to password. That was the concrete cause of the observed username/password swap.
- 4RTools now captures the current Vanilla client and identifies the login controls as three separately bordered stacked UI controls. The second detected control is the username field and the third is the password field. It then clicks each field independently before replacing its contents.
- Username and password clicks are chosen from conservative interior rectangles of the detected controls. The password itself is never logged. If the two fields cannot be identified confidently, no credentials are typed.
- Game-server selection no longer reuses the proxy/service coordinate. It now identifies the central Vanilla server-selection dialog and its bordered first/only server row, clicks only inside that detected row, clamps the list to its first item and confirms with Enter.
- The server step verifies that the dialog actually disappears. If it remains, it retries once using a newly captured/detected row and then fails closed instead of blindly continuing to character selection.
- The same visual recognition and verification code is used by unattended recovery and by the numbered `3 FILL USER/PW` and `5 SERVER` diagnostics.
- Diagnostic captures are saved as `login-screen-last.png`, `server-screen-last.png`, and failure-specific server/login captures under the persistent Logs directory when useful.
- Recognition uses current client pixels, relative search regions, detected borders/spacing and normalized input coordinates rather than absolute desktop coordinates. It is designed for different client sizes, Windows DPI/scaling and moderately softened/blurry rendering.
- New offline tests cover distinct username/password recognition and one-server dialog recognition at 800x600, 1280x720, 1920x1080 and 2560x1440, including softened login rendering and blank-screen fail-closed behavior.
- Existing working stages remain unchanged: launcher/GAME START, proxy selection, character selection, resume hotkey, two-client assignment, STOP TEST behavior, persistent account configuration, bounded per-startup logging, and the GitHub self-updater.

The final implementation was built and tested on Windows through the release pipeline before publication. Real Vanilla/Gepard interaction still requires local validation, but the changed stages now fail closed when their target UI cannot be recognized instead of typing/clicking into an unverified location. No Gepard bypass, code injection, packet manipulation or game-memory writes are used.
