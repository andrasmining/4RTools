using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace _4RTools.Model.Vanilla
{
    /// <summary>
    /// Recognizes only the two reported terminal Message dialogs. These are visual
    /// templates, not OCR or a general "white rectangle means disconnected" rule.
    /// Resources contain dialog-only crops; no desktop, account or character data.
    /// Unknown layouts/messages deliberately do not authorize closing a client.
    /// </summary>
    internal static class VanillaDisconnectPattern
    {
        private const int SignatureWidth = 128, SignatureHeight = 16;
        private sealed class Signature
        {
            internal double[] Ink;
            internal double Aspect, WidthFraction;
        }
        private sealed class Template
        {
            internal VanillaVisualState State;
            internal Signature Text;
        }
        private static readonly Lazy<Template[]> Templates = new Lazy<Template[]>(() => new[]
        {
            Load("LoggingOut", VanillaVisualState.LoggingOut),
            Load("Disconnected", VanillaVisualState.Disconnected)
        });

        internal static Stream OpenReference(string name)
        {
            if (name != "LoggingOut" && name != "Disconnected") throw new ArgumentException("Unknown terminal dialog.");
            return typeof(VanillaDisconnectPattern).Assembly.GetManifestResourceStream("Vanilla.TerminalDialog." + name + ".png")
                ?? throw new InvalidDataException("Missing terminal-dialog reference: " + name);
        }

        private static Template Load(string name, VanillaVisualState state)
        {
            using (Stream stream = OpenReference(name))
            using (var bitmap = new Bitmap(stream))
            {
                byte[] pixels = ReadPixels(bitmap);
                // The independently supplied dialog crop has a 273 x 99 white body.
                var body = new Rectangle(1, 19, 273, 99);
                Signature signature = TextSignature(pixels, bitmap.Width, body);
                if (signature == null) throw new InvalidDataException("Invalid terminal-dialog reference: " + name);
                return new Template { State = state, Text = signature };
            }
        }

        internal static VanillaVisualState Classify(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 320 || bitmap.Height < 240
                || bitmap.Width > 4096 || bitmap.Height > 4096) return VanillaVisualState.Unknown;
            byte[] pixels = ReadPixels(bitmap);
            int width = bitmap.Width, height = bitmap.Height;
            int leftLimit = width / 10, rightLimit = width * 9 / 10;
            var seen = new HashSet<Rectangle>();
            Rectangle matchedBody = Rectangle.Empty;
            VanillaVisualState result = VanillaVisualState.Unknown;
            for (int y = height * 18 / 100; y < height * 88 / 100; y += Math.Max(1, height / 360))
            {
                int x = leftLimit;
                while (x < rightLimit)
                {
                    if (!Light(pixels, width, x, y)) { x++; continue; }
                    int left = x;
                    while (x < rightLimit && Light(pixels, width, x, y)) x++;
                    int bodyWidth = x - left;
                    if (left == leftLimit || x == rightLimit || bodyWidth < 120 || bodyWidth > 1100) continue;
                    int axis = left + bodyWidth * 72 / 100, top = y, bottom = y + 1;
                    while (top > 0 && Light(pixels, width, axis, top - 1)) top--;
                    while (bottom < height && Light(pixels, width, axis, bottom)) bottom++;
                    int bodyHeight = bottom - top;
                    double aspect = bodyWidth / (double)bodyHeight;
                    if (bodyHeight < 45 || bodyHeight > 450 || aspect < 2.2 || aspect > 3.2) continue;
                    var body = new Rectangle(left, top, bodyWidth, bodyHeight);
                    if (!seen.Add(body)) continue;
                    if (seen.Count > 64) return VanillaVisualState.Unknown; // Ambiguous scene: bounded work, no action.
                    if (!HasMessageFrame(pixels, width, body)) continue;
                    Signature text = TextSignature(pixels, width, body);
                    if (text == null) continue;
                    foreach (Template template in Templates.Value)
                    {
                        if (!Matches(template.Text, text)) continue;
                        // Slightly different runs can describe the same antialiased border,
                        // but two separate matching dialogs are not one actionable observation.
                        if (result != VanillaVisualState.Unknown
                            && (result != template.State || !matchedBody.IntersectsWith(body)))
                            return VanillaVisualState.Unknown;
                        result = template.State;
                        matchedBody = body;
                    }
                }
            }
            return result;
        }

        private static bool HasMessageFrame(byte[] pixels, int width, Rectangle body)
        {
            int light = 0, total = 0;
            for (int y = body.Top; y < body.Bottom; y += Math.Max(1, body.Height / 20))
                for (int x = body.Left; x < body.Right; x += Math.Max(1, body.Width / 40))
                { total++; if (Light(pixels, width, x, y)) light++; }
            if (light < total * .86) return false;
            int blue = 0;
            total = 0;
            for (int y = Math.Max(0, body.Top - (int)(body.Width * .065)); y < body.Top - 1; y++)
                for (int x = body.Left + body.Width * 8 / 100; x < body.Right - body.Width * 8 / 100; x += 2)
                {
                    int p = (y * width + x) * 3;
                    total++;
                    if (pixels[p] - pixels[p + 2] > 10 && pixels[p + 1] > pixels[p + 2] && pixels[p] > 130) blue++;
                }
            return total > 0 && blue >= total * .30;
        }

        private static Signature TextSignature(byte[] pixels, int width, Rectangle body)
        {
            var area = Rectangle.FromLTRB(body.Left + (int)(body.Width * .02), body.Top + (int)(body.Height * .035),
                body.Right - (int)(body.Width * .03), body.Top + (int)(body.Height * .34));
            int left = area.Right, right = area.Left, top = area.Bottom, bottom = area.Top, count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (Gray(pixels, width, x, y) < 165)
                    {
                        count++; left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
            if (count < 25 || bottom - top < 5) return null;
            int w = right - left + 1, h = bottom - top + 1;
            var ink = new double[SignatureWidth * SignatureHeight];
            // Bilinear sampling keeps modest DPI/resampling differences from turning
            // into a different message. Matching still requires the full text shape.
            for (int y = 0; y < SignatureHeight; y++)
                for (int x = 0; x < SignatureWidth; x++)
                {
                    double sx = Math.Max(0, Math.Min(w - 1, (x + .5) * w / SignatureWidth - .5));
                    double sy = Math.Max(0, Math.Min(h - 1, (y + .5) * h / SignatureHeight - .5));
                    int ix = (int)sx, iy = (int)sy;
                    double fx = sx - ix, fy = sy - iy;
                    double a = Gray(pixels, width, left + ix, top + iy) * (1 - fx)
                        + Gray(pixels, width, left + Math.Min(ix + 1, w - 1), top + iy) * fx;
                    double b = Gray(pixels, width, left + ix, top + Math.Min(iy + 1, h - 1)) * (1 - fx)
                        + Gray(pixels, width, left + Math.Min(ix + 1, w - 1), top + Math.Min(iy + 1, h - 1)) * fx;
                    ink[y * SignatureWidth + x] = 1 - (a * (1 - fy) + b * fy) / 255.0;
                }
            return new Signature { Ink = ink, Aspect = w / (double)h, WidthFraction = w / (double)body.Width };
        }

        private static bool Matches(Signature reference, Signature candidate)
        {
            if (Math.Abs(reference.Aspect / candidate.Aspect - 1) > .18
                || Math.Abs(reference.WidthFraction - candidate.WidthFraction) > .035) return false;
            for (int shift = -1; shift <= 1; shift++)
            {
                bool matched = true;
                double dot = 0, a2 = 0, b2 = 0;
                for (int block = 0; block < 4; block++)
                {
                    double blockDot = 0, blockA2 = 0, blockB2 = 0;
                    for (int y = 0; y < SignatureHeight; y++)
                        for (int x = block * 32; x < (block + 1) * 32; x++)
                        {
                            int cx = x + shift;
                            if (cx < 0 || cx >= SignatureWidth) continue;
                            double a = reference.Ink[y * SignatureWidth + x], b = candidate.Ink[y * SignatureWidth + cx];
                            blockDot += a * b; blockA2 += a * a; blockB2 += b * b;
                        }
                    if (blockA2 == 0 || blockB2 == 0 || blockDot / Math.Sqrt(blockA2 * blockB2) < .74) matched = false;
                    dot += blockDot; a2 += blockA2; b2 += blockB2;
                }
                if (matched && a2 > 0 && b2 > 0 && dot / Math.Sqrt(a2 * b2) >= .86) return true;
            }
            return false;
        }

        private static double Gray(byte[] pixels, int width, int x, int y)
        {
            int p = (y * width + x) * 3;
            return (pixels[p] + pixels[p + 1] + pixels[p + 2]) / 3.0;
        }
        private static bool Light(byte[] pixels, int width, int x, int y)
        {
            int p = (y * width + x) * 3;
            int b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
            int min = Math.Min(r, Math.Min(g, b));
            return min >= 228 && Math.Max(r, Math.Max(g, b)) - min <= 30;
        }
        private static byte[] ReadPixels(Bitmap original)
        {
            using (var bitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImageUnscaled(original, 0, 0);
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var pixels = new byte[bitmap.Width * bitmap.Height * 3];
                    for (int y = 0; y < bitmap.Height; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width * 3, bitmap.Width * 3);
                    return pixels;
                }
                finally { bitmap.UnlockBits(data); }
            }
        }
    }
}
