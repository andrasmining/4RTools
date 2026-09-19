# 4RTools Vanilla 0.6.52

## Automation-first character roster

The Recovery & relog character table now puts the three per-character enable states first:

1. **Enabled**
2. **Weight**
3. **Smart TP**

Smart Teleport's two row-specific details are also visible directly beside those switches:

- **TP sec** — stationary seconds before Smart Teleport triggers.
- **TP hotkey** — the configured per-character Smart Teleport hotkey.

The remaining columns follow with Description, Username, Slot, Character name, Resume, Password, Proxy, PID and Status.

Weight intentionally remains a simple **Yes/No** per-character column in this roster. Weight/Cart thresholds, STOP/Inventory/Cart hotkeys, categories and mail settings remain on the dedicated Weight tab rather than duplicating them into the character list.

No automation behavior, identity ownership, recovery timing or persistence format is changed by this release; this is a presentation/readability improvement over the existing saved per-character settings.

## Validation scope and limits

Automated validation covers the exact character-column order, Enabled/Weight/Smart Teleport state rendering, Smart Teleport seconds/hotkey rendering, long/many-row character tables, multiple Full-HD and narrow responsive breakpoints, enlarged text, no horizontal account-table scrollbar, Debug/Release regressions, portable packaging/launch, native recovery checks, public release/source/checksum verification and updater discovery.

The native UI validation uses mock character data only and does not operate a live Vanilla/Gepard client.
