# 4RTools Vanilla 0.6.35

## Verified autobattle startup and recovery

- Startup, recovery and the resume diagnostic now share a bounded movement verifier. After the configured hotkey, fresh validated X/Y samples are observed throughout a 10-second window; a change on either axis succeeds immediately, including movement that returns to its initial position later.
- No movement triggers another focused hotkey attempt, with a fresh baseline and a final movement check after refocusing. There are **three total attempts** (initial send plus two retries), not three extra retries. After the third unsuccessful window, the account reports failure and later gameplay polls cannot restart the same retry budget.
- Cold startup holds the current client's input lease until movement verification and minimization complete. Later queued clients do not launch after a failed cold start. Continuous recovery likewise keeps input serialized through verification. Already-running adopted clients remain untoggled.
- The existing account status column shows compact verification/retry progress, with detailed reasons in hover text and logs. Automatic minimization excludes a client while verification is still in progress.
- STOP, configuration changes, replaced processes/sessions/characters, map transitions, dead characters, unknown or stale coordinates, failed reads and loss of foreground focus prevent additional keys. Monotonic deadlines and operation generations prevent clock changes or an old worker from restarting or completing a newer attempt. Modified chord dispatch releases held keys even when cancellation or an input error interrupts it.
- The verified, fingerprint-matched read-only state provider and existing X/Y/map mappings are reused. No game-memory writes, offset guesses, packet actions, injections or security bypasses are introduced. Existing settings, profile storage, two-active-client limit, packaging and updater behavior are preserved.
- Repository policy now explicitly requires end-to-end ownership through a published, verified release rather than stopping at a commit, push or queued workflow.

## Validation and limits

Publication requires the existing standard Windows runner gates: shipped-profile validation, Debug and Release builds with the full offline regression suite, portable ZIP construction and checksum verification, inert Windows executable launch smoke test, and all 18 native mock-data UI scenarios. New deterministic tests cover the three-attempt state machine, timing, movement, cancellation, state validity, client/operation isolation, failure latching, account progress and held-key cleanup. No test needs a live game process or real account credentials.

These automated Windows build, package and mock-data UI checks are **not live Vanilla/Gepard gameplay or RDP testing**. The new movement check has not been exercised against a live game client in this development session. Movement is not proof of combat or of why the character moved; conversely, a character fighting while stationary for all three windows can fail this deliberately movement-based check. Existing legacy dependency/compiler warnings remain visible and are not suppressed by this patch.
