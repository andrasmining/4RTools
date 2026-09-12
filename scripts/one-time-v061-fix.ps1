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

# Avoid exception-filter type-patterns here. This project carries an old package/reference
# graph on .NET Framework and the CI compiler resolves those exception types inconsistently.
# Retrying every copy failure is safe: retries are bounded and the update payload is hash-
# verified before and after staging; the final failure is still surfaced to the user.
$old='                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { last = ex; Thread.Sleep(500); }'
$new='                catch (Exception ex) { last = ex; Thread.Sleep(500); }'
if(-not $t.Contains($old)){throw 'Updater retry exception-filter anchor missing.'}
$t=$t.Replace($old,$new)
if ($t -match 'ex\s+is\s+(IOException|UnauthorizedAccessException)') { throw 'Legacy exception type-pattern remained in VanillaUpdater.cs.' }
Write $p $t

$p='Model/Vanilla/VanillaIntegratedShell.cs'
$t=Read $p
$old='            Process.Start(new ProcessStartInfo { FileName = VanillaAppData.RootDirectory, UseShellExecute = true });'
if(-not $t.Contains($old)){throw 'Integrated data-folder opener anchor missing.'}
$t=$t.Replace($old,'            Process.Start(VanillaAppData.RootDirectory);')
if ($t -match 'UseShellExecute') { throw 'UseShellExecute remained in VanillaIntegratedShell.cs after compatibility patch.' }
Write $p $t

# Be deliberately explicit about sibling version discovery. Directory.GetParent on a
# normalized directory string proved brittle in the Windows CI migration regression.
# The release folders are siblings, so inspect the current DirectoryInfo.Parent and
# filter names case-insensitively instead of relying on a wildcard implementation.
$p='Model/Vanilla/VanillaAppData.cs'
$t=Read $p
$old=@'
            DirectoryInfo parent;
            try { parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch { yield break; }
            if (parent == null || !parent.Exists) yield break;
            DirectoryInfo[] siblings;
            try { siblings = parent.GetDirectories("4RTools-Vanilla-v*"); }
            catch { yield break; }
            foreach (var sibling in siblings.OrderByDescending(d => d.LastWriteTimeUtc))
'@
$new=@'
            DirectoryInfo parent;
            try { parent = new DirectoryInfo(current).Parent; }
            catch { yield break; }
            if (parent == null || !parent.Exists) yield break;
            DirectoryInfo[] siblings;
            try
            {
                siblings = parent.GetDirectories()
                    .Where(d => d.Name.StartsWith("4RTools-Vanilla-v", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .ToArray();
            }
            catch { yield break; }
            foreach (var sibling in siblings)
'@
if(-not $t.Contains($old)){throw 'LegacyInstallCandidates sibling-discovery anchor missing.'}
$t=$t.Replace($old,$new)
Write $p $t

Write-Host '0.6.1 compatibility and migration patches applied.'
