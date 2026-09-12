Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)

function Replace-Once([string] $Path, [string] $Old, [string] $New) {
    $text = [IO.File]::ReadAllText($Path)
    $first = $text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Expected fragment not found in $Path`: $Old" }
    if ($text.IndexOf($Old, $first + $Old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected fragment is not unique in $Path`: $Old"
    }
    $text = $text.Substring(0, $first) + $New + $text.Substring($first + $Old.Length)
    [IO.File]::WriteAllText($Path, $text, $utf8)
}

$path = 'Model/Vanilla/VanillaReconnect.cs'
$text = [IO.File]::ReadAllText($path)
$pattern = '(?s)        private void Launch\(Runtime runtime, DateTimeOffset now\)\s*\{.*?(?=        private void Bind\(Runtime runtime, int pid, bool freshLaunch, string detail\))'
$regex = [regex]::new($pattern)
$matches = $regex.Matches($text)
if ($matches.Count -ne 1) { throw "Expected exactly one Launch method, found $($matches.Count)." }
$newLaunch = @'
        private void Launch(Runtime runtime, DateTimeOffset now)
        {
            string executable = settings.LaunchExecutable;
            string arguments = settings.LaunchArguments ?? "";
            string accountId = runtime.Account.Id;
            string label = runtime.Account.Label;
            runtime.LastLaunch = now;
            runtime.ResumeSent = false;
            runtime.ScriptRunning = true;
            SetStage(runtime, VanillaReconnectStage.Launching,
                VanillaPatcherLauncher.IsPatcher(executable)
                    ? "Starting patcher.exe and waiting for GAME START"
                    : "Starting configured Vanilla executable");

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                bool aborted = false;
                try
                {
                    VanillaPatcherLauncher.Launch(executable, arguments,
                        message => Log(label + ": " + message),
                        () =>
                        {
                            lock (gate)
                            {
                                aborted = disposed || !running;
                                return aborted;
                            }
                        });
                }
                catch (Exception ex) { error = ex.Message; }

                lock (gate)
                {
                    Runtime current;
                    if (!runtimes.TryGetValue(accountId, out current)) return;
                    current.ScriptRunning = false;
                    if (aborted || disposed || !running)
                    {
                        SetStage(current, VanillaReconnectStage.Stopped, "Supervisor stopped");
                    }
                    else if (error == null)
                    {
                        SetStage(current, VanillaReconnectStage.WaitingForWindow,
                            "Launcher completed; waiting for Vanilla MMO window");
                    }
                    else
                    {
                        SetStage(current, VanillaReconnectStage.Backoff, "Launch failed: " + error);
                        Log(label + ": launch failed: " + error);
                    }
                }
                RaiseUpdated();
            });
        }

'@
$text = $regex.Replace($text, $newLaunch, 1)

$oldCanLaunch = 'if (!settings.AutoRecover || aliveCount >= settings.MaxClients || runtime.ScriptRunning) return false;'
$newCanLaunch = 'if (!settings.AutoRecover || aliveCount >= settings.MaxClients || runtime.ScriptRunning || runtimes.Values.Any(r => r.ScriptRunning && !r.ProcessId.HasValue)) return false;'
if (-not $text.Contains($oldCanLaunch)) { throw 'CanLaunch guard was not found.' }
$text = $text.Replace($oldCanLaunch, $newCanLaunch)

$oldDetect = 'copy.LaunchExecutable = path;'
$newDetect = 'copy.LaunchExecutable = VanillaPatcherLauncher.PreferPatcherBesideClient(path);'
if (-not $text.Contains($oldDetect)) { throw 'Running-client path assignment was not found.' }
$text = $text.Replace($oldDetect, $newDetect)

$oldLabel = 'Text = "Vanilla launch EXE"'
$newLabel = 'Text = "Launcher EXE (patcher.exe recommended)"'
if (-not $text.Contains($oldLabel)) { throw 'Launcher path label was not found.' }
$text = $text.Replace($oldLabel, $newLabel)

$oldButton = 'AddButton(pathRow, "Use running client path", DetectPath);'
$newButton = 'AddButton(pathRow, "Use patcher from running client", DetectPath);'
if (-not $text.Contains($oldButton)) { throw 'Detect-path button was not found.' }
$text = $text.Replace($oldButton, $newButton)
[IO.File]::WriteAllText($path, $text, $utf8)

Replace-Once 'Tests/Vanilla.Diagnostics.Tests.csproj' '<Compile Include="LegacyProfileTests.cs" />' "<Compile Include=`"LegacyProfileTests.cs`" />`r`n    <Compile Include=`"VanillaPatcherLauncherTests.cs`" />"
Replace-Once 'Tests/Program.cs' 'failed += LegacyProfileTests.Run();' "failed += LegacyProfileTests.Run();`r`n            failed += VanillaPatcherLauncherTests.Run();"

Replace-Once 'Properties/AssemblyInfo.cs' '[assembly: AssemblyVersion("0.3.0.0")]' '[assembly: AssemblyVersion("0.4.0.0")]'
Replace-Once 'Properties/AssemblyInfo.cs' '[assembly: AssemblyFileVersion("0.3.0.0")]' '[assembly: AssemblyFileVersion("0.4.0.0")]'
Replace-Once 'Forms/Container.cs' 'this.Text = "4RTools - Vanilla extension v0.3.0";' 'this.Text = "4RTools - Vanilla extension v0.4.0";'
Replace-Once 'Program.cs' 'Success = true, Version = "0.3.0", PointerBytes = IntPtr.Size,' 'Success = true, Version = "0.4.0", PointerBytes = IntPtr.Size,'

$notesPath = 'RELEASE-NOTES.md'
$notes = [IO.File]::ReadAllText($notesPath)
if (-not $notes.Contains('# 4RTools Vanilla 0.3.0')) { throw '0.3.0 release heading not found.' }
$notes = $notes.Replace('# 4RTools Vanilla 0.3.0', '# 4RTools Vanilla 0.4.0')
$oldSection = '## New in 0.3.0: overnight restart and relog recovery'
if (-not $notes.Contains($oldSection)) { throw '0.3.0 feature section not found.' }
$newSection = @'
## New in 0.4.0: patcher startup and GitHub Releases

Vanilla recovery now launches the configured executable exactly as selected. When that path is `patcher.exe`, 4RTools waits for the patcher window, clicks its visible **GAME START** button at normalized window coordinates, and retries while patching is still in progress. The retry loop stops immediately when a new `Vanilla MMO.exe` process appears. Launches are serialized so two managed accounts do not race the same patcher. The existing path picker now prefers `patcher.exe` beside a running Vanilla client when available; direct-client launching remains supported.

Release distribution is also moved to normal **GitHub Releases**. Every version bump runs a dedicated Windows release workflow that rebuilds the x86 executable, runs the full offline test suite, creates and smoke-tests the portable ZIP, verifies checksums, and only then publishes the ZIP plus SHA256 file as GitHub Release assets. Portable ZIPs remain CI/release artifacts and are not committed to the source tree.

## New in 0.3.0: overnight restart and relog recovery
'@
$notes = $notes.Replace($oldSection, $newSection.TrimEnd())
$notes = $notes.Replace('The intended 0.3.0 output is:', 'The intended 0.4.0 output is:')
$notes = $notes.Replace('dist/4RTools-Vanilla-v0.3.0/', 'dist/4RTools-Vanilla-v0.4.0/')
$notes = $notes.Replace('dist/4RTools-Vanilla-v0.3.0-portable.zip', 'dist/4RTools-Vanilla-v0.4.0-portable.zip')
[IO.File]::WriteAllText($notesPath, $notes, $utf8)

$readmePath = 'packaging/README.txt'
$readme = [IO.File]::ReadAllText($readmePath)
$oldQuick = @'
3. The Vanilla Restart & Relog manager opens on first use. If Vanilla is already
   running, choose "Use running client path"; otherwise browse to the Vanilla
   executable/launcher that normally starts the protected client.
'@
$newQuick = @'
3. The Vanilla Restart & Relog manager opens on first use. Set the launcher path
   to Vanilla's patcher.exe. If a client is already running, choose "Use patcher
   from running client"; it prefers patcher.exe beside Vanilla MMO.exe.
'@
$oldQuick = $oldQuick.TrimStart("`r", "`n")
$newQuick = $newQuick.TrimStart("`r", "`n")
if (-not $readme.Contains($oldQuick)) { throw 'Packaging quick-start launcher text not found.' }
$readme = $readme.Replace($oldQuick, $newQuick)
$marker = "The supervisor can relaunch a closed Vanilla client, wait for the Gepard/client`r`nstartup path"
$newLine = "`r`n"
if (-not $readme.Contains($marker)) {
    $marker = "The supervisor can relaunch a closed Vanilla client, wait for the Gepard/client`nstartup path"
    $newLine = "`n"
}
if (-not $readme.Contains($marker)) { throw 'Packaging supervisor paragraph not found.' }
$patcherParagraph = "When patcher.exe is configured, the supervisor starts it, waits for its window,$newLine" +
    "clicks the visible GAME START button, and stops retrying as soon as a new Vanilla$newLine" +
    "MMO process appears. Direct executable launching remains supported.$newLine$newLine"
$readme = $readme.Replace($marker, $patcherParagraph + $marker)
$checksumOld = 'SHA256SUMS.txt lists original packaged payload hashes and the ZIP has an adjacent' + $newLine + '.sha256 file.'
$checksumNew = $checksumOld + ' Versioned ZIPs are published by verified GitHub Releases and are not' + $newLine + 'committed to the source tree.'
if (-not $readme.Contains($checksumOld)) { throw 'Packaging checksum paragraph not found.' }
$readme = $readme.Replace($checksumOld, $checksumNew)
[IO.File]::WriteAllText($readmePath, $readme, $utf8)

Remove-Item '.github/workflows/one-time-patcher-release.yml'
Remove-Item 'scripts/one-time-patcher-release.ps1'
