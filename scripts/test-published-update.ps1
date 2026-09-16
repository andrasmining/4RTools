[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string] $Version,
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')][string] $BaselineTag = 'v0.6.39'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne 'andrasmining/4RTools' -or -not $env:RUNNER_TEMP) {
    throw 'Published-update validation requires the repository Windows Actions runner.'
}
if (Test-Path Env:GH_TOKEN) { $token = $env:GH_TOKEN } else { $token = $null }
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Published-update validation requires the workflow GitHub token.' }
if ([Environment]::Is64BitProcess) {
    $host32 = Join-Path $env:WINDIR 'SysWOW64/WindowsPowerShell/v1.0/powershell.exe'
    & $host32 -NoProfile -NonInteractive -File $PSCommandPath -Version $Version -BaselineTag $BaselineTag
    if ($LASTEXITCODE -ne 0) { throw 'The x86 published-updater probe failed.' }
    return
}
if ([version]$BaselineTag.Substring(1) -ge [version]$Version) { throw 'Updater baseline must be older than the release.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{
    'User-Agent' = '4RTools-Release-Verification'
    Accept = 'application/vnd.github+json'
    Authorization = 'Bearer ' + $token.Trim()
    'X-GitHub-Api-Version' = '2022-11-28'
}
$api = 'https://api.github.com/repos/andrasmining/4RTools/releases'
$baseline = Invoke-RestMethod "$api/tags/$BaselineTag" -Headers $headers -TimeoutSec 30
if ($baseline.draft -or $baseline.prerelease) { throw 'Updater baseline must be a published stable release.' }
$name = "4RTools-Vanilla-$BaselineTag-portable.zip"
$work = Join-Path $env:RUNNER_TEMP ('updater-probe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
foreach ($assetName in @($name, "$name.sha256")) {
    $asset = @($baseline.assets | Where-Object { $_.name -ceq $assetName })
    if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded') { throw "Missing baseline asset $assetName." }
    $uri = [uri]$asset[0].browser_download_url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or
        -not $uri.AbsolutePath.StartsWith('/andrasmining/4RTools/releases/download/')) { throw 'Unexpected baseline download origin.' }
    Invoke-WebRequest $uri -OutFile (Join-Path $work $assetName) -UseBasicParsing -TimeoutSec 120
}
$zip = Join-Path $work $name
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Content "$zip.sha256" -Raw).Trim() -notmatch ('^' + $hash + '\s+\*?' + [regex]::Escape($name) + '$')) {
    throw 'The published baseline checksum does not match.'
}
Expand-Archive $zip (Join-Path $work 'baseline')
$executables = @(Get-ChildItem (Join-Path $work 'baseline') -Filter '4RTools-Vanilla.exe' -Recurse)
if ($executables.Count -ne 1) { throw 'Expected one baseline executable.' }
$assembly = [Reflection.Assembly]::LoadFrom($executables[0].FullName)
$updater = $assembly.GetType('_4RTools.Model.Vanilla.VanillaUpdater', $true)
$current = $updater.GetProperty('CurrentVersion').GetValue($null, $null)
if ($current -ne [version]$BaselineTag.Substring(1)) { throw 'Baseline executable version mismatch.' }

# Keep this a test of the previously published binary's real CheckAsync. Its
# private static HttpClient is retrieved, not replaced; the disposable Actions
# runner adds only an Authorization header so GitHub's shared anonymous quota
# cannot invalidate the probe. The published baseline executable bytes remain
# untouched and normal user/VPS requests remain anonymous.
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$httpField = $updater.GetField('Http', $flags)
if ($null -eq $httpField) { throw 'Published updater HTTP client could not be located.' }
$http = $httpField.GetValue($null)
if ($null -eq $http) { throw 'Published updater HTTP client is unavailable.' }
if ($null -eq $http.DefaultRequestHeaders.Authorization) {
    $http.DefaultRequestHeaders.TryAddWithoutValidation('Authorization', 'Bearer ' + $token.Trim()) | Out-Null
}

$task = $updater.GetMethod('CheckAsync').Invoke($null, $null)
if (-not $task.Wait(30000)) { throw 'Published updater check timed out.' }
$info = $task.Result
if ($null -eq $info -or $info.TagName -cne "v$Version" -or $info.Version -ne [version]$Version) {
    throw 'The installed-version updater cannot discover the intended new release.'
}
$latest = Invoke-RestMethod "$api/latest" -Headers $headers -TimeoutSec 30
$expectedName = "4RTools-Vanilla-v$Version-portable.zip"
$expectedZip = "https://github.com/andrasmining/4RTools/releases/download/v$Version/$expectedName"
if ($latest.draft -or $latest.prerelease -or $latest.tag_name -cne "v$Version" -or
    $info.ZipName -cne $expectedName -or $info.ZipUrl -cne $expectedZip -or
    $info.ChecksumUrl -cne "$expectedZip.sha256") { throw 'Latest endpoint or updater asset selection mismatch.' }
$report = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist/published/updater-discovery.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $report) -Force | Out-Null
[ordered]@{
    baselineVersion = $current.ToString(3)
    discoveredVersion = $info.Version.ToString(3)
    latestReleaseId = $latest.id
    zipUrl = $info.ZipUrl
    checksumUrl = $info.ChecksumUrl
    originalUpdaterCheckPassed = $true
    baselineBytesModified = $false
    authenticatedActionsProbe = $true
    publicLatestVerified = $true
    installedOnUserMachine = $false
} | ConvertTo-Json | Out-File $report -Encoding utf8
Write-Host "Published updater verified: $current -> $Version; no user installation was changed."
