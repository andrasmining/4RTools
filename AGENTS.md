# Repository operating policy

Read this file at the start of every task in this repository. It applies to the
coding agent and every delegated agent. Later explicit user instructions take
precedence; update this file when they establish a new long-term repository
policy.

## Role and responsibility

The coding agent is the hands-on engineer and sole code implementer. The user
does not edit code or perform development work. For requested features, fixes,
refactors, diagnostics, automation, UI, memory/state reading, packaging, tests,
builds, releases, and repository cleanup, inspect the repository, implement the
work, test it, review it, commit it, push it, and verify the push yourself.

Do not substitute a plan, pseudocode, suggested commands, TODO list, or directions
for the completed work. Never ask the user to edit C# or JSON, locate methods,
paste code, change project files, compile, package, configure developer tooling,
discover memory offsets, or use Cheat Engine. Instructions for future engineers
may be documented, but must not become work the user has to finish.

Ask the user only when an external fact or unavoidable in-game interaction is
required and cannot be obtained otherwise. Request one simple, specific action
at a time, such as "Please move exactly one cell to the right." Then continue
the technical investigation yourself.

## Inspect before changing anything

The current repository is the source of truth. Do not rely on conversation
descriptions when the files now differ. At the beginning of a task:

1. Run `git status`; inspect the current branch, tracking branch, configured
   remotes, recent commits, staged changes, and unstaged/untracked work.
2. Find applicable `AGENTS.md` files and additional contributor instructions.
3. Read the current files to be changed and understand their callers,
   dependencies, existing behavior, and local modifications.
4. Fetch/pull and reconcile remote changes when appropriate, preserving all
   existing work. Never merge or switch branches blindly over local changes.

Existing modifications may belong to the user or earlier agent sessions. Treat
them as valuable. Before changing or deleting existing work, understand its
purpose and whether it is still used, and preserve useful functionality. Do not
overwrite newer work, discard experiments that are still needed, or remove
changes merely because they are inconvenient.

## Final integration and branch cleanup

Completed work belongs in remote `main`, not a leftover working branch. Verify
that `main` contains the final tested commit, then delete completed task branches
locally and remotely. Remove obsolete temporary workflows and patch payloads.
The normal finished state is only `main`; preserve valid concurrent work before
integrating or removing an older task branch. Never delete release tags.

A genuine external blocker may leave one clearly identified working branch.
Resume it on the next pass, solve or replace the failed approach, integrate the
result into `main`, and delete it. Do not accumulate unfinished branches or hide
a failed implementation by abandoning it on another branch.

## Mandatory Git workflow

All meaningful completed work must be committed. All commits must be pushed to
the configured remote. Completed implementation must not remain only in the
local working tree. Apply this workflow throughout the task:

1. Inspect status, branch, repository, and remotes.
2. Fetch and reconcile remote changes when appropriate.
3. Make a focused, coherent change.
4. Run the relevant validation and fix failures.
5. Review `git diff`, deliberately stage the intended files, and review
   `git diff --staged`, including new files.
6. Commit with a concise, descriptive message.
7. Verify the repository, current branch, and intended push remote; run
   `git push` or explicitly push the appropriate current branch.
8. Verify that the remote branch contains the committed SHA.
9. Continue with the next logical piece of work.

Prefer coherent incremental commits, such as diagnostics infrastructure, a
memory resolver, a state model, a rule, a UI, tests, packaging, or a live-validation
fix. Avoid both meaningless line-by-line commits and an enormous final commit
containing unrelated changes. Each commit should build when reasonably possible.

Example messages:

- `feat: add Vanilla read-only diagnostics`
- `feat: add smart teleport automation`
- `feat: add SP recovery sequence`
- `fix: validate target state before teleporting`
- `build: add portable release packaging`
- `test: cover automation cooldown logic`

Never commit secrets, credentials, personal absolute paths, unrelated junk,
temporary memory dumps, or generated debugging artifacts unless an artifact is
intentionally required for the repository.

## Branches, remote changes, and push failures

Inspect the existing branch structure first. Continue on the current branch if
it is already the intended working branch. Create a clearly named development
branch only when appropriate; do not create unnecessary branches or commit to
an unrelated branch. Never switch branches in a way that loses existing work.

If the remote advances, fetch and safely rebase or merge as appropriate,
preserve both sets of work, resolve conflicts carefully, rerun relevant tests,
and then push. Do not casually rewrite shared history. Never use
`git reset --hard`, `git clean -fd`, destructive checkout, or force push to make
problems disappear. A force push requires an explicit user instruction and a
clearly safe reason; it is not part of the normal workflow.

Pushing is part of completion. Do not infer success from a local commit or claim
a push succeeded without checking the remote. If authentication, permissions,
branch protection, or remote configuration prevents pushing:

- Preserve the work in local commits.
- Report the exact blocker, current branch, and commit SHA.
- Continue independent local engineering when possible, subject to the initial
  policy gate below, and retry pushing when the blocker is resolved.
- Clearly identify the task as not fully pushed until verification succeeds.

## Initial policy gate and first commit

Before any further feature implementation after introducing this policy,
`AGENTS.md` must exist, be reviewed, be committed, and be successfully pushed to
the configured remote branch. This initial gate remains in force if its push
fails; the general permission to continue local work after later push failures
does not override it.

For the first policy commit:

1. Review this file and run `git status`.
2. Stage only `AGENTS.md`, unless another existing change is intentionally part
   of that commit. Preserve all other uncommitted work.
3. Commit with exactly `chore: add repository agent workflow`.
4. Push the current branch to its configured remote.
5. Verify that the remote branch contains the commit before further feature work.

## Implementation quality and autonomy

Make focused production-quality changes. Prefer clear abstractions, reusable
logic, explicit validation, useful diagnostics, deterministic behavior, and
robust error handling. Preserve working upstream behavior and avoid unnecessary
application redesign.

Avoid temporary hacks, duplicate implementations, scattered magic values,
abandoned experiments, unnecessary architectural rewrites, TODO-only work, and
placeholder features presented as finished. Evolve a necessary prototype into
production code or remove it when it is no longer needed before completion.

## Product direction

The Vanilla workspace is now the primary product surface. Keep it first and use
it for recovery, live read-only client state, automation, temporary actions,
diagnostics, updates, and future Vanilla-specific features. Up to two Vanilla
clients should be visible and manageable without switching to the legacy UI.
The original 4RTools interface remains available only as a secondary compatibility
tab; preserve useful stock code and reuse proven memory/input components, but do
not make new Vanilla workflows depend on the legacy single-client selector.
Diagnostics and discovery support address mapping and verification. Selecting a
process or reading bytes does not by itself prove gameplay semantics.

If the requirement is clear, inspect, implement, test, commit, push, and continue
without asking after each small change. Work until the requested deliverable is
actually complete. Do not stop merely because one desired field cannot be
discovered: investigate other permitted reliable signals that can satisfy the
same high-level requirement, without circumventing a blocked read or action.

## Mandatory end-to-end completion and release

For implementation tasks, the default deliverable is a usable, integrated,
published release unless the user explicitly requests source-only work. A plan,
local edit, commit, push, pull request, queued workflow, green compile, or draft
release is NOT completion. Own implementation, tests, integration, versioning,
packaging, publication, and verification in the current session. Inspect failed
jobs, fix their causes, rerun the existing gates, and verify the final non-draft
release, tag/source SHA, downloadable assets, checksums, and update discovery.
Do not end with running CI, instructions for the user to build/release, or a
promise of future/background work. Respect branch protection and permissions.
For a genuine tool/permission/platform blocker, preserve completed work and
report exactly what failed, what was completed, and what remains unverified.
Never fabricate test, deployment, release, or in-game success.

When asked to update project instructions first, provide the copyable block
within the requested character limit before implementation, then persist the
enduring policy here without deleting unrelated valid rules. This automation
repository is `andrasmining/4RTools`; the archived `andrasmining/cinder-index`
market dashboard is a different project and must not receive autobattle changes.

## Required autobattle resume verification

After stable gameplay, obtain a fresh verified X/Y baseline for the intended
client immediately before its configured resume hotkey. Observe coordinates
throughout the following 10 seconds: a change in X OR Y verifies movement.
Do not compare only the beginning/end positions or require both axes to change.
If no movement is observed, refocus the intended client, refresh its baseline,
and retry. Recheck movement after refocusing and before another toggle.
Allow THREE total hotkey attempts (initial send plus at most two retries), each
with its own ten-second observation window. Stop immediately upon movement.
After three unsuccessful windows, display/log failure and latch it for that
attempt; a subsequent gameplay poll must not reset the retry budget. Sending a
key is not verified resume, and movement is not proof of combat.

Share one cancellable, nonblocking verifier across startup, recovery, and the
resume diagnostic. Keep per-client identity and the serialized input lease
through verification and minimization. Do not toggle adopted healthy clients.
Display compact attempt/retry progress in the account row and details in logs.
Use the existing read-only fingerprinted coordinate provider, not guessed
offsets, screenshots of coordinates, or stale values. Missing/unverified/stale
state is not zero or stillness. STOP, configuration/identity changes, map
transitions, disconnects, failed reads, and lost focus must prevent late keys.
Use monotonic time and ensure an old worker cannot complete a newer operation.
Cover first/second/third success, three-window failure, single-axis/intermediate
movement, deadline/refocus races, cancellation, stale/missing state, identity
changes, lease isolation and input cleanup with deterministic regression tests.
Keep existing Windows build, portable launch and mock-data UI gates intact.

## Terminal dialogs and sequential replacement

Treat verified `Now Logging Out.` and `Disconnected from Server.` dialogs as
terminal client states requiring close and replacement, including an assigned
existing client encountered during START. Confirm with fresh matching visual
observations. A generic white/modal rectangle, stale frame, failed capture or
unknown message cannot authorize closing a client or pressing Enter.

If one client fails, leave healthy siblings alone. If both fail, hold the same
global recovery lease through closing the first client, confirmed process exit,
replacement launch, login, verified Autobattle movement and minimization. Only
then may the next client close/restart. Failed attempts release their lease into
bounded backoff; missing configuration must not strand other accounts. Bind every
close/termination to the observed process identity and current runtime/operation.
Never treat an inaccessible process or failed exit query as a confirmed exit.
STOP, settings changes, client replacement and disposal cancel delayed actions.
Cover both exact dialogs, single-client and dual-client failure, native visual
matching, unknown-message rejection, close/exit ordering and cancellation tests.

## Required validation

Test every meaningful change as far as available tooling permits. Appropriate
checks include restore/build, full Release builds, unit/integration tests,
syntax checks, configuration serialization, timers/cooldowns, state transitions,
runtime diagnostics, live Vanilla validation, and portable-package launch tests.
For documentation-only changes, review content and diff rather than claiming
application behavior was tested.

Inspect the current solution and build tooling before running it. The project
currently targets .NET Framework 4.7.2; `scripts/build.ps1`, when present, rebuilds
the solution in Release and Debug and runs the offline diagnostics tests. The
agent must run the relevant checks itself. Record baseline warnings separately
from newly introduced issues.

A successful compile alone does not establish correct behavior. Distinguish
static validation, unit/integration validation, and actual live Vanilla
validation in documentation and reports. If a test fails, investigate and fix
the underlying issue, rerun it, and only then commit/push the completed change.

## Vanilla and Gepard boundaries

The user-provided Vanilla project context cites the following documented features:

- "4R Tools Supported"
- "Gepard 3.0 Protection"
- "24/7 Auto-Attack + Slave System"

This project may extend 4RTools for Vanilla-specific automation, but stock 4RTools
support must not be treated as permission for arbitrary modified binaries or
additional behavior. Keep the distinction between stock behavior and local
extensions explicit.

Do not bypass, disable, patch, evade, inject into, hide from, spoof, or otherwise
interfere with Gepard or any game security mechanism. Do not inject code into
Vanilla, manipulate packets, or communicate directly using the game server
protocol. Do not alter Vanilla installation files or account settings.

Prefer read-only client observation plus ordinary keyboard/mouse input through
the mechanisms already used by 4RTools. Use Vanilla's own Autobattle for movement
and combat; do not replace it with a monster scanner or bot pathfinding. If a
read or action is blocked by Gepard, stop that line of investigation and report
the exact operation and error. Do not use alternate memory-access paths, token
manipulation, handle duplication, injection, or protection changes as a workaround.

For live checks, use a test character when possible. Do not purchase, delete,
drop, or trade items, and do not automate aggressively. Before enabling a live
automated action, prove the corresponding read-only state signal in diagnostics.
Successful metadata queries or executable-header reads do not verify gameplay
fields or authorize live actions.

## Memory and state safety

Vanilla-specific discovery and automation must remain read-only with respect to
game memory. Do not use `WriteProcessMemory` or any equivalent to change HP, SP,
coordinates, targets, inventory, skill state, or any other game state. Memory is
observable input for decisions, never an action destination.

Keep state discovery separate from action/rule logic. Centralize Vanilla offsets,
pointer chains, and any permitted signatures/configuration. Validate addresses
and pointer widths against the actual process architecture; do not infer bitness
from an AnyCPU configuration name or silently truncate an address.

Unknown fields are optional and must not appear as valid zero, false, no target,
or idle state. Fail safely, retain useful errors, stop on denied/failed reads,
and invalidate stale observations. Do not silently swallow new diagnostic
failures or retry around protection. Record how candidate fields were verified,
including relog, map change, client restart, and PC restart where relevant.

## Local launch and portable releases

For this user's development and live-validation sessions, launch the application
from an executable inside this GitHub repository with this repository as its
working directory. This is the user's established local environment requirement.
Do not alter antivirus/security settings yourself. Keep build caches and logs
out of source control; their storage location is separate from application launch.

Run the Vanilla product with administrator privileges (`requireAdministrator`) so
its integrity level matches the elevated Vanilla MMO clients it observes. This is
the intentional product runtime model. A failed read must still remain read-only:
do not use elevation as a gateway to injection, memory writes, token manipulation,
handle duplication, protection changes, or alternate access paths.

For release-oriented tasks, done means a usable artifact as well as source code.
When requested, produce `dist/<release-folder>/` and
`dist/<portable-release>.zip`. The user must be able to copy/unzip the package
onto another Windows PC and run it without Visual Studio, Git, NuGet, or source
code. Handle dependencies and clearly document any required Windows/.NET runtime.

The agent owns building, dependency handling, packaging, checksums, release notes,
and validation, including package launch testing where possible. Retain the
upstream MIT license and all required copyright/permission notices, including
`Copyright (c) 2022 4RTools`, and include them with distributed artifacts.

## Release/version and persistent-data policy

Use conservative pre-1.0 versioning while the Vanilla integration is still being
stabilized live. Prefer patch releases for iterative fixes and UX/reliability
improvements, minor releases for coherent larger milestones, and do not approach
or declare 1.0 merely because several internal changes landed. A 1.0 release
requires explicit product readiness and substantial live validation.

User configuration must survive application upgrades. Keep writable user data
outside versioned release folders under the stable per-user application-data
root, migrate compatible older schemas automatically, and preserve old data when
a safe migration cannot be proven. Release ZIPs must not contain mutable user
profile/recovery/log directories. Backwards-incompatible schema changes require
an explicit migration or a clear, intentional reset path rather than silent
corruption or accidental default replacement.

Normal end-user operation should be one 4RTools application/window. Integrate
Vanilla recovery, automation, diagnostics, data paths, and update status into the
main Vanilla workspace instead of creating competing top-level manager windows.
The main 4RTools window must never use tray-only hiding as its minimized state:
when the user minimizes it, keep it visible as a normal Windows taskbar item and
preserve the minimized window state. An existing legacy tray icon may remain for
compatibility, but it must not replace the taskbar window or cause minimize-to-tray
behavior. The normal user-visible window states are active/restored or taskbar-
minimized.

Keep CHECK FOR UPDATES and the current version/update status together in a
separate right-aligned status area of the top Vanilla header. Keep ordinary
workspace/debug actions on the left so update/version information reads as status
rather than as part of the main action cluster.

The Vanilla UI is Full-HD-first and resolution-responsive. Treat ordinary
1920x1080-class desktops and RDP sessions around 1980x1020 as primary layouts;
do not reserve vertical space on the assumption that a 2K display is available.
Use compact headers and client cards, proportional table/list columns, and
responsive panel breakpoints: broad screens may place related status/log panels
side-by-side, while narrower screens should stack them. Every workspace and
embedded tool must keep critical controls reachable through sensible scrolling
when the available width or height is smaller. Prefer reflow and content-aware
sizing over fixed tall sections or fixed column widths that clip important data.
Keep regression coverage for the main layout breakpoints and portable smoke test.

Recovery settings are auto-save UI: do not require a separate Save button. The
launcher path, account edits, recovery/watchdog switches, and per-account proxy
selection must persist automatically and surface a brief success/error indication.
Launch arguments are intentionally hidden/empty. The managed-client count is not
an independent setting: derive it from the number of enabled account rows (up to
two). Proxy selection is account-specific and must follow the account being
started or recovered, not one shared global proxy control.

During the current live-hardening phase, global debug logging is ON by default.
Keep one Debug log checkbox and one COPY DEBUG LOG action in the left action area
of the top Vanilla header. The copied bundle should aggregate application,
startup/recovery, memory-access, update, and other available logs. Debug mode
should record process/PID changes, stage/visual transitions, launcher evidence,
focus attempts, clicks/keys/hotkeys, and errors with timestamps while never
logging passwords or typed secret contents. Remove or reduce this temporary
always-on default only when the user explicitly asks after hardening is complete.

## Multi-client reconnect and outage policy

Vanilla supports up to two managed clients on this PC, but automated recovery UI
input must be globally serialized. Never run launcher/proxy/login/server/character
selection or resume-hotkey recovery workflows for two clients in parallel. One
client owns the recovery lease from launcher start through confirmed gameplay and
bounded autobattle movement verification; other clients remain queued until that client is
Online or its attempt fails/backoffs. Existing healthy clients must not be
disturbed merely because another client is recovering.

While the reconnect supervisor is running, healthy managed Vanilla clients are
minimized by default and left running. Initial startup and recovery use the same
serialized policy: finish one client through confirmed gameplay and verified autobattle movement, minimize it, then allow the next queued client to start. If one
client disconnects later, keep every other healthy client minimized and untouched,
recover only the affected client, and minimize that client again after successful
return to gameplay. If a healthy supervised client is manually restored/on-screen,
the supervisor should minimize it again on its next observation cycle.

Cold startup is stricter than merely holding a nominal recovery flag. Do not
start the periodic multi-client supervisor until the startup orchestrator has
finished the current enabled client through gameplay, verified autobattle movement, and
confirmed minimization. Only then may the next missing enabled account launch.
If focus or visual recognition fails during cold startup, leave the current client
running, stop/fail closed, and do not close it or advance to a later account.

Before every automated click, key, credential entry, server/character action, or
resume hotkey, bring the intended Vanilla window to the foreground and verify it
actually owns focus. Drive startup primarily from observed expected UI states
(proxy list, login controls, server dialog, character-ready surface, gameplay)
with short human-like settle delays after recognition. Avoid long blind sleeps
when the next expected screen can be detected; poll the expected state and act
shortly after stable recognition instead.

After gameplay has been confirmed, a detected disconnect/logged-out modal or a
return to the login/service shell is a recovery event: close that affected
Vanilla client and recover it through the normal launcher path. If a client exits
outright, queue the same recovery path. Do not rely on an artificial close/restart
test as the primary validation of outages; keep the manual network-drop test for
real disconnect behavior.

Failed unattended recovery attempts must use exponential backoff rather than a
hot retry loop. The current policy starts from the configured base delay (30
seconds by default), doubles after each failed attempt, and caps the interval at
one hour. A confirmed successful return to gameplay resets the failure/backoff
state. Keep retry/backoff state visible in logs/status and preserve fail-closed
visual recognition: when a required UI region cannot be identified confidently,
do not guess a click location or type credentials.

Resolution/DPI robustness is a product requirement. Prefer current-client visual
recognition, client-relative normalized geometry, and structural layout detection
over absolute desktop pixels. Tests for recognized login/proxy/server surfaces
must cover multiple resolutions and softened/resampled rendering. Do not claim
arbitrary future UI changes are guaranteed; unknown layouts must stop safely and
produce useful captures/logs.

## Cleanliness, documentation, and final reporting

Before finalizing substantial work, review for abandoned experiments, temporary
files, memory dumps, unintended logs, personal paths, and credentials/secrets.
Remove only understood, unneeded artifacts without losing user work. Update
`.gitignore` where appropriate and retain genuinely useful diagnostics.

Update documentation when behavior changes materially. Release notes should
concisely describe changes, tests, actual live tests, and known limitations.
Documentation must not require the user to finish the engineering work.

After completing a task, report:

- What changed and the important implementation decisions.
- Tests performed and what was actually validated live.
- Resulting commit SHA(s), branch, and verified push status.
- Release artifact locations, when applicable.
- Any actual remaining limitation or exact blocker.

Do not present unverified behavior, unfinished features, or unpushed commits as
completed work.


## Primary autofarming movement watchdog

For enabled, supervised autofarming clients, X/Y health is the primary recovery
signal. After startup/recovery has finished, restart an affected client when no
fresh verified coordinate movement has been observed for 120 seconds. Missing,
unreadable, unverified and stale coordinates remain invalid, not zero; their
absence must not reset or indefinitely defer that deadline. Use monotonic elapsed
time, the existing fingerprinted read-only fleet observations, and distinct
per-client/session baselines. Credit intermediate movement even when the latest
position returns to the previous coordinates. Reset on STOP, configuration/client
replacement and valid map/session changes; do not accrue time during login or
recovery. Never reopen a failed reader for the same client to evade a blocked read.

A recognized terminal dialog can trigger recovery sooner after confirmation.
Pattern recognition adds diagnostic evidence but is not required for a timed-out
movement watchdog. Unknown popups must not receive blind Enter/click input. Keep
the same global recovery lease from closing through verified exit, relaunch,
login, verified Autobattle movement and minimization. Recover only the affected
client; simultaneous failures remain sequential. Failure/backoff releases the
lease without pretending the failed attempt succeeded. Verify process identity
and cancellation before close/termination, and never treat denied access as exit.


## Character roster and identity discovery

The recovery table is a CHARACTER roster, not one row per login account. Keep
Description, Username, Slot and Character name in that order after Enabled,
then existing hotkey/password/proxy/PID/status fields. Several rows may share
one username; at most two rows may be enabled. Preserve row IDs, encrypted
passwords and proxy associations. Keep historical JSON names for migration.

Discover running characters at startup and as fresh verified observations arrive.
Add missing characters once, disabled. Never guess passwords, proxy, username or
slot. Match processes by independently verified character identity, not PID order,
description or username alone. Ambiguous matches remain unbound. Only populate
fields supported by verified memory mappings. The current profile includes user-supplied login username copies at module offsets
0xD343F8 and 0xD39159; use both in shared state/diagnostics and require agreement
before identity use. Character slots are not yet mapped and stay unknown/editable. A name
verified after this tool's own successful configured login can enrich that exact
row, never transfer credentials to another character. Passive discovery must not
cancel recovery; edits, disabling, character/session replacement, STOP and disposal
must invalidate stale ownership. Cover migration, deduplication, multiple chars
per username, null slots, enabled limits and ownership in regression/native UI tests.


## Username + character-name identity

The logical unique key is the complete username + character-name pair. Use it
consistently in discovery, validation, process binding, removal suppression and
confirmation; never substitute name-only or PID order. Keep persistent row GUIDs
for credential/proxy compatibility. Missing/invalid username observations defer
automatic row creation rather than creating new username-less rows.

Enrich a configured legacy username-only row in place when one compatible row
and one fresh observed character for that username identify it unambiguously.
A missing memory slot is not a reason to duplicate that row. Preserve description,
slot, password, proxy, enabled state and ID. Multiple candidate rows/characters
remain unresolved, never guessed. Only untouched empty auto-discovery duplicates
may be folded into that configured row; user-configured rows must survive.
Record screenshot provenance accurately; automated tests do not prove independent
live relog/restart validation of a supplied address.

## VPS update delivery is mandatory

The user's VPS is updated through the application's GitHub-release updater.
Every implementation task must finish with a tested public stable release,
explicitly marked Latest, unless the user explicitly requests source-only work.
A chat ZIP, Actions artifact, branch, commit, PR or queued release is not an
alternative deliverable. Do not stop at those intermediate states.

Verify the public releases/latest response and both expected portable ZIP and
checksum assets. Exercise the previously published application's real updater
against the endpoint, verify downloads and source identity, integrate all work
into main and remove completed task branches. Diagnose and repair failures;
replace unsuitable approaches instead of handing development back to the user.
State a genuine unavoidable external blocker accurately only after exhausting
practical authorized alternatives. Never claim publication or VPS installation
without evidence; testing update discovery is not installing on the user's VPS.
