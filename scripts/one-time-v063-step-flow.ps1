Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$t=ReadText $p
$start=$t.IndexOf('        private void RunStepTest(VanillaReconnectTestStep step)')
$end=$t.IndexOf('        private void WaitForDiagnosticStep(', $start)
if($start -lt 0 -or $end -le $start){ throw 'RunStepTest block not found.' }
$replacement=@'
        private void RunStepTest(VanillaReconnectTestStep step)
        {
            var selected = SelectedAccount();
            if (selected == null) { MessageBox.Show(this, "Select one account row first.", "Step test"); return; }
            try
            {
                // A newer numbered step deliberately supersedes any previous diagnostic wait.
                // This is important when the user manually advances the game while step 1 is
                // still waiting for the launcher result.
                if (testRunning)
                {
                    testRunning = false;
                    testGeneration++;
                }

                ReadTop();
                supervisor.Apply(settings, true);
                // Always reset supervisor runtime flags before a manual step, even when the
                // continuous supervisor itself is already stopped.
                supervisor.Stop();

                var live = Process.GetProcessesByName("Vanilla MMO");
                int liveCount;
                try { liveCount = live.Length; }
                finally { foreach (var process in live) process.Dispose(); }
                if (liveCount > 1)
                    throw new InvalidOperationException("One-client diagnostics found multiple Vanilla clients. Leave only the client you want to test running.");

                if (liveCount == 1)
                {
                    supervisor.AssignSingleDiagnosticClient(selected.Id);
                    if (step == VanillaReconnectTestStep.LauncherGameStart)
                    {
                        int already = BeginTest("STEP 1 already satisfied for " + selected.Label);
                        CompleteTest(already, true, "A Vanilla client is already running. GAME START is therefore already satisfied; press the next numbered step to test only that stage.");
                        return;
                    }

                    int generation = BeginTest("STEP TEST: " + step + " for " + selected.Label);
                    supervisor.RunDiagnosticStep(selected.Id, step);
                    WaitForDiagnosticStep(generation, selected.Id, step, 45000);
                    return;
                }

                // No Vanilla window exists: run the numbered diagnostic from the beginning.
                // This lets every button work independently instead of requiring the user to
                // remember which earlier test left the client on which screen.
                int chainGeneration = BeginTest("CHAIN TEST from GAME START through " + step + " for " + selected.Label);
                RunDiagnosticChainFromScratch(chainGeneration, selected.Id, step);
            }
            catch (Exception ex) { FailTestImmediately("Step test " + step, ex); }
        }

        private void RunDiagnosticChainFromScratch(int generation, string accountId, VanillaReconnectTestStep targetStep)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    for (int value = (int)VanillaReconnectTestStep.LauncherGameStart; value <= (int)targetStep; value++)
                    {
                        if (generation != testGeneration) return;
                        var step = (VanillaReconnectTestStep)value;
                        if (step != VanillaReconnectTestStep.LauncherGameStart) supervisor.AssignSingleDiagnosticClient(accountId);
                        supervisor.RunDiagnosticStep(accountId, step);
                        string failure;
                        if (!WaitForDiagnosticCompletion(accountId, step, step == VanillaReconnectTestStep.LauncherGameStart ? 150000 : 45000, out failure))
                        {
                            CompleteTest(generation, false, failure);
                            return;
                        }
                        if (step == targetStep)
                        {
                            CompleteTest(generation, true, "Chain test passed through " + targetStep + ". If the screen is correct, continue with the next numbered step.");
                            return;
                        }

                        int delay = step == VanillaReconnectTestStep.LauncherGameStart
                            ? Math.Max(1000, settings.GepardWaitMs)
                            : step == VanillaReconnectTestStep.SelectCharacter
                                ? Math.Max(1000, settings.GameLoadMs)
                                : Math.Max(500, settings.StageDelayMs);
                        int waited = 0;
                        while (waited < delay)
                        {
                            if (generation != testGeneration) return;
                            int slice = Math.Min(250, delay - waited);
                            Thread.Sleep(slice);
                            waited += slice;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CompleteTest(generation, false, "Chain test stopped: " + ex.Message);
                }
            });
        }

        private bool WaitForDiagnosticCompletion(string accountId, VanillaReconnectTestStep step, int timeoutMs, out string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            string successDetail = "Diagnostic step completed: " + step;
            string failurePrefix = "Diagnostic step failed:";
            while (DateTime.UtcNow < deadline)
            {
                var current = supervisor.Statuses().FirstOrDefault(s => string.Equals(s.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    if (string.Equals(current.Detail, successDetail, StringComparison.Ordinal))
                    {
                        failure = null;
                        return true;
                    }
                    if (current.Detail != null && current.Detail.StartsWith(failurePrefix, StringComparison.Ordinal))
                    {
                        failure = current.Detail;
                        return false;
                    }
                }
                Thread.Sleep(250);
            }
            failure = "Step " + step + " timed out. Press COPY LOG and paste the reconnect log for analysis.";
            return false;
        }

'@
$t=$t.Substring(0,$start)+$replacement+$t.Substring($end)
$t=$t.Replace('Selected account only. Close Vanilla first. This opens the launcher, visually locates the yellow GAME START button, clicks it with foreground input, and waits for Vanilla/Gepard. It stops there.', 'If no Vanilla client exists, test GAME START from the launcher. If a client already exists, step 1 is already satisfied.')
$t=$t.Replace('Selected account only. With one Vanilla client running, choose the configured proxy on Select Service.', 'If one Vanilla client exists, test only Proxy. If none exists, automatically run from GAME START through Proxy.')
$t=$t.Replace('Selected account only. With one client running at login, explicitly click/replace username FIRST, Tab to password, replace password, and DO NOT submit so you can visually verify both fields.', 'Existing client: test only credential filling. No client: automatically run all earlier numbered steps first. Credentials are filled without submitting.')
$t=$t.Replace('Selected account only. Press Enter once to submit the credentials currently visible on the login screen.', 'Existing client: submit only. No client: automatically run all earlier numbered steps first, then submit.')
$t=$t.Replace('Selected account only. Select the first/only game server and press Enter.', 'Existing client: server step only. No client: automatically run all earlier numbered steps first.')
$t=$t.Replace('Selected account only. Click the configured character slot and character-screen Game Start.', 'Existing client: character step only. No client: automatically run all earlier numbered steps first.')
$t=$t.Replace('Selected account only. Focus that exact Vanilla window and send the configured Autobattle resume hotkey once, e.g. Alt+2.', 'Existing client: resume hotkey only. No client: automatically run the complete numbered sequence first.')
WriteText $p $t
Write-Host 'Independent diagnostic step flow applied.'
