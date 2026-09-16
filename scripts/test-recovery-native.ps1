[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$test = Join-Path $root 'Tests/bin/Release/Vanilla.Diagnostics.Tests.exe'
if (-not (Test-Path -LiteralPath $test)) { throw 'Build Release tests before native recovery validation.' }
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
& $test --native-recovery-tests 2>&1 | Tee-Object -FilePath (Join-Path $root 'dist/native-recovery.log')
if ($LASTEXITCODE -ne 0) { throw 'Native recovery tests failed.' }
