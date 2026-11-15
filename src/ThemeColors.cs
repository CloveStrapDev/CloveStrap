// Theme colours: pick a theme's colours from a picture, and keep every theme clearly different from the others.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace CloveStrap
{
    static class ThemeColors
    {
        /// <summary>How different two colours look (0 = identical, ~765 = black vs white), weighted the way eyes see.</summary>
        public static double Distance(Color a, Color b)
        {
            double rm = (a.R + b.R) / 2.0;
            double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db);
        }

        public const double MinGap = 110;   // closer than this reads as "the same colour"

        /// <summary>Every theme's accent except the one being edited.</summary>
        public static List<KeyValuePair<string, Color>> Others(string exceptId, IEnumerable<CustomTheme> customs)
        {
            var list = Theme.Schemes.Where(s => s.Character && s.Id != exceptId).Select(s => new KeyValuePair<string, Color>(s.Name, s.Accent)).ToList();
            if (customs != null)
                foreach (var c in customs)
                {
                    if (c.Id == exceptId) continue;
                    var col = Theme.ParseHex(c.Accent);
                    if (col != null) list.Add(new KeyValuePair<string, Color>(string.IsNullOrWhiteSpace(c.Name) ? "another theme" : c.Name, col.Value));
                }
            return list;
        }

        /// <summary>The name of a theme whose colour is too close to this one, or null if it's distinct.</summary>
        public static string TooCloseTo(Color c, List<KeyValuePair<string, Color>> others)
        {
            foreach (var o in others) if (Distance(c, o.Value) < MinGap) return o.Key;
            return null;
        }

        static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        /// <summary>Makes a colour work as an accent: vivid enough to stand out on a dark (or light) background.</summary>
        static Color Vivid(Color c, bool light)
        {
            double h, s, v;
            ToHsv(c, out h, out s, out v);
            s = Math.Max(0.45, Math.Min(0.85, s * 1.15));
            v = light ? Math.Max(0.55, Math.Min(0.75, v)) : Math.Max(0.80, Math.Min(0.97, v + 0.1));
            return FromHsv(h, s, v);
        }

        /// <summary>
        /// Looks at a picture and picks a theme from it: the strongest colour in it as the accent (skipping any that would
        /// look like another theme's), and light or dark from how bright the picture is overall.
        /// </summary>
        public static void FromPicture(Image img, List<KeyValuePair<string, Color>> others, out Color accent, out bool light)
        {
            const int N = 72;
            var bins = new double[24];
            var sumR = new double[24]; var sumG = new double[24]; var sumB = new double[24];
            double lum = 0, count = 0;
            using (var small = new Bitmap(N, N, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    float sc = Math.Min((float)N / img.Width, (float)N / img.Height);
                    g.DrawImage(img, (N - img.Width * sc) / 2, (N - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                }
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var p = small.GetPixel(x, y);
                        if (p.A < 128) continue;
                        lum += (0.299 * p.R + 0.587 * p.G + 0.114 * p.B) / 255; count++;
                        double h, s, v; ToHsv(p, out h, out s, out v);
                        if (s < 0.28 || v < 0.30) continue;
                        int bin = (int)(h / 15) % 24;
                        double w = s * s * v;
                        if (h >= 8 && h <= 45 && s < 0.62) w *= 0.3;   // skin tones make dull accents: prefer clothes, hair and eyes
                        bins[bin] += w; sumR[bin] += p.R * w; sumG[bin] += p.G * w; sumB[bin] += p.B * w;
                    }
            }
            light = count > 0 && lum / count > 0.62;
            var order = Enumerable.Range(0, 24).Where(i => bins[i] > 0).OrderByDescending(i => bins[i]).ToList();
            foreach (int i in order)
            {
                var avg = Color.FromArgb((int)(sumR[i] / bins[i]), (int)(sumG[i] / bins[i]), (int)(sumB[i] / bins[i]));
                var c = Vivid(avg, light);
                if (TooCloseTo(c, others) == null) { accent = c; return; }
            }
            // nothing colourful (or every colour is taken): walk round the colour wheel from the strongest one
            double baseHue = 0, hs, hv;
            if (order.Count > 0) ToHsv(Color.FromArgb((int)(sumR[order[0]] / bins[order[0]]), (int)(sumG[order[0]] / bins[order[0]]), (int)(sumB[order[0]] / bins[order[0]])), out baseHue, out hs, out hv);
            for (int step = 0; step < 24; step++)
            {
                var c = FromHsv(baseHue + step * 15, 0.65, light ? 0.65 : 0.9);
                if (TooCloseTo(c, others) == null) { accent = c; return; }
            }
            accent = FromHsv(baseHue, 0.65, light ? 0.65 : 0.9);
        }

        /// <summary>The first colour from a list that isn't too close to any existing theme.</summary>
        public static string FirstDistinct(IEnumerable<string> hexes, List<KeyValuePair<string, Color>> others)
        {
            foreach (var h in hexes)
            {
                var c = Theme.ParseHex(h);
                if (c != null && TooCloseTo(c.Value, others) == null) return h;
            }
            return null;
        }
    }
}
