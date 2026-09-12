Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p){ [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t){ [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaReconnect.cs'
$t=ReadText $p
$old=@'
            if (string.IsNullOrWhiteSpace(runtime.Account.UserName) || string.IsNullOrWhiteSpace(runtime.Account.ProtectedPassword))
            {
                SetStage(runtime, VanillaReconnectStage.NeedsConfiguration, "Username/password missing");
                return;
            }
'@
$new=@'
            if (string.IsNullOrWhiteSpace(runtime.Account.UserName) || string.IsNullOrWhiteSpace(runtime.Account.ProtectedPassword))
            {
                runtime.RecoveryOwned = false;
                SetStage(runtime, VanillaReconnectStage.NeedsConfiguration, "Username/password missing");
                return;
            }
'@
if(-not $t.Contains($old)){ throw 'Missing configuration lease-release anchor.' }
$t=$t.Replace($old,$new)
WriteText $p $t

$p='AGENTS.md'
$t=ReadText $p
$anchor='## Cleanliness, documentation, and final reporting'
$policy=@'
## Multi-client reconnect and outage policy

Vanilla supports up to two managed clients on this PC, but automated recovery UI
input must be globally serialized. Never run launcher/proxy/login/server/character
selection or resume-hotkey recovery workflows for two clients in parallel. One
client owns the recovery lease from launcher start through confirmed gameplay and
the one-shot resume hotkey; other clients remain queued until that client is
Online or its attempt fails/backoffs. Existing healthy clients must not be
disturbed merely because another client is recovering.

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

'@
if(-not $t.Contains($anchor)){ throw 'AGENTS policy insertion anchor missing.' }
$t=$t.Replace($anchor,$policy+$anchor)
WriteText $p $t
Write-Host 'Final sequential recovery policy patch applied.'
