Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$p='Model/Vanilla/VanillaReconnect.cs'
$t=[IO.File]::ReadAllText($p)
$old=@'
                foreach (var runtime in runtimes.Values)
                {
                    runtime.ScriptRunning = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped");
                }
'@
$new=@'
                foreach (var runtime in runtimes.Values)
                {
                    runtime.ScriptRunning = false;
                    runtime.RecoveryOwned = false;
                    SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped");
                }
'@
if(-not $t.Contains($old)){ throw 'Stop runtime reset anchor missing.' }
$t=$t.Replace($old,$new)
$old=@'
                    if (aborted || disposed || !running)
                    {
                        SetStage(current, VanillaReconnectStage.Stopped, "Supervisor stopped");
                    }
'@
$new=@'
                    if (aborted || disposed || !running)
                    {
                        current.RecoveryOwned = false;
                        SetStage(current, VanillaReconnectStage.Stopped, "Supervisor stopped");
                    }
'@
if(-not $t.Contains($old)){ throw 'Launch abort reset anchor missing.' }
$t=$t.Replace($old,$new)
[IO.File]::WriteAllText($p,$t,$utf8)
Write-Host 'Recovery lease stop/reset patch applied.'
