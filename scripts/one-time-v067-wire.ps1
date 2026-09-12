Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
function ReadText([string]$p){ [IO.File]::ReadAllText($p) }
function WriteText([string]$p,[string]$t){ [IO.File]::WriteAllText($p,$t,$utf8) }

$path='Model/Vanilla/VanillaForegroundInput.cs'
$text=ReadText $path
$text=$text.Replace('using System.Drawing;' + [Environment]::NewLine, 'using System.Drawing;' + [Environment]::NewLine + 'using System.Drawing.Imaging;' + [Environment]::NewLine + 'using System.IO;' + [Environment]::NewLine)
$anchor=@'
        public void Press(Keys key)
        {
'@
$addition=@'
        internal Bitmap CaptureClientBitmap()
        {
            Activate();
            RECT rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read Vanilla client area for visual recognition.");
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 200 || height < 120) throw new InvalidOperationException("Vanilla client area is too small for visual recognition: " + width + "x" + height);
            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(window, ref origin)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot map Vanilla client for visual recognition.");
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(width, height));
            return bitmap;
        }

'@
if(-not $text.Contains($anchor)){ throw 'Foreground input Press anchor missing.' }
$text=$text.Replace($anchor,$addition+$anchor)
WriteText $path $text

$path='Model/Vanilla/VanillaReconnect.cs'
$text=ReadText $path
$old=@'
                        input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                        input.Press(Keys.Home);
                        for (int i = 0; i < (int)config.Proxy; i++) input.Press(Keys.Down);
                        input.Press(Keys.Enter);
                        Thread.Sleep(config.StageDelayMs);
'@
$new=@'
                        using (Bitmap proxyImage = input.CaptureClientBitmap())
                        {
                            VanillaProxyLayout proxyLayout;
                            string proxyDetection;
                            if (!VanillaProxyPattern.TryDetect(proxyImage, out proxyLayout, out proxyDetection))
                                throw new InvalidOperationException("Proxy list was not detected confidently; no proxy input was sent. " + proxyDetection);
                            string proxyCapture = Path.Combine(baseDirectory, "Logs", "proxy-screen-last.png");
                            try { Directory.CreateDirectory(Path.GetDirectoryName(proxyCapture)); proxyImage.Save(proxyCapture, ImageFormat.Png); } catch { }
                            int routeIndex = (int)config.Proxy;
                            Rectangle safe = proxyLayout.Rows[routeIndex];
                            var random = new Random(unchecked(Environment.TickCount ^ pid ^ (routeIndex * 7919)));
                            int marginX = Math.Max(1, safe.Width / 4), marginY = Math.Max(1, safe.Height / 4);
                            int px = random.Next(safe.Left + marginX, Math.Max(safe.Left + marginX + 1, safe.Right - marginX));
                            int py = random.Next(safe.Top + marginY, Math.Max(safe.Top + marginY + 1, safe.Bottom - marginY));
                            double x = (px + 0.5) / proxyImage.Width;
                            double y = (py + 0.5) / proxyImage.Height;
                            input.ClickNormalized(x, y);
                            for (int i = 0; i < 8; i++) { input.Press(Keys.Up); Thread.Sleep(55); }
                            for (int i = 0; i < routeIndex; i++) { input.Press(Keys.Down); Thread.Sleep(70); }
                            input.Press(Keys.Enter);
                            Log(account.Label + ": proxy " + config.Proxy + " selected from detected safe row " + safe + " at verified-inside point (" + px + "," + py + "); " + proxyDetection);
                        }
                        Thread.Sleep(config.StageDelayMs);
'@
if(-not $text.Contains($old)){ throw 'Production proxy-selection anchor missing.' }
$text=$text.Replace($old,$new)

$fields=@'
        private readonly object gate = new object();
        private readonly string baseDirectory;
        private readonly VanillaReconnectStore store;
'@
$fieldsNew=@'
        private readonly object gate = new object();
        private readonly string baseDirectory;
        private readonly VanillaReconnectStore store;
        private readonly VanillaSessionLog sessionLog;
'@
if(-not $text.Contains($fields)){ throw 'Supervisor field anchor missing.' }
$text=$text.Replace($fields,$fieldsNew)
$ctor=@'
        public VanillaReconnectSupervisor(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory);
            store = new VanillaReconnectStore(this.baseDirectory);
            settings = store.Load();
            RebuildRuntimes();
        }
'@
$ctorNew=@'
        public VanillaReconnectSupervisor(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory);
            sessionLog = new VanillaSessionLog(this.baseDirectory);
            store = new VanillaReconnectStore(this.baseDirectory);
            settings = store.Load();
            RebuildRuntimes();
        }
'@
if(-not $text.Contains($ctor)){ throw 'Supervisor constructor anchor missing.' }
$text=$text.Replace($ctor,$ctorNew)
$text=$text.Replace('        public string LogPath { get { return Path.Combine(baseDirectory, "Logs", "reconnect.log"); } }','        public string LogPath { get { return sessionLog.CurrentPath; } }')
$logOld=@'
        private void Log(string text)
        {
            string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text;
            try
            {
                string dir = Path.Combine(baseDirectory, "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "reconnect.log"), line + Environment.NewLine);
            }
            catch { }
            var handler = Logged;
            if (handler != null) handler(line);
        }
'@
$logNew=@'
        private void Log(string text)
        {
            string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text;
            try { sessionLog.WriteLine(line); } catch { }
            var handler = Logged;
            if (handler != null) handler(line);
        }
'@
if(-not $text.Contains($logOld)){ throw 'Supervisor log method anchor missing.' }
$text=$text.Replace($logOld,$logNew)
$text=$text.Replace('TipByText(this, "OPEN LOG", "Open Logs\\reconnect.log in your default text editor.");','TipByText(this, "OPEN LOG", "Open the current startup session log. A fresh log is created on every 4RTools startup and each part is capped at 10 MB.");')
$text=$text.Replace('TipByText(this, "COPY LOG", "Copy the complete reconnect log to the clipboard for pasting into ChatGPT.");','TipByText(this, "COPY LOG", "Copy the current startup session log part to the clipboard for diagnostics.");')
WriteText $path $text

$path='Model/Vanilla/VanillaReconnectDiagnostics.cs'
$text=ReadText $path
$old=@'
                            case VanillaReconnectTestStep.ProxySelection:
                                input.ClickNormalized(config.Anchors.ServiceListX, config.Anchors.ServiceListY);
                                input.Press(Keys.Home);
                                for (int i = 0; i < (int)config.Proxy; i++) input.Press(Keys.Down);
                                input.Press(Keys.Enter);
                                break;
'@
$new=@'
                            case VanillaReconnectTestStep.ProxySelection:
                                using (Bitmap proxyImage = input.CaptureClientBitmap())
                                {
                                    VanillaProxyLayout proxyLayout;
                                    string proxyDetection;
                                    if (!VanillaProxyPattern.TryDetect(proxyImage, out proxyLayout, out proxyDetection))
                                        throw new InvalidOperationException("Proxy list was not detected confidently; no proxy input was sent. " + proxyDetection);
                                    string proxyCapture = Path.Combine(baseDirectory, "Logs", "proxy-screen-last.png");
                                    try { Directory.CreateDirectory(Path.GetDirectoryName(proxyCapture)); proxyImage.Save(proxyCapture); } catch { }
                                    int routeIndex = (int)config.Proxy;
                                    Rectangle safe = proxyLayout.Rows[routeIndex];
                                    var random = new Random(unchecked(Environment.TickCount ^ discoveredPid.Value ^ (routeIndex * 7919)));
                                    int marginX = Math.Max(1, safe.Width / 4), marginY = Math.Max(1, safe.Height / 4);
                                    int px = random.Next(safe.Left + marginX, Math.Max(safe.Left + marginX + 1, safe.Right - marginX));
                                    int py = random.Next(safe.Top + marginY, Math.Max(safe.Top + marginY + 1, safe.Bottom - marginY));
                                    double x = (px + 0.5) / proxyImage.Width;
                                    double y = (py + 0.5) / proxyImage.Height;
                                    input.ClickNormalized(x, y);
                                    for (int i = 0; i < 8; i++) { input.Press(Keys.Up); Thread.Sleep(55); }
                                    for (int i = 0; i < routeIndex; i++) { input.Press(Keys.Down); Thread.Sleep(70); }
                                    input.Press(Keys.Enter);
                                    Log("TEST " + account.Label + ": proxy " + config.Proxy + " selected from detected safe row " + safe + " at verified-inside point (" + px + "," + py + "); " + proxyDetection);
                                }
                                break;
'@
if(-not $text.Contains($old)){ throw 'Diagnostic proxy-selection anchor missing.' }
$text=$text.Replace($old,$new)
WriteText $path $text

$path='Tests/Vanilla.Diagnostics.Tests.csproj'
$text=ReadText $path
if(-not $text.Contains('<Reference Include="System.Drawing" />')){
    $text=$text.Replace('    <Reference Include="System.Core" />','    <Reference Include="System.Core" />'+[Environment]::NewLine+'    <Reference Include="System.Drawing" />')
}
$testAnchor='    <Compile Include="VanillaPatcherLauncherTests.cs" />'
if(-not $text.Contains($testAnchor)){ throw 'Tests project patcher include missing.' }
if(-not $text.Contains('VanillaProxyPatternTests.cs')){
    $text=$text.Replace($testAnchor,$testAnchor+[Environment]::NewLine+'    <Compile Include="VanillaProxyPatternTests.cs" />'+[Environment]::NewLine+'    <Compile Include="VanillaSessionLogTests.cs" />')
}
WriteText $path $text

$path='Tests/Program.cs'
$text=ReadText $path
$programAnchor='            failed += VanillaPatcherLauncherTests.Run();'
if(-not $text.Contains($programAnchor)){ throw 'Tests Program patcher suite anchor missing.' }
if(-not $text.Contains('VanillaProxyPatternTests.Run()')){
    $text=$text.Replace($programAnchor,$programAnchor+[Environment]::NewLine+'            failed += VanillaProxyPatternTests.Run();'+[Environment]::NewLine+'            failed += VanillaSessionLogTests.Run();')
}
WriteText $path $text

Write-Host '0.6.7 wiring applied.'
