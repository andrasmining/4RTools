using System;
using System.Linq;
using System.Text;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaUtf8MemoryDiscoveryTests
    {
        public static int Run()
        {
            int failed = 0;
            failed += Test("UTF-8 finder encodes exact map names", EncodeMapNames);
            failed += Test("UTF-8 finder locates byte-aligned and unaligned text", FindsText);
            failed += Test("UTF-8 finder retains overlapping matches", FindsOverlappingText);
            failed += Test("UTF-8 finder rejects unusable search text", RejectsInvalidText);
            return failed;
        }

        private static int Test(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); return 1; }
        }

        private static void EncodeMapNames()
        {
            string map = "yuno_fild08";
            byte[] actual = VanillaUtf8MemoryDiscoverySession.EncodeSearchText(map);
            byte[] expected = Encoding.UTF8.GetBytes(map);
            Check(actual.SequenceEqual(expected), "Map name was not encoded as exact UTF-8 bytes.");
        }

        private static void FindsText()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("xx-yuno_fild08-yy-yuno_fild02-zz");
            int[] first = VanillaUtf8MemoryDiscoverySession.FindPatternOffsets(bytes, Encoding.UTF8.GetBytes("yuno_fild08"));
            int[] second = VanillaUtf8MemoryDiscoverySession.FindPatternOffsets(bytes, Encoding.UTF8.GetBytes("yuno_fild02"));
            Check(first.Length == 1 && first[0] == 3, "First map name offset was not found exactly.");
            Check(second.Length == 1 && second[0] > first[0], "Second map name offset was not found exactly.");
        }

        private static void FindsOverlappingText()
        {
            int[] offsets = VanillaUtf8MemoryDiscoverySession.FindPatternOffsets(
                Encoding.ASCII.GetBytes("aaaa"), Encoding.ASCII.GetBytes("aa"));
            Check(offsets.SequenceEqual(new[] { 0, 1, 2 }), "Overlapping text matches were lost.");
        }

        private static void RejectsInvalidText()
        {
            Throws<ArgumentException>(() => VanillaUtf8MemoryDiscoverySession.EncodeSearchText(""));
            Throws<ArgumentException>(() => VanillaUtf8MemoryDiscoverySession.EncodeSearchText("x"));
            Throws<ArgumentException>(() => VanillaUtf8MemoryDiscoverySession.EncodeSearchText("a\0b"));
            Throws<ArgumentException>(() => VanillaUtf8MemoryDiscoverySession.EncodeSearchText(new string('x', 97)));
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name + ".");
        }
    }
}
