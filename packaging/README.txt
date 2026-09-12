4RTools Vanilla
==============

This is an independent extension of 4RTools, not an official upstream or
Vanilla MMO release. Copyright (c) 2022 4RTools. See LICENSE.

Vanilla's documentation lists 4R Tools as supported. This fork keeps Gepard
intact: it does not disable, bypass, patch, hide from, or interfere with
Gepard Shield. Vanilla-specific recovery uses ordinary window input and
read-only observation where applicable.

Quick start: overnight restart/relogin
--------------------------------------
1. Unzip the entire portable folder into a writable location.
2. Run 4RTools-Vanilla.exe and accept its Windows administrator prompt.
3. The Vanilla Restart & Relog manager opens on first use. Set the launcher path
   to Vanilla's patcher.exe. If a client is already running, choose "Use patcher
   from running client"; it prefers patcher.exe beside Vanilla MMO.exe.
4. Leave Proxy on Tokyo unless you intentionally use another route.
5. Configure up to two account profiles on this PC. For each one enter the
   username, password, character slot, and Autobattle resume hotkey (default
   Ctrl+2).
6. Save, enable "Auto relaunch/relogin", and press START SUPERVISOR.
7. Once verified locally, enable "Start supervisor with 4RTools" for unattended
   recovery after a 4RTools restart.

When patcher.exe is configured, the supervisor starts it, waits for its window,
clicks the visible GAME START button, and stops retrying as soon as a new Vanilla
MMO process appears. Direct executable launching remains supported.

The supervisor can relaunch a closed Vanilla client, wait for the Gepard/client
startup path, choose the configured proxy, login, select the first Vanilla
server entry, select the configured character slot, enter the game, and send
the configured Autobattle hotkey once gameplay is detected. It also watches for
the bright Vanilla login/service shell after a lag disconnect and can acknowledge
the observed in-game logout/disconnect modal with Enter.

Passwords are protected with Windows DPAPI and never written to the reconnect
log. DPAPI protection is intentionally tied to the current Windows user/machine,
so enter passwords once on each PC; copying the portable folder does not make a
saved password usable on another machine.

The UI uses normalized client coordinates rather than fixed pixels, so the same
profile is not tied to one screen resolution. It still assumes the same Vanilla
UI layout. If Vanilla changes that layout, stop the supervisor and update/test
the fork rather than trying to bypass Gepard.

Existing 4RTools/Vanilla features
---------------------------------
The original 4RTools window, Ragnarok Client selector, profiles, Autopot/Ygg,
AHK spammer, macro chains/songs, Auto Refresh timers, buffs, status recovery,
and settings remain available.

The Vanilla tab also provides Smart Teleport/additional rules and read-only
diagnostics. Current HP/SP/name mappings for the known Vanilla build are still
candidate/unverified mappings for gameplay semantics; dependent automation stays
gated until controlled live verification proves them. The restart/relogin
manager does not depend on those offsets.

Requirements
------------
Windows 10 or Windows 11, with Microsoft .NET Framework 4.7.2 or a later 4.x
runtime. The application targets x86 and can run on x64 Windows. No Visual
Studio, Git, NuGet, SDK, or source tree is required to use it.

Profiles and local data
-----------------------
Original 4RTools settings use Profile/. Additional automation settings use
Profiles/. Restart/relogin settings use VanillaReconnect/. Logs use Logs/.
All paths are relative to the portable application folder.

No repository release contains your usernames, passwords, personal profiles,
or memory dumps. The password field stored under VanillaReconnect/ is DPAPI
ciphertext for the local Windows user/machine.

Release identity and integrity
------------------------------
VERSION.txt records the fork version, architecture, source commit, and build
validation. RELEASE-NOTES.md records feature and validation details.
SHA256SUMS.txt lists original packaged payload hashes and the ZIP has an adjacent
.sha256 file. Versioned ZIPs are published by verified GitHub Releases and are not
committed to the source tree.

The MIT license for 4RTools is in LICENSE. Embedded third-party dependencies
have their own licenses/notices in THIRD-PARTY-NOTICES.txt.
