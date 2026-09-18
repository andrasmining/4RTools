# 4RTools Vanilla 0.6.48

## Per-character Weight / Cart switch

Weight automation is now independently switchable on every saved character row. The Recovery & relog character table shows a **Weight** column, and the character editor has a **Weight / Cart** checkbox. The shared Weight-tab thresholds, inventory categories, Inventory/Cart hotkeys and optional e-mail configuration are master settings; they apply only to characters whose per-character Weight switch is enabled.

Existing character profiles default to Weight enabled so an upgrade from 0.6.47 does not silently disable a previously enabled global Weight policy. Turning Weight off for one character suppresses both automatic Cart maintenance and weight e-mail actions for that character while leaving verified live weight visible. The other managed character is unaffected.

Changing the character switch while Cart maintenance owns that client cancels the outstanding Cart operation at its next ownership/cancellation check. A disabled character cannot acquire a new Weight/Cart input lease. Identity discovery/enrichment, relogging and account/character cloning preserve the per-character switch.

The 0.6.47 UI-only Cart safety rules remain unchanged: inventory/cart memory is never read or written; Enter is sent only after positive quantity-dialog recognition, so a quantity-one drag never receives Enter; uncertain geometry or Cart refusal fails closed into manual hold with Autobattle left OFF.

## Validation limits

Release validation covers per-character serialization/clone persistence, identity enrichment preserving the disabled switch, character-editor/table rendering, Debug/Release regression suites, native recovery checks, responsive mock UI, portable package/launch integrity, public release identity and updater discovery. The engineering environment still cannot execute a real Cart drag-and-drop inside the user's live Vanilla/Gepard session, so live transfer behavior remains dependent on the fail-closed UI recognizers and the user's supplied screenshots.
