# 4RTools Vanilla 0.6.6

This patch fixes the concrete launcher-window bug exposed by the 0.6.5 diagnostic log.

- Fixed GAME START input being routed to the wrong top-level window. The Vanilla launcher process exposes a tiny Delphi `TApplication` helper window as `Process.MainWindowHandle`, while the real visible launcher is a separate `TThorForm` titled `Vanilla MMO Launcher`. 0.6.5 was therefore calculating its coordinates against a 1x1 client area and physically clicking the wrong screen point even though Windows reported `SendInput` success.
- Launcher window resolution now enumerates every visible top-level window owned by the launcher PID and deliberately prefers the real `TThorForm` / `Vanilla MMO Launcher` surface, with usable client area taking precedence over tiny helper windows.
- Visual GAME START capture, physical `SendInput`, targeted mouse-message fallback, native child-control inspection, and launcher diagnostics now all operate on the same resolved visible launcher HWND instead of falling back to `Process.MainWindowHandle`.
- Foreground mouse input can now be given an explicit resolved HWND, so the click geometry is calculated from the real launcher window while ordinary Vanilla game input keeps its existing behavior.
- Existing selected-account two-client behavior from 0.6.5 remains: with `Clients = 2`, one client may stay running while the selected second account launches a new client.
- Unique PID ownership, `STOP TEST`, automatic test cancellation on window closure, persistent configuration, encrypted passwords, and the GitHub updater remain unchanged.

The exact source fix was built, tested, packaged and portable-smoke-tested on Windows in GitHub Actions before being committed. The real Vanilla launcher still requires local validation, but the previous log now gives a definitive root cause rather than an unknown input failure.
