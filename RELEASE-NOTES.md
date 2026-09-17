# 4RTools Vanilla 0.6.45

## Launcher foreground recovery when the desktop owns focus

The supplied 0.6.44 live log showed the exact showstopper: after the affected client exited,
the Vanilla launcher was visible but Windows `Program Manager`/desktop remained foreground.
The old GAME START path called `SetForegroundWindow`, Windows applied its foreground-lock rule,
the launcher only flashed orange on the taskbar, and the foreground-dependent click was rejected.
The user had to click the launcher manually before GAME START rendered as the active yellow button.

Launcher startup now uses the verified launcher HWND and a bounded Win32 input-queue activation
step. It temporarily joins only the current/launcher/foreground GUI input queues, requests focus
for that exact launcher window, verifies that it really became foreground, and immediately detaches.
If ownership/focus cannot be proven, no GAME START action is sent and the bounded launcher loop
retries safely. There are no taskbar/desktop coordinates.

After two stable visual GAME START detections, the single activation is now sent as a direct
client-window mouse message to the verified launcher HWND at the image-detected client point.
It does not move the machine cursor and does not depend on a global SendInput click remaining in
foreground. The existing 2.5-second launcher settle, double 750ms visual confirmation and 15-second
post-activation retry spacing remain.

## GDI+ helper window is permanently non-interactive

Vanilla creates a hidden 1x1 top-level `GDI+ Hook Window Class` named
`GDI+ Window (Vanilla MMO.exe)` before the real game window. Because its caption contains
`Vanilla MMO`, the old resolver mistakenly scored it as a real game window and called
`ShowWindow(SW_RESTORE)`, which made the strange GDI+ taskbar window visible. That helper is now
classified with the other non-interactive bootstrap windows: it is never restored, focused,
selected for visual recognition, or sent input. 4RTools waits for the real Vanilla/Gepard window.

## Validation limits

Release validation covers Debug/Release regressions for GDI-helper exclusion and bounded launcher
activation, portable launch/package checks, native test-owned process recovery and mock-data UI.
Public assets, source identity and updater discovery are verified after publication. The build
runner cannot reproduce the user's Chrome Remote Desktop foreground-lock state or run live
Vanilla/Gepard; the supplied 0.6.44 screenshots/debug log are the live evidence for the repaired
window-state paths.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.6.45. SHA256:

- `4RTools-Vanilla-v0.6.45-portable.zip`: `e9b236b64d7fe662b28f4fbd165e6714b7bab380f14392006b8fe048166e0f70`
- `4RTools-Vanilla.exe`: `48733af7a363be3bcb99567ae256062b523302ae8caac40c99acdb64431c8c4b`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
