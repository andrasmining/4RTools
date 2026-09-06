using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using _4RTools.Model;
using _4RTools.Model.Vanilla;
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
            // This developer entry point creates no stock workers or updater.
            if (args.Length > 0 && args[0] == "--vanilla-snapshot")
            {
                CaptureSnapshot(args);
                return;
            }
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
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
            // Application app = new Application();
            // app.IsMdiContainer = true;

            //Forms.ClientUpdaterForm app = new Forms.ClientUpdaterForm();
            //Forms.Container app = new Forms.Container();
            Forms.AutoPatcher app = new Forms.AutoPatcher();
            System.Windows.Forms.Application.Run(app);
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
    }
}
