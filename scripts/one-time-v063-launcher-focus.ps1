Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }

$p='Model/Vanilla/VanillaForegroundInput.cs'
$t=ReadText $p
$old=@'
        public void ClickNormalized(double x, double y)
        {
            Activate();
            if (x < 0 || x > 1 || y < 0 || y > 1) throw new ArgumentOutOfRangeException("Normalized coordinates must be within 0..1.");
'@
$new=@'
        public void ClickNormalized(double x, double y, bool requireForeground = true)
        {
            RefreshWindow();
            if (requireForeground)
            {
                Activate();
            }
            else
            {
                // SetForegroundWindow is intentionally best-effort here. Windows may deny focus
                // when 4RTools did not most recently receive user input, even though the launcher
                // is already visible. A mouse click only needs the launcher restored and raised;
                // the real screen-coordinate click below can then reach GAME START without
                // requiring keyboard focus.
                ShowWindow(window, 9);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(120);
            }
            if (x < 0 || x > 1 || y < 0 || y > 1) throw new ArgumentOutOfRangeException("Normalized coordinates must be within 0..1.");
'@
if(-not $t.Contains($old)){ throw 'ClickNormalized source anchor not found.' }
WriteText $p ($t.Replace($old,$new))

$p='Model/Vanilla/VanillaPatcherLauncher.cs'
$t=ReadText $p
$old='                                    input.ClickNormalized(clickX, clickY);'
$new='                                    input.ClickNormalized(clickX, clickY, requireForeground: false);'
if(-not $t.Contains($old)){ throw 'Launcher click source anchor not found.' }
$t=$t.Replace($old,$new)
$t=$t.Replace('GAME START click sent to launcher PID {0} at normalized ({1:0.000}, {2:0.000}) [{3}]; waiting for Vanilla/Gepard startup before any retry.', 'GAME START click sent to launcher PID {0} at normalized ({1:0.000}, {2:0.000}) [{3}]; foreground focus is best-effort for launcher mouse clicks; waiting for Vanilla/Gepard startup before any retry.')
WriteText $p $t

Write-Host '0.6.3 launcher focus fix applied.'
