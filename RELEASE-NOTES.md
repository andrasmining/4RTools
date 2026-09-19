# 4RTools Vanilla 0.6.58

## Fresh bounded logs on every application start

Global debug logging no longer grows one permanent `debug.log`.

- Every normal 4RTools start begins with a fresh live `debug.log`.
- The previous debug session is renamed to a timestamped archive before the new process writes its first debug line.
- Every application-managed `.log` file is hard-capped at **10 MiB**.
- Fixed-name logs rotate into timestamped archives before exceeding the cap.
- Reconnect/session logs keep their session naming and also obey the same 10 MiB hard cap, including a single unusually large payload.
- Existing oversized legacy/migrated logs are split into bounded timestamped parts during startup migration.
- Log families keep bounded history rather than growing without limit.
- COPY DEBUG LOG uses the current debug session/current operational logs instead of concatenating every old debug archive; recent action summaries may still read recent archives.

## Verified Cart weight memory

This release adds the read-only Cart weight pair supplied from the live Memory Finder evidence for the current verified Vanilla executable:

- **Current Cart weight:** module + `0xD34B3C` (absolute `0x01134B3C` with the observed `0x00400000` module base). The supplied controlled sample changed **4000 -> 4005**.
- **Maximum Cart weight:** module + `0xD34B40` (absolute `0x01134B40`), the adjacent stable **10000** value from the supplied max-weight search.

The pair is accepted only when the current value is within range and the maximum is exactly the known Vanilla Cart capacity of **10000**. Unknown, stale, unverified or incoherent readings remain unusable for automation.

## Compact live client cards

The top fleet cards now show four compact read-only resources for each Vanilla client:

- HP
- SP
- carried Weight
- Cart Weight

Each gets a short bar rather than consuming half the card width. The Location line also shows the remaining Cart capacity when verified. The previous **Activity** field is removed because activity semantics have not been independently verified.

## Capacity-aware Cart filling

Weight/Cart maintenance now treats verified Cart capacity as a first-class safety signal.

Below 95% Cart weight, the existing category walker remains in charge. Starting **at 95%**, transfers enter precision mode so the Cart is never intentionally overfilled.

The currently verified farming item rules are:

- **Use / Mastela Fruit:** 3 weight per item.
- **Etc / Peco Feather:** 1 weight per item.
- Equip remains unknown for precision filling.

For known items, 4RTools calculates the maximum count that can fit in the remaining Cart capacity. A quantity is typed only after the normal quantity dialog is positively detected. If the carried stack is already proven small enough to fit, the existing full-stack confirmation remains valid. Every accepted precision transfer must then produce a coherent verified Cart-weight increase; an inconsistent delta fails closed.

If a 3-weight Mastela cannot fit the final 1–2 weight, that category is skipped and the lighter 1-weight Peco Feather can finish the remainder. If the Cart is still near-full but not complete because no suitable known item is currently available, Autobattle resumes and the Cart fill is retried later with a bounded cadence instead of looping UI input.

Unknown-weight categories are never blindly transferred at/above 95%.

## Cart-full and DONE farming milestones

A verified **100% Cart** is now a farming milestone.

- When valid SMTP/recipient settings are saved, 4RTools sends one **Cart full** e-mail as soon as Cart reaches 100%. This milestone mail is independent of the optional carried-weight warning checkbox.
- Farming then continues normally while the character still has carrying capacity.
- When Cart remains 100% and carried Weight reaches **50% or more**, 4RTools sends the dedicated Weight Autobattle STOP hotkey.
- That character enters an intentional completed-farming hold so reconnect/movement recovery cannot restart Autobattle.
- One **DONE** e-mail is sent through the same saved SMTP transport.
- The completed hold is cleared only by the explicit Weight/Cart hold-clear action.

If Cart reaches 100% during a maintenance pass while carried Weight is already at least 50%, the client remains stopped instead of briefly resuming Autobattle.

## Recovery/UI regressions retained

The previous v0.6.57 autoattack/Smart-Teleport recovery remains unchanged: movement suppresses further recovery input immediately, the three bounded recovery cycles remain serialized, and continued stationarity escalates through the existing 180-second restart path.

The Recovery layout was also tightened so the compact four-resource client cards and the reconnect Log both remain inside the viewport on Full-HD, 1366x768 with 150% text, and 1050x700/RDP-style layouts, including long update-status notifications.

## Validation

The Windows validation pipeline covers:

- shipped build-profile validation and exact Cart mapping offsets;
- Cart maximum=10000 sanity validation and percentage calculation;
- 95% precision boundary;
- Mastela/Peco unit-weight and remaining-capacity calculations;
- bounded near-full retry policy;
- milestone-mail transport policy;
- fresh-session debug logging, timestamped archives and 10 MiB hard rotation;
- Debug and Release test suites;
- portable package/smoke validation;
- native test-owned recovery checks;
- mock-data UI screenshots/layout validation across desktop, RDP and enlarged-text sizes.

The engineering environment cannot execute the final workflow against the user's live Vanilla/Gepard client. The supplied screenshots are therefore the live evidence for the new Cart offsets, while the exact live quantity-entry/Cart-delta/DONE sequence remains the next VPS runtime-validation boundary.
