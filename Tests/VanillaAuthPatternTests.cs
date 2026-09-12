using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaAuthPatternTests
    {
        private static int passed, failed;

        internal static int Run()
        {
            Test("Login pattern detects username and password fields across resolutions", LoginAcrossResolutions);
            Test("Login pattern tolerates softened rendering", LoginSoftened);
            Test("Server pattern detects the single-server dialog across resolutions", ServerAcrossResolutions);
            Test("Server pattern rejects unrelated blank screens", RejectBlank);
            Console.WriteLine("Auth/server pattern: {0} passed; {1} failed.", passed, failed);
            return failed;
        }

        private static void LoginAcrossResolutions()
        {
            foreach (Size size in new[] { new Size(800, 600), new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440) })
            {
                using (Bitmap bitmap = LoginImage(size.Width, size.Height, false))
                {
                    VanillaLoginLayout layout;
                    string evidence;
                    Assert(VanillaAuthPattern.TryDetectLogin(bitmap, out layout, out evidence), size + " failed: " + evidence);
                    Point expectedUser = new Point((int)(size.Width * 0.49), (int)(size.Height * 0.64));
                    Point expectedPassword = new Point((int)(size.Width * 0.49), (int)(size.Height * 0.665));
                    Assert(layout.UserName.Contains(expectedUser), "Username safe area missed expected center at " + size + ": " + layout.UserName);
                    Assert(layout.Password.Contains(expectedPassword), "Password safe area missed expected center at " + size + ": " + layout.Password);
                    Point chosen = VanillaAuthPattern.PickInside(layout.UserName, 42);
                    Assert(layout.UserName.Contains(chosen), "Random username click escaped safe area.");
                }
            }
        }

        private static void LoginSoftened()
        {
            using (Bitmap large = LoginImage(1600, 1000, true))
            using (Bitmap reduced = new Bitmap(1000, 625))
            using (Graphics graphics = Graphics.FromImage(reduced))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(large, new Rectangle(0, 0, reduced.Width, reduced.Height));
                VanillaLoginLayout layout;
                string evidence;
                Assert(VanillaAuthPattern.TryDetectLogin(reduced, out layout, out evidence), "Softened login failed: " + evidence);
            }
        }

        private static void ServerAcrossResolutions()
        {
            foreach (Size size in new[] { new Size(800, 600), new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440) })
            {
                using (Bitmap bitmap = ServerImage(size.Width, size.Height))
                {
                    VanillaServerLayout layout;
                    string evidence;
                    Assert(VanillaAuthPattern.TryDetectServerDialog(bitmap, out layout, out evidence), size + " failed: " + evidence);
                    Point expected = new Point((int)(size.Width * 0.50), (int)(size.Height * 0.61));
                    Assert(layout.ServerRow.Contains(expected), "Server safe row missed expected point at " + size + ": " + layout.ServerRow + "; " + evidence);
                    Point chosen = VanillaAuthPattern.PickInside(layout.ServerRow, 77);
                    Assert(layout.ServerRow.Contains(chosen), "Random server click escaped safe row.");
                }
            }
        }

        private static void RejectBlank()
        {
            using (var bitmap = new Bitmap(1280, 720))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                VanillaLoginLayout login;
                VanillaServerLayout server;
                string evidence;
                Assert(!VanillaAuthPattern.TryDetectLogin(bitmap, out login, out evidence), "Blank screen became a login form.");
                Assert(!VanillaAuthPattern.TryDetectServerDialog(bitmap, out server, out evidence), "Blank screen became a server dialog.");
            }
        }

        private static Bitmap LoginImage(int width, int height, bool softened)
        {
            var bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(248, 251, 252));
                graphics.SmoothingMode = softened ? SmoothingMode.AntiAlias : SmoothingMode.None;
                int controlWidth = Math.Max(72, (int)(width * 0.085));
                int lineGap = Math.Max(14, (int)(height * 0.025));
                int left = (int)(width * 0.49) - controlWidth / 2;
                int top = (int)(height * 0.60);
                using (var fill = new SolidBrush(Color.FromArgb(247, 247, 247)))
                using (var pen = new Pen(softened ? Color.FromArgb(145, 145, 145) : Color.FromArgb(100, 100, 100), softened ? 2f : 1f))
                using (var ink = new SolidBrush(Color.FromArgb(70, 70, 70)))
                {
                    for (int row = 0; row < 3; row++)
                    {
                        Rectangle box = new Rectangle(left, top + row * lineGap, controlWidth, lineGap);
                        graphics.FillRectangle(fill, box);
                        graphics.DrawRectangle(pen, box);
                        if (row > 0) graphics.FillRectangle(ink, left + 12, box.Top + Math.Max(3, lineGap / 3), Math.Max(12, controlWidth / 3), 2);
                    }
                }
            }
            return bitmap;
        }

        private static Bitmap ServerImage(int width, int height)
        {
            var bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(247, 250, 251));
                int dialogWidth = Math.Max(140, (int)(width * 0.17));
                int dialogHeight = Math.Max(90, (int)(height * 0.16));
                int left = width / 2 - dialogWidth / 2;
                int top = (int)(height * 0.57);
                Rectangle dialog = new Rectangle(left, top, dialogWidth, dialogHeight);
                using (var fill = new SolidBrush(Color.FromArgb(239, 239, 239)))
                using (var list = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.FromArgb(95, 95, 95), 1f))
                using (var selection = new SolidBrush(Color.FromArgb(220, 228, 248)))
                using (var ink = new SolidBrush(Color.FromArgb(65, 65, 65)))
                {
                    graphics.FillRectangle(fill, dialog);
                    graphics.DrawRectangle(pen, dialog);
                    Rectangle listRect = new Rectangle(left + Math.Max(5, dialogWidth / 25), top + Math.Max(14, dialogHeight / 10), dialogWidth - Math.Max(10, dialogWidth / 12), (int)(dialogHeight * 0.68));
                    graphics.FillRectangle(list, listRect);
                    graphics.DrawRectangle(pen, listRect);
                    Rectangle row = new Rectangle(listRect.Left + 2, listRect.Top + 2, listRect.Width - 4, Math.Max(9, dialogHeight / 10));
                    graphics.FillRectangle(selection, row);
                    graphics.FillRectangle(ink, row.Left + 8, row.Top + row.Height / 2, Math.Max(30, row.Width / 3), 2);
                    graphics.DrawRectangle(pen, left + (int)(dialogWidth * 0.66), top + (int)(dialogHeight * 0.84), Math.Max(28, dialogWidth / 8), Math.Max(12, dialogHeight / 10));
                    graphics.DrawRectangle(pen, left + (int)(dialogWidth * 0.82), top + (int)(dialogHeight * 0.84), Math.Max(28, dialogWidth / 8), Math.Max(12, dialogHeight / 10));
                }
            }
            return bitmap;
        }

        private static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
    }
}
