Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function Read([string]$p) { [IO.File]::ReadAllText($p) }
function Write([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

# This repo's legacy .NET/package graph resolves a ProcessStartInfo surface on CI
# where UseShellExecute is not available. The updater launches executables directly,
# so no shell flag is needed anyway. Remove both multiline and inline occurrences.
$p='Model/Vanilla/VanillaUpdater.cs'
$t=Read $p
$t=[Text.RegularExpressions.Regex]::Replace($t, '(?m)^\s*UseShellExecute\s*=\s*true,?\s*\r?\n', '')
$t=[Text.RegularExpressions.Regex]::Replace($t, ',\s*UseShellExecute\s*=\s*true', '')
if ($t -match 'UseShellExecute') { throw 'UseShellExecute remained in VanillaUpdater.cs after compatibility patch.' }
Write $p $t

$p='Model/Vanilla/VanillaIntegratedShell.cs'
$t=Read $p
$old='            Process.Start(new ProcessStartInfo { FileName = VanillaAppData.RootDirectory, UseShellExecute = true });'
if(-not $t.Contains($old)){throw 'Integrated data-folder opener anchor missing.'}
$t=$t.Replace($old,'            Process.Start(VanillaAppData.RootDirectory);')
if ($t -match 'UseShellExecute') { throw 'UseShellExecute remained in VanillaIntegratedShell.cs after compatibility patch.' }
Write $p $t

Write-Host '0.6.1 compatibility patch applied.'
