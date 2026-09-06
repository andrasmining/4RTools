<p align="center">
  <img src="/assets/images/combo-tools.png">
</p>

# 4RTools Vanilla Companion

An independent extension of [4RTools](https://github.com/4RTools/4RTools) for
Vanilla MMO. Vanilla's own Autobattle remains responsible for finding monsters,
movement, and combat. The companion adds client diagnostics, profiles, and
guarded rules for teleporting and SP recovery through ordinary hotkeys.

**Current live limitation:** client identity and bounded read-only memory access
have been demonstrated, but gameplay fields have not yet been verified. Rules
requiring that state remain disabled. See [release notes](RELEASE-NOTES.md) for
the exact tested client build, completed checks, and untested behavior. A
successful build or visible UI is not evidence that gameplay automation works.

## Portable application

The release workflow produces
`dist/4RTools-Vanilla-v0.1.0-portable.zip` and its unpacked folder. Extract the
whole folder and run **4RTools-Vanilla.exe**. Keep the EXE and its `.config`
together. The package includes the license, third-party notices, instructions,
version information, and SHA256 checksums.

The application targets x86 and needs Windows with Microsoft .NET Framework
4.7.2 or a later 4.x runtime. Windows 10 x64 is the local validation environment;
Windows 11 x64 has not yet been tested on a separate machine. No Visual Studio,
Git, NuGet, SDK, or source tree is required to use the portable application.

In the companion, select a running Vanilla client and a profile, review the
connection/state status, and configure the desired rules through the UI.
Profiles can be saved, imported, and exported without editing JSON. New profiles
start with dry-run enabled and automation OFF. The global OFF control and
configurable emergency hotkey cancel pending actions. The default emergency
key is **Pause**.

Known build definitions are local, so the companion can identify supported
client builds offline. Unrecognized builds, stale data, and unavailable required
fields stop dependent automation. Users do not enter offsets or process IDs to
make an unknown build appear supported.

## Vanilla functionality

- Automatic process enumeration and explicit selection among running clients.
- Read-only connection, executable fingerprint, architecture checks, optional
  state observations, timestamps, diagnostics, and snapshot export.
- Smart Idle teleport with separate no-target and no-combat timers, cooldown,
  grace period, and optional coordinate-based stuck recovery.
- Explicit Fixed Interval mode with the available state checks and its own
  interval. It cannot guarantee combat avoidance when combat state is unknown.
- SP threshold recovery with a configurable key/wait/click sequence, cooldown,
  out-of-combat option, and Test Once.
- An additional-rule editor for timed, HP/SP, status present/missing, no-target,
  no-combat, and stationary conditions, with per-rule sequences and cooldowns.
- Dry-run, global ON/OFF, emergency stop, disconnect handling, and validated
  portable settings. Only one action sequence runs at a time.

The rules require independently verified state. Features listed here describe
the implementation; their actual live validation is recorded in
[RELEASE-NOTES.md](RELEASE-NOTES.md). The companion does not write game memory,
inject code, manipulate packets, or bypass Gepard. A blocked operation is
reported and stopped.

## Original 4RTools

The original 4RTools interface and feature configuration remain available from
the companion. Its supported-server behavior remains separate from Vanilla
client discovery. The fork does not use the upstream self-updater to replace
its executable.

Upstream 4RTools is an all-in-one tool for **Ragnarök Online** servers, providing
Autopot, skill spam, macro songs, and other configurable actions. These are
upstream features; listing them does not imply they have been tested against
Vanilla with this modified release.

<img src='assets/images/ragnarok-icon.png' width='40'>

### Upstream features

- [x] ON/OFF Button (with shortcut key)
- [x] Autopot
- [x] Autobuff status
- [x] Manage Profiles
- [x] AHK Spammer
- [x] Auto Refresh Spammer
- [x] Autobuff Stuffs
- [x] Autobuff skills
- [x] Song Macro
- [x] Macro Switch/Macro Chain
- [x] ATK x DEF Mode switch

## Build and release engineering

Maintainers can use the existing Visual Studio 2022 solution or the repository
PowerShell scripts. These steps are not required to run a portable release.

```powershell
# Restore, rebuild Release and Debug, and run offline tests.
& .\scripts\build.ps1

# Build/test the explicit x86 companion release configuration.
& .\scripts\build.ps1 -VanillaRelease

# Rebuild/test Release, package, verify a relocated launch, and create checksums.
& .\scripts\build-release.ps1
```

MSBuild and a .NET desktop build environment are needed only for development.
The build script uses the existing .NET Framework 4.7.2 target and a pinned
Microsoft reference package if the targeting pack is missing. NuGet dependencies
are restored at build time and embedded with Costura/Fody. Application data is
relative to the executable folder; caches and validation logs are separate.

The release script requires a matching executable version and packages only the
intended executable/config, tracked client build definitions, and distribution
assets. It creates a new ZIP, verifies checksums, and launches an extracted copy
inside the repository. `-Replace` preserves any old release folder and profiles
under `dist/.previous/` before publishing the replacement.

[The diagnostics engineering report](docs/vanilla-diagnostics.md) records the
earlier baseline audit and read-only investigation. Current behavior and release
validation take precedence in [the release notes](RELEASE-NOTES.md).

## Attribution and license

This fork is independent of upstream 4RTools and Vanilla MMO. The unchanged
[MIT license](LICENSE) includes `Copyright (c) 2022 4RTools`. Third-party runtime
components retain their own [licenses and notices](packaging/THIRD-PARTY-NOTICES.txt).

Upstream community links:

- [Website](https://www.4rtools.com.br/)
- [Discord](https://discord.gg/HRWvG5ut)

### References

https://github.com/k1ngJ/dtAP

### Upstream collaborators

<a href="https://github.com/4RTools/4RTools/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=4RTools/4RTools" />
</a>
