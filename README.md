<p align="center">
  <img src="/assets/images/combo-tools.png">
</p>

# 4RTools Vanilla

An independent fork of [4RTools](https://github.com/4RTools/4RTools) that adds
Vanilla MMO to the original application. The familiar **Ragnarok Client** list,
**Profile** selector, Autopot, spammers, macros, timers, and buff controls remain
the main interface. Vanilla state access uses the read-only integration. The
**Vanilla** tab includes **Open diagnostics** and **Smart Teleport and extra
rules**; the latter opens an owned window sharing the selected client session.

Vanilla's own Autobattle remains responsible for finding monsters, movement,
and combat. The extension supplies conditional ordinary hotkeys around it.

**Current validation:** the original window detects and selects Vanilla, displays
the observed character name, and reads HP **456/456** and SP **113/113** from
matching module-relative candidates. Repeated stable reads have succeeded;
controlled changes and lifecycle checks are still required. These observations
remain marked **Unverified**, and dependent actions remain disabled. See
[release notes](RELEASE-NOTES.md) for the exact tested build, verified fields,
and actual live behavior; displaying a feature is not proof of its game effect.

## Portable application

The release workflow produces
`dist/4RTools-Vanilla-v0.2.0-portable.zip` and its unpacked folder. Extract the
whole folder and run **4RTools-Vanilla.exe**. Keep the EXE and its `.config`
together. The package includes the license, third-party notices, instructions,
version information, and SHA256 checksums.

The application targets x86 and needs Windows with Microsoft .NET Framework
4.7.2 or a later 4.x runtime. Windows 10 x64 is the local validation environment;
Windows 11 x64 has not yet been tested on a separate machine. No Visual Studio,
Git, NuGet, SDK, or source tree is required to use the portable application.

Run the EXE to open the original 4RTools window. Select your Vanilla instance in
**Ragnarok Client**, choose your **Profile**, and configure the existing feature
tabs as usual. In **Vanilla**, use **Smart Teleport and extra rules** for the
additional settings or **Open diagnostics** to inspect state. The application
starts OFF. Additional rules start with dry-run enabled and have an emergency
stop key, **Pause** by default. Original and extra automation cannot run together.

Original settings remain in `Profile/`; additional automation settings use
`Profiles/`. Both are relative to the application folder. The distribution does
not contain personal development profiles. The new `0.2.0` artifact uses its own
folder, preserving the previous `0.1.0` release and any settings within it.

Known build definitions are local, so the application can identify supported
client builds offline. Unrecognized builds, stale data, and unavailable required
fields stop dependent automation. Users do not enter offsets or process IDs to
make an unknown build appear supported.

## Vanilla integration

- Vanilla process enumeration in the original Ragnarok Client selector.
- Reuse of original feature controls and configuration through the read-only
  Vanilla state provider; individual features require their corresponding
  gameplay data to be verified.
- Read-only connection, executable fingerprint, architecture checks, optional
  state observations, timestamps, diagnostics, and snapshot export.
- Stable buffered readouts and state cells updated only when their values change.
- Smart Idle teleport with separate no-target and no-combat timers, cooldown,
  grace period, and optional coordinate-based stuck recovery.
- Explicit Fixed Interval mode with the available state checks and its own
  interval. It cannot guarantee combat avoidance when combat state is unknown.
- SP threshold recovery with a configurable key/wait/click sequence, cooldown,
  out-of-combat option, and Test Once.
- An additional-rule editor for timed, HP/SP, status present/missing, no-target,
  no-combat, and stationary conditions, with per-rule sequences and cooldowns.
- Dry-run, global ON/OFF, emergency stop, disconnect handling, and validated
  portable settings. Original and extra automation are mutually exclusive, and
  only one extra action sequence runs at a time.

The rules require independently verified state. Features listed here describe
the implementation; their actual live validation is recorded in
[RELEASE-NOTES.md](RELEASE-NOTES.md). The Vanilla integration does not write game memory,
inject code, manipulate packets, or bypass Gepard. A blocked operation is
reported and stopped.

## Original 4RTools

The original 4RTools interface is the application opened at normal startup.
Vanilla is integrated into its existing client selection and read methods;
other servers retain their existing support. The fork does not use the upstream
self-updater to replace its executable.

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

# Build/test the explicit x86 fork release configuration.
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
