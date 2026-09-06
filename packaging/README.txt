4RTools Vanilla
==============

This is an independent extension of 4RTools, not an official upstream or
Vanilla MMO release. Copyright (c) 2022 4RTools. See LICENSE.

The current Vanilla build is recognized. Its observed character name and HP/SP
values are displayed from candidate mappings, but controlled-change and
lifecycle verification remain outstanding. The values are marked Unverified,
and dependent actions stay disabled. See RELEASE-NOTES.md.

Run
---
1. Unzip the entire portable folder into a writable location.
2. Run 4RTools-Vanilla.exe and accept its Windows administrator prompt.
3. Start Vanilla and select its instance in the original Ragnarok Client list.
4. Choose your Profile and configure the existing Autopot, spammer, macro,
   timer, or buff tabs as usual.
5. In the Vanilla tab, choose Smart Teleport and extra rules to open the extra
   settings, or Open diagnostics to inspect observations. The extra settings
   window shares the original application's selected client session.

No Visual Studio, Git, NuGet, SDK, source tree, or offset entry is required.
The EXE and its .config must remain together. Managed dependencies are embedded.
Copy the entire folder, including your saved profiles, when moving to another PC.
The original 4RTools window is the main application. Vanilla extends its client
selection and state access; the additional controls do not replace that window.

Requirements
------------
Windows 10 or Windows 11, with Microsoft .NET Framework 4.7.2 or a later 4.x
runtime. The application targets x86 and can run on x64 Windows. No .NET SDK is
needed. On a machine missing the required framework, Windows/.NET will display
a runtime installation message before the application starts. Obtain any needed
runtime only from Microsoft:
https://dotnet.microsoft.com/download/dotnet-framework

The upstream administrator manifest is retained. See RELEASE-NOTES.md for the
operating systems and permissions actually tested for this release.

Profiles and local data
-----------------------
Profiles, build definitions, and logs are local to the application folder.
Original 4RTools settings use Profile/. Additional automation settings use
Profiles/. Both directories are initially empty; the application creates its
defaults when first run. Existing releases are separate folders and are not
overwritten by a differently numbered release.
No personal development profile, account information, or memory dump is included.
Known, verified client definitions can be packaged locally for offline use.
An unknown client build or unavailable required state disables dependent rules.
See RELEASE-NOTES.md for the exact verified state and remaining limitations.

Automation
----------
Vanilla's own Autobattle controls movement and combat. Vanilla integration uses
read-only observations and ordinary hotkeys; it does not alter game memory.
Use the original ON/OFF control for original 4RTools features. Additional rules
offer dry-run and their own OFF/emergency-stop controls. Original and extra
automation cannot run at the same time. Keep actions disabled when the
application reports that their required state is unavailable or unverified.

Release identity and integrity
------------------------------
VERSION.txt records the fork version, architecture, source commit, and build
validation. RELEASE-NOTES.md records actual live validation. SHA256SUMS.txt lists
the original packaged files. The ZIP has an adjacent .sha256 checksum file.
User-created profiles and logs naturally are not part of the original checksums.

The MIT license for 4RTools is in LICENSE. Embedded third-party dependencies have
their own licenses and notices in THIRD-PARTY-NOTICES.txt.
