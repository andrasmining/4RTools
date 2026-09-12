Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p) { [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t) { [IO.File]::WriteAllText($p,$t,$utf8) }
function ReplaceExact([string]$p,[string]$old,[string]$new) {
  $t=ReadText $p
  if(-not $t.Contains($old)){ throw "Anchor not found in $p`n$old" }
  WriteText $p ($t.Replace($old,$new))
}

$p='Model/Vanilla/VanillaIntegratedShell.cs'
$t=ReadText $p
if(-not $t.Contains('using System.Linq;')) { $t=$t.Replace("using System.IO;`r`n", "using System.IO;`r`nusing System.Linq;`r`n") }
$t=$t.Replace(
'        private TabPage vanillaRecoveryPage, vanillaRulesPage, vanillaDiagnosticsPage, vanillaAboutPage;',
'        private TabPage vanillaRecoveryPage, vanillaRulesPage, vanillaDiagnosticsPage, vanillaAboutPage;`r`n        private TabControl primaryWorkspace;`r`n        private TabPage primaryVanillaPage, primaryLegacyPage;`r`n        private Panel legacySurface;')
$t=$t.Replace(
'            ExpandForIntegratedWorkspace();`r`n            BuildIntegratedVanillaWorkspace();',
'            ExpandForIntegratedWorkspace();`r`n            BuildPrimaryWorkspaceShell();`r`n            BuildIntegratedVanillaWorkspace();')
$pattern='(?s)        private void ExpandForIntegratedWorkspace\(\)\r?\n        \{.*?\r?\n        \}\r?\n\r?\n        private void BuildIntegratedVanillaWorkspace\(\)'
$replacement=@'
        private void ExpandForIntegratedWorkspace()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(1500, Math.Max(1180, area.Width - 30));
            int height = Math.Min(1000, Math.Max(780, area.Height - 30));
            MinimumSize = new Size(Math.Min(1120, width), Math.Min(740, height));
            Size = new Size(width, height);
            StartPosition = FormStartPosition.CenterScreen;
            if (!smokeTest) WindowState = FormWindowState.Maximized;
        }

        private void BuildPrimaryWorkspaceShell()
        {
            if (primaryWorkspace != null) return;
            Control[] legacyControls = Controls.Cast<Control>().Where(control => !(control is MdiClient)).ToArray();
            legacySurface = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White,
                AutoScrollMinSize = new Size(920, 650)
            };
            foreach (Control control in legacyControls)
            {
                Controls.Remove(control);
                legacySurface.Controls.Add(control);
            }

            primaryWorkspace = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(18, 7),
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };
            primaryVanillaPage = new TabPage("Vanilla") { Padding = new Padding(8), UseVisualStyleBackColor = true };
            primaryLegacyPage = new TabPage("Original 4RTools") { Padding = new Padding(4), UseVisualStyleBackColor = true };
            primaryLegacyPage.Controls.Add(legacySurface);
            primaryWorkspace.TabPages.Add(primaryVanillaPage);
            primaryWorkspace.TabPages.Add(primaryLegacyPage);
            primaryWorkspace.SelectedTab = primaryVanillaPage;
            Controls.Add(primaryWorkspace);
            primaryWorkspace.BringToFront();
        }

        private void BuildIntegratedVanillaWorkspace()
'@
if(-not [regex]::IsMatch($t,$pattern)){ throw 'Could not replace integrated workspace sizing method.' }
$t=[regex]::Replace($t,$pattern,$replacement,1)
$t=$t.Replace('            tabPageVanilla.Controls.Clear();'+[Environment]::NewLine,'')
$t=$t.Replace('            tabPageVanilla.Controls.Add(root);','            primaryVanillaPage.Controls.Add(root);')
WriteText $p $t

$p='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$t=ReadText $p
$pattern='(?s)        private Control BuildStepTests\(\)\r?\n        \{.*?\r?\n        \}\r?\n\r?\n        private void ConfigureStepTestHoverHelp\(\)'
$replacement=@'
        private Control BuildStepTests()
        {
            var box = new GroupBox
            {
                Text = "Step-by-step one-client diagnostic (select one account row first)",
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(8),
                AutoSize = false
            };
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            AddButton(row, "1 GAME START", () => RunStepTest(VanillaReconnectTestStep.LauncherGameStart));
            AddButton(row, "2 PROXY", () => RunStepTest(VanillaReconnectTestStep.ProxySelection));
            AddButton(row, "3 FILL USER/PW", () => RunStepTest(VanillaReconnectTestStep.FillCredentials));
            AddButton(row, "4 SUBMIT LOGIN", () => RunStepTest(VanillaReconnectTestStep.SubmitCredentials));
            AddButton(row, "5 SERVER", () => RunStepTest(VanillaReconnectTestStep.SelectGameServer));
            AddButton(row, "6 CHARACTER", () => RunStepTest(VanillaReconnectTestStep.SelectCharacter));
            AddButton(row, "7 RESUME HOTKEY", () => RunStepTest(VanillaReconnectTestStep.ResumeHotkey));
            box.Controls.Add(row);
            return box;
        }

        private void ConfigureStepTestHoverHelp()
'@
if(-not [regex]::IsMatch($t,$pattern)){ throw 'Could not replace step-test layout.' }
$t=[regex]::Replace($t,$pattern,$replacement,1)
$t=$t.Replace('This opens the launcher, performs one real foreground click on GAME START, and waits for Vanilla/Gepard.', 'This opens the launcher, visually locates the yellow GAME START button, clicks it with foreground input, and waits for Vanilla/Gepard.')
WriteText $p $t

$p='Model/Vanilla/VanillaForegroundInput.cs'
$t=ReadText $p
$old=@'
            Thread.Sleep(80);
            Send(new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
            });
            Thread.Sleep(80);
'@
$new=@'
            Thread.Sleep(120);
            Send(new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } }
            });
            Thread.Sleep(110);
            Send(new[]
            {
                new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
            });
            Thread.Sleep(130);
'@
if(-not $t.Contains($old)){ throw 'Foreground click anchor not found.' }
WriteText $p ($t.Replace($old,$new))

$p='Tests/VanillaPatcherLauncherTests.cs'
$t=ReadText $p
$t=$t.Replace('            Test("Patcher beside Vanilla client is preferred when present", PreferAdjacentPatcher);', '            Test("Patcher beside Vanilla client is preferred when present", PreferAdjacentPatcher);`r`n            Test("GAME START visual detector is available", VisualDetectorAvailable);')
$anchor='        private static Type LauncherType()'
$method=@'
        private static void VisualDetectorAvailable()
        {
            MethodInfo method = LauncherType().GetMethod("TryFindGameStart", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(method != null, "TryFindGameStart helper is missing.");
            object[] args = { null, 0d, 0d, null };
            Assert(!(bool)method.Invoke(null, args), "A null launcher image must not produce a GAME START candidate.");
        }

'@
if(-not $t.Contains($anchor)){ throw 'Launcher test anchor not found.' }
$t=$t.Replace($anchor,$method+$anchor)
WriteText $p $t

Write-Host '0.6.2 UI and launcher fixes applied.'
