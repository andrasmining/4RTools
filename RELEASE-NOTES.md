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

- `4RTools-Vanilla-v0.6.0-portable.zip`: `fd095222a92f119cc213923b3fad94a713dd1ca1dbf4f28ac3d19f3e225e1d80`
- `4RTools-Vanilla.exe`: `0d86cda483d9bf674996fff216c7b3480ce5a425d8b166c708adc85e1a7a03d3`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
