using System;
using System.Drawing;
using System.Reflection;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaProxyPatternTests
    {
        private static int passed, failed;
        internal static int Run()
        {
            Test("Proxy pattern rejects blank screens", RejectBlank);
            Test("Proxy pattern detects four rows across resolutions", DetectScaled);
            Test("Proxy pattern tolerates softened text", DetectSoftText);
            Console.WriteLine("Proxy pattern: {0} passed; {1} failed.", passed, failed);
            return failed;
        }

        private static void RejectBlank()
        {
            using (var image = new Bitmap(1280, 720))
            using (var g = Graphics.FromImage(image))
            {
                g.Clear(Color.White);
                object[] args = { image, null, null };
                Assert(!(bool)Detect().Invoke(null, args), "Blank image must fail closed.");
            }
        }

        private static void DetectScaled()
        {
            foreach (var size in new[] { new Size(800, 600), new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440) })
            using (var image = MakeImage(size.Width, size.Height, Color.FromArgb(25, 25, 25)))
            {
                object[] args = { image, null, null };
                Assert((bool)Detect().Invoke(null, args), "Expected rows at " + size.Width + "x" + size.Height + ". Evidence: " + args[2]);
                object layout = args[1];
                Rectangle[] rows = (Rectangle[])layout.GetType().GetProperty("Rows").GetValue(layout, null);
                Assert(rows.Length == 4, "Exactly four rows are required.");
                for (int i = 1; i < rows.Length; i++) Assert(rows[i].Top > rows[i - 1].Top, "Rows must be top-to-bottom.");
            }
        }

        private static void DetectSoftText()
        {
            using (var image = MakeImage(1600, 900, Color.FromArgb(105, 105, 105)))
            {
                object[] args = { image, null, null };
                Assert((bool)Detect().Invoke(null, args), "Softened text should remain detectable. Evidence: " + args[2]);
            }
        }

        private static Bitmap MakeImage(int width, int height, Color text)
        {
            var image = new Bitmap(width, height);
            using (var g = Graphics.FromImage(image))
            using (var brush = new SolidBrush(text))
            {
                g.Clear(Color.FromArgb(232, 238, 244));
                int spacing = Math.Max(12, (int)(height * 0.026));
                int startY = (int)(height * 0.56) - spacing;
                int textWidth = Math.Max(80, (int)(width * 0.13));
                int left = width / 2 - textWidth / 2;
                for (int row = 0; row < 4; row++)
                {
                    int centerY = startY + row * spacing;
                    for (int x = left; x < left + textWidth; x += 9)
                        g.FillRectangle(brush, x, centerY - 3, Math.Min(6, left + textWidth - x), 7);
                }
            }
            return image;
        }

        private static MethodInfo Detect()
        {
            Type type = typeof(VanillaReconnectSettings).Assembly.GetType("_4RTools.Model.Vanilla.VanillaProxyPattern", true);
            return type.GetMethod("TryDetect", BindingFlags.Static | BindingFlags.NonPublic);
        }
        private static void Test(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
