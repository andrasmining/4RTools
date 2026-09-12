# 4RTools Vanilla 0.6.5

This remains a focused live-debug patch release. The goal is to remove ambiguity from the launcher problem and make selected-account testing behave correctly with Vanilla's supported two-client setup.

- Fixed selected-account diagnostics when another Vanilla client is already running. With `Clients = 2`, selecting Client 2 and pressing `1 GAME START` is now allowed to start a second Vanilla client while Client 1 remains open. A running client only satisfies step 1 for the account to which that PID is actually assigned.
- Fixed duplicate PID ownership in the reconnect supervisor. The same running `Vanilla MMO.exe` PID can no longer remain assigned to two configured account rows; stale duplicate ownership is cleared before unassigned clients are matched.
- Kept diagnostics scoped to the selected account. Existing configured clients do not block a selected-account test unless the configured maximum client count is genuinely already reached.
- Greatly expanded GAME START telemetry. Each attempt now records the launcher PID/HWND, window class/title, visibility, foreground window, launcher client size/origin, normalized and absolute click coordinates, actual cursor position, `WindowFromPoint` hit window, DPI, and the exact `SendInput` down/up return counts and Win32 errors.
- Targeted fallback clicks now send mouse messages to the actual child/hit window under the GAME START coordinate when that child belongs to the launcher, and log the resolved target plus PostMessage results.
- Enumerates native child controls on the launcher. If a real child control exposes `GAME START` in its text, 4RTools also attempts a normal `BM_CLICK` and logs whether Windows accepted it.
- Visual detection no longer collapses failures into the generic `visual detector unavailable` message. Logs now preserve `PrintWindow` status/error, visible-screen capture results, bitmap size, detector pixel counts, candidate rejection details and screen origin.
- Diagnostic step 1 writes the most recent launcher captures to the persistent Logs directory as `launcher-print-last.png` and `launcher-screen-last.png`. These are intended strictly for diagnosing the launcher UI and can be inspected alongside `reconnect.log` if the launcher still ignores input.
- `STOP TEST` and automatic stop-on-closed-launcher / stop-on-closed-selected-client behavior remain enabled.
- Persistent account settings, encrypted passwords, profiles and the GitHub self-updater remain unchanged.

The implementation is built, tested, packaged and portable-smoke-tested on Windows in GitHub Actions before release. GitHub Actions cannot reproduce the real Vanilla launcher/Gepard desktop, so the enhanced telemetry and launcher captures are specifically intended to make the next local GAME START test conclusive rather than speculative.
