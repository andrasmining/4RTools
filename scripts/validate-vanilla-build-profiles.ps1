[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$profileDir = Join-Path $root 'VanillaBuilds'
if (-not (Test-Path -LiteralPath $profileDir -PathType Container)) {
    throw "Missing VanillaBuilds directory: $profileDir"
}

$allowed = @{
    CurrentHP         = @('UInt32')
    MaxHP             = @('UInt32')
    CurrentSP         = @('UInt32')
    MaxSP             = @('UInt32')
    CurrentWeight     = @('UInt32')
    MaxWeight         = @('UInt32')
    CharacterName     = @('Utf8')
    UserName          = @('Utf8')
    UserNameMirror    = @('Utf8')
    CharacterSlot     = @('Int32')
    X                 = @('Int32')
    Y                 = @('Int32')
    CurrentTargetId   = @('UInt32', 'UInt64')
    ActionState       = @('UInt32')
    Map               = @('Utf8')
    AutobattleEnabled = @('Boolean8')
    StatusEffects     = @('UInt32Array')
    ClientReady       = @('Boolean8')
    Loading           = @('Boolean8')
}

$profiles = @(Get-ChildItem -LiteralPath $profileDir -Filter '*.json' -File | Sort-Object Name)
if ($profiles.Count -eq 0) { throw 'No Vanilla build profiles were found.' }

foreach ($file in $profiles) {
    $raw = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    try { $profile = $raw | ConvertFrom-Json }
    catch { throw "$($file.Name): invalid JSON: $($_.Exception.Message)" }

    if ($profile.SchemaVersion -ne 1) { throw "$($file.Name): unsupported SchemaVersion $($profile.SchemaVersion)." }
    if ([string]::IsNullOrWhiteSpace([string]$profile.Label)) { throw "$($file.Name): missing Label." }
    if ([string]::IsNullOrWhiteSpace([string]$profile.Sha256) -or [string]$profile.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$($file.Name): invalid Sha256."
    }
    if ($profile.Machine -notin @(332, 34404)) { throw "$($file.Name): unsupported Machine $($profile.Machine)." }
    if ([uint64]$profile.ImageSize -lt 4096) { throw "$($file.Name): invalid ImageSize." }
    if ($null -eq $profile.MemoryMap -or $profile.MemoryMap.SchemaVersion -ne 1) { throw "$($file.Name): invalid MemoryMap." }
    if ([string]::IsNullOrWhiteSpace([string]$profile.MemoryMap.ProcessName)) { throw "$($file.Name): MemoryMap.ProcessName is required." }
    if ($null -eq $profile.MemoryMap.Fields) { throw "$($file.Name): MemoryMap.Fields is required." }

    $fieldProperties = @($profile.MemoryMap.Fields.PSObject.Properties)
    foreach ($property in $fieldProperties) {
        $name = $property.Name
        if (-not $allowed.ContainsKey($name)) { throw "$($file.Name): unknown field '$name'." }
        $mapping = $property.Value
        if ($null -eq $mapping) { throw "$($file.Name): $name mapping is null." }
        $encoding = [string]$mapping.Encoding
        if ($allowed[$name] -notcontains $encoding) {
            throw "$($file.Name): $name uses unsupported encoding $encoding; expected $($allowed[$name] -join ' or ')."
        }
        if ([string]::IsNullOrWhiteSpace([string]$mapping.Address)) { throw "$($file.Name): $name has no Address." }
        if ([string]::IsNullOrWhiteSpace([string]$mapping.Module)) { throw "$($file.Name): $name has no Module." }
        if ([string]$mapping.Module -ne [string]$profile.MemoryMap.ProcessName) {
            throw "$($file.Name): $name Module '$($mapping.Module)' does not match ProcessName '$($profile.MemoryMap.ProcessName)'."
        }
        if ($encoding -in @('Utf8', 'UInt32Array')) {
            $byteCount = [int]$mapping.ByteCount
            if ($byteCount -lt 1 -or $byteCount -gt 256) { throw "$($file.Name): $name has invalid ByteCount $byteCount." }
            if ($encoding -eq 'UInt32Array' -and ($byteCount % 4) -ne 0) { throw "$($file.Name): $name UInt32Array ByteCount must be divisible by 4." }
        }
    }

    $verified = @($profile.VerifiedFields)
    if ($verified.Count -ne (@($verified | Select-Object -Unique)).Count) { throw "$($file.Name): VerifiedFields contains duplicates." }
    foreach ($name in $verified) {
        $mappingProperty = $profile.MemoryMap.Fields.PSObject.Properties[[string]$name]
        if ($null -eq $mappingProperty) { throw "$($file.Name): verified field '$name' has no mapping." }
        if ([string]::IsNullOrWhiteSpace([string]$mappingProperty.Value.Evidence)) { throw "$($file.Name): verified field '$name' has no Evidence." }
    }
    if ($verified -contains 'CurrentTargetId' -and @($profile.NoTargetValues).Count -eq 0) {
        throw "$($file.Name): verified CurrentTargetId requires NoTargetValues."
    }
    if ($verified -contains 'ActionState' -and @($profile.ActionStates).Count -eq 0) {
        throw "$($file.Name): verified ActionState requires ActionStates."
    }

    Write-Host "Validated $($file.Name): $($verified.Count) verified fields."
}

Write-Host "Validated $($profiles.Count) Vanilla build profile(s)."
