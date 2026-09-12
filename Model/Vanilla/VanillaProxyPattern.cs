using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace _4RTools.Model.Vanilla
{
    internal sealed class VanillaProxyLayout
    {
        public Rectangle SearchArea { get; set; }
        public Rectangle[] Rows { get; set; }
        public string Evidence { get; set; }
    }

    internal static class VanillaProxyPattern
    {
        private sealed class Band
        {
            public int Top, Bottom, Left, Right;
            public double CenterY;
            public int Width { get { return Right - Left + 1; } }
            public int Height { get { return Bottom - Top + 1; } }
        }

        internal static bool TryDetect(Bitmap bitmap, out VanillaProxyLayout layout, out string evidence)
        {
            layout = null;
            evidence = "proxy list not detected";
            if (bitmap == null || bitmap.Width < 480 || bitmap.Height < 320)
            {
                evidence = bitmap == null ? "bitmap is null" : "bitmap too small: " + bitmap.Width + "x" + bitmap.Height;
                return false;
            }

            int width = bitmap.Width, height = bitmap.Height;
            var search = Rectangle.FromLTRB((int)(width * 0.28), (int)(height * 0.43), (int)(width * 0.72), (int)(height * 0.78));
            int[] counts = new int[search.Height];
            int peak = 0;
            for (int y = search.Top; y < search.Bottom; y++)
            {
                int count = 0;
                for (int x = search.Left; x < search.Right; x++) if (IsDarkNeutral(bitmap.GetPixel(x, y))) count++;
                counts[y - search.Top] = count;
                if (count > peak) peak = count;
            }
            if (peak < 8) { evidence = "no dark text bands in central service-list area"; return false; }

            int threshold = Math.Max(5, (int)Math.Round(peak * 0.20));
            var bands = new List<Band>();
            int i = 0;
            while (i < counts.Length)
            {
                if (counts[i] < threshold) { i++; continue; }
                int start = i;
                while (i + 1 < counts.Length && counts[i + 1] >= threshold) i++;
                int end = i;
                int left = search.Right, right = search.Left - 1;
                for (int yy = search.Top + start; yy <= search.Top + end; yy++)
                    for (int x = search.Left; x < search.Right; x++)
                        if (IsDarkNeutral(bitmap.GetPixel(x, yy))) { if (x < left) left = x; if (x > right) right = x; }
                if (right >= left)
                {
                    var band = new Band { Top = search.Top + start, Bottom = search.Top + end, Left = left, Right = right, CenterY = search.Top + (start + end) / 2.0 };
                    if (band.Width >= Math.Max(28, (int)(width * 0.025)) && band.Height <= Math.Max(30, (int)(height * 0.040))) bands.Add(band);
                }
                i++;
            }
            if (bands.Count < 4) { evidence = "found only " + bands.Count + " plausible text bands"; return false; }

            Band[] best = null;
            double bestScore = double.MaxValue;
            for (int a = 0; a < bands.Count - 3; a++)
            for (int b = a + 1; b < bands.Count - 2; b++)
            for (int c = b + 1; c < bands.Count - 1; c++)
            for (int d = c + 1; d < bands.Count; d++)
            {
                var rows = new[] { bands[a], bands[b], bands[c], bands[d] };
                double d1 = rows[1].CenterY - rows[0].CenterY, d2 = rows[2].CenterY - rows[1].CenterY, d3 = rows[3].CenterY - rows[2].CenterY;
                double spacing = (d1 + d2 + d3) / 3.0;
                if (spacing < height * 0.012 || spacing > height * 0.060) continue;
                double spacingError = (Math.Abs(d1 - spacing) + Math.Abs(d2 - spacing) + Math.Abs(d3 - spacing)) / spacing;
                if (spacingError > 0.50) continue;
                int commonLeft = rows.Max(r => r.Left), commonRight = rows.Min(r => r.Right);
                if (commonRight - commonLeft < Math.Max(18, (int)(width * 0.018))) continue;
                double centerX = rows.Average(r => (r.Left + r.Right) / 2.0), centerY = rows.Average(r => r.CenterY);
                double score = spacingError + Math.Abs(centerX / width - 0.50) * 3.0 + Math.Abs(centerY / height - 0.60) * 1.5;
                if (score < bestScore) { bestScore = score; best = rows; }
            }
            if (best == null) { evidence = "no regular four-row service-list pattern found"; return false; }

            int safeLeft = best.Max(r => r.Left), safeRight = best.Min(r => r.Right);
            int commonWidth = safeRight - safeLeft + 1;
            int inset = Math.Max(2, commonWidth / 8);
            safeLeft += inset; safeRight -= inset;
            if (safeRight <= safeLeft) { evidence = "detected rows have no common safe horizontal area"; return false; }
            double meanSpacing = ((best[1].CenterY - best[0].CenterY) + (best[2].CenterY - best[1].CenterY) + (best[3].CenterY - best[2].CenterY)) / 3.0;
            int halfHeight = Math.Max(2, (int)Math.Floor(meanSpacing * 0.16));
            var rowsOut = new Rectangle[4];
            for (int row = 0; row < 4; row++)
            {
                int cy = (int)Math.Round(best[row].CenterY);
                rowsOut[row] = Rectangle.FromLTRB(safeLeft, cy - halfHeight, safeRight + 1, cy + halfHeight + 1);
                if (!search.Contains(rowsOut[row])) { evidence = "safe row escaped search area"; return false; }
            }
            evidence = "detected four proxy rows; score=" + bestScore.ToString("0.000") + "; rows=" + string.Join(" | ", rowsOut.Select((r, n) => n + ":" + r));
            layout = new VanillaProxyLayout { SearchArea = search, Rows = rowsOut, Evidence = evidence };
            return true;
        }

        private static bool IsDarkNeutral(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            int luminance = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
            return luminance <= 150 && (max - min) <= 95;
        }
    }
}
