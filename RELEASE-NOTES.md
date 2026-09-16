# 4RTools Vanilla 0.6.41

## Guaranteed post-login Autobattle start

A successful login/relog no longer depends on a later supervisor poll to start
Autobattle. After gameplay is stably detected, the client settles for 7 seconds,
then the configured Autobattle/slave hotkey is always sent. Fresh verified X/Y is
observed for 10 seconds. If the character does not move, the intended client is
revalidated and the hotkey is sent again, for three attempts total.

Logs now expose the post-login settle, every `Sending autobattle hotkey attempt X/3`
message and each 10-second X/Y verification window. The same sequence is used by
cold startup and normal recovery/relogin.

## 30-second hotkey-first movement recovery

The online autofarming movement watchdog is reduced from 120 seconds to 30 seconds.
Unchanged, missing, unreadable, unverified and stale X/Y do not reset the deadline.
When the watchdog fires, it first invokes the same three-attempt hotkey verifier;
it does not immediately restart the process. A recognized terminal disconnect/logout
can still request replacement sooner after its existing confirmation rules.

If all three hotkey attempts fail, only the affected client is restarted under the
global recovery lease. The replacement repeats login -> 7-second settle -> up to
three hotkeys -> verified X/Y movement -> minimization. Another failed client waits
until that complete sequence finishes; healthy siblings are untouched.

## Bounded client restart budget

One movement-failure incident permits at most three client restarts. A successful
verified movement observation clears the incident and resets the restart budget.
If verified movement cannot be established after the third replacement cycle, the
supervisor stops completely, records a terminal failure, and sends no further
automatic hotkeys or restart requests until the user manually starts it again.
Manual Start resets the bounded incident state.

Existing process/PID/session/character ownership checks, STOP/configuration
cancellation, read-only memory boundaries, terminal-dialog handling, username +
character identity, and resolution-independent keyboard character selection remain.

## Validation and limits

Release validation covers Debug/Release regression suites, the three-hotkey verifier,
30-second watchdog behavior, three-restart exhaustion and reset, sequential dual-client
recovery, cancellation/ownership boundaries, native test-owned process recovery,
portable launch/package checks and native mock-data UI checks. The public release ZIP,
checksum and exact source identity are verified after publication, and the previously
published executable's real updater must discover this Latest release before cleanup.

No live Vanilla/Gepard/RDP client was available to this engineering session. The
behavior is therefore validated by Windows/runtime/state tests and packaging, not by
an independent live gameplay login. Existing dependency/compiler warnings remain
visible.
