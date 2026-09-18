using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using _4RTools.Model.Vanilla;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaMemoryAndWeightTests
    {
        public static int Run()
        {
            int failed = 0;
            failed += Test("Memory finder parses decimal and hexadecimal values", ParseValues);
            failed += Test("Memory finder rejects values outside selected width", ParseBounds);
            failed += Test("Memory finder copies all candidates with module offsets", CandidateClipboard);
            failed += Test("Configured launcher resolves adjacent Vanilla executable", ConfiguredExecutableResolution);
            failed += Test("Weight fields accept UInt32 memory mappings", WeightMappings);
            failed += Test("Weight alert thresholds enforce re-arm hysteresis", WeightThresholds);
            failed += Test("Enabled weight e-mail alerts require SMTP transport", WeightMailValidation);
            failed += Test("Weight cart settings validate independent UI automation", WeightCartSettings);
            failed += Test("Inventory vision finds toggled slot panel and occupied slot", InventoryVision);
            failed += Test("Quantity Enter is armed only by positive quantity dialog structure", QuantityPromptGuard);
            failed += VanillaUtf8MemoryDiscoveryTests.Run();
            return failed;
        }

        private static int Test(string name, System.Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); return 1; }
        }

        private static void ParseValues()
        {
            Equal(123L, VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.UInt32, "123"), "decimal");
            Equal(0xFFFFFFFFL, VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.UInt32, "0xFFFFFFFF"), "uint hex");
            Equal(-1L, VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Int32, "0xFFFFFFFF"), "signed hex");
        }

        private static void ParseBounds()
        {
            Throws(() => VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Byte, "256"));
            Throws(() => VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.UInt16, "-1"));
            Throws(() => VanillaMemoryDiscoverySession.ParseSearchValue(VanillaMemoryScanValueType.Int16, "32768"));
        }

        private static void CandidateClipboard()
        {
            var candidates = new List<VanillaMemoryCandidate>
            {
                new VanillaMemoryCandidate { Address = 0x401000, MainModuleOffset = 0x1000, PreviousValue = 383, CurrentValue = 388 },
                new VanillaMemoryCandidate { Address = 0xD362EC, MainModuleOffset = 0xD352EC, PreviousValue = 2630, CurrentValue = 2630 }
            };
            string text = VanillaMemoryDiscoveryPanel.FormatCandidatesForClipboard(9928, VanillaMemoryScanValueType.UInt32, VanillaMemoryScanScope.MainModule, candidates);
            Contains(text, "PID=9928\tType=UInt32\tScope=MainModule\tCandidates=2", "clipboard metadata");
            Contains(text, "0x00401000\t0x1000\t383\t388\t5", "first candidate");
            Contains(text, "0x00D362EC\t0xD352EC\t2630\t2630\t0", "second candidate");
        }

        private static void ConfiguredExecutableResolution()
        {
            string directory = Path.Combine(Path.GetTempPath(), "4rtools-vanilla-path-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string launcher = Path.Combine(directory, "Vanilla Launcher.exe");
                string client = Path.Combine(directory, "Vanilla MMO.exe");
                File.WriteAllBytes(launcher, new byte[] { 1 });
                File.WriteAllBytes(client, new byte[] { 2 });
                Equal(client, ReadOnlyProcessMemory.ResolveVanillaExecutableFromLaunch(launcher), "adjacent client");
                Equal(client, ReadOnlyProcessMemory.ResolveVanillaExecutableFromLaunch(client), "direct client");
                if (ReadOnlyProcessMemory.ResolveVanillaExecutableFromLaunch(Path.Combine(directory, "missing.exe")) != client)
                    throw new Exception("A configured launcher path in the Vanilla directory must still resolve the adjacent client.");
            }
            finally { try { Directory.Delete(directory, true); } catch { } }
        }

        private static void WeightMappings()
        {
            string json = "{\"SchemaVersion\":1,\"ProcessName\":\"Vanilla MMO.exe\",\"Fields\":{"
                + "\"CurrentWeight\":{\"Module\":\"Vanilla MMO.exe\",\"Address\":\"0x100\",\"Encoding\":\"UInt32\"},"
                + "\"MaxWeight\":{\"Module\":\"Vanilla MMO.exe\",\"Address\":\"0x104\",\"Encoding\":\"UInt32\"}}}";
            VanillaMemoryMap map = VanillaMemoryMap.Parse(json);
            if (!map.Fields.ContainsKey(VanillaField.CurrentWeight) || !map.Fields.ContainsKey(VanillaField.MaxWeight))
                throw new Exception("Weight fields were not retained by the memory map.");
        }

        private static void WeightThresholds()
        {
            var settings = new VanillaWeightAlertSettings { ThresholdPercent = 85m, RearmPercent = 80m };
            settings.Validate(false);
            settings.RearmPercent = 85m;
            Throws(() => settings.Validate(false));
            settings.RearmPercent = 90m;
            Throws(() => settings.Validate(false));
            settings.RearmPercent = 80m; settings.ThresholdPercent = 0m;
            Throws(() => settings.Validate(false));
        }

        private static void WeightCartSettings()
        {
            var settings = new VanillaWeightAlertSettings
            {
                AutoCartEnabled = true, AutoCartThresholdPercent = 50m, AutoCartRearmPercent = 40m,
                TransferUseItems = true, TransferEtcItems = true, TransferEquipItems = false
            };
            settings.Validate(false);
            if (settings.InventoryHotkeyText != "Alt+E" || settings.CartHotkeyText != "Alt+W")
                throw new Exception("Default Inventory/Cart hotkeys changed unexpectedly.");
            settings.AutoCartRearmPercent = 50m;
            Throws(() => settings.Validate(false));
            settings.AutoCartRearmPercent = 40m; settings.TransferUseItems = settings.TransferEtcItems = settings.TransferEquipItems = false;
            Throws(() => settings.Validate(false));
        }

        private static void InventoryVision()
        {
            using (var before = new Bitmap(800, 600))
            using (var after = new Bitmap(800, 600))
            {
                using (Graphics g = Graphics.FromImage(before)) g.Clear(Color.FromArgb(80, 70, 55));
                using (Graphics g = Graphics.FromImage(after))
                {
                    g.Clear(Color.FromArgb(80, 70, 55));
                    Rectangle panel = new Rectangle(70, 90, 360, 300);
                    g.FillRectangle(Brushes.White, panel);
                    int[] xs = { 150, 191, 232, 273, 314, 355, 396 };
                    int[] ys = { 175, 216, 257, 298, 339 };
                    using (var pale = new SolidBrush(Color.FromArgb(205, 216, 232)))
                    {
                        foreach (int y in ys) foreach (int x in xs) g.FillEllipse(pale, x - 17, y - 9, 34, 18);
                    }
                    // One occupied source slot hides most of the empty-slot oval.
                    g.FillRectangle(Brushes.OrangeRed, xs[0] - 11, ys[0] - 10, 22, 21);
                }
                Rectangle detected; bool opened;
                if (!VanillaInventoryVision.TryFindToggledPanel(before, after, out detected, out opened) || !opened)
                    throw new Exception("Opened inventory panel was not detected.");
                VanillaUiSlotGrid grid = VanillaInventoryVision.DetectSlotGrid(after, detected);
                Point? occupied = VanillaInventoryVision.FirstOccupiedSlot(after, grid);
                if (!occupied.HasValue || Math.Abs(occupied.Value.X - 150) > 8 || Math.Abs(occupied.Value.Y - 175) > 8)
                    throw new Exception("Occupied inventory slot was not resolved from the detected lattice.");
            }
        }

        private static void QuantityPromptGuard()
        {
            using (var noPrompt = new Bitmap(800, 600))
            using (var prompt = new Bitmap(800, 600))
            {
                using (Graphics g = Graphics.FromImage(noPrompt)) g.Clear(Color.FromArgb(80, 70, 55));
                using (Graphics g = Graphics.FromImage(prompt))
                {
                    g.Clear(Color.FromArgb(80, 70, 55));
                    g.FillRectangle(Brushes.White, 290, 250, 220, 58);
                    using (var selected = new SolidBrush(Color.FromArgb(111, 158, 242))) g.FillRectangle(selected, 306, 281, 70, 17);
                    g.FillRectangle(Brushes.LightGray, 445, 278, 48, 22);
                }
                if (VanillaInventoryVision.HasQuantityPrompt(noPrompt))
                    throw new Exception("A frame without a quantity dialog must never authorize Enter.");
                if (!VanillaInventoryVision.HasQuantityPrompt(prompt))
                    throw new Exception("Positive quantity-dialog structure was not detected.");

                using (var ordinaryUi = new Bitmap(800, 600))
                {
                    using (Graphics g = Graphics.FromImage(ordinaryUi))
                    {
                        g.Clear(Color.FromArgb(80, 70, 55));
                        // Basic Info-like panel with prominent blue bars must never be mistaken
                        // for the short/wide quantity modal.
                        g.FillRectangle(Brushes.White, 30, 35, 280, 120);
                        using (var bar = new SolidBrush(Color.FromArgb(90, 145, 235)))
                        {
                            g.FillRectangle(bar, 70, 80, 180, 12);
                            g.FillRectangle(bar, 70, 104, 160, 12);
                        }
                    }
                    if (VanillaInventoryVision.HasQuantityPrompt(ordinaryUi))
                        throw new Exception("Ordinary white/blue gameplay UI must never authorize Enter.");
                }
            }
        }

        private static void WeightMailValidation()
        {
            var settings = new VanillaWeightAlertSettings { Enabled = true, ThresholdPercent = 85m, RearmPercent = 80m };
            Throws(() => settings.Validate(false));
            settings.SmtpHost = "smtp.example.invalid";
            settings.FromAddress = "sender@example.invalid";
            settings.ToAddress = "receiver@example.invalid";
            settings.Validate(false);
            VanillaWeightAlertSettings clone = settings.Clone();
            if (!clone.Enabled || clone.SmtpHost != settings.SmtpHost || clone.ToAddress != settings.ToAddress)
                throw new Exception("Weight alert settings clone lost data.");
        }

        private static void Equal(long expected, long actual, string label)
        {
            if (expected != actual) throw new Exception(label + ": expected " + expected + ", got " + actual + ".");
        }

        private static void Equal(string expected, string actual, string label)
        {
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new Exception(label + ": expected '" + expected + "', got '" + actual + "'.");
        }

        private static void Contains(string text, string expected, string label)
        {
            if (text == null || !text.Contains(expected)) throw new Exception(label + ": missing '" + expected + "'.");
        }

        private static void Throws(System.Action action)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new Exception("Expected ArgumentException.");
        }
    }
}
