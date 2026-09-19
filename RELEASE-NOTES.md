# 4RTools Vanilla 0.6.54

## Cart category click verification retry

This release fixes the live Weight/Cart failure where 4RTools correctly detected **Favorite** as the current Inventory category and **Use** as the target, sent the Use click, but then stopped because the selected-tab state could not be positively verified.

The category switch path is now more robust while remaining fully detection-driven:

- Recover the four-tab Inventory rail from the live panel/slot/separator structure.
- Use the **actual detected blue selected-tab fill** to recover the horizontal clickable rail band when available, instead of relying only on separator-line width.
- Click only deterministic safe interior points inside the freshly detected target-tab rectangle.
- If the first click is not visually confirmed, re-detect the entire rail and retry at up to two additional safe interior points.
- After each click, poll fresh screenshots for up to a bounded timeout rather than trusting one fixed-delay frame.
- Accept either a direct target selected-tab classification or a strong visual transition where the target highlight rises/dominates while the old selected tab loses its highlight.
- Require two consecutive positive visual confirmations before any item drag begins.
- If all bounded attempts remain unverified, fail closed with Autobattle OFF and no drag input.

The visible Recovery log now records each click attempt and whether Windows accepted it for the verified Vanilla client, followed by the observed selection/score while waiting for visual confirmation. Full coordinate/window diagnostics remain available in COPY DEBUG LOG.

The previously added safeguards remain: categories advance only after two fresh empty-grid detections, Cart destinations must be positively detected empty slots, and there is no arbitrary fallback drop point.

## Input timing boundary

This release does **not** add randomized positions or randomized delays for the purpose of avoiding bot/anti-cheat detection. UI robustness comes from detected control bounds, deterministic bounded retry points and waits driven by observed UI state.

## Validation scope and limits

Automated validation covers category-rail detection at multiple panel locations/scales, recovery of the click band from blue selected-tab fill even when separator lines are shorter, three distinct safe interior retry points, direct selected-index verification, weaker but unambiguous highlight-transition verification, ambiguous-state rejection, Debug/Release builds, portable launch, native recovery and responsive UI validation.

The engineering environment still cannot operate the user's live Vanilla/Gepard session. The exact v0.6.53 failure from the supplied screenshot/log is addressed structurally, but the final live click/selection behavior remains a VPS observation boundary.
