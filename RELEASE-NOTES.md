# 4RTools Vanilla 0.6.47

## UI-only Weight / Cart management

The former Alerts page is now **Weight**. It keeps the optional e-mail alert and adds automatic
Cart maintenance driven by the existing verified read-only `CurrentWeight` / `MaxWeight` state.
The default Cart trigger is 50% carried weight and the re-arm threshold is 40%; both are
configurable. Use, Equip and Etc categories can be selected independently (Equip defaults off),
and Inventory/Cart hotkeys are configurable.

Cart maintenance does **not** read or write inventory memory. It acquires the same globally
serialized per-client input ownership used by recovery, toggles the character's configured
Autobattle/slave hotkey OFF, opens Inventory and Cart through their configured UI hotkeys,
detects the resulting panels and slot lattice from the current client image, and transfers items
with ordinary drag-and-drop. Screen coordinates are derived from the detected client/panel/slot
geometry rather than fixed desktop positions. The existing shared fleet reader supplies weight
and post-action movement verification; no second gameplay memory reader is opened.

Stack quantities are handled fail-closed. `Enter` is sent **only** when a short/wide quantity
dialog with the focused numeric selection is positively detected after a drag. A quantity-one
item produces no quantity dialog, so no Enter is sent. Missing or ambiguous dialog evidence never
authorizes Enter. A late/uncleared quantity dialog, failed drag, missing safe UI geometry, lost
ownership, or cart refusal stops further input.

After all selected categories are processed, 4RTools closes the detected Inventory/Cart panels,
resumes Autobattle using the exact shared three-attempt ResumeHotkey verifier, requires fresh X/Y
movement, and then minimizes the client. If the Cart cannot accept another item or progress cannot
be verified, Autobattle remains OFF and **only that character** enters a manual Cart hold so the
user can empty/inspect it; recovery does not restart that held character. The Weight page provides
a `CLEAR MANUAL CART HOLD` action after the user has corrected the Cart. Healthy sibling clients
remain untouched.

## Validation limits

Release validation covers settings persistence/validation, synthetic Inventory slot recognition,
positive-only quantity-dialog recognition (including a regression that ordinary blue/white game
UI cannot authorize Enter), Debug/Release regressions, native recovery checks, responsive mock UI,
portable launch/package integrity, public release identity, and updater discovery. The engineering
environment cannot run the user's live Vanilla/Gepard client or perform real Cart drag-and-drop, so
the supplied screenshots establish the UI shapes but final live transfer behavior is not claimed
as independently validated. Any uncertain live UI state fails closed and leaves the affected
character for manual inspection rather than continuing blind input.
