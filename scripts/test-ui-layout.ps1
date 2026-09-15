[CmdletBinding()]
param(
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\bin\Release\4RTools-Vanilla.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\ui-layout')
)
$ErrorActionPreference = 'Stop'
$ApplicationPath = [IO.Path]::GetFullPath($ApplicationPath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $ApplicationPath)) { throw "Application missing: $ApplicationPath" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$harness = Join-Path (Split-Path $ApplicationPath -Parent) 'UiLayoutHarness.exe'
$source = Join-Path $PSScriptRoot '..\Tests\UiLayout\Program.cs'
& $csc /nologo /target:exe /platform:x86 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$harness" $source
if ($LASTEXITCODE -ne 0) { throw 'UI layout harness compilation failed.' }
$stdout = Join-Path $OutputDirectory 'harness-stdout.txt'
$stderr = Join-Path $OutputDirectory 'harness-stderr.txt'
$process = Start-Process -FilePath $harness -ArgumentList @(('"{0}"' -f $ApplicationPath), ('"{0}"' -f $OutputDirectory)) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if (-not $process.WaitForExit(120000)) {
    $process.Kill()
    throw 'UI layout harness timed out; refusing to publish an unverified layout.'
}
$process.Refresh()
if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout | Write-Host }
if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr | Write-Host }
if ($process.ExitCode -ne 0) { throw "UI layout checks failed (exit $($process.ExitCode)). See $OutputDirectory." }
if (-not (Test-Path (Join-Path $OutputDirectory 'layout-report.txt'))) { throw 'UI layout report was not produced.' }
Write-Host 'Native Windows UI layout checks passed with mock accounts; no game input or live services were enabled.'
