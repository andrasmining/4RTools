# 4RTools Vanilla 0.6.55

## Correct active Inventory tab detection

The v0.6.54 live test showed that the **Use** tab had actually been selected correctly, but 4RTools still reported **Favorite** and rejected the action.

The cause was visual semantics: Vanilla renders **Fav with a blue background even when Fav is not the active category**. Blue Fav is styling, not the selection indicator.

The active Inventory category is now detected from the tab structure itself:

- Recover the four category tab boundaries from the detected Inventory panel and slot lattice.
- Recover the category rail's vertical borders from repeated grayscale UI-rule pixels.
- The **active tab is the one whose right edge is open/merged into the Inventory body**.
- Inactive tabs retain a closed vertical right border.
- Fav may remain blue without being classified as active.
- The active/open-edge state must be stable enough to beat the other three tabs before it is accepted.
- If the requested tab is already active, two fresh captures confirm it and no redundant click is sent.
- Otherwise the existing bounded detected-tab retry path remains: up to three deterministic safe interior click points, fresh rail detection between attempts, and two positive captures before item dragging.

This exactly matches the supplied live screenshot, where **Use items are visible and Use is active while Fav remains blue**.

All previous Cart safeguards remain: two fresh empty-grid scans before advancing categories, only positively detected empty Cart destinations, no fallback drop coordinates, visible Recovery-log progress, and fail-closed manual hold when UI state is uncertain.

## Validation scope and limits

The regression fixture now explicitly reproduces the live appearance: **Use active + Fav blue**. It also tests other active tabs, multiple panel positions/scales, open-edge transition verification, bounded click points, Debug/Release builds, portable launch, native recovery and responsive UI validation.

The engineering environment cannot drive the user's live Vanilla/Gepard window, so the final end-to-end confirmation remains the next VPS test. No randomized anti-detection input behavior, game-memory writes, injection, packet manipulation or Gepard bypass is used.
