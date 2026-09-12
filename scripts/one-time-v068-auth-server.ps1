Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p){ [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t){ [IO.File]::WriteAllText($p,$t,$utf8) }

$path='Model/Vanilla/VanillaReconnect.cs'
$text=ReadText $path
$old=@'
                    input.Activate();
                    input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);
                    Thread.Sleep(180);
                    input.ReplaceFocusedText(account.UserName);
                    input.Press(Keys.Tab);
                    Thread.Sleep(180);
                    input.ReplaceFocusedText(password);
                    Thread.Sleep(220);
                    input.Press(Keys.Enter);
                    Thread.Sleep(config.StageDelayMs);

                    input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                    input.Press(Keys.Home);
                    input.Press(Keys.Enter);
                    Thread.Sleep(config.StageDelayMs);
'@
$new=@'
                    input.Activate();
                    FillDetectedCredentials(input, account, password, pid, true, account.Label + ": ");
                    Thread.Sleep(config.StageDelayMs);

                    SelectDetectedGameServer(input, pid, config.StageDelayMs, account.Label + ": ");
'@
if(-not $text.Contains($old)){ throw 'Production credentials/server anchor missing.' }
$text=$text.Replace($old,$new)

$anchor=@'
        private static void WaitForWindow(int pid, int timeoutMs)
        {
'@
$helpers=@'
        private void FillDetectedCredentials(VanillaForegroundInput input, VanillaReconnectAccount account, string password,
            int pid, bool submit, string logPrefix)
        {
            if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Username/password is missing.");

            VanillaLoginLayout layout;
            string evidence;
            using (Bitmap image = WaitForLoginUi(input, 15000, out layout, out evidence))
            {
                SaveUiCapture(image, "login-screen-last.png");
                Point userPoint = VanillaAuthPattern.PickInside(layout.UserName, unchecked(Environment.TickCount ^ pid ^ 0x41A7));
                Point passwordPoint = VanillaAuthPattern.PickInside(layout.Password, unchecked(Environment.TickCount ^ pid ^ 0x6D2B));
                input.ClickNormalized((userPoint.X + 0.5) / image.Width, (userPoint.Y + 0.5) / image.Height);
                Thread.Sleep(160);
                input.ReplaceFocusedText(account.UserName);
                Thread.Sleep(140);
                input.ClickNormalized((passwordPoint.X + 0.5) / image.Width, (passwordPoint.Y + 0.5) / image.Height);
                Thread.Sleep(160);
                input.ReplaceFocusedText(password);
                Log(logPrefix + "credentials filled by two separately detected fields; usernamePoint=" + userPoint
                    + "; passwordPoint=" + passwordPoint + "; " + evidence + ". Password was not logged.");
            }
            if (submit)
            {
                Thread.Sleep(180);
                input.Press(Keys.Enter);
            }
        }

        private void SelectDetectedGameServer(VanillaForegroundInput input, int pid, int stageDelayMs, string logPrefix)
        {
            VanillaServerLayout layout;
            string evidence;
            using (Bitmap image = WaitForServerUi(input, 15000, out layout, out evidence))
            {
                SaveUiCapture(image, "server-screen-last.png");
                Point rowPoint = VanillaAuthPattern.PickInside(layout.ServerRow, unchecked(Environment.TickCount ^ pid ^ 0x27D4));
                input.ClickNormalized((rowPoint.X + 0.5) / image.Width, (rowPoint.Y + 0.5) / image.Height);
                Thread.Sleep(160);
                // The Vanilla list owns keyboard focus after the verified row click. Home clamps to
                // the only/first game server without relying on a fixed coordinate, then Enter confirms.
                input.Press(Keys.Home);
                Thread.Sleep(90);
                input.Press(Keys.Enter);
                Log(logPrefix + "game server selected from detected first-row safe area at " + rowPoint + "; " + evidence);
            }

            int settle = Math.Max(900, Math.Min(stageDelayMs, 3000));
            Thread.Sleep(settle);
            using (Bitmap verify = input.CaptureClientBitmap())
            {
                VanillaServerLayout stillThere;
                string verifyEvidence;
                if (VanillaAuthPattern.TryDetectServerDialog(verify, out stillThere, out verifyEvidence))
                {
                    SaveUiCapture(verify, "server-screen-still-open.png");
                    Point retryPoint = VanillaAuthPattern.PickInside(stillThere.ServerRow, unchecked(Environment.TickCount ^ pid ^ 0x51F3));
                    input.ClickNormalized((retryPoint.X + 0.5) / verify.Width, (retryPoint.Y + 0.5) / verify.Height);
                    Thread.Sleep(140);
                    input.Press(Keys.Enter);
                    Thread.Sleep(Math.Max(700, settle / 2));
                    using (Bitmap secondVerify = input.CaptureClientBitmap())
                    {
                        VanillaServerLayout finalDialog;
                        string finalEvidence;
                        if (VanillaAuthPattern.TryDetectServerDialog(secondVerify, out finalDialog, out finalEvidence))
                        {
                            SaveUiCapture(secondVerify, "server-screen-failed.png");
                            throw new InvalidOperationException("Server dialog remained after two verified row selections; no further input was sent. " + finalEvidence);
                        }
                    }
                    Log(logPrefix + "server dialog required one verified retry and then closed.");
                }
            }
        }

        private Bitmap WaitForLoginUi(VanillaForegroundInput input, int timeoutMs, out VanillaLoginLayout layout, out string evidence)
        {
            Stopwatch watch = Stopwatch.StartNew();
            string last = "not sampled";
            Bitmap lastImage = null;
            try
            {
                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    if (lastImage != null) { lastImage.Dispose(); lastImage = null; }
                    lastImage = input.CaptureClientBitmap();
                    if (VanillaAuthPattern.TryDetectLogin(lastImage, out layout, out evidence))
                    {
                        Bitmap result = lastImage;
                        lastImage = null;
                        return result;
                    }
                    last = evidence;
                    Thread.Sleep(250);
                }
                if (lastImage != null) SaveUiCapture(lastImage, "login-screen-not-detected.png");
                throw new InvalidOperationException("Login username/password controls were not detected confidently; no credentials were typed. " + last);
            }
            finally { if (lastImage != null) lastImage.Dispose(); }
        }

        private Bitmap WaitForServerUi(VanillaForegroundInput input, int timeoutMs, out VanillaServerLayout layout, out string evidence)
        {
            Stopwatch watch = Stopwatch.StartNew();
            string last = "not sampled";
            Bitmap lastImage = null;
            try
            {
                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    if (lastImage != null) { lastImage.Dispose(); lastImage = null; }
                    lastImage = input.CaptureClientBitmap();
                    if (VanillaAuthPattern.TryDetectServerDialog(lastImage, out layout, out evidence))
                    {
                        Bitmap result = lastImage;
                        lastImage = null;
                        return result;
                    }
                    last = evidence;
                    Thread.Sleep(250);
                }
                if (lastImage != null) SaveUiCapture(lastImage, "server-screen-not-detected.png");
                throw new InvalidOperationException("The single-server dialog was not detected confidently; no server-selection input was sent. " + last);
            }
            finally { if (lastImage != null) lastImage.Dispose(); }
        }

        private void SaveUiCapture(Bitmap image, string fileName)
        {
            try
            {
                string directory = Path.Combine(baseDirectory, "Logs");
                Directory.CreateDirectory(directory);
                image.Save(Path.Combine(directory, fileName), ImageFormat.Png);
            }
            catch { }
        }

'@
if(-not $text.Contains($anchor)){ throw 'WaitForWindow insertion anchor missing.' }
$text=$text.Replace($anchor,$helpers+$anchor)
WriteText $path $text

$path='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$text=ReadText $path
$old=@'
                            case VanillaReconnectTestStep.FillCredentials:
                                string password = store.UnprotectPassword(account.ProtectedPassword);
                                if (string.IsNullOrEmpty(account.UserName) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Username/password is missing.");
                                input.ClickNormalized(config.Anchors.UserNameX, config.Anchors.UserNameY);
                                Thread.Sleep(180);
                                input.ReplaceFocusedText(account.UserName);
                                input.Press(Keys.Tab);
                                Thread.Sleep(180);
                                input.ReplaceFocusedText(password);
                                Log("TEST " + account.Label + ": username then password filled without submitting; password was not logged.");
                                break;
'@
$new=@'
                            case VanillaReconnectTestStep.FillCredentials:
                                string password = store.UnprotectPassword(account.ProtectedPassword);
                                FillDetectedCredentials(input, account, password, discoveredPid.Value, false, "TEST " + account.Label + ": ");
                                break;
'@
if(-not $text.Contains($old)){ throw 'Diagnostic credentials anchor missing.' }
$text=$text.Replace($old,$new)
$old=@'
                            case VanillaReconnectTestStep.SelectGameServer:
                                input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                                input.Press(Keys.Home);
                                input.Press(Keys.Enter);
                                break;
'@
$new=@'
                            case VanillaReconnectTestStep.SelectGameServer:
                                SelectDetectedGameServer(input, discoveredPid.Value, config.StageDelayMs, "TEST " + account.Label + ": ");
                                break;
'@
if(-not $text.Contains($old)){ throw 'Diagnostic server anchor missing.' }
$text=$text.Replace($old,$new)
WriteText $path $text

$path='Tests/Vanilla.Diagnostics.Tests.csproj'
$text=ReadText $path
$anchor='    <Compile Include="VanillaProxyPatternTests.cs" />'
if(-not $text.Contains($anchor)){ throw 'Test csproj proxy anchor missing.' }
if(-not $text.Contains('VanillaAuthPatternTests.cs')){
    $text=$text.Replace($anchor,$anchor+[Environment]::NewLine+'    <Compile Include="VanillaAuthPatternTests.cs" />')
}
WriteText $path $text

$path='Tests/Program.cs'
$text=ReadText $path
$anchor='            failed += VanillaProxyPatternTests.Run();'
if(-not $text.Contains($anchor)){ throw 'Program proxy suite anchor missing.' }
if(-not $text.Contains('VanillaAuthPatternTests.Run()')){
    $text=$text.Replace($anchor,$anchor+[Environment]::NewLine+'            failed += VanillaAuthPatternTests.Run();')
}
WriteText $path $text

Write-Host '0.6.8 auth/server reliability wiring applied.'
