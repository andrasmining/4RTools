# 4RTools Vanilla 0.6.37

## Character roster

The recovery table now represents characters rather than login accounts. Its
identity columns are Description, Username, Slot and Character name, after Enabled
and before the existing hotkey, password, proxy, PID and status columns. Multiple
characters can share one username while retaining independent slots, descriptions
and recovery preferences. At most two rows may be enabled.

The character editor exposes the same fields and offers freshly observed names.
Empty slots are unknown, never silently slot 1. Passwords and proxies remain
user-controlled. Disabled incomplete profiles can be saved; missing login
configuration prevents unattended launch.

## Automatic discovery and safe ownership

Startup and subsequent fresh fleet observations add unlisted running characters
automatically, once, disabled. The existing fingerprinted read-only reader supplies
identity fields; no extra memory reader or guessed offsets are introduced. Only
missing verified fields are filled; saved descriptions, enabled flags, slots,
passwords and proxies are not overwritten.

Existing processes are assigned by verified character identity rather than PID
or start order. A username alone cannot identify a character. Ambiguous, stale or
contradictory observations remain unassigned. Character/session changes prevent
old workers from sending input or closing a replacement character. The expected
name is checked before startup Autobattle hotkeys. A successful tool-owned
configured login can learn the name for that exact legacy row.

Passive discovery does not cancel recovery. Row edits, disabling, STOP and disposal
cancel stale operations. Detection preserves failed-state latches and the
close-to-relaunch lease. The existing 120-second movement watchdog, terminal
dialog handling and sequential two-client recovery remain intact.

## Persistence and validation

The full character catalog is authoritative; a stale two-row runtime subset
cannot overwrite newer fields or resurrect removed rows. Historical JSON/file
names, IDs, encrypted passwords and proxy associations remain compatible. Atomic
saves preserve the previous file; malformed/future catalogs are not replaced
with incomplete fallbacks.

Release gates include Windows Debug/Release builds and the complete offline suite,
character discovery/persistence/ownership regressions, portable package launch and
hash checks, native test-owned process recovery, and native UI checks for the 18
existing layouts plus actual character discovery and the character editor. Public
assets and exact clean source identity are verified after publication; temporary
transport files and the completed working branch are removed.

## Explicit limits

The current shipped Vanilla memory profile verifies character names, not login
usernames or one-based character slots. Optional trusted-field support is included,
but values remain unknown/editable unless configured or available from a verified
mapping. They are not inferred from names, PID order or account rows. Fully
automatic username/slot discovery on the current client is not claimed: live
client evidence needed to map those two fields is unavailable in this session.

No live Vanilla/Gepard client or user RDP desktop was available. Offline/native
Windows checks do not prove live game behavior. Existing dependency/compiler
warnings remain visible. No game-memory writes, injection or protection bypass.
