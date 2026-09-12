Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function Read([string]$p) { [IO.File]::ReadAllText($p) }
function Write([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

# The repo's legacy package graph exposes a ProcessStartInfo surface without
# UseShellExecute. Use the already-supported Process.Start overloads instead.
$p='Model/Vanilla/VanillaUpdater.cs'
$t=Read $p
$t=$t.Replace(',`r`n                UseShellExecute = true','')
$t=$t.Replace(',`n                UseShellExecute = true','')
$t=$t.Replace(', UseShellExecute = true','')
Write $p $t

$p='Model/Vanilla/VanillaIntegratedShell.cs'
$t=Read $p
$old='            Process.Start(new ProcessStartInfo { FileName = VanillaAppData.RootDirectory, UseShellExecute = true });'
if(-not $t.Contains($old)){throw 'Integrated data-folder opener anchor missing.'}
$t=$t.Replace($old,'            Process.Start(VanillaAppData.RootDirectory);')
Write $p $t

Write-Host '0.6.1 compatibility patch applied.'
