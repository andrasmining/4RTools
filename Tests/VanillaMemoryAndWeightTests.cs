using System;
using System.Collections.Generic;
using _4RTools.Model.Vanilla;

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
            failed += Test("Weight fields accept UInt32 memory mappings", WeightMappings);
            failed += Test("Weight alert thresholds enforce re-arm hysteresis", WeightThresholds);
            failed += Test("Enabled weight e-mail alerts require SMTP transport", WeightMailValidation);
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
