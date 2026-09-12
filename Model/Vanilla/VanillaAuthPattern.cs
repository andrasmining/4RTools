using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace _4RTools.Model.Vanilla
{
    internal sealed class VanillaLoginLayout
    {
        public Rectangle UserName { get; set; }
        public Rectangle Password { get; set; }
        public string Evidence { get; set; }
    }

    internal sealed class VanillaServerLayout
    {
        public Rectangle Dialog { get; set; }
        public Rectangle ServerRow { get; set; }
        public string Evidence { get; set; }
    }

    internal static class VanillaAuthPattern
    {
        private sealed class EdgeLine
        {
            public int Y, Left, Right;
            public int Width { get { return Right - Left + 1; } }
            public double CenterX { get { return (Left + Right) / 2.0; } }
        }

        internal static bool TryDetectLogin(Bitmap bitmap, out VanillaLoginLayout layout, out string evidence)
        {
            layout = null;
            evidence = "login controls not detected";
            if (!Usable(bitmap, out evidence)) return false;

            byte[] gray = Gray(bitmap);
            int width = bitmap.Width, height = bitmap.Height;
            Rectangle search = Rectangle.FromLTRB((int)(width * 0.30), (int)(height * 0.50), (int)(width * 0.68), (int)(height * 0.78));
            List<EdgeLine> lines = FindHorizontalLines(gray, width, height, search,
                Math.Max(45, (int)(width * 0.045)), Math.Max(90, (int)(width * 0.18)), 0.35, 0.62);
            if (lines.Count < 3)
            {
                evidence = "found only " + lines.Count + " candidate login control edges in " + search;
                return false;
            }

            EdgeLine[] best = null;
            double bestScore = double.MaxValue;
            for (int a = 0; a < lines.Count - 2; a++)
            for (int b = a + 1; b < lines.Count - 1; b++)
            for (int c = b + 1; c < lines.Count; c++)
            {
                EdgeLine first = lines[a], second = lines[b], third = lines[c];
                double gap1 = second.Y - first.Y, gap2 = third.Y - second.Y;
                double gap = (gap1 + gap2) / 2.0;
                if (gap < Math.Max(9, height * 0.010) || gap > height * 0.055) continue;
                double gapError = Math.Abs(gap1 - gap2) / gap;
                if (gapError > 0.32) continue;
                int overlapLeft = Math.Max(first.Left, Math.Max(second.Left, third.Left));
                int overlapRight = Math.Min(first.Right, Math.Min(second.Right, third.Right));
                int overlap = overlapRight - overlapLeft + 1;
                if (overlap < Math.Min(first.Width, Math.Min(second.Width, third.Width)) * 0.65) continue;
                double centers = Math.Max(first.CenterX, Math.Max(second.CenterX, third.CenterX))
                    - Math.Min(first.CenterX, Math.Min(second.CenterX, third.CenterX));
                if (centers > width * 0.035) continue;
                double meanX = (first.CenterX + second.CenterX + third.CenterX) / 3.0;
                double meanY = (first.Y + second.Y + third.Y) / 3.0;
                double widthError = (Math.Abs(first.Width - second.Width) + Math.Abs(second.Width - third.Width))
                    / (double)Math.Max(1, first.Width + second.Width + third.Width);
                double score = gapError * 3.0 + widthError * 2.0
                    + Math.Abs(meanX / width - 0.49) * 2.0
                    + Math.Abs(meanY / height - 0.65);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new[] { first, second, third };
                }
            }
            if (best == null)
            {
                evidence = "no three regularly stacked login controls matched among " + lines.Count + " edge lines";
                return false;
            }

            int safeLeft = Math.Max(best[0].Left, Math.Max(best[1].Left, best[2].Left));
            int safeRight = Math.Min(best[0].Right, Math.Min(best[1].Right, best[2].Right));
            int xInset = Math.Max(3, (safeRight - safeLeft + 1) / 10);
            safeLeft += xInset;
            safeRight -= xInset;
            int gapPixels = (int)Math.Round(((best[1].Y - best[0].Y) + (best[2].Y - best[1].Y)) / 2.0);
            int yInset = Math.Max(2, gapPixels / 5);
            Rectangle user = Rectangle.FromLTRB(safeLeft, best[1].Y + yInset, safeRight + 1, best[2].Y - yInset + 1);
            int passwordBottom = Math.Min(height - 1, best[2].Y + gapPixels);
            Rectangle password = Rectangle.FromLTRB(safeLeft, best[2].Y + yInset, safeRight + 1, passwordBottom - yInset + 1);
            if (user.Width < 20 || user.Height < 3 || password.Width < 20 || password.Height < 3)
            {
                evidence = "detected login geometry produced unsafe input rectangles";
                return false;
            }

            evidence = "detected stacked login controls; score=" + bestScore.ToString("0.000")
                + "; edges=" + string.Join(",", best.Select(line => line.Y + ":" + line.Left + "-" + line.Right))
                + "; username=" + user + "; password=" + password;
            layout = new VanillaLoginLayout { UserName = user, Password = password, Evidence = evidence };
            return true;
        }

        internal static bool TryDetectServerDialog(Bitmap bitmap, out VanillaServerLayout layout, out string evidence)
        {
            layout = null;
            evidence = "server dialog not detected";
            if (!Usable(bitmap, out evidence)) return false;

            byte[] gray = Gray(bitmap);
            int width = bitmap.Width, height = bitmap.Height;
            Rectangle search = Rectangle.FromLTRB((int)(width * 0.28), (int)(height * 0.44), (int)(width * 0.72), (int)(height * 0.84));
            List<EdgeLine> lines = FindHorizontalLines(gray, width, height, search,
                Math.Max(100, (int)(width * 0.105)), Math.Max(180, (int)(width * 0.30)), 0.34, 0.66);
            if (lines.Count < 2)
            {
                evidence = "found only " + lines.Count + " candidate server-dialog edges in " + search;
                return false;
            }

            EdgeLine top = null, bottom = null;
            double bestScore = double.MaxValue;
            for (int a = 0; a < lines.Count - 1; a++)
            for (int b = a + 1; b < lines.Count; b++)
            {
                EdgeLine first = lines[a], second = lines[b];
                int dialogHeight = second.Y - first.Y;
                if (dialogHeight < Math.Max(70, (int)(height * 0.075)) || dialogHeight > height * 0.28) continue;
                int overlapLeft = Math.Max(first.Left, second.Left), overlapRight = Math.Min(first.Right, second.Right);
                int overlap = overlapRight - overlapLeft + 1;
                if (overlap < Math.Min(first.Width, second.Width) * 0.68) continue;
                double centerDiff = Math.Abs(first.CenterX - second.CenterX);
                if (centerDiff > width * 0.05) continue;
                double meanCenter = (first.CenterX + second.CenterX) / 2.0;
                double meanWidth = (first.Width + second.Width) / 2.0;
                double score = Math.Abs(meanCenter / width - 0.50) * 2.5
                    + Math.Abs(meanWidth / width - 0.17) * 1.2
                    + Math.Abs(dialogHeight / (double)height - 0.16)
                    + centerDiff / width;
                if (score < bestScore)
                {
                    bestScore = score;
                    top = first;
                    bottom = second;
                }
            }
            if (top == null || bottom == null)
            {
                evidence = "no central server-dialog rectangle matched among " + lines.Count + " edge lines";
                return false;
            }

            int left = Math.Min(top.Left, bottom.Left), right = Math.Max(top.Right, bottom.Right);
            int dialogHeightPixels = bottom.Y - top.Y;
            Rectangle dialog = Rectangle.FromLTRB(left, top.Y, right + 1, bottom.Y + 1);
            int rowLeft = left + Math.Max(5, (int)(dialog.Width * 0.07));
            int rowRight = right - Math.Max(5, (int)(dialog.Width * 0.15));
            int rowTop = top.Y + Math.Max(4, (int)(dialogHeightPixels * 0.10));
            int rowBottom = top.Y + Math.Max(8, (int)(dialogHeightPixels * 0.21));
            rowBottom = Math.Min(rowBottom, bottom.Y - 4);
            Rectangle row = Rectangle.FromLTRB(rowLeft, rowTop, rowRight + 1, rowBottom + 1);
            if (row.Width < 30 || row.Height < 4)
            {
                evidence = "server dialog was found but its first-row safe area is too small";
                return false;
            }

            evidence = "detected one-server dialog; score=" + bestScore.ToString("0.000")
                + "; dialog=" + dialog + "; firstRowSafe=" + row;
            layout = new VanillaServerLayout { Dialog = dialog, ServerRow = row, Evidence = evidence };
            return true;
        }

        internal static Point PickInside(Rectangle rectangle, int seed)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0) throw new ArgumentException("Safe click rectangle is empty.");
            int marginX = Math.Max(1, rectangle.Width / 5);
            int marginY = Math.Max(1, rectangle.Height / 5);
            int minX = rectangle.Left + Math.Min(marginX, Math.Max(0, rectangle.Width - 1));
            int maxX = rectangle.Right - Math.Min(marginX, Math.Max(0, rectangle.Width - 1));
            int minY = rectangle.Top + Math.Min(marginY, Math.Max(0, rectangle.Height - 1));
            int maxY = rectangle.Bottom - Math.Min(marginY, Math.Max(0, rectangle.Height - 1));
            if (maxX <= minX) { minX = rectangle.Left; maxX = rectangle.Right; }
            if (maxY <= minY) { minY = rectangle.Top; maxY = rectangle.Bottom; }
            var random = new Random(seed);
            return new Point(random.Next(minX, Math.Max(minX + 1, maxX)), random.Next(minY, Math.Max(minY + 1, maxY)));
        }

        private static bool Usable(Bitmap bitmap, out string evidence)
        {
            if (bitmap == null) { evidence = "bitmap is null"; return false; }
            if (bitmap.Width < 480 || bitmap.Height < 320)
            {
                evidence = "bitmap too small: " + bitmap.Width + "x" + bitmap.Height;
                return false;
            }
            evidence = null;
            return true;
        }

        private static List<EdgeLine> FindHorizontalLines(byte[] gray, int width, int height, Rectangle search,
            int minWidth, int maxWidth, double minCenterX, double maxCenterX)
        {
            List<EdgeLine> raw = FindHorizontalLines(gray, width, height, search, minWidth, maxWidth, minCenterX, maxCenterX, 20);
            if (raw.Count < 3) raw = FindHorizontalLines(gray, width, height, search, minWidth, maxWidth, minCenterX, maxCenterX, 11);
            int clusterTolerance = Math.Max(2, height / 320);
            var clustered = new List<EdgeLine>();
            foreach (EdgeLine candidate in raw.OrderBy(line => line.Y).ThenByDescending(line => line.Width))
            {
                int index = clustered.FindIndex(line => Math.Abs(line.Y - candidate.Y) <= clusterTolerance
                    && HorizontalOverlap(line, candidate) >= Math.Min(line.Width, candidate.Width) * 0.50);
                if (index < 0) clustered.Add(candidate);
                else if (candidate.Width > clustered[index].Width) clustered[index] = candidate;
            }
            return clustered.OrderBy(line => line.Y).ToList();
        }

        private static List<EdgeLine> FindHorizontalLines(byte[] gray, int width, int height, Rectangle search,
            int minWidth, int maxWidth, double minCenterX, double maxCenterX, int threshold)
        {
            var lines = new List<EdgeLine>();
            int allowedGap = Math.Max(3, width / 550);
            for (int y = Math.Max(1, search.Top); y < Math.Min(height - 1, search.Bottom); y++)
            {
                int bestLeft = -1, bestRight = -1;
                int start = -1, lastEdge = -1000;
                for (int x = Math.Max(1, search.Left); x < Math.Min(width - 1, search.Right); x++)
                {
                    int gradient = Math.Abs(gray[(y + 1) * width + x] - gray[(y - 1) * width + x]);
                    if (gradient >= threshold)
                    {
                        if (start < 0 || x - lastEdge > allowedGap) start = x;
                        lastEdge = x;
                    }
                    if (start >= 0 && (x - lastEdge > allowedGap || x == search.Right - 1))
                    {
                        int right = lastEdge;
                        int span = right - start + 1;
                        double center = (start + right) / 2.0 / width;
                        if (span >= minWidth && span <= maxWidth && center >= minCenterX && center <= maxCenterX
                            && (bestLeft < 0 || span > bestRight - bestLeft + 1))
                        {
                            bestLeft = start;
                            bestRight = right;
                        }
                        start = -1;
                    }
                }
                if (bestLeft >= 0) lines.Add(new EdgeLine { Y = y, Left = bestLeft, Right = bestRight });
            }
            return lines;
        }

        private static int HorizontalOverlap(EdgeLine a, EdgeLine b)
        {
            return Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) + 1);
        }

        private static byte[] Gray(Bitmap bitmap)
        {
            Bitmap source = bitmap.PixelFormat == PixelFormat.Format24bppRgb
                ? bitmap
                : Clone24(bitmap);
            bool dispose = !object.ReferenceEquals(source, bitmap);
            BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * source.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                byte[] gray = new byte[source.Width * source.Height];
                for (int y = 0; y < source.Height; y++)
                {
                    int row = data.Stride >= 0 ? y * stride : (source.Height - 1 - y) * stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int offset = row + x * 3;
                        int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                        gray[y * source.Width + x] = (byte)((30 * r + 59 * g + 11 * b) / 100);
                    }
                }
                return gray;
            }
            finally
            {
                source.UnlockBits(data);
                if (dispose) source.Dispose();
            }
        }

        private static Bitmap Clone24(Bitmap bitmap)
        {
            var clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(clone)) graphics.DrawImageUnscaled(bitmap, 0, 0);
            return clone;
        }
    }
}
