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
$filteredCatch='catch\s*\(Exception\s+ex\)\s*when\s*\(ex\s+is\s+IOException\s*\|\|\s*ex\s+is\s+UnauthorizedAccessException\)\s*\{\s*last\s*=\s*ex;\s*Thread\.Sleep\(500\);\s*\}'
$plainCatch='catch (Exception ex) { last = ex; Thread.Sleep(500); }'
if ([Text.RegularExpressions.Regex]::IsMatch($t, $filteredCatch)) {
    $t=[Text.RegularExpressions.Regex]::Replace($t, $filteredCatch, $plainCatch)
}
if (-not $t.Contains($plainCatch)) { throw 'Updater bounded retry catch is missing after compatibility patch.' }
if ($t -match 'ex\s+is\s+(IOException|UnauthorizedAccessException)') { throw 'Legacy exception type-pattern remained in VanillaUpdater.cs.' }
Write $p $t

$p='Model/Vanilla/VanillaIntegratedShell.cs'
$t=Read $p
$old='            Process.Start(new ProcessStartInfo { FileName = VanillaAppData.RootDirectory, UseShellExecute = true });'
$new='            Process.Start(VanillaAppData.RootDirectory);'
if ($t.Contains($old)) { $t=$t.Replace($old,$new) }
if (-not $t.Contains($new)) { throw 'Integrated data-folder opener is missing after compatibility patch.' }
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
if ($t.Contains($old)) { $t=$t.Replace($old,$new) }
if (-not $t.Contains('try { parent = new DirectoryInfo(current).Parent; }') -or -not $t.Contains('.Where(d => d.Name.StartsWith("4RTools-Vanilla-v", StringComparison.OrdinalIgnoreCase))')) {
    throw 'Hardened sibling-release discovery is missing after compatibility patch.'
}
Write $p $t

# Keep AutomationProfileStore honest about its constructor argument. Tests and explicit
# import/export scenarios need an isolated root, while the production session deliberately
# passes the stable per-user data root. This avoids hidden global state in the store and
# still keeps normal user profiles outside versioned application folders.
$p='Model/Vanilla/Automation/AutomationProfileStore.cs'
$t=Read $p
$old=@'
            VanillaAppData.InitializeAndMigrateLegacy(AppDomain.CurrentDomain.BaseDirectory);
            directory = VanillaAppData.VanillaProfilesDirectory;
            Directory.CreateDirectory(directory);
'@
$new=@'
            string root = Path.GetFullPath(baseDirectory);
            directory = Path.Combine(root, "Profiles", "Vanilla");
            Directory.CreateDirectory(directory);
'@
if ($t.Contains($old)) { $t=$t.Replace($old,$new) }
if (-not $t.Contains('directory = Path.Combine(root, "Profiles", "Vanilla");')) { throw 'Automation profile store root patch is missing.' }
if ($t.Contains('directory = VanillaAppData.VanillaProfilesDirectory;')) { throw 'Automation profile store still ignores its constructor root.' }
Write $p $t

$p='Model/Vanilla/Automation/VanillaAutomationSession.cs'
$t=Read $p
$old='            profiles = new AutomationProfileStore(this.baseDirectory);'
$new='            profiles = new AutomationProfileStore(VanillaAppData.RootDirectory);'
if ($t.Contains($old)) { $t=$t.Replace($old,$new) }
if (-not $t.Contains($new)) { throw 'Production automation session does not use persistent user profile storage.' }
Write $p $t

Write-Host '0.6.1 compatibility, migration and persistent-profile patches applied.'
