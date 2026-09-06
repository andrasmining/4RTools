using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using _4RTools.Model;
using _4RTools.Model.Vanilla;
using _4RTools.Model.Vanilla.Automation;
using _4RTools.Utils;

namespace _4RTools
{
    internal static class Program
    {
        /// <summary>
        /// Ponto de entrada principal para o aplicativo.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--vanilla-discover")
            {
                Discover(args);
                return;
            }
            // This developer entry point creates no stock workers or updater.
            if (args.Length > 0 && args[0] == "--vanilla-session-check")
            {
                CheckSession(args);
                return;
            }
            if (args.Length > 0 && args[0] == "--vanilla-snapshot")
            {
                CaptureSnapshot(args);
                return;
            }
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--portable-smoke-test")
            {
                PortableSmokeTest(args);
                return;
            }
            if (args.Length > 0 && args[0] == "--vanilla-diagnostics")
            {
                try
                {
                    ProfileSingleton.Create("Default");
                    var diagnostics = new Forms.VanillaDiagnosticsForm();
                    if (args.Contains("--demo")) diagnostics.StartDemo();
                    System.Windows.Forms.Application.Run(diagnostics);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "Vanilla diagnostics");
                    Environment.ExitCode = 1;
                }
                return;
            }
            try
            {
                if (args.Contains("--stock-ui"))
                {
                    LoadStockClients();
                    System.Windows.Forms.Application.Run(new Forms.Container());
                    return;
                }
                using (var session = new VanillaAutomationSession(AppDomain.CurrentDomain.BaseDirectory))
                {
                    var app = new Forms.VanillaAutomationForm(session, () =>
                    {
                        session.SetEnabled(false);
                        ProfileSingleton.Create("Default");
                        new Forms.VanillaDiagnosticsForm().Show();
                    });
                    System.Windows.Forms.Application.Run(app);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message, "4RTools Vanilla stopped");
                Environment.ExitCode = 1;
            }
        }

        internal static void OpenStockTools()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Windows.Forms.Application.ExecutablePath,
                Arguments = "--stock-ui",
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = true
            });
        }

        private static void LoadStockClients()
        {
            var clients = LocalServerManager.GetLocalClients();
            clients.AddRange(JsonConvert.DeserializeObject<System.Collections.Generic.List<ClientDTO>>(Resources._4RTools.ETCResource.supported_servers));
            foreach (var client in clients)
            {
                // Vanilla always goes through the read-only, fingerprinted companion path.
                if (Client.IsVanillaProcessName(client.name)) continue;
                ClientListSingleton.AddClient(new Client(client));
            }
        }

        private static void PortableSmokeTest(string[] args)
        {
            string output = null;
            try
            {
                int outputIndex = Array.IndexOf(args, "--output");
                if (outputIndex < 0 || outputIndex + 1 >= args.Length) throw new ArgumentException("Smoke test requires --output <new-json-path>.");
                output = args[outputIndex + 1];
                if (File.Exists(output)) throw new IOException("Smoke report already exists.");
                using (var session = new VanillaAutomationSession(AppDomain.CurrentDomain.BaseDirectory))
                using (var form = new Forms.VanillaAutomationForm(session))
                {
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    form.VerifyDisplayedSettings();
                    session.Tick();
                    session.Settings.Validate();
                    if (session.IsEnabled || session.Snapshot != null || IntPtr.Size != 4)
                        throw new InvalidOperationException("Unexpected startup automation, game attachment, or process architecture.");
                    var stock = JsonConvert.DeserializeObject<System.Collections.Generic.List<ClientDTO>>(Resources._4RTools.ETCResource.supported_servers);
                    if (stock == null || stock.Count == 0) throw new InvalidOperationException("Bundled stock resources missing.");
                    int imageIndex = Array.IndexOf(args, "--screenshot");
                    if (imageIndex >= 0 && imageIndex + 1 < args.Length)
                        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(args[imageIndex + 1]); }
                    form.Close();
                    File.WriteAllText(output, JsonConvert.SerializeObject(new
                    {
                        Success = true, Version = "0.1.0", PointerBytes = IntPtr.Size,
                        ExecutableDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        WorkingDirectory = Environment.CurrentDirectory,
                        ProfileRoundTrip = session.GetProfileNames().Count > 0,
                        StockClientDefinitions = stock.Count,
                        GameplayAttached = false, InputSent = false,
                        Checked = "Embedded JSON dependency, resources, portable settings, UI construction/show/close and automation OFF"
                    }, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                if (output != null && !File.Exists(output))
                    File.WriteAllText(output, JsonConvert.SerializeObject(new { Success = false, Error = ex.ToString() }, Formatting.Indented));
            }
        }

        private static void CaptureSnapshot(string[] args)
        {
            string output = null;
            try
            {
                int outputIndex = Array.IndexOf(args, "--output");
                if (outputIndex < 0 || outputIndex + 1 >= args.Length)
                    throw new ArgumentException("Use --vanilla-snapshot <pid> --output <new-json-path> [--map <json-path>] [--probe].");
                output = args[outputIndex + 1];
                if (File.Exists(output)) throw new IOException("Snapshot output already exists; choose a new path.");
                int pid;
                if (args.Length < 2 || !int.TryParse(args[1], out pid) || pid <= 0)
                    throw new ArgumentException("A positive process ID is required.");
                int mapIndex = Array.IndexOf(args, "--map");
                if (mapIndex >= 0 && mapIndex + 1 >= args.Length) throw new ArgumentException("Missing map path.");
                VanillaMemoryMap map = mapIndex < 0
                    ? VanillaMemoryMap.Parse(new VanillaDiagnosticsSettings().MemoryMapJson)
                    : VanillaMemoryMap.Load(args[mapIndex + 1]);
                using (var memory = new ReadOnlyProcessMemory(pid))
                {
                    string probe = null;
                    if (args.Contains("--probe"))
                    {
                        // Explicit bounded read of the known module header, not a game-state field.
                        // A failure ends this attempt; the source never gets constructed afterward.
                        probe = BitConverter.ToString(memory.ReadBytes(memory.MainModuleBaseAddress, 2));
                    }
                    using (var source = new MemoryStateSource(memory, map))
                    {
                        VanillaClientState snapshot = source.Poll(DateTimeOffset.UtcNow);
                        object result = probe == null ? (object)snapshot : new { HeaderProbe = probe, Snapshot = snapshot };
                        File.WriteAllText(output, JsonConvert.SerializeObject(result, Formatting.Indented));
                        if (source.IsStopped) Environment.ExitCode = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                Console.Error.WriteLine(ex);
                if (output != null && !File.Exists(output))
                {
                    try { File.WriteAllText(output, JsonConvert.SerializeObject(new { Error = ex.ToString(), SampledAtUtc = DateTimeOffset.UtcNow }, Formatting.Indented)); }
                    catch (Exception writeError) { Console.Error.WriteLine(writeError); }
                }
            }
        }

        private static void CheckSession(string[] args)
        {
            try
            {
                int pid, outputIndex = Array.IndexOf(args, "--output");
                if (args.Length < 2 || !int.TryParse(args[1], out pid) || outputIndex < 0 || outputIndex + 1 >= args.Length)
                    throw new ArgumentException("Use --vanilla-session-check <pid> --output <new-json-path>.");
                string output = args[outputIndex + 1];
                if (File.Exists(output)) throw new IOException("Output already exists.");
                using (var session = new VanillaAutomationSession(AppDomain.CurrentDomain.BaseDirectory))
                {
                    // No input is possible, regardless of saved user preferences.
                    session.ApplySettings(new VanillaAutomationSettings { DryRun = true });
                    session.Connect(pid);
                    if (session.Snapshot == null) throw new InvalidOperationException(session.Status);
                    session.SetEnabled(true);
                    File.WriteAllText(output, JsonConvert.SerializeObject(new
                    {
                        session.ExecutablePath, session.Fingerprint, session.BuildProfile,
                        session.Status, session.IsEnabled, session.Snapshot,
                        InputSent = false, DryRun = true
                    }, Formatting.Indented));
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        }

        private static void Discover(string[] args)
        {
            try
            {
                int pid;
                int outputIndex = Array.IndexOf(args, "--output"), captureIndex = Array.IndexOf(args, "--capture");
                if (args.Length < 2 || !int.TryParse(args[1], out pid) || outputIndex < 0 || outputIndex + 1 >= args.Length)
                    throw new ArgumentException("Use --vanilla-discover <pid> --output <new-json-path> [--scan] [--capture <new-png-path>].");
                string output = args[outputIndex + 1];
                if (File.Exists(output)) throw new IOException("Output already exists.");
                string capture = captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : null;
                if (capture != null && File.Exists(capture)) throw new IOException("Capture output already exists.");
                int statsIndex = Array.IndexOf(args, "--stats");
                uint[] expected = statsIndex >= 0 && statsIndex + 1 < args.Length
                    ? args[statsIndex + 1].Split(',').Select(value => uint.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : null;
                var result = VanillaDiscovery.Inspect(pid, args.Contains("--scan"), capture, expected, args.Contains("--restore-window"));
                File.WriteAllText(output, JsonConvert.SerializeObject(result, Formatting.Indented));
                if (result.Error != null) Environment.ExitCode = 1;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        }
    }
}
