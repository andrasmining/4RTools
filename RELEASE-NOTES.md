# 4RTools Vanilla 0.6.40

## Resolution-agnostic character selection

Character selection no longer clicks configured character-grid or GAME START
coordinates. After the character screen is stably detected and foreground ownership
is verified, the client is driven only with keyboard navigation: Up/Left clamp the
selection to the top-left card, Right/Down move to the configured one-based slot,
and Enter starts that character. The same path is used for cold startup and recovery.
This removes the resolution/DPI failure that could click an empty card and open the
new-character flow.

Every one of the 15 target slots is regression-tested from every possible initial
selection. Existing read-only username + character-name checks still verify the
actual gameplay identity before the Autobattle resume hotkey; a mismatch fails closed.
The supplied incident screenshot showed slot 2 required while a coordinate click landed
on an empty card, which is the regression this release removes.

## Username reader and diagnostics

The shipped fingerprinted build profile contains both user-supplied username
locations: main module + `0xD343F8` and + `0xD39159`. At the supplied module base
`0x00400000`, these resolve to `0x011343F8` and `0x01139159` respectively. They are
read through the existing read-only source as `UserName` and `UserNameMirror`, not
through an extra reader, a scan, or a protection workaround.

Both fields appear in diagnostics and snapshot exports with values, addresses,
validation and provenance. Both bounded UTF-8 copies must be NUL-terminated and
agree in the same sample before username is used for identity. Missing, invalid,
unverified or disagreeing copies remain unusable for identity; raw diagnostic
values are retained. A failed memory read still stops the reader without retrying
another access method. No passwords are read from game memory.

## Composite character identity and legacy migration

The logical unique key is **username + character name** throughout discovery,
validation, process matching and removal suppression. Persistent GUIDs remain
unchanged for compatibility with encrypted passwords and per-row proxy settings.
Several characters may share a username; the same name on different usernames
also remains separate. Missing usernames defer automatic row creation rather than
creating new username-less rows.

A configured legacy row with no character name is enriched in place when one
compatible row and one fresh observed character unambiguously match its username.
An unavailable memory slot no longer forces another row. Its description, configured
slot, password, proxy, enabled flag and ID are preserved. Ambiguous observations,
multiple legacy candidates and contradictory known slots are not guessed.

Untouched empty name-only discovery duplicates left by 0.6.37 can be folded into
the matching configured row. Profiles with edited descriptions, credentials, slots,
proxies, hotkeys or enabled flags are never automatically deleted. Repeated discovery
and subsequent application reloads do not recreate the folded row.

Transient unknown usernames do not fabricate character replacement or erase the
existing recovery owner. An actually changed username cancels old ownership.
Startup hotkeys validate the expected username even for legacy rows whose character
name is being learned. Existing two-client limits, sequential recovery, terminal
dialog handling, the 120-second movement watchdog and startup movement checks remain.

## VPS updater delivery

Releases are explicitly marked Latest. Publication checks reject downgrades,
verify the public latest endpoint, and run the real updater from the previously
published executable to confirm discovery of the new version and its ZIP/checksum
URLs. The disposable GitHub Actions validation injects its workflow token only into
the updater probe so shared-runner anonymous API limits cannot invalidate a release;
normal VPS/end-user update checks remain anonymous and unchanged.

## Validation and limits

Publication is gated on full Windows Debug/Release regression suites, shipped
profile validation, x86/version and portable-package checks, an inert executable
launch, native test-owned process recovery, and native UI rendering. Regression
coverage includes all 15 character slots from all possible initial selections,
the supplied username offsets through the shared reader/adapter, relocated modules,
copy conflicts, null/truncated values, two-client isolation, username/name key
collisions, legacy migration, credential preservation and stale ownership.

The public release ZIP/checksum and exact clean source identity are verified after
publication. The previously published executable's real updater must discover this
Latest release before completed task branches are removed.

The character-selection fix is validated with deterministic input/state tests and
Windows packaging/UI checks, not a live Vanilla character-selection session. No live
Vanilla/Gepard client or user RDP desktop was available in this engineering session.
Character-slot memory mapping remains unavailable; configured slots are retained.
Existing dependency/compiler warnings remain visible.
