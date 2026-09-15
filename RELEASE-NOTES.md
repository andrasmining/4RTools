# 4RTools Vanilla 0.6.33

## Responsive recovery workspace

- The embedded Recovery & relog view follows the actual parent viewport instead of being capped at a previous desktop width. Full-HD layouts retain the requested two-thirds accounts / one-third log split.
- All nine account columns fit inside the visible table. On, Slot, Resume, Password, Proxy and PID use compact measured widths; Account, Username and Status share the remaining space. Long values and detailed recovery state remain available through hover text.
- The launcher/actions strip is measured from its visible controls. The large unused area beneath it is removed, and controls wrap when necessary rather than extending off-screen.
- The Accounts section uses the remaining pane height, reserves at least four rows plus a spare row's breathing room, and scrolls internally for larger saved-profile lists. Existing unlimited saved profiles / maximum two enabled clients behavior is preserved.
- Update/version information stays right-aligned and can reflow below the left header actions on narrow windows. A new multiline update/error message no longer clips on its first display.
- Live-client cards retain visible location and activity together on the final row. Card height respects font metrics instead of hiding fields to meet an arbitrary compact height.
- Account actions, normal taskbar minimization, the global debug controls and the existing TESTS menu are retained.

## Validation

The existing standard Windows GitHub Actions runners build and test both Debug and Release. The release is packaged, checksum-verified and launched in the existing inert portable smoke test. Publication is also gated on the native Windows mock-data UI harness in `scripts/test-ui-layout.ps1`.

The UI harness instantiates the production application controls, supplies fictional account/runtime/fleet data, resizes the actual window, renders PNG screenshots and checks geometry. Its 15 scenarios cover 1920x1020, 1904x981, 1980x1020, 1600x900, 1366x768 and 1050x700 client areas; 2, 4, 12 and 40 saved profiles; normal, 125% and 150% recovery text sizes; long status messages; scrolling to the final account; selection retention across tab changes; and repeated shrink/grow transitions.

Assertions cover every account column, viewport containment, the Full-HD 2:1 split, four visible account rows plus breathing room, toolbar spacing, live-card text, update/debug controls and stable settled bounds. Screenshots and a geometry report are retained as workflow artifacts for inspection. Failures stop publication rather than being treated as a successful compile.

These are native Windows **mock-data UI** checks, not live Vanilla/Gepard startup or a connection to the user's RDP session. The harness keeps live process observation, input, recovery, email alerts and update requests inactive. Text-size scenarios do not claim to emulate every physical monitor/DPI configuration. No paid testing service or additional infrastructure is required.

Existing legacy NuGet audit/compiler warnings are not hidden; dependency upgrades are outside this layout release. Recovery orchestration, gameplay-memory mappings, administrator requirements and game-input semantics are unchanged.
