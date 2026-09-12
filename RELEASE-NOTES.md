# 4RTools Vanilla 0.6.1

This is deliberately a patch release while the Vanilla recovery workflow is still being calibrated live.

- Integrated the reconnect/recovery manager into the main 4RTools Vanilla tab; normal use is now one application window and the original 4RTools tray icon.
- Added a single persistent user-data root under `%LOCALAPPDATA%\4RTools Vanilla\` so profiles, recovery accounts, local server definitions, logs, and DPAPI-protected passwords survive replacing/updating the application folder.
- Merged the previous `Profile` / `Profiles` split into one persistent `Profiles` root with `Stock` and `Vanilla` subfolders.
- Added first-run migration from the current folder and sibling `4RTools-Vanilla-v*` folders, preserving sibling folders as rollback backups.
- Added startup GitHub Release checks and a checksum-verified self-updater. Updates verify both the published ZIP SHA-256 and the packaged `SHA256SUMS.txt` manifest before restart.
- Added Data & updates UI showing exact persistent paths plus manual update/data-folder controls.
- Portable release packages no longer contain user-data directories.

CI validates Release compilation, offline regression tests, packaging, and portable smoke launch. Real Vanilla/Gepard UI interaction still requires local live validation and is not claimed from CI.
