# 4RTools Vanilla 0.6.0

- Launcher GAME START clicks now target the child control under the visible button instead of only the top-level launcher window.
- Login entry explicitly overwrites username first, Tabs to password, overwrites password, then submits. Legacy bad login-field anchors are migrated automatically.
- Resume hotkeys use verified foreground ordinary Windows input so modifier combinations such as Alt+2 go to the selected Vanilla client.
- Added seven selected-account step tests: GAME START, proxy, fill credentials without submitting, submit login, server, character, resume hotkey.
- Added hover documentation for reconnect controls and tests.
- Added OPEN LOG and COPY LOG.
- X exits 4RTools; minimizing the reconnect window hides it to the tray. Tray menu includes Exit 4RTools.

CI validates compilation, tests and portable packaging. Real Vanilla/Gepard interaction remains a local live test and is not claimed from CI.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.6.0. SHA256:

- `4RTools-Vanilla-v0.6.0-portable.zip`: `5b1891f932eae5f3f8844fb0c90375623e84fee195c8c1f909dacb993538d913`
- `4RTools-Vanilla.exe`: `0814f665c311232553a9ad25e624d900a3ccbcc59426e5231d4280eaef31d2f4`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
