using System;
using System.IO;
using System.Reflection;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class DiscoveryTests
    {
        public static void Run()
        {
            var first = VanillaExecutableIdentity.Read(Assembly.GetExecutingAssembly().Location);
            var second = VanillaExecutableIdentity.Read(Assembly.GetExecutingAssembly().Location);
            if (first.Sha256.Length != 64 || first.Sha256 != second.Sha256 || first.ImageSize == 0 || first.Sections.Count == 0)
                throw new Exception("Executable identity must be stable and contain valid PE metadata.");
            string invalidPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(invalidPath, "This is not a PE image.");
                bool rejected = false;
                try { VanillaExecutableIdentity.Read(invalidPath); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Malformed executable identity must be rejected.");
            }
            finally { File.Delete(invalidPath); }
        }
    }
}
