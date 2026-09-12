Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$nl = [Environment]::NewLine

function Fix-LiteralNewlines([string]$path) {
    $text = [IO.File]::ReadAllText($path)
    $text = $text.Replace('`r`n', $nl)
    [IO.File]::WriteAllText($path, $text, $utf8)
}

Fix-LiteralNewlines 'Model/Vanilla/VanillaIntegratedShell.cs'
Fix-LiteralNewlines 'Tests/VanillaPatcherLauncherTests.cs'

$p = 'Model/Vanilla/VanillaIntegratedShell.cs'
$t = [IO.File]::ReadAllText($p)
$old = '            ExpandForIntegratedWorkspace();' + $nl + '            BuildIntegratedVanillaWorkspace();'
$new = '            ExpandForIntegratedWorkspace();' + $nl + '            BuildPrimaryWorkspaceShell();' + $nl + '            BuildIntegratedVanillaWorkspace();'
if ($t.Contains($old)) { $t = $t.Replace($old, $new) }
if (-not $t.Contains('            BuildPrimaryWorkspaceShell();')) { throw 'Primary Vanilla workspace call was not installed.' }
if ($t.Contains('`r`n')) { throw 'Literal newline escapes remain in VanillaIntegratedShell.cs.' }
[IO.File]::WriteAllText($p, $t, $utf8)

$p = 'Tests/VanillaPatcherLauncherTests.cs'
$t = [IO.File]::ReadAllText($p)
if ($t.Contains('`r`n')) { throw 'Literal newline escapes remain in VanillaPatcherLauncherTests.cs.' }
[IO.File]::WriteAllText($p, $t, $utf8)

Write-Host '0.6.2 generated source fixup applied.'
