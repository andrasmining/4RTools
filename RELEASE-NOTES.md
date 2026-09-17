# 4RTools Vanilla 0.6.46

## Immediate minimize after verified Autobattle movement

A recovery/restart client no longer waits through the 60-second visible/cursor-idle grace after
the configured Autobattle hotkey has already been proven by fresh X/Y movement. Once movement
verification succeeds, 4RTools immediately minimizes that same owned client, confirms it is
minimized, completes the recovery, and releases the serialized gate for the next queued client.

The 60-second user-presence grace is still retained for adopted/already-running clients and normal
steady-state minimization where there is no fresh restart-owned movement handshake. Manual explicit
minimize remains immediate. If you need to actively interact with a client while supervised recovery
is running, pause or stop supervision first; recovery automation otherwise owns the restarted client
through movement verification and minimization.

This applies consistently to normal recovery login, sequential cold startup, and the shared
restart-only ResumeHotkey recovery path. It does not add any new hotkey sends, process access, game
memory writes, coordinate clicks, or parallel recovery behavior.

## Validation limits

Release validation covers the new immediate-after-movement policy and the retained 60-second grace
for ordinary/adopted clients, full Debug/Release regressions, portable package/launch checks, native
test-owned process recovery, and mock-data UI validation. Public assets, source identity, and updater
discovery are verified after publication. The Windows runner cannot reproduce the user's live RDP
session or run Vanilla/Gepard, so live in-game behavior remains limited to the user's supplied log
showing successful X/Y movement followed by the unnecessary 60-second wait that this release removes.

<!-- BEGIN GENERATED RELEASE CHECKSUMS -->

Release version: 0.6.46. SHA256:

- `4RTools-Vanilla-v0.6.46-portable.zip`: `7e1238f2869a503f695dd4d297c7eb49e33e5e164086bf993e1c8744d961957e`
- `4RTools-Vanilla.exe`: `6121ac0ab03070fbf9ce34239511cdfcb6803c007ea4a955cbd805347d73d146`

These generated hashes are excluded from the packaged notes to avoid a circular ZIP checksum.
<!-- END GENERATED RELEASE CHECKSUMS -->
