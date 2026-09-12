Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$p='Model/Vanilla/VanillaReconnect.cs'
$t=[IO.File]::ReadAllText($p)
$old=@'
            foreach (var runtime in runtimes.Values.Where(r => !desiredIds.Contains(r.Account.Id)))
                if (runtime.Stage != VanillaReconnectStage.Stopped) SetStage(runtime, VanillaReconnectStage.Stopped, "Account disabled or above client limit");
'@
$new=@'
            foreach (var runtime in runtimes.Values.Where(r => !desiredIds.Contains(r.Account.Id)))
            {
                // A profile disabled while it was recovering must immediately release the
                // global recovery lease so another configured client cannot be starved.
                runtime.ScriptRunning = false;
                runtime.RecoveryOwned = false;
                if (runtime.Stage != VanillaReconnectStage.Stopped)
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Account disabled or above client limit");
            }
'@
if(-not $t.Contains($old)){ throw 'Disabled runtime recovery-lease anchor missing.' }
$t=$t.Replace($old,$new)
[IO.File]::WriteAllText($p,$t,$utf8)
Write-Host 'Disabled-account recovery lease patch applied.'
