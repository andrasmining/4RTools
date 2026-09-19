# 4RTools Vanilla 0.6.56

## First-slot Weight / Cart traversal

The v0.6.55 live run selected **Use** correctly and moved the first two Use stacks, but after the category was empty it falsely detected another item and attempted one more drag.

The Cart walker is now intentionally simpler:

- Vanilla compacts items to the front of each category, so **only the first Inventory slot is authoritative**.
- The first slot is compared against the freshest visually detected empty-slot reference from the same detected grid.
- Classification uses both pale-slot coverage and local template difference rather than the old single threshold.
- **Two consecutive Occupied captures** are required before any drag.
- **Two consecutive Empty captures** mean the category is complete and 4RTools advances to the next enabled tab.
- If first-slot evidence remains ambiguous through the bounded verification window, no drag is sent and the character fails closed to manual hold.

Cart destination handling is also simplified. Vanilla accepts a transfer anywhere inside the Cart item body, so 4RTools no longer searches for a destination that appears empty. It rotates deterministically through safe centers of the **detected Cart grid**. This stays resolution/DPI independent and removes a second unnecessary source of visual classification failure.

The existing safeguards remain: selected-category verification, quantity-dialog-only Enter, transfer-progress verification, serialized ownership, visible Weight/Cart logging, manual hold on uncertainty, and verified Autobattle resume.

## Draggable Characters / Log divider

Recovery & relog now uses a real draggable splitter between **Characters** and **Log**. The initial desktop layout remains approximately 2:1, but the divider can be dragged to widen the Log for diagnostics or give more room back to the character table. On narrow layouts the splitter becomes horizontal.

Default layout validation still requires the character table to fit without an unnecessary horizontal scrollbar. If the user deliberately shrinks the character pane far below its default width, a horizontal table scrollbar is allowed.

## Validation scope and limits

Automated coverage includes empty-versus-occupied first-slot classification, detected-reference comparison, rotating safe Cart destinations, active-tab semantics, draggable splitter behavior, Full-HD/narrow/enlarged-text layouts, Debug/Release builds, portable launch and native recovery checks.

No stochastic input behavior is added for anti-detection. Cart destination variation is deterministic and derived only from detected Cart geometry.

The engineering environment still cannot execute the full workflow against the user's live Vanilla/Gepard client, so the exact first-slot transition after moving the final live item remains the next VPS validation boundary.
