# 4RTools Vanilla 0.6.51

## Dedicated Autobattle STOP for Weight / Cart maintenance

Weight/Cart cleanup now has its own persisted **Autobattle STOP** hotkey. The default is **Alt+3**, and it can be changed directly in the Weight tab with the same live key-capture behavior used by the Inventory and Cart hotkeys.

The previous implementation incorrectly reused the character's Recovery **ResumeHotkey** before Cart work. That was unsafe when Vanilla uses different commands to stop and start Autobattle. Cart maintenance now sends the dedicated Weight STOP chord before opening Inventory/Cart, and the existing per-character Recovery ResumeHotkey is used only after cleanup to start Autobattle again and verify fresh X/Y movement.

Existing Weight settings created by older releases migrate without manual editing: because the STOP field did not exist in those JSON files, it receives the new safe default **Alt+3**. Custom STOP hotkeys are persisted independently from Resume, Inventory and Cart hotkeys.

The manual **TESTS → Weight/Cart clean now (selected)** action uses the same production path, so it also sends the dedicated STOP command before UI manipulation.

## Validation scope and limits

Automated validation covers default/migration behavior, invalid STOP-key rejection, independence from the per-character ResumeHotkey, clone/serialization persistence, native Weight-panel rendering with the visible Alt+3 default, Debug/Release regressions, portable packaging/launch, native recovery checks, responsive UI checks, public release/source/checksum verification and updater discovery.

The engineering environment cannot run the user's live Vanilla/Gepard client. The actual Alt+3 effect therefore still requires live observation on the VPS, but the release uses ordinary owned-window keyboard input only and retains the existing fail-closed Cart/UI safeguards. No game-memory writes, injection, packet manipulation or Gepard bypass is used.
