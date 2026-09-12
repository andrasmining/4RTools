Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)

$path = 'Model/Vanilla/VanillaPatcherLauncher.cs'
$text = Get-Content $path -Raw

$old = '[DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);'
$new = '[DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);' + [Environment]::NewLine + '        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);'
if (-not $text.Contains($old)) { throw 'EnumChildWindows anchor missing.' }
$text = $text.Replace($old, $new)

$old = '                                IntPtr launcherHwnd = GetMainWindow(patcherPid.Value);'
$new = '                                IntPtr launcherHwnd = ResolveLauncherWindow(patcherPid.Value);' + [Environment]::NewLine + '                                if (launcherHwnd == IntPtr.Zero) throw new InvalidOperationException("No visible launcher top-level window was found for PID " + patcherPid.Value + ".");'
if (-not $text.Contains($old)) { throw 'launcher HWND anchor missing.' }
$text = $text.Replace($old, $new)

$old = '                                    using (var input = new VanillaForegroundInput(patcherPid.Value))'
$new = '                                    using (var input = new VanillaForegroundInput(patcherPid.Value, launcherHwnd))'
if (-not $text.Contains($old)) { throw 'foreground input launcher anchor missing.' }
$text = $text.Replace($old, $new)

$old = '                    IntPtr hwnd = process.MainWindowHandle;'
$new = '                    IntPtr hwnd = ResolveLauncherWindow(processId);'
if (-not $text.Contains($old)) { throw 'capture window anchor missing.' }
$text = $text.Replace($old, $new)

$old = '            IntPtr main = GetMainWindow(processId);'
$new = '            IntPtr main = ResolveLauncherWindow(processId);'
if (-not $text.Contains($old)) { throw 'targeted click window anchor missing.' }
$text = $text.Replace($old, $new)

$old = '                        if (process.MainWindowHandle == IntPtr.Zero || !IsWindowVisible(process.MainWindowHandle)) continue;'
$new = '                        IntPtr resolvedWindow = ResolveLauncherWindow(process.Id);' + [Environment]::NewLine + '                        if (resolvedWindow == IntPtr.Zero || !IsWindowVisible(resolvedWindow)) continue;'
if (-not $text.Contains($old)) { throw 'launcher candidate window anchor missing.' }
$text = $text.Replace($old, $new)

$old = '                        rows.Add("PID=" + process.Id + " exited=" + process.HasExited + " hwnd=" + DescribeWindow(process.MainWindowHandle)'
$new = '                        rows.Add("PID=" + process.Id + " exited=" + process.HasExited + " processMain=" + DescribeWindow(process.MainWindowHandle)' + [Environment]::NewLine + '                            + " resolvedLauncher=" + DescribeWindow(ResolveLauncherWindow(process.Id))'
if (-not $text.Contains($old)) { throw 'launcher candidate log anchor missing.' }
$text = $text.Replace($old, $new)

$oldMethod = @'
        private static IntPtr GetMainWindow(int processId)
        {
            using (var process = Process.GetProcessById(processId))
            {
                process.Refresh();
                return process.MainWindowHandle;
            }
        }
'@
$newMethod = @'
        private static IntPtr ResolveLauncherWindow(int processId)
        {
            IntPtr best = IntPtr.Zero;
            long bestScore = long.MinValue;
            EnumWindows((hwnd, state) =>
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != (uint)processId || !IsWindowVisible(hwnd)) return true;

                RECT rect;
                int width = 0, height = 0;
                if (GetClientRect(hwnd, out rect))
                {
                    width = Math.Max(0, rect.Right - rect.Left);
                    height = Math.Max(0, rect.Bottom - rect.Top);
                }

                string title = WindowTitle(hwnd);
                string cls = WindowClass(hwnd);
                long score = (long)width * height;
                if (title.IndexOf("Vanilla MMO Launcher", StringComparison.OrdinalIgnoreCase) >= 0) score += 1000000000L;
                if (cls.IndexOf("TThorForm", StringComparison.OrdinalIgnoreCase) >= 0) score += 500000000L;
                if (width < 100 || height < 100) score -= 100000000L;
                if (score > bestScore)
                {
                    best = hwnd;
                    bestScore = score;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }
'@
if (-not $text.Contains($oldMethod)) { throw 'GetMainWindow method anchor missing.' }
$text = $text.Replace($oldMethod, $newMethod)
[IO.File]::WriteAllText($path, $text, $utf8)

$path = 'Model/Vanilla/VanillaForegroundInput.cs'
$text = Get-Content $path -Raw
$old = '        private readonly Process process;' + [Environment]::NewLine + '        private IntPtr window;'
$new = '        private readonly Process process;' + [Environment]::NewLine + '        private readonly IntPtr preferredWindow;' + [Environment]::NewLine + '        private IntPtr window;'
if (-not $text.Contains($old)) { throw 'preferredWindow field anchor missing.' }
$text = $text.Replace($old, $new)

$oldCtor = @'
        public VanillaForegroundInput(int processId)
        {
            process = Process.GetProcessById(processId);
            RefreshWindow();
        }
'@
$newCtor = @'
        public VanillaForegroundInput(int processId) : this(processId, IntPtr.Zero) { }

        public VanillaForegroundInput(int processId, IntPtr preferredWindow)
        {
            process = Process.GetProcessById(processId);
            this.preferredWindow = preferredWindow;
            RefreshWindow();
        }
'@
if (-not $text.Contains($oldCtor)) { throw 'VanillaForegroundInput constructor anchor missing.' }
$text = $text.Replace($oldCtor, $newCtor)

$oldRefresh = @'
            process.Refresh();
            window = process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Vanilla client window is not ready.");
'@
$newRefresh = @'
            process.Refresh();
            window = preferredWindow != IntPtr.Zero && IsWindow(preferredWindow) ? preferredWindow : process.MainWindowHandle;
            if (window == IntPtr.Zero || !IsWindow(window)) throw new InvalidOperationException("Vanilla client window is not ready.");
'@
if (-not $text.Contains($oldRefresh)) { throw 'RefreshWindow anchor missing.' }
$text = $text.Replace($oldRefresh, $newRefresh)
[IO.File]::WriteAllText($path, $text, $utf8)

Write-Host 'Visible launcher top-level window routing applied.'
