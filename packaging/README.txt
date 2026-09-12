4RTools Vanilla
==============

This is an independent extension of 4RTools, not an official upstream or
Vanilla MMO release. Copyright (c) 2022 4RTools. See LICENSE.

Vanilla's documentation lists 4R Tools as supported. This fork keeps Gepard
intact: it does not disable, bypass, patch, hide from, or interfere with
Gepard Shield. Vanilla-specific recovery uses ordinary window input and
read-only observation where applicable.

One application window
----------------------
Run 4RTools-Vanilla.exe. Vanilla is the first main feature tab and contains one
integrated workspace with Recovery & relog, Automation rules, Diagnostics, and
Data & updates. The old separate reconnect-manager window and second tray icon
are no longer used. Minimize the main 4RTools window to use its normal tray icon;
closing the main window exits the application.

Recovery quick start
--------------------
1. Set Launcher EXE to Vanilla Launcher.exe or patcher.exe.
2. Leave Proxy on Tokyo unless you intentionally use another route.
3. Configure one or two account profiles with username, password, character slot,
   and the resume hotkey used by Vanilla Autobattle.
4. Save and test the steps individually when calibrating a client/UI change.
5. Enable Auto relaunch/relogin. Enable Start supervisor with 4RTools when you
   want monitoring to begin automatically whenever 4RTools starts.

Passwords are protected with Windows DPAPI and are never written to logs. DPAPI
protection is tied to the current Windows user/machine, so passwords must still
be entered once on each PC.

Persistent configuration
------------------------
User configuration is deliberately outside the versioned application folder.
The default data root is:

  %LOCALAPPDATA%\4RTools Vanilla\

It contains one Profiles root:

  Profiles\Stock\          original 4RTools profiles
  Profiles\Vanilla\        Vanilla automation-rule profiles
  VanillaReconnect\        recovery accounts/settings (reconnect.json)
  Logs\                    reconnect/automation/update logs
  supported_servers.json   locally added original-4RTools server definitions

The release ZIP contains none of those user-data folders. On first run, 0.6.1
migrates compatible data from the current application directory and from nearby
older sibling folders named 4RTools-Vanilla-v*. Sibling version folders are kept
as rollback backups. This means you can unzip 0.6.1 beside 0.6.0 and retain the
same local configuration automatically on that Windows user/PC.

Automatic updates
-----------------
Every normal startup checks the latest GitHub Release for this fork. If a newer
release exists, 4RTools offers to download and restart into it. Before applying
an update it verifies the release ZIP SHA-256 file and every file listed in the
packaged SHA256SUMS.txt manifest. User data is outside the install directory, so
updating application files does not replace profiles, recovery accounts, or
DPAPI-protected passwords.

The Data & updates page shows the exact paths in use and provides Open Data
Folder and Check for Updates controls.

Requirements
------------
Windows 10 or Windows 11 with Microsoft .NET Framework 4.7.2 or a later 4.x
runtime. The application targets x86 and can run on x64 Windows. No Visual
Studio, Git, NuGet, SDK, or source tree is required to use it.

Release identity and integrity
------------------------------
VERSION.txt records the fork version, architecture, source commit, and build
validation. RELEASE-NOTES.md records feature and validation details.
SHA256SUMS.txt lists packaged payload hashes and the ZIP has an adjacent .sha256
file. Versioned ZIPs are published by verified GitHub Releases and are not
committed to the source tree.

The MIT license for 4RTools is in LICENSE. Embedded third-party dependencies
have their own licenses/notices in THIRD-PARTY-NOTICES.txt.