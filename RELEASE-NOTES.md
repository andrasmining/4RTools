# 4RTools Vanilla 0.6.53

## Weight / Cart category traversal hardening

This release fixes the live Cart-maintenance failure observed after the **Use** category had been cleared.

The previous implementation derived the Use/Equip/Etc click from relative panel coordinates. That could select the wrong vertical tab on another client layout/DPI and leave Autobattle stopped in an uncertain UI state.

Category switching is now fully vision-driven:

- Detect the current Inventory panel and slot lattice.
- Detect the four vertical category tabs from the live separator structure beside the detected slot grid.
- Click only the detected target-tab bounds.
- Positively verify the selected-tab highlight on two fresh captures before any item drag.
- Scan the selected category's detected slot grid.
- When no occupied item is found, require **two fresh empty-grid scans** before declaring that category complete and advancing to the next enabled category.
- Continue through Use / Equip / Etc according to the Weight-tab switches.

Cart destinations are also stricter: every drag now requires a positively detected empty Cart slot. The old calculated fallback drop point has been removed. If no empty Cart slot is detected, the character is held safely with Autobattle OFF instead of guessing.

## Live diagnostics

Every major Weight/Cart step is now mirrored into the visible **Recovery & relog Log** as well as the global debug log: lease acquisition, Autobattle STOP, Inventory/Cart detection, category detection/selection verification, first empty scan, confirmed empty-category advance, item movement, quantity-dialog handling, completion, cancellation and failure/manual-hold reasons.

This makes unattended Cart behavior directly visible without having to infer progress from the game window.

## Validation scope and limits

Automated tests cover category-rail detection at different panel locations/scales, selected-tab recognition with selected boundaries visually obscured, empty vs occupied slot recognition, strict detected-empty Cart destinations, Debug/Release builds, portable packaging/launch, native recovery checks and the responsive UI suite.

The detector design was additionally checked against the supplied live screenshot structure, where the Inventory showed the Favorite tab selected after the Use transfer. The engineering environment still cannot execute clicks inside the user's live Vanilla/Gepard session, so the final live end-to-end category switch remains a VPS observation boundary. No game-memory writes, injection, packet manipulation or Gepard bypass is used.
