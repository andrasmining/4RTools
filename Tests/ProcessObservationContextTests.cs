using System;
using System.Globalization;
using System.Threading;
using _4RTools.Utils;

namespace Vanilla.Diagnostics.Tests
{
    internal static class ProcessObservationContextTests
    {
        public static int Run()
        {
            int failed = 0;
            failed += Test("Observer context separates target errors from observer identity", ErrorIdentity);
            failed += Test("Unavailable observer metadata never implies no elevation or low integrity", UnknownMetadata);
            failed += Test("Observer context preserves UTC and pointer width across display cultures", InvariantContext);
            return failed;
        }

        private static void ErrorIdentity()
        {
            var context = new ProcessObservationContext(123, 4,
                new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero), false, 0x2000);
            string error = context.DescribeNativeFailure("OpenProcess(read/limited-query, 0x1010)", 456, 5);
            Check(error.StartsWith("OpenProcess(read/limited-query, 0x1010) failed for PID 456: Win32 5 (", StringComparison.Ordinal),
                "The original operation, mask, target PID and native error must remain first.");
            Check(error.Contains("Observer PID 123, 32-bit") && error.Contains("elevated no, integrity medium (0x2000)"),
                "Observer identity must be clearly separate from the denied target.");
            string scanError = context.DescribeNativeFailure("OpenProcess(read/query, 0x0410)", 456, 5);
            Check(scanError.Contains("OpenProcess(read/query, 0x0410)"), "Scanner access must retain its own distinct mask.");
        }

        private static void UnknownMetadata()
        {
            var context = new ProcessObservationContext(123, 8, null, null, null, "Self token query failed.");
            string text = context.ToString();
            Check(text.Contains("started unknown, elevated unknown, integrity unknown."),
                "Missing token or start metadata must stay explicitly unknown.");
            Check(text.Contains("Self-context error: Self token query failed."), "Keep self-query failure separate from target failure.");
            string unexpected = new ProcessObservationContext(123, 8, null, true, 0x2345).ToString();
            Check(unexpected.Contains("elevated yes, integrity unrecognized (0x2345)"),
                "An unfamiliar integrity RID must be reported exactly without inventing a category.");
        }

        private static void InvariantContext()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");
                var context = new ProcessObservationContext(123, 8,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(2)), true, 0x3000);
                string text = context.ToString();
                Check(text.Contains("64-bit, started 2026-09-13T10:00:00.0000000+00:00"),
                    "The observer's start must be UTC, invariant, and independent of target architecture.");
                Check(context.StartedAtUtc.Value.Offset == TimeSpan.Zero && text.Contains("integrity high (0x3000)"),
                    "Preserve normalized structured metadata and a known integrity label.");
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        private static int Test(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); return 1; }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
