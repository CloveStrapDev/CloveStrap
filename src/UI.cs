// CloveStrap UI - custom-drawn interface (C# 5 / WinForms)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CloveStrap
{
    // ------------------------------------------------------------------ theme
    static class Theme
    {
        public static Color Bg, Side, Card, CardHot, Row, Input, InputHot, Selected, Border, BorderHot, Line,
                            Text, Muted, Dim, Warn, Bad, Accent, OnAccent, ToggleOff, Track, Knob, NavHover, NavActive;
        public static bool Light;
        public static string SchemeId;

        public class Scheme
        {
            public string Id, Name, Sub;
            public Color Accent, OnAccent, TintColor;
            public bool Light, Character;
            public float Tint;
            public Scheme(string id, string name, string sub, Color accent, Color on, bool light, bool character, Color tint, float amount)
            {
                Id = id; Name = name; Sub = sub; Accent = accent; OnAccent = on; Light = light; Character = character; TintColor = tint; Tint = amount;
            }
        }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static readonly Scheme[] Schemes =
        {
            new Scheme("clove", "Clove", "Lavender & plum", C(186, 132, 236), C(22, 10, 34), false, true, C(120, 64, 160), 0.05f),
            new Scheme("natsuki", "Natsuki", "Pink & black", C(255, 111, 174), C(30, 6, 16), false, true, C(255, 111, 174), 0.035f),
            new Scheme("mikasa", "Mikasa", "Black & scarf red", C(200, 42, 56), Color.White, false, true, C(120, 20, 30), 0.03f),
            new Scheme("dio", "DIO", "Gold & shadow", C(236, 188, 52), C(26, 18, 2), false, true, C(236, 160, 40), 0.035f),
            new Scheme("asuna", "Asuna", "White & knight red", C(214, 56, 64), Color.White, true, true, C(255, 214, 196), 0.18f),

            new Scheme("sakura", "Sakura", "Dark", C(249, 168, 212), C(26, 10, 18), false, false, C(249, 168, 212), 0.02f),
            new Scheme("violet", "Violet", "Dark", C(167, 139, 250), C(14, 8, 28), false, false, C(167, 139, 250), 0.02f),
            new Scheme("ice", "Ice", "Dark", C(125, 211, 252), C(6, 18, 26), false, false, C(125, 211, 252), 0.02f),
            new Scheme("lime", "Lime", "Dark", C(205, 245, 100), C(12, 14, 6), false, false, C(205, 245, 100), 0.015f),
            new Scheme("amber", "Amber", "Dark", C(251, 191, 36), C(24, 16, 2), false, false, C(251, 191, 36), 0.02f),
            new Scheme("crimson", "Crimson", "Dark", C(244, 63, 94), Color.White, false, false, C(244, 63, 94), 0.02f),
            new Scheme("mono", "Mono", "Dark", C(240, 240, 243), C(12, 12, 14), false, false, C(255, 255, 255), 0f),
            new Scheme("paper", "Paper", "Light", C(32, 32, 38), Color.White, true, false, C(255, 255, 255), 0f),
        };

        // the user's own themes (Appearance > Your themes), keyed by id ("custom" or "custom-xxxxxxxx")
        static Dictionary<string, Scheme> customs = new Dictionary<string, Scheme>();

        static Theme() { ApplyScheme("clove"); }

        public static bool IsCustom(string id) { return id != null && id.StartsWith("custom", StringComparison.Ordinal); }

        public static Scheme Get(string id)
        {
            Scheme s;
            if (id != null && customs.TryGetValue(id, out s)) return s;
            return Schemes.FirstOrDefault(x => x.Id == id) ?? Schemes[0];
        }

        public static Scheme MakeCustom(CustomTheme t)
        {
            Color a = ParseHex(t.Accent) ?? C(255, 111, 174);
            bool bright = a.R * 0.299 + a.G * 0.587 + a.B * 0.114 > 150;
            Color on = bright ? Mix(C(14, 14, 16), a, 0.12f) : Color.White;
            string name = string.IsNullOrWhiteSpace(t.Name) ? "Untitled" : t.Name.Trim();
            return t.Light
                ? new Scheme(t.Id, name, "", a, on, true, true, Mix(Color.White, a, 0.35f), 0.15f)
                : new Scheme(t.Id, name, "", a, on, false, true, a, 0.035f);
        }

        public static void SetCustoms(IEnumerable<CustomTheme> list)
        {
            var d = new Dictionary<string, Scheme>();
            if (list != null) foreach (var t in list) if (t != null && IsCustom(t.Id)) d[t.Id] = MakeCustom(t);
            customs = d;
        }

        public static Color? ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            hex = hex.Trim().TrimStart('#');
            int v;
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v)) return null;
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static string Hex(Color c) { return "#" + (c.ToArgb() & 0xFFFFFF).ToString("X6"); }

        public static Color PreviewBg(Scheme s) { return s.Light ? Mix(C(243, 244, 246), s.TintColor, s.Tint) : Mix(C(11, 11, 12), s.TintColor, s.Tint); }
        public static Color PreviewCard(Scheme s) { return s.Light ? C(255, 255, 255) : Mix(C(19, 19, 21), s.TintColor, s.Tint); }

        public static void ApplyScheme(string id)
        {
            var s = Get(id);
            SchemeId = s.Id; Light = s.Light; Accent = s.Accent; OnAccent = s.OnAccent;
            float t = s.Tint; Color tc = s.TintColor;
            if (!Light)
            {
                Bg = Mix(C(11, 11, 12), tc, t); Side = Mix(C(14, 14, 16), tc, t); Card = Mix(C(19, 19, 21), tc, t);
                CardHot = Mix(C(24, 24, 27), tc, t); Row = Mix(C(22, 22, 25), tc, t);
                Input = Mix(C(25, 25, 28), tc, t); InputHot = Mix(C(34, 34, 38), tc, t);
                Border = Mix(C(37, 37, 41), tc, t * 1.6f); BorderHot = Mix(C(62, 62, 68), tc, t * 2); Line = Mix(C(30, 30, 33), tc, t);
                Text = C(236, 236, 239); Muted = C(146, 146, 154); Dim = C(98, 98, 106);
                Warn = C(245, 185, 74); Bad = C(248, 113, 113);
                ToggleOff = Mix(C(42, 42, 46), tc, t); Track = ToggleOff; Knob = C(236, 236, 239);
                NavHover = Mix(C(22, 22, 25), tc, t); NavActive = Mix(C(26, 26, 29), Accent, 0.12f);
                Selected = Mix(C(40, 40, 45), Accent, 0.2f);
            }
            else
            {
                Bg = Mix(C(243, 244, 246), tc, t); Side = Mix(C(255, 255, 255), tc, t * 0.35f); Card = C(255, 255, 255);
                CardHot = Mix(C(251, 251, 252), tc, t * 0.5f); Row = C(255, 255, 255);
                Input = C(255, 255, 255); InputHot = Mix(C(246, 247, 249), tc, t * 0.5f);
                Border = Mix(C(226, 228, 233), tc, t * 0.5f); BorderHot = Mix(C(196, 200, 208), Accent, 0.15f); Line = Mix(C(236, 237, 240), tc, t * 0.5f);
                Text = C(28, 28, 34); Muted = C(104, 110, 122); Dim = C(150, 156, 168);
                Warn = C(196, 112, 8); Bad = C(205, 45, 45);
                ToggleOff = C(220, 223, 229); Track = C(225, 228, 233); Knob = C(255, 255, 255);
                NavHover = Mix(C(246, 247, 249), tc, t * 0.5f); NavActive = Mix(C(255, 255, 255), Accent, 0.09f);
                Selected = Mix(C(255, 255, 255), Accent, 0.16f);
            }
        }

        public static readonly Font H1 = F("Segoe UI Variable Display Semib", 17f, "Segoe UI Semibold");
        public static readonly Font H2 = F("Segoe UI Variable Display Semib", 12.5f, "Segoe UI Semibold");
        public static readonly Font H3 = F("Segoe UI Variable Display Semib", 11.5f, "Segoe UI Semibold");
        public static readonly Font RowTitle = F("Segoe UI Variable Text Semibold", 9.75f, "Segoe UI Semibold");
        public static readonly Font Body = F("Segoe UI Variable Text", 9.5f, "Segoe UI");
        public static readonly Font Small = F("Segoe UI Variable Text", 8.75f, "Segoe UI");
        public static readonly Font Logo = F("Segoe UI Variable Display Semib", 12.5f, "Segoe UI Semibold");
        public static readonly Font Mono = F("Cascadia Mono", 7.75f, "Consolas");
        public static readonly Font MonoBody = F("Cascadia Mono", 9f, "Consolas");
        public static readonly Font Icon = F("Segoe Fluent Icons", 11f, "Segoe MDL2 Assets");
        public static readonly Font IconSmall = F("Segoe Fluent Icons", 9f, "Segoe MDL2 Assets");

        static Font F(string name, float size, string fallback)
        {
            var f = new Font(name, size);
            if (!string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { f.Dispose(); f = new Font(fallback, size); }
            return f;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static bool IsDark(Color c) { return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 140; }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix);
        }

        public static Size Measure(string s, Font f)
        {
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>The clove star: four points joined by smooth inward curves (a clove flower bud seen from above).</summary>
        public static GraphicsPath CloveStar(float cx, float cy, float radius, float waist)
        {
            var p = new GraphicsPath();
            var tips = new[] { new PointF(cx, cy - radius), new PointF(cx + radius, cy), new PointF(cx, cy + radius), new PointF(cx - radius, cy) };
            for (int i = 0; i < 4; i++)
            {
                PointF a = tips[i], b = tips[(i + 1) % 4];
                float dx = (a.X + b.X) / 2 - cx, dy = (a.Y + b.Y) / 2 - cy;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                var mid = new PointF(cx + dx / len * radius * waist, cy + dy / len * radius * waist);
                p.AddBezier(a, mid, mid, b);
            }
            p.CloseFigure();
            return p;
        }

        public static int LogoStyle = 1;   // 0 plain star, 1 star with a bud, 2 star with a small inner star

        static Image brandLogo;
        static bool brandTried;

        /// <summary>The CloveStrap logo (the "C" with fox ears), built into the exe.</summary>
        public static Image BrandLogo()
        {
            if (brandTried) return brandLogo;
            brandTried = true;
            try
            {
                using (var s = typeof(Theme).Assembly.GetManifestResourceStream("CloveStrap.logo.png"))
                    if (s != null) using (var tmp = Image.FromStream(s)) brandLogo = new Bitmap(tmp);
            }
            catch { }
            return brandLogo;
        }

        public static void DrawLogo(Graphics g, RectangleF r)
        {
            var img = BrandLogo();
            if (img == null) { DrawLogo(g, r, Accent); return; }
            var oldI = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawContain(g, img, r, false);
            g.InterpolationMode = oldI;
        }

        public static void DrawLogo(Graphics g, RectangleF r, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float w = r.Width;
            Color bg = Color.FromArgb(18, 18, 20);
            Fill(g, r, w * 0.24f, bg);
            Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, w - 1, w - 1), w * 0.24f, Color.FromArgb(48, 48, 54));
            float cx = r.X + w / 2f, cy = r.Y + w / 2f, rad = w * 0.39f;
            float waist = w <= 20 ? 0.36f : w <= 34 ? 0.30f : 0.25f;   // fuller at small sizes so it still reads in the taskbar
            using (var star = CloveStar(cx, cy, rad, waist))
            using (var b = new SolidBrush(accent))
                g.FillPath(b, star);
            if (w >= 24 && LogoStyle == 1)
            {
                float d = rad * 0.34f;
                using (var hole = new SolidBrush(bg)) g.FillEllipse(hole, cx - d / 2, cy - d / 2, d, d);
            }
            else if (w >= 24 && LogoStyle == 2)
            {
                using (var inner = CloveStar(cx, cy, rad * 0.34f, 0.2f))
                using (var hb = new SolidBrush(bg))
                    g.FillPath(hb, inner);
            }
        }

        /// <summary>Draws an image scaled to fit, anchored to the bottom centre.</summary>
        public static void DrawContain(Graphics g, Image img, RectangleF area, bool bottom = true)
        {
            float sc = Math.Min(area.Width / img.Width, area.Height / img.Height);
            float w = img.Width * sc, h = img.Height * sc;
            float x = area.X + (area.Width - w) / 2, y = bottom ? area.Bottom - h : area.Y + (area.Height - h) / 2;
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, x, y, w, h);
            g.InterpolationMode = old;
        }
    }

    // ------------------------------------------------------------------ error voice (only for errors)
    static class Cute
    {
        static readonly string[] Faces = { "OwO", "UwU", ">_<", "qwq", "(｡•́︿•̀｡)", "nya~" };
        static readonly Random R = new Random();

        public static string Face() { return Faces[R.Next(Faces.Length)]; }

        public static string Wont(string what, string without)
        {
            return "CloveStrap won't " + what + " without " + without + " " + Face();
        }

        public static string Reason(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is WebException || e is System.Net.Sockets.SocketException) return "an internet connection";
                if (e is UnauthorizedAccessException) return "permission to write its files";
                string m = e.Message ?? "";
                if (m.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0) return "Roblox closed first";
                if (m.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0) return "an internet connection";
                if (m.IndexOf("close Roblox", StringComparison.OrdinalIgnoreCase) >= 0) return "Roblox closed first";
            }
            return "a little help";
        }
    }

    // ------------------------------------------------------------------ character pictures (user supplied)
    static class Characters
    {
        static readonly string Dir = Path.Combine(Paths.Base, "Characters");
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        static string FileFor(string id) { return Path.Combine(Dir, id + ".png"); }

        // Character themes use pictures built into the exe; only the Custom theme uses a picture the user picks.
        public static Image Get(string id)
        {
            Image img;
            if (cache.TryGetValue(id, out img)) return img;
            img = null;
            try
            {
                if (Theme.IsCustom(id))
                {
                    string f = FileFor(id);
                    if (File.Exists(f))
                        using (var fs = File.OpenRead(f)) using (var tmp = Image.FromStream(fs)) img = new Bitmap(tmp);
                }
                else
                {
                    using (var s = typeof(Characters).Assembly.GetManifestResourceStream("CloveStrap.Characters." + id + ".png"))
                        if (s != null) using (var tmp = Image.FromStream(s)) img = new Bitmap(tmp);
                }
            }
            catch { img = null; }
            cache[id] = img;
            return img;
        }

        public static void Set(string id, string sourceFile)
        {
            Directory.CreateDirectory(Dir);
            using (var fs = File.OpenRead(sourceFile))
            using (var src = Image.FromStream(fs))
            {
                float sc = Math.Min(1f, 1400f / Math.Max(src.Width, src.Height));
                using (var bmp = new Bitmap(Math.Max(1, (int)(src.Width * sc)), Math.Max(1, (int)(src.Height * sc)), PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, bmp.Width, bmp.Height);
                    }
                    bmp.Save(FileFor(id), ImageFormat.Png);
                }
            }
            Image old;
            if (cache.TryGetValue(id, out old) && old != null) old.Dispose();
            cache.Remove(id);
        }

        public static void Remove(string id)
        {
            Image old;
            if (cache.TryGetValue(id, out old) && old != null) old.Dispose();
            cache.Remove(id);
            try { File.Delete(FileFor(id)); } catch { }
        }
    }

    // ------------------------------------------------------------------ native helpers
    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static void StyleWindow(IntPtr h)
        {
            try
            {
                int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
                int border = ColorTranslator.ToWin32(Theme.Border); DwmSetWindowAttribute(h, 34, ref border, 4);
                int dark = Theme.Light ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
            }
            catch { }
        }

        public static void Scrollbars(Control c)
        {
            try
            {
                string theme = Theme.Light ? "Explorer" : "DarkMode_Explorer";
                SetWindowTheme(c.Handle, theme, null);
                foreach (Control k in c.Controls) SetWindowTheme(k.Handle, theme, null);
            }
            catch { }
        }

        public static void Placeholder(TextBox t, string text)
        {
            try { SendMessage(t.Handle, 0x1501, (IntPtr)1, text); } catch { }
        }
    }

    // ------------------------------------------------------------------ base control
    class VControl : Control
    {
        protected bool Hover, Down;

        public VControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
        }

        protected Graphics Begin(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            return g;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
    }

    // ------------------------------------------------------------------ toggle switch
    class Toggle : VControl
    {
        bool on;
        float anim;
        readonly Timer timer = new Timer { Interval = 15 };
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            Size = new Size(40, 22);
            Cursor = Cursors.Hand;
            timer.Tick += (o, e) =>
            {
                float target = on ? 1f : 0f;
                anim += (target - anim) * 0.35f;
                if (Math.Abs(target - anim) < 0.01f) { anim = target; timer.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set { on = value; anim = value ? 1f : 0f; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            on = !on;
            timer.Start();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color off = Hover ? Theme.Mix(Theme.ToggleOff, Theme.Muted, 0.15f) : Theme.ToggleOff;
            Theme.Fill(g, r, r.Height / 2, Theme.Mix(off, Theme.Accent, anim));
            float d = Height - 8;
            float x = 4f + anim * (Width - d - 8.5f);
            Color knob = Theme.Light ? Color.White : Theme.Mix(Theme.Muted, Theme.OnAccent, anim);
            if (Theme.Light) using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, x, 4.5f, d, d);
            using (var b = new SolidBrush(knob)) g.FillEllipse(b, x, 3.5f, d, d);
        }
    }

    // ------------------------------------------------------------------ segmented control (single choice)
    class Chips : VControl
    {
        string[] items = new string[0];
        readonly List<Rectangle> rects = new List<Rectangle>();
        int sel = -1, hot = -1;
        public event EventHandler SelectedIndexChanged;
        const int H = 32, Pad = 3;

        public Chips(params string[] items)
        {
            Cursor = Cursors.Hand;
            Items = items;
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; Relayout(); }
        }

        public int SelectedIndex
        {
            get { return sel; }
            set { sel = value; Invalidate(); }
        }

        void Relayout()
        {
            rects.Clear();
            int x = Pad;
            foreach (var s in items)
            {
                int w = Theme.Measure(s, Theme.Body).Width + 24;
                rects.Add(new Rectangle(x, Pad, w, H - Pad * 2));
                x += w;
            }
            Size = new Size(x + Pad, H);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i >= 0 && i != sel)
            {
                sel = i;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, outer, 8, Theme.Light ? Color.FromArgb(244, 245, 247) : Theme.Input);
            Theme.Stroke(g, outer, 8, Theme.Border);
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (i == sel)
                {
                    Theme.Fill(g, r, 6, Theme.Selected);
                    if (Theme.Light) Theme.Stroke(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 6, Theme.Mix(Theme.Border, Theme.Accent, 0.4f));
                }
                else if (i == hot) Theme.Fill(g, r, 6, Theme.InputHot);
                Theme.Draw(g, items[i], Theme.Body, r, i == sel ? (Theme.Light ? Theme.Mix(Theme.Accent, Color.Black, 0.25f) : Theme.Text) : (i == hot ? Theme.Text : Theme.Muted),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // ------------------------------------------------------------------ multi-select pills (wrapping)
    class MultiChips : VControl
    {
        readonly string[] labels;
        public readonly bool[] On;
        readonly List<Rectangle> rects = new List<Rectangle>();
        int hot = -1;
        public event EventHandler Changed;

        public MultiChips(string[] labels, int width)
        {
            this.labels = labels;
            On = new bool[labels.Length];
            Width = width;
            Cursor = Cursors.Hand;
            Relayout();
        }

        void Relayout()
        {
            rects.Clear();
            int x = 0, y = 0;
            foreach (var s in labels)
            {
                int w = Theme.Measure(s, Theme.Body).Width + 34;
                if (x > 0 && x + w > Width) { x = 0; y += 36; }
                rects.Add(new Rectangle(x, y, w, 30));
                x += w + 6;
            }
            Height = (rects.Count == 0 ? 30 : rects.Max(r => r.Bottom)) + 1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i >= 0)
            {
                On[i] = !On[i];
                Invalidate();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                if (On[i])
                {
                    Theme.Fill(g, rf, 15, Theme.Selected);
                    Theme.Stroke(g, rf, 15, Theme.Mix(Theme.Border, Theme.Accent, 0.55f));
                }
                else
                {
                    Theme.Fill(g, rf, 15, i == hot ? Theme.InputHot : Theme.Input);
                    Theme.Stroke(g, rf, 15, Theme.Border);
                }
                Theme.Draw(g, On[i] ? "" : "", Theme.IconSmall, new Rectangle(r.X + 8, r.Y, 14, r.Height), On[i] ? Theme.Accent : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.Draw(g, labels[i], Theme.Body, new Rectangle(r.X + 24, r.Y, r.Width - 28, r.Height), On[i] || i == hot ? Theme.Text : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // ------------------------------------------------------------------ slider
    class Slider : VControl
    {
        readonly int min, max;
        int val;
        public Func<int, string> Format = v => v.ToString();
        public event EventHandler ValueChanged;
        const int LabelW = 70;

        public Slider(int min, int max)
        {
            this.min = min; this.max = max;
            Size = new Size(300, 30);
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return val; }
            set { val = Math.Max(min, Math.Min(max, value)); Invalidate(); }
        }

        RectangleF Track { get { return new RectangleF(8, Height / 2f - 2, Width - LabelW - 20, 4); } }

        void SetFromX(int x)
        {
            var t = Track;
            int v = (int)Math.Round(min + (max - min) * Math.Max(0, Math.Min(1, (x - t.X) / t.Width)));
            if (v != val)
            {
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFromX(e.X); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var t = Track;
            Theme.Fill(g, t, 2, Theme.Track);
            float frac = max == min ? 0 : (val - min) / (float)(max - min);
            float kx = t.X + frac * t.Width;
            if (frac > 0) Theme.Fill(g, new RectangleF(t.X, t.Y, kx - t.X, t.Height), 2, Theme.Accent);
            float d = Hover || Down ? 16 : 14;
            var kr = new RectangleF(kx - d / 2, Height / 2f - d / 2, d, d);
            using (var b = new SolidBrush(Theme.Knob)) g.FillEllipse(b, kr);
            if (Theme.Light) using (var p = new Pen(Theme.Accent, 2f)) g.DrawEllipse(p, kr.X + 1, kr.Y + 1, kr.Width - 2, kr.Height - 2);
            var lr = new Rectangle(Width - LabelW, 1, LabelW - 1, Height - 3);
            Theme.Fill(g, lr, 7, Theme.Input);
            Theme.Stroke(g, new RectangleF(lr.X + 0.5f, lr.Y + 0.5f, lr.Width - 1, lr.Height - 1), 7, Theme.Border);
            Theme.Draw(g, Format(val), Theme.MonoBody, lr, val == min ? Theme.Muted : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------ button
    class VButton : VControl
    {
        public bool Primary;
        public string Glyph;
        public bool Big;

        public VButton(string text, string glyph = null, bool primary = false)
        {
            Text = text; Glyph = glyph; Primary = primary;
            Cursor = Cursors.Hand;
            int w = Theme.Measure(text, Theme.Body).Width + 30 + (glyph != null ? 22 : 0);
            Size = new Size(Math.Max(84, w), 34);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = 8;
            Color fg;
            if (Primary)
            {
                Color c = Theme.Accent;
                if (Hover) c = Theme.Mix(c, Color.White, 0.15f);
                if (Down) c = Theme.Mix(c, Color.Black, 0.12f);
                Theme.Fill(g, r, rad, c);
                fg = Theme.OnAccent;
            }
            else
            {
                Theme.Fill(g, r, rad, Down ? Theme.Input : Hover ? Theme.InputHot : Theme.Input);
                Theme.Stroke(g, r, rad, Hover ? Theme.BorderHot : Theme.Border);
                fg = Theme.Text;
            }
            Font f = Big ? Theme.RowTitle : Theme.Body;
            Size ts = Theme.Measure(Text, f);
            int gw = Glyph != null ? 22 : 0;
            int x = (Width - ts.Width - gw) / 2;
            if (Glyph != null)
                Theme.Draw(g, Glyph, Theme.IconSmall, new Rectangle(x, 0, 16, Height), Primary ? fg : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Text, f, new Rectangle(x + gw, 0, ts.Width + 4, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    }

    // ------------------------------------------------------------------ title bar button
    class IconButton : VControl
    {
        public string Glyph;
        public bool Danger;

        public IconButton(string glyph, bool danger = false)
        {
            Glyph = glyph; Danger = danger;
            Size = new Size(46, 34);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            if (Hover) using (var b = new SolidBrush(Danger ? Color.FromArgb(196, 43, 28) : Theme.InputHot)) g.FillRectangle(b, ClientRectangle);
            Theme.Draw(g, Glyph, Theme.IconSmall, ClientRectangle, Hover && Danger ? Color.White : Hover ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------ status pill (top right)
    class StatusPill : VControl
    {
        Color dot;
        public int AnchorRight, MinLeft = 170;
        public StatusPill() { Height = 30; dot = Theme.Accent; }

        const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // Long messages wrap onto two lines instead of being cut off
        public void Set(string text, Color c)
        {
            Text = text;
            dot = c;
            int max = Math.Max(160, AnchorRight - MinLeft);
            int one = Theme.Measure(text, Theme.Small).Width + 40;
            if (one <= max)
            {
                Width = one; Height = 30; Top = 13;
            }
            else
            {
                Width = max;
                int th = TextRenderer.MeasureText(text, Theme.Small, new Size(max - 40, 0), Wrap).Height;
                Height = Math.Min(46, th + 12); Top = Math.Max(4, 28 - Height / 2);
            }
            Left = AnchorRight - Width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Math.Min(15, r.Height / 2);
            Theme.Fill(g, r, rad, Theme.Card);
            Theme.Stroke(g, r, rad, Theme.Border);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, 13, Height / 2f - 3, 6, 6);
            Color fg = dot == Theme.Accent ? Theme.Muted : dot;
            if (Height <= 30)
                Theme.Draw(g, Text, Theme.Small, new Rectangle(26, 0, Width - 30, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            else
            {
                int th = TextRenderer.MeasureText(Text, Theme.Small, new Size(Width - 40, 0), Wrap).Height;
                Theme.Draw(g, Text, Theme.Small, new Rectangle(26, (Height - th) / 2, Width - 40, th + 2), fg, Wrap | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // ------------------------------------------------------------------ text box
    class VTextBox : VControl
    {
        public readonly TextBox Box;

        public VTextBox(bool multiline = false)
        {
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = multiline ? Theme.MonoBody : Theme.Body, Multiline = multiline };
            if (multiline) { Box.ScrollBars = ScrollBars.Vertical; Box.WordWrap = false; Box.AcceptsReturn = true; Box.AcceptsTab = true; }
            Controls.Add(Box);
            Size = new Size(200, 34);
            Cursor = Cursors.IBeam;
            Box.GotFocus += (o, e) => Invalidate();
            Box.LostFocus += (o, e) => Invalidate();
            Box.HandleCreated += (o, e) => { if (multiline) Native.Scrollbars(Box); };
            Click += (o, e) => Box.Focus();
            WordShortcuts(Box);
        }

        /// <summary>
        /// Ctrl+Backspace / Ctrl+Delete delete a whole word and Ctrl+A selects all, like everywhere else in Windows.
        /// (The plain text box would otherwise type a little box character for Ctrl+Backspace.)
        /// </summary>
        public static void WordShortcuts(TextBox t)
        {
            t.KeyDown += (o, e) =>
            {
                if (!e.Control || e.Alt) return;
                if (e.KeyCode == Keys.A) { t.SelectAll(); e.Handled = e.SuppressKeyPress = true; return; }
                if (e.KeyCode != Keys.Back && e.KeyCode != Keys.Delete) return;
                e.Handled = e.SuppressKeyPress = true;
                if (t.ReadOnly) return;
                string s = t.Text;
                int start = t.SelectionStart, len = t.SelectionLength;
                if (len > 0) { t.Text = s.Remove(start, len); t.SelectionStart = start; return; }
                if (e.KeyCode == Keys.Back)
                {
                    int i = start;
                    while (i > 0 && char.IsWhiteSpace(s[i - 1])) i--;                 // spaces before the word
                    bool word = i > 0 && char.IsLetterOrDigit(s[i - 1]);
                    while (i > 0 && !char.IsWhiteSpace(s[i - 1]) && char.IsLetterOrDigit(s[i - 1]) == word) i--;
                    t.Text = s.Remove(i, start - i);
                    t.SelectionStart = i;
                }
                else
                {
                    int i = start;
                    bool word = i < s.Length && char.IsLetterOrDigit(s[i]);
                    while (i < s.Length && !char.IsWhiteSpace(s[i]) && char.IsLetterOrDigit(s[i]) == word) i++;
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;                // and the spaces after it
                    t.Text = s.Remove(start, i - start);
                    t.SelectionStart = start;
                }
            };
            t.KeyPress += (o, e) => { if (e.KeyChar == (char)127) e.Handled = true; };   // never type the box character
        }

        public string Placeholder { set { Box.HandleCreated += (o, e) => Native.Placeholder(Box, value); if (Box.IsHandleCreated) Native.Placeholder(Box, value); } }

        public override string Text { get { return Box.Text; } set { Box.Text = value; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Box.Multiline) Box.SetBounds(12, 10, Width - 18, Height - 20);
            else Box.SetBounds(11, (Height - Box.PreferredHeight) / 2 + 1, Width - 22, Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 8, Theme.Input);
            Theme.Stroke(g, r, 8, Box.Focused ? Theme.Mix(Theme.Accent, Theme.Border, 0.3f) : Theme.Border);
        }
    }

    // ------------------------------------------------------------------ progress bar
    class VProgress : VControl
    {
        int value = -1;
        float phase;
        readonly Timer timer = new Timer { Interval = 16 };

        public VProgress()
        {
            Height = 4;
            timer.Tick += (o, e) => { phase = (phase + 0.011f) % 1.35f; Invalidate(); };
            timer.Start();
        }

        public int Value
        {
            get { return value; }
            set { this.value = value; if (value < 0) timer.Start(); else timer.Stop(); Invalidate(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0, 0, Width, Height);
            Theme.Fill(g, r, Height / 2f, Theme.Track);
            if (value < 0)
            {
                using (var clip = Theme.Round(r, Height / 2f))
                {
                    g.SetClip(clip);
                    Theme.Fill(g, new RectangleF((phase - 0.3f) * Width, 0, Width * 0.3f, Height), Height / 2f, Theme.Accent);
                    g.ResetClip();
                }
            }
            else if (value > 0)
                Theme.Fill(g, new RectangleF(0, 0, Width * value / 100f, Height), Height / 2f, Theme.Accent);
        }
    }

    // ------------------------------------------------------------------ card with icon header and boxed rows
    class Card : Panel
    {
        class Row { public string Title, Desc; public int Y, H; public bool Full, Boxed = true; public int TextW; }
        readonly List<Row> rows = new List<Row>();
        readonly string title, glyph;
        int next;
        public const int Pad = 12, Inner = 14, Gap = 6;

        public Card(int width, string title, string glyph)
        {
            this.title = title; this.glyph = glyph;
            Width = width;
            next = title != null ? 50 : Pad;
            Height = next + Pad;
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        int ContentRight { get { return Width - Pad - Inner; } }

        void Finish(Row r)
        {
            rows.Add(r);
            next += r.H + Gap;
            Height = next - Gap + Pad;
            Invalidate();
        }

        public object AddRow(string rowTitle, string desc, Control ctl)
        {
            int cx = ctl != null ? ContentRight - ctl.Width : ContentRight;
            int textW = cx - Pad - Inner - 20;
            var flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int th = Theme.Measure(rowTitle, Theme.RowTitle).Height;
            int dh = string.IsNullOrEmpty(desc) ? 0 : TextRenderer.MeasureText(desc, Theme.Small, new Size(textW, 0), flags).Height + 3;
            int h = Math.Max(Math.Max(52, th + dh + 24), (ctl != null ? ctl.Height : 0) + 20);
            var r = new Row { Title = rowTitle, Desc = desc, Y = next, H = h, TextW = textW };
            if (ctl != null)
            {
                ctl.Location = new Point(cx, next + (h - ctl.Height) / 2);
                ctl.BackColor = Theme.Row;
                Controls.Add(ctl);
            }
            Finish(r);
            return r;
        }

        public object AddFull(string rowTitle, string desc, Control ctl)
        {
            var r = new Row { Title = rowTitle, Desc = desc, Y = next, Full = true, TextW = Width - (Pad + Inner) * 2 };
            int top = string.IsNullOrEmpty(desc) ? 40 : 54;
            ctl.Location = new Point(Pad + Inner, next + top);
            ctl.BackColor = Theme.Row;
            Controls.Add(ctl);
            r.H = top + ctl.Height + 14;
            Finish(r);
            return r;
        }

        public void AddRaw(Control ctl, bool boxed)
        {
            var r = new Row { Y = next, Boxed = boxed };
            if (boxed)
            {
                ctl.Location = new Point(Pad + Inner, next + 10);
                ctl.BackColor = Theme.Row;
                r.H = ctl.Height + 20;
            }
            else
            {
                ctl.Location = new Point(Pad, next);
                ctl.BackColor = Theme.Card;
                r.H = ctl.Height;
            }
            Controls.Add(ctl);
            Finish(r);
        }

        public int InnerWidth { get { return Width - (Pad + Inner) * 2; } }

        // ---- search: what this card contains, and a short glow on the row you searched for
        public string CardTitle { get { return title; } }
        public IEnumerable<string[]> SearchRows() { foreach (var r in rows) if (r.Title != null) yield return new[] { r.Title, r.Desc ?? "" }; }
        public int RowY(string rowTitle) { var r = rows.FirstOrDefault(x => x.Title == rowTitle); return r == null ? 0 : r.Y; }
        Row hiRow; float hiK; Timer hiTimer;
        public void Highlight(string rowTitle)
        {
            hiRow = rows.FirstOrDefault(x => x.Title == rowTitle);
            if (hiRow == null) return;
            hiK = 1;
            if (hiTimer == null)
            {
                hiTimer = new Timer { Interval = 16 };
                var started = DateTime.UtcNow;
                hiTimer.Tick += (o, e) =>
                {
                    double t = (DateTime.UtcNow - (DateTime)hiTimer.Tag).TotalSeconds;
                    hiK = t < 0.9 ? 1 : (float)Math.Max(0, 1 - (t - 0.9) / 0.9);
                    if (hiRow != null) Invalidate(new Rectangle(0, hiRow.Y - 2, Width, hiRow.H + 4));
                    if (hiK <= 0) { hiTimer.Stop(); hiRow = null; }
                };
            }
            hiTimer.Tag = DateTime.UtcNow;
            hiTimer.Start();
            Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing && hiTimer != null) hiTimer.Dispose(); base.Dispose(disposing); }

        public void SetDesc(object row, string desc)
        {
            ((Row)row).Desc = desc;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, rr, 12, Theme.Card);
            Theme.Stroke(g, rr, 12, Theme.Border);

            if (title != null)
            {
                var ib = new RectangleF(Pad + 2.5f, 11.5f, 27, 27);
                Theme.Fill(g, ib, 7, Theme.Row);
                Theme.Stroke(g, ib, 7, Theme.Border);
                Theme.Draw(g, glyph ?? "", Theme.IconSmall, Rectangle.Round(ib), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.Draw(g, title, Theme.RowTitle, new Rectangle(Pad + 40, 11, Width - 60, 28), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            foreach (var r in rows)
            {
                var box = new RectangleF(Pad + 0.5f, r.Y + 0.5f, Width - Pad * 2 - 1, r.H - 1);
                if (r.Boxed)
                {
                    bool hi = r == hiRow && hiK > 0;
                    Theme.Fill(g, box, 9, hi ? Theme.Mix(Theme.Row, Theme.Accent, 0.22f * hiK) : Theme.Row);
                    Theme.Stroke(g, box, 9, hi ? Theme.Mix(Theme.Border, Theme.Accent, hiK) : Theme.Border);
                }
                if (r.Title == null) continue;
                var flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
                int x = Pad + Inner;
                if (r.Full)
                {
                    Theme.Draw(g, r.Title, Theme.RowTitle, new Rectangle(x, r.Y + 14, r.TextW, 20), Theme.Text, flags);
                    if (r.Desc != null) Theme.Draw(g, r.Desc, Theme.Small, new Rectangle(x, r.Y + 34, r.TextW, 18), Theme.Muted, flags);
                    continue;
                }
                int th = Theme.Measure(r.Title, Theme.RowTitle).Height;
                int dh = string.IsNullOrEmpty(r.Desc) ? 0 : TextRenderer.MeasureText(r.Desc, Theme.Small, new Size(r.TextW, 0), flags).Height + 3;
                int y = r.Y + (r.H - th - dh) / 2;
                Theme.Draw(g, r.Title, Theme.RowTitle, new Rectangle(x, y, r.TextW, th + 2), Theme.Text, flags);
                if (dh > 0) Theme.Draw(g, r.Desc, Theme.Small, new Rectangle(x, y + th + 3, r.TextW, dh + 2), Theme.Muted, flags);
            }
        }
    }

    // ------------------------------------------------------------------ preset card
    class PresetCard : VControl
    {
        public readonly PresetInfo Info;
        public readonly int Index;
        bool active;
        public List<KeyValuePair<string, string>> Specs = new List<KeyValuePair<string, string>>();
        public string Extras = "";
        public int FlagCount;

        public PresetCard(PresetInfo info, int index)
        {
            Info = info; Index = index;
            Size = new Size(250, 270);
            Cursor = Cursors.Hand;
        }

        public bool Active { get { return active; } set { active = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 12, Hover ? Theme.CardHot : Theme.Card);
            Theme.Stroke(g, r, 12, active ? Theme.Accent : (Hover ? Theme.BorderHot : Theme.Border), active ? 1.5f : 1f);

            Theme.Draw(g, Index.ToString("00"), Theme.Mono, new Rectangle(20, 18, 40, 16), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            string tag = active ? "ACTIVE" : (Info.Recommended ? "DEFAULT" : null);
            if (tag != null)
            {
                Size ts = Theme.Measure(tag, Theme.Mono);
                int tx = Width - 20 - ts.Width;
                Theme.Draw(g, tag, Theme.Mono, new Rectangle(tx, 18, ts.Width + 2, 16), active ? Theme.Accent : Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                if (active) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, tx - 11, 21.5f, 6, 6);
            }

            Theme.Draw(g, Info.Name, Theme.H3, new Rectangle(20, 40, Width - 40, 24), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            Theme.Draw(g, Info.Desc, Theme.Small, new Rectangle(20, 66, Width - 40, 18), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 20, 94, Width - 20, 94);
            int y = 104;
            foreach (var kv in Specs)
            {
                Theme.Draw(g, kv.Key, Theme.Mono, new Rectangle(20, y + 2, 100, 16), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                Theme.Draw(g, kv.Value, Theme.Small, new Rectangle(110, y, Width - 130, 18), Theme.Text, TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += 19;
            }
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 20, y + 5, Width - 20, y + 5);
            string fc = FlagCount + " FLAGS";
            Size fs = Theme.Measure(fc, Theme.Mono);
            Theme.Draw(g, "EXTRAS", Theme.Mono, new Rectangle(20, y + 15, 80, 16), Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            Theme.Draw(g, fc, Theme.Mono, new Rectangle(Width - 20 - fs.Width, y + 15, fs.Width + 2, 16), active ? Theme.Accent : Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            Theme.Draw(g, Extras, Theme.Small, new Rectangle(20, y + 33, Width - 40, 34), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------ character theme card
    class CharacterCard : VControl
    {
        public readonly string SchemeId;
        public event EventHandler Picked;
        Rectangle portrait;
        static readonly Font Initial = new Font("Segoe UI Semibold", 54f);

        public CharacterCard(string schemeId)
        {
            SchemeId = schemeId;
            Size = new Size(148, 262);
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); portrait = new Rectangle(8, 8, Width - 16, 206); }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Picked != null) Picked(this, EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var s = Theme.Get(SchemeId);
            bool active = Theme.SchemeId == s.Id;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 12, Hover ? Theme.CardHot : Theme.Card);
            Theme.Stroke(g, r, 12, active ? s.Accent : (Hover ? Theme.BorderHot : Theme.Border), active ? 1.5f : 1f);

            Color pbg = Theme.PreviewBg(s), pc = Theme.PreviewCard(s);
            using (var path = Theme.Round(portrait, 9))
            {
                using (var br = new LinearGradientBrush(portrait, Theme.Mix(pc, s.Accent, 0.10f), Theme.Mix(pbg, s.Accent, 0.30f), LinearGradientMode.Vertical))
                    g.FillPath(br, path);
                var img = Characters.Get(s.Id);
                if (img != null)
                {
                    g.SetClip(path);
                    Theme.DrawContain(g, img, new RectangleF(portrait.X + 4, portrait.Y + 6, portrait.Width - 8, portrait.Height - 6));
                    g.ResetClip();
                }
                else if (Theme.IsCustom(s.Id))
                {
                    Color fg = Theme.IsDark(pbg) ? Color.FromArgb(170, 255, 255, 255) : Color.FromArgb(140, 0, 0, 0);
                    using (var pen = new Pen(Theme.Mix(fg, s.Accent, 0.4f), 1.2f) { DashStyle = DashStyle.Dash })
                    using (var dp = Theme.Round(new RectangleF(portrait.X + 6, portrait.Y + 6, portrait.Width - 12, portrait.Height - 12), 7))
                        g.DrawPath(pen, dp);
                    Theme.Draw(g, "", Theme.Icon, new Rectangle(portrait.X, portrait.Y + portrait.Height / 2 - 26, portrait.Width, 22), s.Accent, TextFormatFlags.HorizontalCenter);
                    Theme.Draw(g, "Your picture", Theme.Small, new Rectangle(portrait.X, portrait.Y + portrait.Height / 2 - 2, portrait.Width, 18), fg, TextFormatFlags.HorizontalCenter);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(70, s.Accent)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.DrawString(s.Name.Substring(0, 1), Initial, b, portrait, sf);
                    }
                }
            }

            Theme.Draw(g, s.Name, Theme.RowTitle, new Rectangle(14, 222, Width - 28, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            if (active)
            {
                Size ts = Theme.Measure("ACTIVE", Theme.Mono);
                Theme.Draw(g, "ACTIVE", Theme.Mono, new Rectangle(Width - 14 - ts.Width, 225, ts.Width + 2, 14), s.Accent, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
            int dx = 14;
            foreach (var c in new[] { pbg, pc, s.Accent })
            {
                var sq = new RectangleF(dx + 0.5f, 243.5f, 13, 13);
                Theme.Fill(g, sq, 4, c);
                Theme.Stroke(g, sq, 4, Theme.Border);
                dx += 18;
            }
        }
    }

    // ------------------------------------------------------------------ one of the user's own themes (Appearance > Your themes)
    class CustomThemeCard : VControl
    {
        public readonly string ThemeId;
        public const int CardW = 200, CardH = 96;

        public CustomThemeCard(string id) { ThemeId = id; Size = new Size(CardW, CardH); Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var s = Theme.Get(ThemeId);
            bool active = Theme.SchemeId == ThemeId;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 11, Hover ? Theme.CardHot : Theme.Card);
            Theme.Stroke(g, r, 11, active ? s.Accent : (Hover ? Theme.BorderHot : Theme.Border), active ? 1.5f : 1f);

            Color pbg = Theme.PreviewBg(s), pc = Theme.PreviewCard(s);
            var thumb = new Rectangle(8, 8, 60, Height - 16);
            using (var path = Theme.Round(thumb, 7))
            {
                using (var br = new LinearGradientBrush(thumb, Theme.Mix(pc, s.Accent, 0.10f), Theme.Mix(pbg, s.Accent, 0.30f), LinearGradientMode.Vertical))
                    g.FillPath(br, path);
                var img = Characters.Get(ThemeId);
                if (img != null)
                {
                    g.SetClip(path);
                    Theme.DrawContain(g, img, new RectangleF(thumb.X + 2, thumb.Y + 3, thumb.Width - 4, thumb.Height - 3));
                    g.ResetClip();
                }
                else
                    Theme.Draw(g, "", Theme.Icon, thumb, Color.FromArgb(160, s.Accent), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            int tx = thumb.Right + 12, tw = Width - tx - 10;
            Theme.Draw(g, s.Name, Theme.RowTitle, new Rectangle(tx, 14, tw, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, active ? "ACTIVE" : (s.Light ? "Light" : "Dark"), active ? Theme.Mono : Theme.Small, new Rectangle(tx, 36, tw, 16),
                       active ? s.Accent : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            int dx = tx;
            foreach (var c in new[] { pbg, pc, s.Accent })
            {
                var sq = new RectangleF(dx + 0.5f, Height - 26.5f, 13, 13);
                Theme.Fill(g, sq, 4, c);
                Theme.Stroke(g, sq, 4, Theme.Border);
                dx += 18;
            }
        }
    }

    class NewThemeCard : VControl
    {
        public NewThemeCard() { Size = new Size(150, CustomThemeCard.CardH); Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (Hover) Theme.Fill(g, r, 11, Theme.CardHot);
            using (var pen = new Pen(Hover ? Theme.Accent : Theme.BorderHot, 1.2f) { DashStyle = DashStyle.Dash })
            using (var p = Theme.Round(r, 11)) g.DrawPath(pen, p);
            Theme.Draw(g, "", Theme.Icon, new Rectangle(0, Height / 2 - 24, Width, 22), Theme.Accent, TextFormatFlags.HorizontalCenter);
            Theme.Draw(g, "New theme", Theme.RowTitle, new Rectangle(0, Height / 2 + 2, Width, 20), Hover ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    // ------------------------------------------------------------------ sidebar pieces
    class NavItem : VControl
    {
        public string Glyph;
        public bool Active;

        public NavItem(string text, string glyph)
        {
            Text = text; Glyph = glyph;
            Size = new Size(212, 36);
            Cursor = Cursors.Hand;
            BackColor = Theme.Side;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (Active)
            {
                Theme.Fill(g, r, 9, Theme.NavActive);
                Theme.Stroke(g, r, 9, Theme.Mix(Theme.Border, Theme.Accent, 0.45f));
            }
            else if (Hover) Theme.Fill(g, r, 9, Theme.NavHover);
            Color fg = Active || Hover ? Theme.Text : Theme.Muted;
            Theme.Draw(g, Glyph, Theme.Icon, new Rectangle(12, 0, 22, Height), Active ? Theme.Accent : fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Text, Active ? Theme.RowTitle : Theme.Body, new Rectangle(44, 0, Width - 48, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    class LogoMark : VControl
    {
        public LogoMark() { Size = new Size(30, 30); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Theme.DrawLogo(g, new RectangleF(0, 0, Width - 1, Height - 1));
        }
    }

    class CharacterArt : VControl
    {
        public event EventHandler AddRequested;
        public CharacterArt() { BackColor = Theme.Side; }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Theme.IsCustom(Theme.SchemeId) && Characters.Get(Theme.SchemeId) == null && AddRequested != null) AddRequested(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var s = Theme.Get(Theme.SchemeId);
            var img = Characters.Get(s.Id);
            if (img != null)
            {
                Cursor = Cursors.Default;
                Theme.DrawContain(g, img, new RectangleF(10, 4, Width - 20, Height - 4));
                return;
            }
            if (!Theme.IsCustom(s.Id)) return;
            Cursor = Cursors.Hand;
            var box = new RectangleF(16.5f, Height / 2f - 50, Width - 33, 100);
            using (var pen = new Pen(Hover ? Theme.Accent : Theme.Border, 1.2f) { DashStyle = DashStyle.Dash })
            using (var p = Theme.Round(box, 10)) g.DrawPath(pen, p);
            Theme.Draw(g, "", Theme.Icon, new Rectangle((int)box.X, (int)box.Y + 22, (int)box.Width, 22), Theme.Accent, TextFormatFlags.HorizontalCenter);
            Theme.Draw(g, "Pick a picture in Appearance", Theme.Small, new Rectangle((int)box.X, (int)box.Y + 50, (int)box.Width, 18), Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    class ProfileChip : VControl
    {
        public string UserName = "Player", Status = "";
        public Image Avatar;
        public ProfileChip() { Size = new Size(212, 56); BackColor = Theme.Side; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 11, Theme.Card);
            Theme.Stroke(g, r, 11, Theme.Border);
            var av = new RectangleF(10, 10, 36, 36);
            using (var b = new SolidBrush(Theme.Mix(Theme.Card, Theme.Accent, 0.35f))) g.FillEllipse(b, av);
            if (Avatar != null)
            {
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(av);
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Avatar, av);
                    g.ResetClip();
                }
            }
            else
            {
                string ini = string.IsNullOrEmpty(UserName) ? "?" : UserName.Substring(0, 1).ToUpperInvariant();
                Theme.Draw(g, ini, Theme.RowTitle, Rectangle.Round(av), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var p = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.5f), 1.5f)) g.DrawEllipse(p, av);
            Theme.Draw(g, UserName, Theme.RowTitle, new Rectangle(56, 10, Width - 64, 18), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, Status, Theme.Small, new Rectangle(56, 29, Width - 64, 16), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------------ overlay preview
    class OverlayPreview : VControl
    {
        public HashSet<string> Items = new HashSet<string>();
        public string Corner = "tl";
        public bool Crosshair;
        public string CrossStyle = "dot";
        public int CrossSize = 1;
        readonly int hz = Display.RefreshRate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 12))
            {
                using (var sky = new LinearGradientBrush(r, Color.FromArgb(120, 165, 215), Color.FromArgb(196, 214, 230), LinearGradientMode.Vertical))
                    g.FillPath(sky, path);
                g.SetClip(path);
                using (var ground = new SolidBrush(Color.FromArgb(92, 140, 84))) g.FillRectangle(ground, 0, Height * 0.62f, Width, Height);
                using (var block = new SolidBrush(Color.FromArgb(150, 150, 160))) g.FillRectangle(block, Width * 0.6f, Height * 0.42f, 46, Height * 0.2f + 1);
                using (var top = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(top, 0, 0, Width, 26);
                using (var ob = OverlayRenderer.Render(OverlayData.Sample(hz), Items, Theme.Accent))
                {
                    if (ob.Height > 2)
                    {
                        float sc = 0.78f;
                        float w = ob.Width * sc, h = ob.Height * sc;
                        float x = Corner.EndsWith("r") ? Width - w - 8 : 8;
                        float y = Corner.StartsWith("b") ? Height - h - 8 : 32;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(ob, x, y, w, h);
                    }
                }
                if (Crosshair)
                    using (var cb = OverlayRenderer.Crosshair(CrossStyle, CrossSize, Theme.Accent))
                        g.DrawImage(cb, Width / 2 - cb.Width / 2, Height / 2 - cb.Height / 2);
                g.ResetClip();
            }
            Theme.Stroke(g, r, 12, Theme.Border);
        }
    }

    // ------------------------------------------------------------------ base window
    class BaseForm : Form
    {
        public BaseForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;
                cp.Style |= 0x20000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle);
        }

        public void Draggable(Control c)
        {
            c.MouseDown += (o, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            };
        }

        protected Label MakeLabel(string text, Font f, Color c, int x, int y, Color? bg = null)
        {
            return new Label { Text = text, Font = f, ForeColor = c, AutoSize = true, Location = new Point(x, y), BackColor = bg ?? Theme.Bg, UseMnemonic = false };
        }
    }

    // ------------------------------------------------------------------ dialog
    class VDialog : BaseForm
    {
        VTextBox box;

        VDialog(string title, string message, string text, bool showBox, bool readOnly, string ok, string cancel)
        {
            Width = 540;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var logo = new LogoMark { Location = new Point(24, 22), Size = new Size(26, 26) };
            var t = new Label { Text = title, Font = Theme.H2, ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(60, 22), MaximumSize = new Size(Width - 84, 0), AutoSize = true, UseMnemonic = false };
            Controls.Add(logo); Controls.Add(t);
            Draggable(this); Draggable(t);
            int y = Math.Max(60, t.Bottom + 14);
            if (!string.IsNullOrEmpty(message))
            {
                var m = new Label { Text = message, Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Bg, Location = new Point(25, y), MaximumSize = new Size(Width - 50, 0), AutoSize = true, UseMnemonic = false };
                Controls.Add(m);
                y += m.PreferredHeight + 16;
            }
            if (showBox)
            {
                box = new VTextBox(true) { Location = new Point(24, y), Size = new Size(Width - 48, 280) };
                box.Box.ReadOnly = readOnly;
                box.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                Controls.Add(box);
                y += 296;
            }
            var okB = new VButton(ok, null, true);
            okB.Location = new Point(Width - 24 - okB.Width, y + 4);
            okB.Click += (o, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(okB);
            if (cancel != null)
            {
                var cB = new VButton(cancel);
                cB.Location = new Point(okB.Left - 8 - cB.Width, y + 4);
                cB.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cB);
            }
            Height = y + 60;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        static DialogResult Show(IWin32Window owner, VDialog d)
        {
            if (owner == null) d.StartPosition = FormStartPosition.CenterScreen;
            return owner == null ? d.ShowDialog() : d.ShowDialog(owner);
        }

        public static void Info(IWin32Window owner, string title, string msg)
        {
            using (var d = new VDialog(title, msg, null, false, true, "OK", null)) Show(owner, d);
        }

        public static bool Confirm(IWin32Window owner, string title, string msg, string yes, string no = "Cancel")
        {
            using (var d = new VDialog(title, msg, null, false, true, yes, no)) return Show(owner, d) == DialogResult.OK;
        }

        public static string Input(IWin32Window owner, string title, string msg, string text, bool readOnly, string ok)
        {
            using (var d = new VDialog(title, msg, text, true, readOnly, ok, readOnly ? null : "Cancel"))
                return Show(owner, d) == DialogResult.OK ? d.box.Text : null;
        }
    }

    // ------------------------------------------------------------------ launch loading screen
    // Report() can be called from any thread, as often as you like: it only stores the latest text and the
    // animation timer draws it, so fast file-by-file updates stay smooth.
    class ProgressForm : BaseForm
    {
        volatile string status = "Starting", detail = "";
        volatile int target = -1;
        float shown, phase, spin, fade;
        bool closing;
        readonly Timer anim = new Timer { Interval = 15 };
        readonly DateTime born = DateTime.UtcNow;
        public string Subtitle;     // e.g. "as builderman"

        public ProgressForm()
        {
            Text = "CloveStrap";
            Size = new Size(460, 250);
            ShowInTaskbar = true;
            Opacity = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            var close = new IconButton("", true) { Location = new Point(Width - 46, 0) };
            close.Click += (o, e) => Environment.Exit(0);
            Controls.Add(close);
            Draggable(this);
            anim.Tick += (o, e) => Step();
            Shown += (o, e) => anim.Start();
            FormClosed += (o, e) => anim.Dispose();
        }

        /// <summary>"Step text" or "Step text\ndetail line".</summary>
        public void Report(string text, int pct)
        {
            if (text == null) return;
            int nl = text.IndexOf('\n');
            status = (nl >= 0 ? text.Substring(0, nl) : text).TrimEnd('.', ' ');
            detail = nl >= 0 ? text.Substring(nl + 1) : "";
            target = pct;
        }

        /// <summary>Fades out, then closes. Call on the UI thread.</summary>
        public void FadeClose()
        {
            closing = true;
            if (!anim.Enabled) Close();
        }

        void Step()
        {
            fade = closing ? Math.Max(0f, fade - 0.12f) : Math.Min(1f, fade + 0.08f);
            try { Opacity = Math.Max(0, fade); } catch { }
            if (closing && fade <= 0f) { anim.Stop(); Close(); return; }
            spin = (spin + 5.5f) % 360f;
            phase = (phase + 0.012f) % 1.4f;
            int t = target;
            if (t >= 0) shown += (Math.Min(100, t) - shown) * 0.12f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, cx = w / 2;

            // soft accent glow behind the logo
            using (var path = new GraphicsPath())
            {
                var glow = new RectangleF(cx - 150, -110, 300, 230);
                path.AddEllipse(glow);
                using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(Theme.Light ? 40 : 55, Theme.Accent), SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) } })
                    g.FillEllipse(br, glow);
            }

            // logo with a spinning ring
            var logo = new RectangleF(cx - 22, 30, 44, 44);
            Theme.DrawLogo(g, logo);
            var ring = RectangleF.Inflate(logo, 9, 9);
            using (var track = new Pen(Color.FromArgb(40, Theme.Accent), 2.2f)) g.DrawEllipse(track, ring);
            using (var arc = new Pen(Theme.Accent, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, ring, spin, 70 + 40 * (float)Math.Sin((DateTime.UtcNow - born).TotalSeconds * 2.2));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // "Loading" with dots that fade in one after another
            const string word = "Loading";
            Size ws = Theme.Measure(word, Theme.H1);
            Size ds = Theme.Measure(".", Theme.H1);
            int wx = cx - (ws.Width + ds.Width * 3) / 2, wy = 104;
            Theme.Draw(g, word, Theme.H1, new Rectangle(wx, wy, ws.Width + 4, ws.Height + 4), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            double secs = (DateTime.UtcNow - born).TotalSeconds;
            for (int i = 0; i < 3; i++)
            {
                double a = 0.5 + 0.5 * Math.Sin(secs * 4.0 - i * 0.9);
                Color dc = Theme.Mix(Theme.Bg, Theme.Accent, (float)(0.25 + 0.75 * a));
                Theme.Draw(g, ".", Theme.H1, new Rectangle(wx + ws.Width + i * ds.Width, wy, ds.Width + 2, ws.Height + 4), dc, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }

            // current step
            string st = status + (string.IsNullOrEmpty(Subtitle) || status.Contains(Subtitle) ? "" : "  ·  " + Subtitle);
            Theme.Draw(g, st, Theme.Body, new Rectangle(28, 140, w - 56, 20), Theme.Muted,
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            // progress bar (eased when we know the percentage, a gliding shimmer when we don't)
            var bar = new RectangleF(48, 176, w - 96, 4);
            Theme.Fill(g, bar, 2, Theme.Track);
            if (target < 0)
            {
                using (var clip = Theme.Round(bar, 2))
                {
                    g.SetClip(clip);
                    float x = bar.X + (phase - 0.35f) * bar.Width;
                    var seg = new RectangleF(x, bar.Y, bar.Width * 0.35f, bar.Height);
                    using (var br = new LinearGradientBrush(new RectangleF(seg.X - 1, seg.Y, seg.Width + 2, seg.Height), Color.FromArgb(0, Theme.Accent), Theme.Accent, LinearGradientMode.Horizontal))
                    {
                        var blend = new ColorBlend(3) { Colors = new[] { Color.FromArgb(0, Theme.Accent), Theme.Accent, Color.FromArgb(0, Theme.Accent) }, Positions = new[] { 0f, 0.5f, 1f } };
                        br.InterpolationColors = blend;
                        g.FillRectangle(br, seg);
                    }
                    g.ResetClip();
                }
            }
            else if (shown > 0.3f)
                Theme.Fill(g, new RectangleF(bar.X, bar.Y, bar.Width * shown / 100f, bar.Height), 2, Theme.Accent);

            // detail line: exactly what's being worked on right now
            string d = detail;
            string pct = target >= 0 && target < 100 ? ((int)Math.Round(shown)) + "%" : "";
            int pw = pct.Length > 0 ? Theme.Measure("100%", Theme.Mono).Width + 8 : 0;
            Theme.Draw(g, d, Theme.Mono, new Rectangle(48, 190, w - 96 - pw, 16), Theme.Dim,
                       (pw > 0 ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.NoPadding | TextFormatFlags.PathEllipsis);
            if (pw > 0) Theme.Draw(g, pct, Theme.Mono, new Rectangle(w - 48 - pw, 190, pw, 16), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);

            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, w - 1, ClientSize.Height - 1);
        }
    }


    // ------------------------------------------------------------------ (i) with a themed hover popup
    class InfoDot : VControl
    {
        readonly string title, body;
        static ToolTip tip;

        public InfoDot(string title, string body)
        {
            this.title = title; this.body = body;
            Size = new Size(18, 18);
            Cursor = Cursors.Help;
            if (tip == null)
            {
                tip = new ToolTip { OwnerDraw = true, InitialDelay = 150, ReshowDelay = 50, AutoPopDelay = 30000, UseFading = true };
                tip.Popup += (o, e) =>
                {
                    var d = e.AssociatedControl as InfoDot;
                    if (d == null) return;
                    var ts = TextRenderer.MeasureText(d.body, Theme.Small, new Size(330, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    e.ToolTipSize = new Size(330 + 28, ts.Height + 52);
                };
                tip.Draw += (o, e) =>
                {
                    var d = e.AssociatedControl as InfoDot;
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Theme.Card);
                    using (var pen = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.45f))) g.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                    if (d == null) return;
                    using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 14, 15, 7, 7);
                    Theme.Draw(g, d.title, Theme.RowTitle, new Rectangle(28, 10, e.Bounds.Width - 40, 18), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    Theme.Draw(g, d.body, Theme.Small, new Rectangle(14, 36, 330, e.Bounds.Height - 44), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                };
            }
            tip.SetToolTip(this, title);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color c = Hover ? Theme.Accent : Theme.Muted;
            if (Hover) using (var b = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillEllipse(b, r);
            using (var pen = new Pen(c, 1.3f)) g.DrawEllipse(pen, r);
            using (var f = new Font("Segoe UI Semibold", 7.5f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(c))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString("i", f, b, new RectangleF(0, 0.5f, Width - 1, Height - 1), sf);
            }
        }
    }

    // ------------------------------------------------------------------ one saved account on the Accounts page
    class AccountRow : VControl
    {
        public readonly Account Acc;
        public Image Avatar;
        public readonly Toggle Alt;
        public readonly VButton LaunchB;
        public readonly IconButton RemoveB;
        public const int RowH = 60;

        public AccountRow(Account a, int width)
        {
            Acc = a;
            Size = new Size(width, RowH);
            BackColor = Theme.Card;
            RemoveB = new IconButton("\uE74D") { Size = new Size(36, 34), BackColor = Theme.Row };
            RemoveB.Location = new Point(Width - 12 - RemoveB.Width, (RowH - 34) / 2);
            LaunchB = new VButton("Launch", "\uE768", true) { BackColor = Theme.Row };
            LaunchB.Location = new Point(RemoveB.Left - 8 - LaunchB.Width, (RowH - 34) / 2);
            Alt = new Toggle { Checked = a.AltMode, BackColor = Theme.Row };
            Alt.Location = new Point(LaunchB.Left - 24 - Alt.Width, (RowH - Alt.Height) / 2);
            Alt.CheckedChanged += (o, e) => Invalidate();
            var info = new InfoDot("Alt mode",
                "Uses as little of your PC as possible:\n\n" +
                "•  Lowest graphics, no grass, gray sky, frozen lighting\n" +
                "•  Windows efficiency mode and lowest CPU priority\n" +
                "•  Only allowed 2 CPU cores\n" +
                "•  Memory freed up every 15 seconds\n\n" +
                "Never affects your main. Applies next launch.")
            { BackColor = Theme.Row };
            info.Location = new Point(Alt.Left - 10 - info.Width, (RowH - info.Height) / 2);
            Controls.AddRange(new Control[] { info, Alt, LaunchB, RemoveB });
            infoLeft = info.Left;
        }

        readonly int infoLeft;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 9, Theme.Row);
            Theme.Stroke(g, r, 9, Alt.Checked ? Theme.Mix(Theme.Border, Theme.Accent, 0.35f) : Theme.Border);

            var av = new RectangleF(14, 12, 36, 36);
            using (var b = new SolidBrush(Theme.Mix(Theme.Row, Theme.Accent, 0.3f))) g.FillEllipse(b, av);
            if (Avatar != null)
            {
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(av);
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Avatar, av);
                    g.ResetClip();
                }
            }
            else
            {
                string n = Acc.DisplayName ?? Acc.Username ?? "?";
                Theme.Draw(g, n.Substring(0, 1).ToUpperInvariant(), Theme.RowTitle, Rectangle.Round(av), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var p = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.5f), 1.5f)) g.DrawEllipse(p, av);

            int textW = infoLeft - 110 - 64;
            string display = Acc.DisplayName ?? Acc.Username;
            Theme.Draw(g, display, Theme.RowTitle, new Rectangle(62, 12, textW, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Theme.Draw(g, "@" + Acc.Username, Theme.Small, new Rectangle(62, 32, textW, 16), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            var lr = new Rectangle(infoLeft - 106, 0, 100, Height);
            Theme.Draw(g, Alt.Checked ? "Alt mode on" : "Alt mode", Theme.Small, lr, Alt.Checked ? Theme.Accent : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------------ main window
    partial class MainForm : BaseForm
    {
        Settings S;
        readonly int hz = Display.RefreshRate();
        bool loading;
        readonly Timer saveTimer = new Timer { Interval = 350 };
        readonly Timer flashTimer = new Timer { Interval = 3200 };

        const int W = 1080, H = 720, SideW = 236, TopH = 56;
        const int PageX = 28;
        readonly int pageW = W - SideW - PageX * 2;
        Panel right;
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly List<NavItem> nav = new List<NavItem>();
        Label pageTitle;
        StatusPill pill;
        ProfileChip profile;
        CharacterArt art;
        string currentPage;
        const string IdleStatus = "Changes save automatically";

        readonly List<PresetCard> presetCards = new List<PresetCard>();
        Chips fpsChips, prioChips, apiChips, msaaChips, texChips, cornerChips, crossStyle, crossSize, viewChips;
        MultiChips overlayItems;
        OverlayPreview preview;
        VTextBox fpsCustom;
        Slider quality;
        Toggle tFullscreen, tSway, tGrass, tMesh, tDpi, tSky, tVoxel, tRegister, tCleanup, tMods, tMulti, tOverlay, tCross, tStartup, tScreenCross, tAntiAfk;
        Card launcherCard;
        object versionRow;
        DataGridView grid;
        Label flagCount, emptyHint;
        string latestVersion, robloxName;
        CrosshairPreview crossPreview;
        Image avatar;

        static readonly int[] FpsChoices = { 60, 120, 144, 165, 240, 360 };
        static readonly string[] ApiValues = { "Automatic", "D3D11", "Vulkan", "OpenGL" };
        static readonly string[] MsaaValues = { "Automatic", "1", "2", "4", "8" };
        static readonly string[] TexValues = { "Automatic", "0", "1", "2", "3" };
        static readonly string[] PrioValues = { "Normal", "AboveNormal", "High" };
        static readonly string[] ViewValues = { "near", "short", "default", "far", "max" };
        static readonly string[] Corners = { "tl", "tr", "bl", "br" };
        static readonly string[] CrossStyles = { "dot", "cross", "circle" };

        public MainForm(string startPage)
        {
            S = Settings.Load();
            if (Program.DemoThemes)
            {
                S.CustomThemes = new List<CustomTheme> { new CustomTheme { Id = "custom-demo1", Name = "Midnight", Accent = "#60A5FA" }, new CustomTheme { Id = "custom-demo2", Name = "Rem", Accent = "#A78BFA" }, new CustomTheme { Id = "custom-demo3", Name = "Lime Light", Accent = "#A3E635", Light = true }, new CustomTheme { Id = "custom-demo4", Name = "Ember", Accent = "#FB923C" } };
                S.ColorScheme = "custom-demo2";
            }
            Theme.SetCustoms(S.CustomThemes);
            Theme.ApplyScheme(Program.SchemeOverride ?? S.ColorScheme);
            Text = "CloveStrap";
            ClientSize = new Size(W, H);
            saveTimer.Tick += (o, e) => { saveTimer.Stop(); SaveNow(true); };
            flashTimer.Tick += (o, e) => { flashTimer.Stop(); if (pill != null) pill.Set(IdleStatus, Theme.Accent); };
            FormClosing += (o, e) => { if (saveTimer.Enabled) { saveTimer.Stop(); SaveNow(false); } };
            Shown += (o, e) => { RefreshStatus(); FindRobloxName(); if (Program.JustUpdated) { Program.JustUpdated = false; Flash("Updated to v" + Updater.CurrentText + "! " + Cute.Face(), Theme.Accent); } if (Program.DemoFlash != null) { pill.Set(Program.DemoFlash, Theme.Warn); flashTimer.Stop(); } if (Program.DemoSearch != null) { searchBox.Box.Text = Program.DemoSearch; if (Program.DemoGoTo == "1") searchPopup.PickSelected(); } if (Program.DemoAsk != null) { ToggleChat(true); demoQueue = new Queue<string>(Program.DemoAsk.Split((char)124)); Ask(demoQueue.Dequeue()); } };
            Build(startPage ?? "Presets");
        }

        // ---------------------------------------------------------- build / rebuild (theme changes rebuild everything)
        void Build(string page)
        {
            // rebuild without drawing anything until it's done, so theme switches land in one clean frame
            bool hold = IsHandleCreated;
            if (hold) Native.SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);   // WM_SETREDRAW off
            try { BuildNow(page); }
            finally
            {
                if (hold) { Native.SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero); Refresh(); }
            }
        }

        // the whole window paints in one pass: no controls flickering in one by one
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }   // WS_EX_COMPOSITED
        }

        void BuildNow(string page)
        {
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            pages.Clear(); nav.Clear(); presetCards.Clear();
            BackColor = Theme.Bg;

            // sidebar
            var side = new Panel { Dock = DockStyle.Left, Width = SideW, BackColor = Theme.Side };
            var logo = new LogoMark { Location = new Point(20, 22) };
            var name = MakeLabel("CloveStrap", Theme.Logo, Theme.Text, 58, 20, Theme.Side);
            var ver = MakeLabel("v" + Updater.CurrentText, Theme.Mono, Theme.Dim, 60, 42, Theme.Side);
            side.Controls.AddRange(new Control[] { logo, name, ver });
            Draggable(side); Draggable(logo); Draggable(name); Draggable(ver);

            int ny = 78;
            foreach (var n in new[] { new[] { "Presets", "" }, new[] { "Accounts", "\uE716" }, new[] { "Usernames", "\uE8EC" },new[] { "Performance", "" }, new[] { "Graphics", "" }, new[] { "Overlay", "" },
                                      new[] { "Mods", "\uE790" }, new[] { "FFlag Editor", "" }, new[] { "Launcher", "" }, new[] { "Appearance", "" } })
            {
                var item = new NavItem(n[0], n[1]) { Location = new Point(12, ny) };
                string pg = n[0];
                item.Click += (o, e) => ShowPage(pg);
                side.Controls.Add(item);
                nav.Add(item);
                ny += 40;
            }

            bool discord = DiscordAccount.SignedIn(S);
            if (discord && discordAvatar == null) discordAvatar = DiscordAccount.Avatar();
            profile = new ProfileChip { Location = new Point(12, H - 70), UserName = discord ? S.DiscordName : (robloxName ?? Environment.UserName), Status = "Checking Roblox", Avatar = discord ? discordAvatar : avatar, Cursor = discord ? Cursors.Default : Cursors.Hand };
            profile.Click += (o, e) => ProfileClicked();
            askButton = new AskButton { Location = new Point(12, H - 70 - 46) };   // "Ask Natsuki", right above your profile
            art = new CharacterArt { Location = new Point(0, ny + 6), Size = new Size(SideW - 1, H - 76 - 46 - ny - 10) };
            art.AddRequested += (o, e) => ShowPage("Appearance");
            var edge = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Border };
            side.Controls.AddRange(new Control[] { art, askButton, profile, edge });

            // right side
            right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var top = new Panel { Dock = DockStyle.Top, Height = TopH, BackColor = Theme.Bg };
            pageTitle = MakeLabel("", Theme.H2, Theme.Text, PageX - 2, 17);
            var close = new IconButton("", true) { Location = new Point(W - SideW - 46, 0) };
            var min = new IconButton("") { Location = new Point(W - SideW - 92, 0) };
            min.Click += (o, e) => WindowState = FormWindowState.Minimized;
            close.Click += (o, e) => Close();
            var launch = new VButton("Launch Roblox", "", true) { Big = true, Size = new Size(150, 34) };
            launch.Location = new Point(min.Left - 10 - launch.Width, 12);
            launch.Click += (o, e) => Launch();
            searchBox = new VTextBox { Size = new Size(196, 34), Location = new Point(168, 11), BackColor = Theme.Bg };
            searchBox.Placeholder = "Search settings   Ctrl+F";
            pill = new StatusPill { AnchorRight = launch.Left - 8, Top = 14, MinLeft = searchBox.Right + 10 };
            pill.Set(IdleStatus, Theme.Accent);
            top.Controls.AddRange(new Control[] { pageTitle, searchBox, pill, launch, min, close });
            Draggable(top); Draggable(pageTitle);
            right.Controls.Add(top);

            Controls.Add(right);
            Controls.Add(side);

            BuildPresets();
            BuildAccounts();
            BuildNames();
            BuildPerformance();
            BuildGraphics();
            BuildOverlay();
            BuildMods();
            BuildFlags();
            BuildLauncher();
            BuildAppearance();
            LoadIntoUi();
            SetUpSearch();
            SetUpAssistant();
            ShowPage(pages.ContainsKey(page) ? page : "Presets");
            ResumeLayout();
            if (IsHandleCreated) { Native.StyleWindow(Handle); RefreshStatus(); }
        }

        Panel NewPage(string key)
        {
            var p = new Panel { Location = new Point(PageX, TopH), Size = new Size(pageW, H - TopH - 16), BackColor = Theme.Bg, Visible = false };
            right.Controls.Add(p);
            p.BringToFront();
            pages[key] = p;
            return p;
        }

        void ShowPage(string key)
        {
            currentPage = key;
            foreach (var kv in pages) kv.Value.Visible = kv.Key == key;
            foreach (var n in nav) { n.Active = n.Text == key; n.Invalidate(); }
            if (pageTitle != null) pageTitle.Text = key;
        }

        Card AddCard(Panel p, ref int y, string title, string glyph, int width = 0, int x = 0)
        {
            var c = new Card(width > 0 ? width : pageW, title, glyph) { Location = new Point(x, y) };
            p.Controls.Add(c);
            return c;
        }

        void Place(Panel p, Card c, ref int y)
        {
            c.Location = new Point(c.Left, y);
            if (!p.Controls.Contains(c)) p.Controls.Add(c);
            y += c.Height + 12;
        }

        // ---------------------------------------------------------- pages
        void BuildPresets()
        {
            var p = NewPage("Presets");
            int cw = (pageW - 28) / 3;
            for (int i = 0; i < Presets.All.Length; i++)
            {
                var info = Presets.All[i];
                var c = new PresetCard(info, i + 1) { Location = new Point((i % 3) * (cw + 14), 4 + (i / 3) * 284), Width = cw };
                c.Click += (o, e) => ApplyPreset(info.Id);
                p.Controls.Add(c);
                presetCards.Add(c);
            }
        }

        void SetSpecs(PresetCard c, Settings s)
        {
            Func<string, string, KeyValuePair<string, string>> kv = (k, v) => new KeyValuePair<string, string>(k, v);
            string aa = s.Msaa == "Automatic" ? "Auto" : s.Msaa == "1" ? "Off" : s.Msaa + "x";
            string[] tex = { "Lowest", "Low", "Medium", "High" };
            int ti;
            string tx = int.TryParse(s.TextureQuality, out ti) && ti >= 0 && ti < 4 ? tex[ti] : "Auto";
            c.Specs = new List<KeyValuePair<string, string>>
            {
                kv("FPS CAP", s.FpsCap > 0 ? s.FpsCap.ToString() : "Monitor (" + hz + ")"),
                kv("RENDERER", s.GraphicsApi == "Automatic" ? "Auto" : s.GraphicsApi),
                kv("ANTI-ALIAS", aa),
                kv("TEXTURES", tx),
                kv("QUALITY", s.QualityOverride > 0 ? "Level " + s.QualityOverride + " / 21" : "Auto"),
            };
            var extras = new List<string>();
            if (s.ExclusiveFullscreen) extras.Add("Fullscreen");
            if (s.RemoveGrass) extras.Add("no grass");
            else if (s.DisableGrassSway) extras.Add("static grass");
            if (s.DisableDpiScale) extras.Add("native res");
            if (s.GraySky) extras.Add("gray sky");
            if (s.LowMeshDetail) extras.Add("low mesh");
            else if (s.ViewDistance == "far" || s.ViewDistance == "max") extras.Add(s.ViewDistance + " detail");
            else if (s.ViewDistance == "short") extras.Add("short detail");
            if (s.PauseVoxelizer) extras.Add("frozen lighting");
            if (s.Priority == "High") extras.Add("high priority");
            c.Extras = extras.Count == 0 ? "No extras" : string.Join(", ", extras);
            c.FlagCount = Flags.Build(s).Count(f => Flags.Allowlist.Contains(f.Key));
        }

        // ---------------------------------------------------------- accounts
        Panel accountList;
        Chips joinChips;
        VTextBox joinBox;
        VButton addAccountB;
        volatile bool loginBusy, loginCancel;
        static readonly Dictionary<long, Image> headshots = new Dictionary<long, Image>();

        static readonly string[] ServerValues = { "any", "empty", "full", "ping" };
        static readonly string[] AltWinValues = { "normal", "minimized" };

        void BuildAccounts()
        {
            var p = NewPage("Accounts");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int aw = pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int y = 4;

            var join = new Card(aw, "Where to go", "");
            var bar = new Panel { Size = new Size(join.InnerWidth, 34), BackColor = Theme.Row };
            joinChips = new Chips("Join place ID", "Follow player") { BackColor = Theme.Row };
            joinBox = new VTextBox { BackColor = Theme.Row, Location = new Point(joinChips.Width + 10, 0) };
            joinBox.Size = new Size(bar.Width - joinBox.Left, 34);
            joinChips.SelectedIndex = S.JoinMode == "follow" ? 1 : 0;
            joinBox.Text = S.JoinValue ?? "";
            joinBox.Box.HandleCreated += (o, e) => UpdateJoinPlaceholder();
            joinChips.SelectedIndexChanged += (o, e) => { UpdateJoinPlaceholder(); SaveJoin(); };
            joinBox.Box.TextChanged += (o, e) => SaveJoin();
            bar.Controls.Add(joinChips); bar.Controls.Add(joinBox);
            join.AddFull("Every account you launch goes here", "Paste a game link or place ID, or follow a player into their server.", bar);
            var serverChips = new Chips("Any", "Emptiest", "Fullest", "Best ping");
            serverChips.SelectedIndex = Math.Max(0, Array.IndexOf(ServerValues, S.ServerPick ?? "any"));
            serverChips.SelectedIndexChanged += (o, e) => { S.ServerPick = ServerValues[Math.Max(0, serverChips.SelectedIndex)]; try { S.Save(); } catch { } };
            join.AddRow("Server", "For place IDs. Launch all puts every account in the same server.", serverChips);
            Place(p, join, ref y);

            var accs = new Card(aw, "Your accounts", "");
            var btns = new Panel { Size = new Size(260, 34), BackColor = Theme.Row };
            addAccountB = new VButton("Add account", "", true) { BackColor = Theme.Row };
            var allB = new VButton("Launch all", "") { BackColor = Theme.Row };
            addAccountB.Location = new Point(btns.Width - addAccountB.Width, 0);
            allB.Location = new Point(addAccountB.Left - 8 - allB.Width, 0);
            addAccountB.Click += (o, e) => AddAccount();
            allB.Click += (o, e) => LaunchAccounts(AccountStore.Load());
            btns.Controls.Add(allB); btns.Controls.Add(addAccountB);
            accs.AddRow("Sign in once", "Log in on roblox.com once. Your password never touches CloveStrap.", btns);

            int rows = Math.Max(2, Math.Min(5, AccountStore.Load().Count));
            accountList = new Panel { Size = new Size(aw - Card.Pad * 2, rows * (AccountRow.RowH + 8)), AutoScroll = true, BackColor = Theme.Card };
            accountList.HandleCreated += (o, e) => Native.Scrollbars(accountList);
            accs.AddRaw(accountList, false);
            Place(p, accs, ref y);

            var alts = new Card(aw, "While they play", "");
            var rejoin = new Toggle { Checked = S.AutoRejoin };
            rejoin.CheckedChanged += (o, e) => { S.AutoRejoin = rejoin.Checked; try { S.Save(); } catch { } Flash(S.AutoRejoin ? "Kicked accounts will rejoin by themselves" : "Auto-rejoin off", Theme.Accent); };
            alts.AddRow("Auto-rejoin", "Rejoins the same game if an account gets kicked or disconnected.", rejoin);
            var altWin = new Chips("Normal", "Minimized");
            altWin.SelectedIndex = Math.Max(0, Array.IndexOf(AltWinValues, S.AltWindows == "minimized" ? "minimized" : "normal"));
            altWin.SelectedIndexChanged += (o, e) => { S.AltWindows = AltWinValues[Math.Max(0, altWin.SelectedIndex)]; try { S.Save(); } catch { } };
            alts.AddRow("Alt windows", "How alt-mode accounts open: like any normal window, or minimized to the taskbar.", altWin);
            Place(p, alts, ref y);

            var note = new Label
            {
                Text = "Alt mode runs an account on the lowest settings. Your main is never touched.",
                Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg, Location = new Point(2, y - 4),
                MaximumSize = new Size(aw - 4, 0), AutoSize = true, UseMnemonic = false
            };
            p.Controls.Add(note);
            p.Controls.Add(new Panel { Location = new Point(0, y + 18), Size = new Size(1, 8), BackColor = Theme.Bg });
            LoadAccounts();
        }

        void UpdateJoinPlaceholder()
        {
            if (joinBox == null || !joinBox.Box.IsHandleCreated) return;
            Native.Placeholder(joinBox.Box, joinChips.SelectedIndex == 1 ? "Username to follow" : "Place ID or game link, e.g. 606849621");
        }

        void SaveJoin()
        {
            if (loading) return;
            S.JoinMode = joinChips.SelectedIndex == 1 ? "follow" : "place";
            S.JoinValue = joinBox.Text.Trim();
            try { S.Save(); } catch { }
        }

        void LoadAccounts()
        {
            if (accountList == null) return;
            accountList.SuspendLayout();
            foreach (Control c in accountList.Controls.Cast<Control>().ToList()) { accountList.Controls.Remove(c); c.Dispose(); }
            var list = AccountStore.Load();
            bool scroll = list.Count * (AccountRow.RowH + 8) > accountList.Height;
            int w = accountList.Width - (scroll ? SystemInformation.VerticalScrollBarWidth + 4 : 0);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var row = new AccountRow(a, w) { Location = new Point(0, i * (AccountRow.RowH + 8)) };
                Image img;
                lock (headshots) if (headshots.TryGetValue(a.UserId, out img)) row.Avatar = img;
                row.Alt.CheckedChanged += (o, e) =>
                {
                    try { AccountStore.SetAlt(a.UserId, row.Alt.Checked); a.AltMode = row.Alt.Checked; }
                    catch (Exception ex) { Flash(Cute.Wont("save that", Cute.Reason(ex)), Theme.Bad); return; }
                    Flash(row.Alt.Checked ? "Alt mode on for " + a.Username + ". Applies next launch" : "Alt mode off for " + a.Username, Theme.Accent);
                };
                row.LaunchB.Click += (o, e) => LaunchAccounts(new List<Account> { a });
                row.RemoveB.Click += (o, e) =>
                {
                    if (!VDialog.Confirm(this, "Remove " + a.Username + "?", "CloveStrap forgets this account. You can sign in again any time.", "Remove")) return;
                    try { AccountStore.Remove(a.UserId); } catch (Exception ex) { Flash(Cute.Wont("remove it", Cute.Reason(ex)), Theme.Bad); return; }
                    LoadAccounts();
                    Flash("Removed " + a.Username, Theme.Accent);
                };
                accountList.Controls.Add(row);
            }
            if (list.Count == 0)
                accountList.Controls.Add(new Label
                {
                    Text = "No accounts yet\r\n\r\nPress Add account and sign in. Add as many as you like,\r\nthen launch them side by side.",
                    Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill, UseMnemonic = false
                });
            accountList.ResumeLayout();

            var missing = list.Where(a => { lock (headshots) return !headshots.ContainsKey(a.UserId); }).Select(a => a.UserId).ToList();
            if (missing.Count == 0) return;
            new Thread(() =>
            {
                foreach (var id in missing)
                {
                    var img = RobloxApi.Headshot(id);
                    if (img == null) continue;
                    lock (headshots) headshots[id] = img;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            foreach (var r in accountList.Controls.OfType<AccountRow>().Where(r => r.Acc.UserId == id)) { r.Avatar = img; r.Invalidate(); }
                        }));
                    }
                    catch { }
                }
            }) { IsBackground = true }.Start();
        }

        void AddAccount()
        {
            if (loginBusy)
            {
                loginCancel = true;
                Flash("Cancelling sign-in", Theme.Accent);
                return;
            }
            loginBusy = true; loginCancel = false;
            addAccountB.Text = "Cancel sign-in";
            pill.Set("Sign in on the Roblox window that just opened", Theme.Accent);
            flashTimer.Stop();
            new Thread(() =>
            {
                string msg; Color col = Theme.Accent; bool reload = false;
                try
                {
                    string cookie = BrowserLogin.Run(() => loginCancel || IsDisposed);
                    if (cookie == null) msg = "Sign-in cancelled";
                    else
                    {
                        var u = RobloxApi.Authenticated(cookie);
                        bool existed = AccountStore.Get(u.Id) != null;
                        AccountStore.AddOrUpdate(u, cookie);
                        Log.Write("Account " + (existed ? "refreshed" : "added") + ": " + u.Name + " (" + u.Id + ")");
                        msg = (existed ? "Signed in again as " : "Added ") + (u.DisplayName ?? u.Name);
                        reload = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Sign-in failed: " + ex);
                    msg = Cute.Wont("add that account", ex is RobloxAuthException || ex.Message.Contains("Edge") ? ex.Message.TrimEnd('.') : Cute.Reason(ex));
                    col = Theme.Bad;
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        loginBusy = false;
                        addAccountB.Text = "Add account";
                        if (reload) { LoadAccounts(); try { Activate(); } catch { } }
                        Flash(msg, col);
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void LaunchAccounts(List<Account> list)
        {
            if (list.Count == 0) { Flash("Add an account first", Theme.Warn); return; }
            string err;
            SaveJoin();
            var target = JoinTarget.Parse(S.JoinMode, S.JoinValue, out err);
            if (target == null) { Flash(Cute.Wont("launch", err), Theme.Warn); joinBox.Box.Focus(); return; }

            // server picker: choose one server for this whole launch, so every account ends up together
            string how = S.ServerPick ?? "any";
            if (target.Mode == "place" && how != "any")
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    target.JobId = RobloxGames.PickServer(target.PlaceId, how, list.Count);
                    if (target.JobId == null) Flash("No server with room for " + list.Count + " found, joining any server", Theme.Warn);
                }
                catch (Exception ex) { Log.Write("Server picker: " + ex.Message); Flash(Cute.Wont("pick a server", Cute.Reason(ex)) + ", joining any server", Theme.Warn); }
                finally { Cursor = Cursors.Default; }
            }

            // a Roblox opened without multi-instance holds the lock, so a new one would close it: offer to fix that first
            if (Deployment.RobloxRunning() && !MultiInstance.Active())
            {
                if (!VDialog.Confirm(this, "O-oh, it appears Roblox is already open OwO",
                        "It was opened without multi-instance, so starting another Roblox would close it >_<\n\n" +
                        "Fix now lets them all run side by side. Your open game stays open.", "Fix now", "Ignore"))
                    return;
                int n;
                try { Cursor = Cursors.WaitCursor; n = MultiInstance.FreeRunning(null); }
                catch (Exception ex) { Log.Write("Fix now failed: " + ex); n = -1; }
                finally { Cursor = Cursors.Default; }
                if (n < 0) { Flash(Cute.Wont("fix that", "Roblox closed first"), Theme.Bad); return; }
                Flash("Fixed! Your open Roblox will stay open " + Cute.Face(), Theme.Accent);
            }

            if (saveTimer.Enabled) { saveTimer.Stop(); SaveNow(false); }
            for (int i = 0; i < list.Count; i++)
            {
                string args = "--account " + list[i].UserId + " " + target.ToArgs() + (i > 0 ? " --delay " + (i * 6) : "");
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false }); }
                catch (Exception ex) { Flash(Cute.Wont("launch", Cute.Reason(ex)), Theme.Bad); return; }
            }
            Flash(list.Count == 1 ? "Launching " + list[0].Username + ", " + target.Describe() : "Launching " + list.Count + " accounts, " + target.Describe(), Theme.Accent);
        }

        void BuildPerformance()
        {
            var p = NewPage("Performance");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int pfw = pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int y = 4;

            var fpsCard = new Card(pfw, "Frame rate", "");
            var fpsWrap = new Panel { Size = new Size(fpsCard.InnerWidth, 32), BackColor = Theme.Row };
            var labels = new List<string> { "Monitor (" + hz + ")" };
            labels.AddRange(FpsChoices.Select(f => f.ToString()));
            labels.Add("Custom");
            fpsChips = new Chips(labels.ToArray()) { BackColor = Theme.Row };
            fpsCustom = new VTextBox { Size = new Size(84, 32), BackColor = Theme.Row, Location = new Point(fpsChips.Width + 10, 0) };
            fpsCustom.Placeholder = "fps";
            fpsCustom.Box.TextAlign = HorizontalAlignment.Center;
            fpsCustom.Box.Font = Theme.MonoBody;
            fpsCustom.Box.KeyPress += (o, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            fpsCustom.Box.TextChanged += (o, e) => Changed(true);
            fpsChips.SelectedIndexChanged += (o, e) =>
            {
                fpsCustom.Visible = fpsChips.SelectedIndex == fpsChips.Items.Length - 1;
                if (fpsCustom.Visible && fpsCustom.Text.Length == 0) { loading = true; fpsCustom.Text = hz.ToString(); loading = false; }
                Changed(true);
            };
            fpsWrap.Controls.Add(fpsChips); fpsWrap.Controls.Add(fpsCustom);
            fpsCard.AddFull("FPS cap", "Presets use your monitor's refresh rate.", fpsWrap);
            Place(p, fpsCard, ref y);

            var tweaks = new Card(pfw, "Tweaks", "");
            tFullscreen = MakeToggle(); tSway = MakeToggle(); tGrass = MakeToggle(); tMesh = MakeToggle();
            tweaks.AddRow("Exclusive fullscreen", "Alt+Enter for real fullscreen. Less input lag.", tFullscreen);
            tweaks.AddRow("Static grass", "Grass stops swaying. It looks the same standing still.", tSway);
            tweaks.AddRow("Remove grass", "Bigger saving on grassy maps. Noticeable.", tGrass);
            tweaks.AddRow("Lower mesh detail", "Simpler models far away. Big gain on weak PCs.", tMesh);
            Place(p, tweaks, ref y);

            var proc = new Card(pfw, "Process", "");
            prioChips = new Chips("Normal", "Above normal", "High");
            prioChips.SelectedIndexChanged += (o, e) => Changed(true);
            proc.AddRow("CPU priority", "Gives Roblox a bit more of your CPU.", prioChips);
            Place(p, proc, ref y);

            var snd = new Card(pfw, "Sound", "");
            var quietRow = new Panel { BackColor = Theme.Row, Height = 32 };
            var quietMode = new Chips("Mute", "Lower") { BackColor = Theme.Row, SelectedIndex = (S.QuietMode ?? "mute") == "lower" ? 1 : 0 };
            var quietToggle = new Toggle { Checked = S.QuietRoblox, BackColor = Theme.Row };
            quietToggle.Location = new Point(quietMode.Width + 16, (32 - quietToggle.Height) / 2);
            quietRow.Width = quietToggle.Right;
            quietRow.Controls.Add(quietMode); quietRow.Controls.Add(quietToggle);
            Func<string> quietDesc = () => !S.QuietRoblox ? "Off."
                : (S.QuietMode ?? "mute") == "lower" ? "Turns Roblox down when you click away."
                : "Mutes Roblox when you click away.";
            object quietRowRef = snd.AddRow("Quieten Roblox when you switch away", quietDesc(), quietRow);
            Action quietSave = () => { try { S.Save(); } catch { } snd.SetDesc(quietRowRef, quietDesc()); };
            quietToggle.CheckedChanged += (o, e) => { S.QuietRoblox = quietToggle.Checked; quietSave(); };
            quietMode.SelectedIndexChanged += (o, e) => { S.QuietMode = quietMode.SelectedIndex == 1 ? "lower" : "mute"; quietSave(); };
            Place(p, snd, ref y);
        }

        void BuildGraphics()
        {
            var p = NewPage("Graphics");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int gw = pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int y = 4;
            var card = new Card(gw, "Rendering", "");
            apiChips = new Chips("Auto", "D3D11", "Vulkan", "OpenGL");
            msaaChips = new Chips("Auto", "Off", "2x", "4x", "8x");
            texChips = new Chips("Auto", "Lowest", "Low", "Medium", "High");
            quality = new Slider(0, 21) { Width = 300 };
            quality.Format = v => v == 0 ? "auto" : v.ToString();
            foreach (var c in new[] { apiChips, msaaChips, texChips }) c.SelectedIndexChanged += (o, e) => Changed(true);
            quality.ValueChanged += (o, e) => Changed(true);
            var find = new VButton("Find", "", true);
            find.Click += (o, e) => FindBestSettings();
            card.AddRow("Read your PC to find the best settings for you?? >_<!!", "Checks your PC and picks the best settings for you.", find);
            card.AddRow("Graphics API", "D3D11 on most PCs. Try Vulkan if you stutter on AMD.", apiChips);
            card.AddRow("Anti-aliasing", "Smooths edges. Off is a few frames faster.", msaaChips);
            card.AddRow("Textures", "Lower saves video memory on 2-4 GB cards.", texChips);
            card.AddRow("Graphics quality", "Also sets how far you can see: higher shows more. Auto lets Roblox decide.", quality);
            viewChips = new Chips("Near", "Short", "Default", "Far", "Max");
            viewChips.SelectedIndexChanged += (o, e) =>
            {
                if (loading) return;
                loading = true; tMesh.Checked = viewChips.SelectedIndex == 0; loading = false;
                Changed(true);
            };
            tMesh.CheckedChanged += (o, e) =>
            {
                if (loading) return;
                loading = true; viewChips.SelectedIndex = tMesh.Checked ? 0 : (viewChips.SelectedIndex == 0 ? 2 : viewChips.SelectedIndex); loading = false;
            };
            card.AddRow("Distant model detail", "When far-away unions and meshes simplify. Doesn't change fog or how far you see; Graphics quality does.", viewChips);
            Place(p, card, ref y);

            var extra = new Card(gw, "Extras", "");
            tDpi = MakeToggle(); tSky = MakeToggle(); tVoxel = MakeToggle();
            extra.AddRow("Ignore Windows scaling", "Sharper on scaled screens, costs FPS.", tDpi);
            extra.AddRow("Gray sky", "Plain gray sky. Easier to spot players.", tSky);
            extra.AddRow("Freeze lighting", "Stops lighting updates. Big saving, can look off.", tVoxel);
            Place(p, extra, ref y);
        }

        void BuildOverlay()
        {
            var p = NewPage("Overlay");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int ow = pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int colW = ow - 236, y = 4;

            var ov = new Card(colW, "In-game overlay", "");
            tOverlay = MakeToggle(false);
            ov.AddRow("Show overlay", "Starts with Roblox. Ctrl+Shift+O hides it.", tOverlay);
            cornerChips = new Chips("Top left", "Top right", "Bottom left", "Bottom right");
            cornerChips.SelectedIndexChanged += (o, e) => Changed(false);
            ov.AddRow("Position", null, cornerChips);
            overlayItems = new MultiChips(OverlayItems.Labels, ov.InnerWidth);
            overlayItems.Changed += (o, e) => Changed(false);
            ov.AddFull("Show", "Network shows latency, jitter and packet loss.", overlayItems);
            Place(p, ov, ref y);

            var cr = new Card(colW, "Shift lock cursor", "");
            tCross = MakeToggle(false);
            crossStyle = new Chips("Dot", "Cross", "Circle");
            crossSize = new Chips("S", "M", "L");
            crossStyle.SelectedIndexChanged += (o, e) => Changed(false);
            crossSize.SelectedIndexChanged += (o, e) => Changed(false);
            cr.AddRow("Custom shift lock cursor", "Replaces the default shift lock icon.", tCross);
            tScreenCross = MakeToggle(false);
            tScreenCross.CheckedChanged += (o, e) =>
            {
                if (loading || !tScreenCross.Checked) return;
                SaveNow(false);
                if (Deployment.RobloxRunning() && !OverlayLauncher.IsRunning()) { try { OverlayLauncher.Start(S); } catch { } }
                Flash("Screen crosshair on. It sits on top of Roblox, over any game's crosshair", Theme.Accent);
            };
            cr.AddRow("Screen crosshair", "Draws your crosshair over any game's own.", tScreenCross);
            cr.AddRow("Style", null, crossStyle);
            cr.AddRow("Size", null, crossSize);
            Place(p, cr, ref y);

            var pc = new Card(224, "Overlay preview", "") { Location = new Point(ow - 224, 4) };
            preview = new OverlayPreview { Size = new Size(pc.Width - Card.Pad * 2, 236) };
            pc.AddRaw(preview, false);
            p.Controls.Add(pc);
            var cc = new Card(224, "Shift lock preview", "") { Location = new Point(ow - 224, pc.Bottom + 12) };
            crossPreview = new CrosshairPreview { Size = new Size(cc.Width - Card.Pad * 2, 96) };
            cc.AddRaw(crossPreview, false);
            p.Controls.Add(cc);
            var start = new VButton("Start overlay now", "") { Location = new Point(ow - 224, cc.Bottom + 12), Width = 224 };
            start.Click += (o, e) =>
            {
                SaveNow(false);
                if (!Deployment.RobloxRunning()) { Flash(Cute.Wont("show the overlay", "Roblox running"), Theme.Warn); return; }
                if (OverlayLauncher.IsRunning()) { Flash("The overlay is already running", Theme.Accent); return; }
                try
                {
                    string r = OverlayLauncher.Start(S, true);
                    Flash(r == "declined" ? Cute.Wont("count FPS", "admin") : "Overlay started. Switch to Roblox to see it", r == "declined" ? Theme.Warn : Theme.Accent);
                }
                catch (Exception ex) { Flash(Cute.Wont("start the overlay", Cute.Reason(ex)), Theme.Bad); }
            };
            p.Controls.Add(start);
            var note = new Label
            {
                Text = "Doesn't show in exclusive fullscreen.",
                Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg, Location = new Point(ow - 222, start.Bottom + 10),
                MaximumSize = new Size(220, 0), AutoSize = true, UseMnemonic = false
            };
            p.Controls.Add(note);
        }

        class CrosshairPreview : VControl
        {
            public bool Enabled2;
            public string Style = "dot";
            public int SizeIndex = 1;
            static Image original;

            static Image Original()
            {
                if (original != null) return original;
                try
                {
                    string v = Deployment.InstalledVersion();
                    if (v == null) return null;
                    string tex = Path.Combine(Paths.Versions, v, "content", "textures");
                    string bak = Path.Combine(tex, "MouseLockedCursor.voidstrapper-original.png");
                    string f = File.Exists(bak) ? bak : Path.Combine(tex, "MouseLockedCursor.png");
                    if (File.Exists(f)) using (var fs = File.OpenRead(f)) using (var img = Image.FromStream(fs)) original = new Bitmap(img);
                }
                catch { }
                return original;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = Begin(e);
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (var path = Theme.Round(r, 10))
                {
                    using (var bg = new LinearGradientBrush(r, Color.FromArgb(118, 160, 205), Color.FromArgb(88, 128, 80), LinearGradientMode.Vertical))
                        g.FillPath(bg, path);
                    g.SetClip(path);
                    using (var shade = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(shade, 0, 0, Width, 18);
                    using (var line = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawLine(line, Width / 2f, 22, Width / 2f, Height - 8);
                    g.ResetClip();
                }
                using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                using (var lb = new SolidBrush(Color.White))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString("ROBLOX", Theme.Mono, lb, new RectangleF(0, 3, Width / 2f, 14), sf);
                    g.DrawString("YOURS", Theme.Mono, lb, new RectangleF(Width / 2f, 3, Width / 2f, 14), sf);
                }
                float cy = 18 + (Height - 18) / 2f;
                var orig = Original();
                if (orig != null) g.DrawImage(orig, Width / 4f - orig.Width / 2f, cy - orig.Height / 2f, orig.Width, orig.Height);
                if (Enabled2)
                {
                    using (var b = OverlayRenderer.Crosshair(Style, SizeIndex, Theme.Accent))
                        g.DrawImage(b, Width * 3 / 4f - b.Width / 2f, cy - b.Height / 2f, b.Width, b.Height);
                }
                else
                {
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var lb = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                        g.DrawString("Off", Theme.Small, lb, new RectangleF(Width / 2f, 18, Width / 2f, Height - 18), sf);
                }
            }
        }

        // Ready-made flags you can just switch on. Only flags on Roblox's allowlist (everything else is ignored by Roblox).
        class QuickFlag
        {
            public string Name, Desc, Group;
            public string[] Flags;   // name, value, name, value...
            public QuickFlag(string name, string desc, string group, params string[] flags) { Name = name; Desc = desc; Group = group; Flags = flags; }
        }

        static readonly string[] LodNear = { "DFIntCSGLevelOfDetailSwitchingDistance", "0", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "0", "DFIntCSGLevelOfDetailSwitchingDistanceL23", "0", "DFIntCSGLevelOfDetailSwitchingDistanceL34", "0" };
        static readonly string[] LodFar = { "DFIntCSGLevelOfDetailSwitchingDistance", "1500", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "3000", "DFIntCSGLevelOfDetailSwitchingDistanceL23", "4500", "DFIntCSGLevelOfDetailSwitchingDistanceL34", "6000" };

        static readonly QuickFlag[] SpeedFlags =
        {
            new QuickFlag("Lowest graphics", "Forces graphics level 1.", "quality", "DFIntDebugFRMQualityLevelOverride", "1"),
            new QuickFlag("No grass", "Removes grass.", null, "FIntFRMMinGrassDistance", "0", "FIntFRMMaxGrassDistance", "0"),
            new QuickFlag("Still grass", "Grass stops swaying.", null, "FIntGrassMovementReducedMotionFactor", "0"),
            new QuickFlag("Gray sky", "Plain gray sky.", null, "FFlagDebugSkyGray", "True"),
            new QuickFlag("Freeze lighting", "Stops lighting updates. More FPS.", null, "DFFlagDebugPauseVoxelizer", "True"),
            new QuickFlag("Low textures", "Blurry textures, less VRAM.", "tex", "DFFlagTextureQualityOverrideEnabled", "True", "DFIntTextureQualityOverride", "0"),
            new QuickFlag("No anti-aliasing", "Jagged edges, more FPS.", "msaa", "FIntDebugForceMSAASamples", "1"),
            new QuickFlag("Low detail far away", "Distant models simplify sooner.", "lod", LodNear),
        };

        static readonly QuickFlag[] LookFlags =
        {
            new QuickFlag("Sharp textures", "Highest texture quality.", "tex", "DFFlagTextureQualityOverrideEnabled", "True", "DFIntTextureQualityOverride", "3"),
            new QuickFlag("Smooth edges", "4x anti-aliasing.", "msaa", "FIntDebugForceMSAASamples", "4"),
            new QuickFlag("Full detail far away", "Distant models keep their detail.", "lod", LodFar),
            new QuickFlag("Crisp UI", "Ignores Windows scaling on high-DPI screens.", null, "DFFlagDisableDPIScale", "True"),
            new QuickFlag("Real fullscreen", "Alt+Enter uses exclusive fullscreen. Less input lag.", null, "FFlagHandleAltEnterFullscreenManually", "False"),
            new QuickFlag("Use DirectX 11", "Try this if you crash or stutter.", "api", "FFlagDebugGraphicsPreferD3D11", "True"),
            new QuickFlag("Use Vulkan", "Can be smoother on some GPUs.", "api", "FFlagDebugGraphicsPreferVulkan", "True"),
            new QuickFlag("Use OpenGL", "For old or odd GPUs.", "api", "FFlagDebugGraphicsPreferOpenGL", "True"),
        };

        readonly List<KeyValuePair<QuickFlag, Toggle>> quickToggles = new List<KeyValuePair<QuickFlag, Toggle>>();
        bool quickSyncing;

        string GridValue(string name)
        {
            foreach (DataGridViewRow r in grid.Rows)
                if (Convert.ToString(r.Cells[0].Value ?? "").Trim() == name) return Convert.ToString(r.Cells[1].Value ?? "").Trim();
            return null;
        }

        void RemoveGridFlag(string name)
        {
            for (int i = grid.Rows.Count - 1; i >= 0; i--)
                if (Convert.ToString(grid.Rows[i].Cells[0].Value ?? "").Trim() == name) grid.Rows.RemoveAt(i);
        }

        bool QuickOn(QuickFlag q)
        {
            for (int i = 0; i < q.Flags.Length; i += 2)
                if (!string.Equals(GridValue(q.Flags[i]), q.Flags[i + 1], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        void SetQuick(QuickFlag q, bool on)
        {
            quickSyncing = true;
            if (on)
            {
                // only one of a group at a time (e.g. one graphics API)
                foreach (var other in SpeedFlags.Concat(LookFlags))
                    if (other != q && other.Group != null && other.Group == q.Group)
                        for (int i = 0; i < other.Flags.Length; i += 2) RemoveGridFlag(other.Flags[i]);
                for (int i = 0; i < q.Flags.Length; i += 2) SetGridFlag(q.Flags[i], q.Flags[i + 1]);
            }
            else
                for (int i = 0; i < q.Flags.Length; i += 2) RemoveGridFlag(q.Flags[i]);
            quickSyncing = false;
            FlagsChanged();
            grid.ClearSelection();
        }

        void SyncQuickToggles()
        {
            if (grid == null) return;
            quickSyncing = true;
            foreach (var kv in quickToggles) kv.Value.Checked = QuickOn(kv.Key);
            quickSyncing = false;
        }

        Card QuickCard(string title, string glyph, int width, QuickFlag[] list)
        {
            var c = new Card(width, title, glyph);
            foreach (var q in list)
            {
                var t = new Toggle();
                var qq = q;
                t.CheckedChanged += (o, e) => { if (!quickSyncing) SetQuick(qq, t.Checked); };
                quickToggles.Add(new KeyValuePair<QuickFlag, Toggle>(q, t));
                c.AddRow(q.Name, q.Desc, t);
            }
            return c;
        }

        void BuildFlags()
        {
            var p = NewPage("FFlag Editor");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int pageW = this.pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int y = 4;

            quickToggles.Clear();
            int half = (pageW - 12) / 2;
            var speed = QuickCard("Quick flags  ·  speed", "\uE945", half, SpeedFlags);
            var looks = QuickCard("Quick flags  ·  looks", "\uE790", pageW - 12 - half, LookFlags);
            speed.Location = new Point(0, y);
            looks.Location = new Point(half + 12, y);
            p.Controls.Add(speed);
            p.Controls.Add(looks);
            y += Math.Max(speed.Height, looks.Height) + 12;

            var add = new Card(pageW, "Add a flag", "");
            var bar = new Panel { Size = new Size(add.InnerWidth, 34), BackColor = Theme.Row };
            var nameBox = new VTextBox { Size = new Size(380, 34), BackColor = Theme.Row };
            nameBox.Placeholder = "Flag name";
            nameBox.Box.Font = Theme.MonoBody;
            var valBox = new VTextBox { Size = new Size(150, 34), Location = new Point(388, 0), BackColor = Theme.Row };
            valBox.Placeholder = "Value";
            valBox.Box.Font = Theme.MonoBody;
            var addB = new VButton("Add", "", true) { BackColor = Theme.Row };
            addB.Location = new Point(bar.Width - addB.Width, 0);
            valBox.Width = addB.Left - 396;
            EventHandler doAdd = (o, e) =>
            {
                string n = nameBox.Text.Trim();
                if (!Regex.IsMatch(n, "^[A-Za-z0-9_]+$")) { Flash(Cute.Wont("add that", "a proper name (letters, numbers, _)"), Theme.Warn); nameBox.Box.Focus(); return; }
                SetGridFlag(n, valBox.Text.Trim());
                nameBox.Text = ""; valBox.Text = "";
                nameBox.Box.Focus();
                FlagsChanged();
            };
            addB.Click += doAdd;
            valBox.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doAdd(o, e); } };
            nameBox.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; valBox.Box.Focus(); } };
            bar.Controls.AddRange(new Control[] { nameBox, valBox, addB });
            add.AddRaw(bar, true);
            Place(p, add, ref y);

            var gridCard = new Card(pageW, "Your flags", "");
            grid = new DataGridView
            {
                Size = new Size(pageW - Card.Pad * 2, 300),
                BackgroundColor = Theme.Card,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.Line,
                EnableHeadersVisualStyles = false,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeight = 32,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                Font = Theme.MonoBody
            };
            grid.RowTemplate.Height = 32;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Dim;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.Mono;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Card;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(12, 0, 0, 0);
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Selected;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.Padding = new Padding(12, 0, 0, 0);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flag", HeaderText = "FLAG", FillWeight = 72 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "VALUE", FillWeight = 28 });
            grid.CellEndEdit += (o, e) => FlagsChanged();
            grid.UserDeletedRow += (o, e) => FlagsChanged();
            grid.HandleCreated += (o, e) => Native.Scrollbars(grid);
            grid.VisibleChanged += (o, e) => { if (grid.Visible) grid.ClearSelection(); };
            gridCard.AddRaw(grid, false);
            emptyHint = new Label
            {
                Text = "No custom flags\r\n\r\nSwitch on a quick flag, add one above or import a JSON file.",
                Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(grid.Left + 1, grid.Top + 40, grid.Width - 2, grid.Height - 60), UseMnemonic = false
            };
            gridCard.Controls.Add(emptyHint);
            emptyHint.BringToFront();
            Place(p, gridCard, ref y);

            flagCount = MakeLabel("", Theme.Small, Theme.Muted, 2, y + 9);
            p.Controls.Add(flagCount);

            int x = pageW;
            Action<string, string, EventHandler> btn = (t, gl, h) =>
            {
                var b = new VButton(t, gl);
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= 8;
                b.Click += h;
                p.Controls.Add(b);
            };
            btn("Clear", "", (o, e) =>
            {
                if (grid.Rows.Count > 0 && VDialog.Confirm(this, "Clear all flags?", "Removes every custom flag from the list.", "Clear")) { grid.Rows.Clear(); FlagsChanged(); }
            });
            btn("Remove ignored", "", (o, e) =>
            {
                for (int i = grid.Rows.Count - 1; i >= 0; i--)
                {
                    string n = Convert.ToString(grid.Rows[i].Cells[0].Value ?? "").Trim();
                    if (n.Length > 0 && !Flags.Allowlist.Contains(n)) grid.Rows.RemoveAt(i);
                }
                FlagsChanged();
            });
            btn("Output", "", (o, e) =>
            {
                string j = Json.FlagsToJson(Flags.Build(S));
                try { Clipboard.SetText(j); } catch { }
                VDialog.Input(this, "ClientAppSettings.json", "Exactly what Roblox receives. Copied to your clipboard.", j, true, "Close");
            });
            btn("Import", "", (o, e) =>
            {
                string json = VDialog.Input(this, "Import flags", "Paste a ClientAppSettings.json. Bloxstrap and Fishstrap exports work.", "", false, "Import");
                if (string.IsNullOrWhiteSpace(json)) return;
                try
                {
                    var flags = Json.ParseFlags(json);
                    foreach (var kv in flags) SetGridFlag(kv.Key, kv.Value);
                    FlagsChanged();
                    int ignored = flags.Keys.Count(k => !Flags.Allowlist.Contains(k));
                    Flash("Imported " + flags.Count + " flags" + (ignored > 0 ? ", " + ignored + " ignored by Roblox" : ""), ignored > 0 ? Theme.Warn : Theme.Accent);
                }
                catch (Exception ex) { VDialog.Info(this, Cute.Wont("import that", "valid JSON"), "Here's what tripped me up: " + ex.Message); }
            });
        }

        void BuildLauncher()
        {
            var p = NewPage("Launcher");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int lw2 = pageW - SystemInformation.VerticalScrollBarWidth - 6;
            int y = 4;
            launcherCard = new Card(lw2, "Roblox", "");
            var check = new VButton("Check for updates", "");
            check.Click += (o, e) => RunUpdate(false);
            versionRow = launcherCard.AddRow("Installed version", "Checking", check);
            tRegister = new Toggle();
            tRegister.CheckedChanged += (o, e) => SetRegistered(tRegister.Checked);
            launcherCard.AddRow("Use for the Play button", "Clicking Play on roblox.com starts Roblox through CloveStrap.", tRegister);
            Place(p, launcherCard, ref y);

            var opts = new Card(lw2, "Options", "");
            tCleanup = MakeToggle(false); tMods = MakeToggle(false); tMulti = MakeToggle(false);
            tStartup = new Toggle();
            tStartup.CheckedChanged += (o, e) =>
            {
                if (loading) return;
                S.StartWithWindows = tStartup.Checked;
                try { Startup.Set(S.StartWithWindows); S.Save(); Flash(S.StartWithWindows ? "CloveStrap will wait in the tray from sign-in" : "Won't start with Windows", Theme.Accent); }
                catch (Exception ex) { Flash(Cute.Wont("change that", Cute.Reason(ex)), Theme.Bad); }
            };
            opts.AddRow("Start with Windows", "Waits in the tray so Roblox opens instantly.", tStartup);
            opts.AddRow("Delete old versions", "Keeps only the current Roblox version.", tCleanup);
            opts.AddRow("Mods folder", "Copies your sounds, cursors and textures in.", tMods);
            opts.AddRow("Multiple instances", "Run several Roblox windows at once.", tMulti);
            tAntiAfk = new Toggle();
            tAntiAfk.CheckedChanged += (o, e) =>
            {
                if (loading) return;
                S.AntiAfk = tAntiAfk.Checked;
                try { S.Save(); } catch { }
                Flash(S.AntiAfk ? "Anti-AFK on. Roblox won't kick you for being idle" : "Anti-AFK off", Theme.Accent);
            };
            opts.AddRow("Anti-AFK", "A tiny jump every 14 minutes so you don't get kicked for idling.", tAntiAfk);
            Place(p, opts, ref y);

            var dc = new Card(lw2, "Discord status", "");
            var dToggle = new Toggle { Checked = S.DiscordPresence };
            dToggle.CheckedChanged += (o, e) => { S.DiscordPresence = dToggle.Checked; try { S.Save(); } catch { } };
            dc.AddRow("Show what you're playing", "Shows \"Playing <game> with CloveStrap\" on Discord.", dToggle);
            var idRow = new Panel { Size = new Size(dc.InnerWidth, 34), BackColor = Theme.Row };
            var idBox = new VTextBox { Size = new Size(260, 34), BackColor = Theme.Row };
            idBox.Placeholder = "Application ID (numbers)";
            idBox.Box.Font = Theme.MonoBody;
            idBox.Text = S.DiscordAppId ?? "";
            idBox.Box.TextChanged += (o, e) =>
            {
                string v = new string(idBox.Text.Where(char.IsDigit).ToArray());
                S.DiscordAppId = v;
                try { S.Save(); } catch { }
            };
            var portal = new VButton("1. Make the app on Discord", "") { BackColor = Theme.Row, Location = new Point(idBox.Right + 8, 0) };
            portal.Click += (o, e) => { try { Process.Start(DiscordPresence.PortalUrl); } catch { } };
            var logo = new VButton("2. Get the logo", "") { BackColor = Theme.Row, Location = new Point(portal.Right + 8, 0) };
            logo.Click += (o, e) =>
            {
                try { string f = DiscordPresence.SaveLogo(); Process.Start("explorer.exe", "/select,\"" + f + "\""); Flash("Logo saved. Upload it to your Discord app", Theme.Accent); }
                catch (Exception ex) { Flash(Cute.Wont("save the logo", Cute.Reason(ex)), Theme.Bad); }
            };
            idRow.Controls.AddRange(new Control[] { idBox, portal, logo });
            if (DiscordAccount.BuiltInAppId.Length < 15)   // only needed when a build has no Discord app of its own
                dc.AddFull("Your Discord app",
                    "Make a free Discord app called CloveStrap, add the logo as its icon and as an art asset named logo, then paste its ID.",
                    idRow);
            Place(p, dc, ref y);

            var tools = new Card(lw2, "Tools", "");
            var row = new Panel { Size = new Size(tools.InnerWidth, 34), BackColor = Theme.Row };
            int x = 0;
            Action<string, string, EventHandler> tool = (t, gl, h) =>
            {
                var b = new VButton(t, gl) { Location = new Point(x, 0), BackColor = Theme.Row };
                b.Click += h;
                row.Controls.Add(b);
                x += b.Width + 8;
            };
            tool("Reinstall", "", (o, e) =>
            {
                if (VDialog.Confirm(this, "Reinstall Roblox?", "Deletes CloveStrap's copy of Roblox and downloads a fresh one. Settings are kept.", "Reinstall"))
                    RunUpdate(true);
            });
            tool("Clear cache", "", (o, e) =>
            {
                if (Deployment.RobloxRunning()) { Flash(Cute.Wont("clear the cache", "Roblox closed first"), Theme.Warn); return; }
                Flash("Cleared " + Tweaks.ClearCache() + " cached files", Theme.Accent);
            });
            tool("Mods", "", (o, e) => { Tweaks.WriteModsReadme(); Process.Start("explorer.exe", "\"" + Paths.Mods + "\""); });
            tool("Install folder", "", (o, e) =>
            {
                string v = Deployment.InstalledVersion();
                Process.Start("explorer.exe", "\"" + (v != null ? Path.Combine(Paths.Versions, v) : Paths.Versions) + "\"");
            });
            tool("Logs", "", (o, e) => Process.Start("explorer.exe", "\"" + Paths.Logs + "\""));
            tools.AddRaw(row, true);
            Place(p, tools, ref y);
        }

        static readonly string[] CustomPalette = { "#FF6FAE", "#F43F5E", "#FB923C", "#FBBF24", "#A3E635", "#34D399", "#22D3EE", "#60A5FA", "#A78BFA", "#E879F9", "#F4F4F5" };

        class ColorRow : VControl
        {
            readonly string[] colors;
            public string Selected;
            public event Action<string> Picked;
            public ColorRow(string[] colors) { this.colors = colors; Size = new Size(colors.Length * 26, 26); Cursor = Cursors.Hand; }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                int i = e.X / 26;
                if (i >= 0 && i < colors.Length && Picked != null) Picked(colors[i]);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = Begin(e);
                for (int i = 0; i < colors.Length; i++)
                {
                    var c = Theme.ParseHex(colors[i]) ?? Color.Gray;
                    var r = new RectangleF(i * 26 + 3, 3, 20, 20);
                    bool sel = string.Equals(colors[i], Selected, StringComparison.OrdinalIgnoreCase);
                    if (sel) Theme.Stroke(g, new RectangleF(r.X - 2.5f, r.Y - 2.5f, r.Width + 5, r.Height + 5), 7, Theme.Text, 1.5f);
                    Theme.Fill(g, r, 5, c);
                    Theme.Stroke(g, r, 5, Color.FromArgb(50, 0, 0, 0));
                }
            }
        }

        Panel themeStrip;
        VTextBox themeName;
        bool focusThemeName;

        CustomTheme ActiveCustom()
        {
            return S.CustomThemes.FirstOrDefault(t => t.Id == S.ColorScheme);
        }

        void BuildAppearance()
        {
            var p = NewPage("Appearance");
            var ids = Theme.Schemes.Where(s => s.Character).Select(s => s.Id).ToList();
            int gap = 12, cw = (pageW - gap * (ids.Count - 1)) / ids.Count;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                var c = new CharacterCard(id) { Location = new Point(i * (cw + gap), 4), Width = cw };
                c.Picked += (o, e) => SetScheme(id);
                p.Controls.Add(c);
            }
            int y = 4 + 262 + 16;

            // ---- your themes: a sideways-scrolling strip
            p.Controls.Add(MakeLabel("YOUR THEMES", Theme.Mono, Theme.Dim, 2, y));
            if (S.CustomThemes.Count > 3)
            {
                var more = MakeLabel("Scroll sideways for more", Theme.Mono, Theme.Dim, 0, y);
                more.Left = pageW - more.PreferredWidth - 2;
                p.Controls.Add(more);
            }
            y += 20;
            themeStrip = new Panel { Location = new Point(0, y), Size = new Size(pageW, CustomThemeCard.CardH + SystemInformation.HorizontalScrollBarHeight + 4), AutoScroll = true, BackColor = Theme.Bg };
            themeStrip.HandleCreated += (o, e) => Native.Scrollbars(themeStrip);
            MouseEventHandler wheel = (o, e) =>
            {
                int x = -themeStrip.AutoScrollPosition.X - e.Delta;
                themeStrip.AutoScrollPosition = new Point(Math.Max(0, x), 0);
            };
            themeStrip.MouseWheel += wheel;
            int x0 = 0;
            CustomThemeCard activeCard = null;
            foreach (var t in S.CustomThemes)
            {
                var th = t;
                var c = new CustomThemeCard(th.Id) { Location = new Point(x0, 0) };
                c.Click += (o, e) => SetScheme(th.Id);
                c.MouseWheel += wheel;
                themeStrip.Controls.Add(c);
                if (th.Id == S.ColorScheme) activeCard = c;
                x0 += c.Width + 10;
            }
            var add = new NewThemeCard { Location = new Point(x0, 0) };
            add.Click += (o, e) => NewTheme();
            add.MouseWheel += wheel;
            themeStrip.Controls.Add(add);
            Control scrollTo = focusThemeName ? (Control)add : activeCard;
            if (scrollTo != null)
                themeStrip.HandleCreated += (o, e) => { try { themeStrip.BeginInvoke((Action)(() => themeStrip.ScrollControlIntoView(scrollTo))); } catch { } };
            p.Controls.Add(themeStrip);
            y += themeStrip.Height + 10;

            // ---- editor for the selected theme
            var cur = ActiveCustom();
            themeName = null;
            if (cur == null)
            {
                var hint = new Card(pageW, "Edit a theme", "");
                var l = new Label
                {
                    Text = "Click a theme to edit it, or press New theme.",
                    Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Row, AutoSize = false, Size = new Size(hint.InnerWidth, 20), UseMnemonic = false
                };
                hint.AddRaw(l, true);
                Place(p, hint, ref y);
                return;
            }

            var ce = new Card(pageW, "Edit \"" + Theme.Get(cur.Id).Name + "\"", "");
            bool hasPic = Characters.Get(cur.Id) != null;
            var picRow = new Panel { BackColor = Theme.Row, Height = 34 };
            themeName = new VTextBox { Size = new Size(200, 34), BackColor = Theme.Row };
            themeName.Placeholder = "Theme name";
            themeName.Box.MaxLength = 24;
            themeName.Text = cur.Name ?? "";
            themeName.Box.TextChanged += (o, e) => RenameTheme(cur, themeName.Text);
            themeName.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ActiveControl = null; } };
            var choose = new VButton(hasPic ? "Change picture" : "Choose picture", "", true) { BackColor = Theme.Row, Location = new Point(themeName.Right + 8, 0) };
            choose.Click += (o, e) => PickThemePicture(cur);
            int px = choose.Right + 8;
            picRow.Controls.Add(themeName); picRow.Controls.Add(choose);
            if (hasPic)
            {
                var remove = new VButton("Remove picture") { BackColor = Theme.Row, Location = new Point(px, 0) };
                remove.Click += (o, e) => { Characters.Remove(cur.Id); Build("Appearance"); Flash("Picture removed", Theme.Accent); };
                picRow.Controls.Add(remove);
                px = remove.Right + 8;
            }
            var del = new IconButton("") { Size = new Size(36, 34), BackColor = Theme.Row, Location = new Point(px, 0) };
            del.Click += (o, e) => DeleteTheme(cur);
            picRow.Controls.Add(del);
            picRow.Width = del.Right;
            ce.AddRow("Name & picture", null, picRow);

            var colRow = new Panel { BackColor = Theme.Row, Height = 32 };
            var colors = new ColorRow(CustomPalette) { BackColor = Theme.Row, Location = new Point(0, 3), Selected = cur.Accent };
            colors.Picked += hex => SetThemeColors(cur, hex, cur.Light);
            var hexBox = new VTextBox { Size = new Size(96, 32), BackColor = Theme.Row, Location = new Point(colors.Width + 10, 0) };
            hexBox.Box.Font = Theme.MonoBody;
            hexBox.Box.MaxLength = 7;
            hexBox.Text = cur.Accent;
            hexBox.Box.KeyDown += (o, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (Theme.ParseHex(hexBox.Text) == null) { Flash(Cute.Wont("use that color", "a hex code like #FF6FAE"), Theme.Warn); return; }
                SetThemeColors(cur, hexBox.Text, cur.Light);
            };
            var baseChips = new Chips("Dark", "Light") { SelectedIndex = cur.Light ? 1 : 0, BackColor = Theme.Row, Location = new Point(hexBox.Right + 12, 0) };
            baseChips.SelectedIndexChanged += (o, e) => SetThemeColors(cur, cur.Accent, baseChips.SelectedIndex == 1);
            colRow.Controls.Add(colors); colRow.Controls.Add(hexBox); colRow.Controls.Add(baseChips);
            colRow.Width = baseChips.Right;
            ce.AddRow("Colors", null, colRow);
            Place(p, ce, ref y);

            if (focusThemeName)
            {
                focusThemeName = false;
                var box = themeName.Box;
                EventHandler go = (o, e) => { try { BeginInvoke((Action)(() => { box.Focus(); box.SelectAll(); })); } catch { } };
                if (box.IsHandleCreated) go(null, null); else box.HandleCreated += go;
            }
        }

        void SaveThemes(bool rebuild)
        {
            Theme.SetCustoms(S.CustomThemes);
            Theme.ApplyScheme(S.ColorScheme);
            try { S.Save(); } catch { }
            if (rebuild) Build("Appearance");
        }

        void NewTheme()
        {
            int n = S.CustomThemes.Count + 1;
            while (S.CustomThemes.Any(t => string.Equals(t.Name, "Theme " + n, StringComparison.OrdinalIgnoreCase))) n++;
            // start with a colour that doesn't look like any other theme
            string accent = ThemeColors.FirstDistinct(CustomPalette, ThemeColors.Others(null, S.CustomThemes)) ?? CustomPalette[new Random().Next(CustomPalette.Length)];
            var t2 = new CustomTheme { Id = "custom-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "Theme " + n, Accent = accent, Light = false };
            S.CustomThemes.Add(t2);
            S.ColorScheme = t2.Id;
            focusThemeName = true;
            SaveThemes(true);
            Flash("New theme made. Give it a name and a picture", Theme.Accent);
        }

        void RenameTheme(CustomTheme t, string name)
        {
            t.Name = name.Trim();
            Theme.SetCustoms(S.CustomThemes);
            Theme.ApplyScheme(S.ColorScheme);
            if (themeStrip != null) foreach (Control c in themeStrip.Controls) c.Invalidate();
            if (art != null) art.Invalidate();
            saveTimer.Stop(); saveTimer.Start();
        }

        void DeleteTheme(CustomTheme t)
        {
            string name = Theme.Get(t.Id).Name;
            if (!VDialog.Confirm(this, "Delete " + name + "?", "Removes this theme and its picture. Your other themes stay.", "Delete")) return;
            S.CustomThemes.Remove(t);
            Characters.Remove(t.Id);
            if (S.ColorScheme == t.Id) S.ColorScheme = S.CustomThemes.Count > 0 ? S.CustomThemes[S.CustomThemes.Count - 1].Id : "natsuki";
            SaveThemes(true);
            Flash("Deleted " + name, Theme.Accent);
        }

        void SetThemeColors(CustomTheme t, string hex, bool light)
        {
            var c = Theme.ParseHex(hex);
            if (c == null) return;
            if (!string.Equals(Theme.Hex(c.Value), t.Accent, StringComparison.OrdinalIgnoreCase))
            {
                // no two themes should look the same
                string twin = ThemeColors.TooCloseTo(c.Value, ThemeColors.Others(t.Id, S.CustomThemes));
                if (twin != null) { Flash("That's almost the same color as " + twin + ". Pick another one >_<", Theme.Warn); return; }
            }
            t.Accent = Theme.Hex(c.Value);
            t.Light = light;
            S.ColorScheme = t.Id;
            SaveThemes(true);
            Flash(Theme.Get(t.Id).Name + " updated", Theme.Accent);
        }

        void PickThemePicture(CustomTheme t)
        {
            using (var d = new OpenFileDialog { Title = "Pick a picture for " + Theme.Get(t.Id).Name, Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Characters.Set(t.Id, d.FileName);
                    // pick the theme's colours from the picture (never one that looks like another theme)
                    var pic = Characters.Get(t.Id);
                    if (pic != null)
                    {
                        Color acc; bool light;
                        ThemeColors.FromPicture(pic, ThemeColors.Others(t.Id, S.CustomThemes), out acc, out light);
                        t.Accent = Theme.Hex(acc);
                        t.Light = light;
                    }
                    S.ColorScheme = t.Id;
                    SaveThemes(true);
                    Flash("Picture saved. Colors picked to match it " + Cute.Face(), Theme.Accent);
                }
                catch (Exception ex)
                {
                    Log.Write("Picture failed: " + ex);
                    Flash(Cute.Wont("use that", "a real image file"), Theme.Bad);
                }
            }
        }

        void SetScheme(string id)
        {
            if (Theme.SchemeId == id) return;
            Theme.SetCustoms(S.CustomThemes);
            Theme.ApplyScheme(id);
            S.ColorScheme = id;
            try { S.Save(); } catch { }
            Build(currentPage ?? "Appearance");
            Flash(Theme.Get(id).Name + " theme", Theme.Accent);
        }

        Toggle MakeToggle(bool perf = true)
        {
            var t = new Toggle();
            t.CheckedChanged += (o, e) => Changed(perf);
            return t;
        }

        void SetGridFlag(string name, string value)
        {
            foreach (DataGridViewRow r in grid.Rows)
                if (Convert.ToString(r.Cells[0].Value) == name) { r.Cells[1].Value = value; return; }
            grid.Rows.Add(name, value);
        }

        void ColorRows()
        {
            int ignored = 0;
            foreach (DataGridViewRow r in grid.Rows)
            {
                string n = Convert.ToString(r.Cells[0].Value ?? "").Trim();
                bool ok = n.Length == 0 || Flags.Allowlist.Contains(n);
                if (!ok) ignored++;
                r.DefaultCellStyle.ForeColor = ok ? Theme.Text : Theme.Warn;
                r.Cells[0].ToolTipText = ok ? "" : "Not on Roblox's allowlist, so Roblox ignores it.";
            }
            int count = grid.Rows.Count;
            emptyHint.Visible = count == 0;
            flagCount.Text = count == 0 ? "" : count + (count == 1 ? " flag" : " flags") + (ignored > 0 ? ", " + ignored + " ignored (yellow)" : ", all accepted");
            flagCount.ForeColor = ignored > 0 ? Theme.Warn : Theme.Muted;
        }

        // ---------------------------------------------------------- state <-> ui
        void LoadIntoUi()
        {
            loading = true;
            int last = fpsChips.Items.Length - 1;
            if (S.FpsCap <= 0) fpsChips.SelectedIndex = 0;
            else { int i = Array.IndexOf(FpsChoices, S.FpsCap); fpsChips.SelectedIndex = i >= 0 ? i + 1 : last; }
            fpsCustom.Visible = fpsChips.SelectedIndex == last;
            fpsCustom.Text = fpsChips.SelectedIndex == last ? S.FpsCap.ToString() : "";

            tFullscreen.Checked = S.ExclusiveFullscreen;
            tSway.Checked = S.DisableGrassSway;
            tGrass.Checked = S.RemoveGrass;
            tMesh.Checked = S.LowMeshDetail;
            viewChips.SelectedIndex = S.LowMeshDetail ? 0 : Math.Max(0, Array.IndexOf(ViewValues, S.ViewDistance ?? "default"));
            if (viewChips.SelectedIndex == 0 && !S.LowMeshDetail) viewChips.SelectedIndex = 2;
            prioChips.SelectedIndex = Math.Max(0, Array.IndexOf(PrioValues, S.Priority));

            apiChips.SelectedIndex = Math.Max(0, Array.IndexOf(ApiValues, S.GraphicsApi));
            msaaChips.SelectedIndex = Math.Max(0, Array.IndexOf(MsaaValues, S.Msaa));
            texChips.SelectedIndex = Math.Max(0, Array.IndexOf(TexValues, S.TextureQuality));
            quality.Value = S.QualityOverride;
            tDpi.Checked = S.DisableDpiScale;
            tSky.Checked = S.GraySky;
            tVoxel.Checked = S.PauseVoxelizer;

            tOverlay.Checked = S.OverlayEnabled;
            cornerChips.SelectedIndex = Math.Max(0, Array.IndexOf(Corners, S.OverlayCorner));
            var items = OverlayItems.Parse(S.OverlayItems);
            for (int i = 0; i < OverlayItems.Keys.Length; i++) overlayItems.On[i] = items.Contains(OverlayItems.Keys[i]);
            overlayItems.Invalidate();
            tCross.Checked = S.Crosshair;
            tScreenCross.Checked = S.ScreenCrosshair;
            crossStyle.SelectedIndex = Math.Max(0, Array.IndexOf(CrossStyles, S.CrosshairStyle));
            crossSize.SelectedIndex = Math.Max(0, Math.Min(2, S.CrosshairSize));
            UpdatePreview();

            tRegister.Checked = Protocol.IsRegistered();
            tCleanup.Checked = S.CleanupOldVersions;
            tMods.Checked = S.ApplyMods;
            tMulti.Checked = S.MultiInstance;
            tStartup.Checked = S.StartWithWindows;
            tAntiAfk.Checked = S.AntiAfk;

            grid.Rows.Clear();
            foreach (var kv in S.CustomFlags) grid.Rows.Add(kv.Key, kv.Value);
            SyncQuickToggles();
            grid.ClearSelection();
            ColorRows();
            RefreshPresets();
            loading = false;
        }

        void ReadUi()
        {
            int last = fpsChips.Items.Length - 1, sel = fpsChips.SelectedIndex;
            if (sel <= 0) S.FpsCap = 0;
            else if (sel == last)
            {
                int v;
                S.FpsCap = int.TryParse(fpsCustom.Text, out v) ? Math.Max(15, Math.Min(1000, v)) : hz;
            }
            else S.FpsCap = FpsChoices[sel - 1];
            S.ExclusiveFullscreen = tFullscreen.Checked;
            S.DisableGrassSway = tSway.Checked;
            S.RemoveGrass = tGrass.Checked;
            S.LowMeshDetail = tMesh.Checked;
            S.ViewDistance = tMesh.Checked ? "near" : ViewValues[Math.Max(0, viewChips.SelectedIndex)];
            if (!tMesh.Checked && S.ViewDistance == "near") S.ViewDistance = "default";
            S.Priority = PrioValues[Math.Max(0, prioChips.SelectedIndex)];
            S.GraphicsApi = ApiValues[Math.Max(0, apiChips.SelectedIndex)];
            S.Msaa = MsaaValues[Math.Max(0, msaaChips.SelectedIndex)];
            S.TextureQuality = TexValues[Math.Max(0, texChips.SelectedIndex)];
            S.QualityOverride = quality.Value;
            S.DisableDpiScale = tDpi.Checked;
            S.GraySky = tSky.Checked;
            S.PauseVoxelizer = tVoxel.Checked;
            S.CleanupOldVersions = tCleanup.Checked;
            S.ApplyMods = tMods.Checked;
            S.MultiInstance = tMulti.Checked;
            S.OverlayEnabled = tOverlay.Checked;
            S.OverlayCorner = Corners[Math.Max(0, cornerChips.SelectedIndex)];
            S.OverlayItems = string.Join(",", OverlayItems.Keys.Where((k, i) => overlayItems.On[i]));
            S.Crosshair = tCross.Checked;
            S.ScreenCrosshair = tScreenCross.Checked;
            S.CrosshairStyle = CrossStyles[Math.Max(0, crossStyle.SelectedIndex)];
            S.CrosshairSize = Math.Max(0, crossSize.SelectedIndex);
        }

        void UpdatePreview()
        {
            if (preview == null) return;
            preview.Items = new HashSet<string>(OverlayItems.Keys.Where((k, i) => overlayItems.On[i]));
            preview.Corner = Corners[Math.Max(0, cornerChips.SelectedIndex)];
            preview.Crosshair = false;
            preview.CrossStyle = CrossStyles[Math.Max(0, crossStyle.SelectedIndex)];
            preview.CrossSize = Math.Max(0, crossSize.SelectedIndex);
            preview.Invalidate();
            if (crossPreview != null) { crossPreview.Enabled2 = tCross.Checked; crossPreview.Style = CrossStyles[Math.Max(0, crossStyle.SelectedIndex)]; crossPreview.SizeIndex = Math.Max(0, crossSize.SelectedIndex); crossPreview.Invalidate(); }
        }

        void ReadFlags()
        {
            S.CustomFlags = new Dictionary<string, string>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                string n = Convert.ToString(r.Cells[0].Value ?? "").Trim();
                if (n.Length == 0 || !Regex.IsMatch(n, "^[A-Za-z0-9_]+$")) continue;
                S.CustomFlags[n] = Convert.ToString(r.Cells[1].Value ?? "").Trim();
            }
        }

        void Changed(bool perf)
        {
            if (loading) return;
            ReadUi();
            if (perf && S.Preset != "custom") S.Preset = "custom";
            RefreshPresets();
            UpdatePreview();
            saveTimer.Stop(); saveTimer.Start();
        }

        void FlagsChanged()
        {
            ColorRows();
            ReadFlags();
            if (!quickSyncing) SyncQuickToggles();
            saveTimer.Stop(); saveTimer.Start();
        }

        void ApplyPreset(string id)
        {
            if (id == "custom") { ShowPage("Performance"); return; }
            Presets.Apply(S, id);
            LoadIntoUi();
            SaveNow(false);
            Flash(Presets.Get(id).Name + " applied", Theme.Accent);
        }

        void RefreshPresets()
        {
            foreach (var c in presetCards)
            {
                c.Active = c.Info.Id == S.Preset;
                Settings tmp;
                if (c.Info.Id == "custom") tmp = S;
                else { tmp = new Settings(); Presets.Apply(tmp, c.Info.Id); }
                SetSpecs(c, tmp);
                c.Invalidate();
            }
        }

        void SaveNow(bool flash)
        {
            try
            {
                S.Save();
                string v = Deployment.InstalledVersion();
                if (v != null && !Deployment.RobloxRunning()) Tweaks.Apply(S, Path.Combine(Paths.Versions, v));
                if (flash) Flash("Saved", Theme.Accent);
            }
            catch (Exception ex)
            {
                Log.Write("Save failed: " + ex);
                Flash(Cute.Wont("save", Cute.Reason(ex)), Theme.Bad);
            }
        }

        void SetRegistered(bool on)
        {
            if (loading) return;
            S.RegisterProtocol = on;
            try
            {
                if (on) Protocol.Register(); else Protocol.Unregister();
                Flash(on ? "Play button now opens CloveStrap" : "Play button restored to the official launcher", Theme.Accent);
            }
            catch (Exception ex) { Flash(Cute.Wont("take over the Play button", Cute.Reason(ex)), Theme.Bad); }
            try { S.Save(); } catch { }
        }

        public void Flash(string text, Color c)
        {
            if (pill == null) return;
            pill.Set(text, c);
            flashTimer.Stop(); flashTimer.Start();
        }

        void Launch()
        {
            if (saveTimer.Enabled) saveTimer.Stop();
            SaveNow(false);
            if (S.RegisterProtocol && !Protocol.IsRegistered()) { try { Protocol.Register(); } catch { } }
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--launch") { UseShellExecute = false });
            Close();
        }

        // ---------------------------------------------------------- status
        void SetRobloxStatus(string sidebar, string row)
        {
            if (profile != null) { profile.Status = sidebar; profile.Invalidate(); }
            if (launcherCard != null) launcherCard.SetDesc(versionRow, row);
        }

        void RefreshStatus()
        {
            string v = Deployment.InstalledVersion();
            SetRobloxStatus("Checking for updates", v != null ? v + ", checking for updates" : "Not installed yet. Installs automatically on first launch.");
            new Thread(() =>
            {
                string latest = null;
                try { latest = Deployment.LatestVersion(); } catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        latestVersion = latest;
                        string cur = Deployment.InstalledVersion();
                        if (cur == null) SetRobloxStatus("Roblox installs on first launch", "Not installed yet. Installs automatically on first launch.");
                        else if (latest == null) SetRobloxStatus("Offline, using installed Roblox", cur + ", couldn't check for updates");
                        else if (latest == cur) SetRobloxStatus("Roblox is up to date", cur + ", up to date");
                        else SetRobloxStatus("Update installs on next launch", cur + ", " + latest + " installs on next launch");
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        Image discordAvatar;

        // profile corner: sign in with Discord (name + picture) or sign out
        void ProfileClicked()
        {
            if (DiscordAccount.SignedIn(S)) return;   // signed in: your picture is just your picture (no signing out from here)
            if (!DiscordAccount.Available(S)) { Flash("Discord sign-in isn't set up yet (needs the CloveStrap Discord app ID)", Theme.Warn); return; }
            Flash("Asking Discord...", Theme.Accent);
            new Thread(() =>
            {
                string err = DiscordAccount.SignIn();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        S = Settings.Load();
                        if (err != null) { Flash(Cute.Wont("sign in", err == "Discord isn't open" ? "the Discord app open" : err), Theme.Warn); return; }
                        discordAvatar = DiscordAccount.Avatar();
                        Build(currentPage ?? "Presets");
                        Flash("Signed in as " + S.DiscordName + " " + Cute.Face(), Theme.Accent);
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        // Roblox username and user id come from Roblox's own log files; the headshot comes from Roblox's thumbnail service.
        void FindRobloxName()
        {
            new Thread(() =>
            {
                string found = null, userId = null;
                try
                {
                    string dir = Path.Combine(Paths.RobloxData, "logs");
                    var re = new Regex("\"UserName\"%3a\"([A-Za-z0-9_]{3,20})\"");
                    var reId = new Regex("\"UserId\"%3a([0-9]{1,15})");
                    foreach (var f in new DirectoryInfo(dir).GetFiles("*.log").Where(x => x.Name.Contains("_Player_")).OrderByDescending(x => x.LastWriteTimeUtc).Take(6))
                    {
                        using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs))
                        {
                            string text = sr.ReadToEnd();
                            var m = re.Match(text);
                            if (m.Success)
                            {
                                found = m.Groups[1].Value;
                                var mi = reId.Match(text);
                                if (mi.Success) userId = mi.Groups[1].Value;
                                break;
                            }
                        }
                    }
                }
                catch { }
                if (found == null) return;

                Image img = null;
                if (userId != null)
                {
                    string cacheFile = Path.Combine(Paths.Base, "avatar_" + userId + ".png");
                    try
                    {
                        if (!File.Exists(cacheFile) || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalHours > 12)
                        {
                            using (var wc = new WebClient())
                            {
                                wc.Headers[HttpRequestHeader.UserAgent] = "CloveStrap/1.0";
                                string json = wc.DownloadString("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=" + userId + "&size=150x150&format=Png&isCircular=false");
                                var mu = Regex.Match(json, "\"imageUrl\"\\s*:\\s*\"(https://[^\"]+)\"");
                                if (mu.Success)
                                {
                                    var bytes = wc.DownloadData(mu.Groups[1].Value);
                                    File.WriteAllBytes(cacheFile, bytes);
                                }
                            }
                        }
                        if (File.Exists(cacheFile))
                            using (var fs = File.OpenRead(cacheFile)) using (var tmp = Image.FromStream(fs)) img = new Bitmap(tmp);
                    }
                    catch (Exception ex) { Log.Write("Avatar: " + ex.Message); }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        robloxName = found;
                        if (img != null) avatar = img;
                        if (profile != null && !DiscordAccount.SignedIn(S)) { profile.UserName = found; profile.Avatar = avatar; profile.Invalidate(); }
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        bool busy;
        void RunUpdate(bool reinstall)
        {
            if (busy) return;
            busy = true;
            Flash(reinstall ? "Reinstalling Roblox" : "Checking for updates", Theme.Accent);
            flashTimer.Stop();
            new Thread(() =>
            {
                string msg; Color col = Theme.Accent;
                try
                {
                    if (reinstall)
                    {
                        string v = Deployment.InstalledVersion();
                        if (v != null)
                        {
                            if (Deployment.RobloxRunning()) throw new Exception("close Roblox first");
                            Directory.Delete(Path.Combine(Paths.Versions, v), true);
                        }
                    }
                    string dir = Deployment.EnsureInstalled((t, pct) =>
                    {
                        string first = t.Split('\n')[0];
                        try { BeginInvoke((Action)(() => { if (pill != null) pill.Set(pct >= 0 ? first + "  " + pct + "%" : first, Theme.Accent); })); } catch { }
                    });
                    Tweaks.Apply(Settings.Load(), dir);
                    if (S.CleanupOldVersions) Deployment.CleanupOld(dir);
                    msg = "Roblox is installed and up to date";
                }
                catch (Exception ex) { Log.Write("Update failed: " + ex); msg = Cute.Wont(reinstall ? "reinstall Roblox" : "update Roblox", Cute.Reason(ex)); col = Theme.Bad; }
                try { BeginInvoke((Action)(() => { busy = false; Flash(msg, col); RefreshStatus(); })); } catch { }
            }) { IsBackground = true }.Start();
        }
    }

    // ------------------------------------------------------------------ entry point
    static class Program
    {
        public static string SchemeOverride;
        public static bool DemoThemes;   // screenshots only
        public static string DemoFlash;  // screenshots only
        public static string DemoSearch, DemoGoTo, DemoAsk;  // screenshots only
        public static bool JustUpdated;   // this run was started by the updater

        /// <summary>Play on roblox.com is on by default: take it (back) whenever CloveStrap opens, e.g. after a Roblox update reclaimed it.</summary>
        static void ClaimPlayButton()
        {
            try
            {
                var s = Settings.Load();
                if (!s.RegisterProtocol) return;
                if (!File.Exists(Paths.SettingsFile)) s.Save();   // remember the default
                if (Protocol.IsRegistered()) return;
                Protocol.Register();
                s.Save();
            }
            catch (Exception ex) { Log.Write("Play button claim failed: " + ex.Message); }
        }
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 16;
            string migrated = Paths.MigrateFromVoidstrapper();
            Paths.Ensure();
            Log.Init();
            if (migrated != null) Log.Write(migrated);
            Tweaks.WriteModsReadme();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            { var st = Settings.Load(); Theme.SetCustoms(st.CustomThemes); Theme.ApplyScheme(st.ColorScheme); }
            int si = Array.IndexOf(args, "--scheme");
            if (si >= 0 && si + 1 < args.Length) { SchemeOverride = args[si + 1]; Theme.ApplyScheme(SchemeOverride); }   // preview without saving

            string uri = args.FirstOrDefault(a =>
                a.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase));

            // ---- overlay
            if (args.Contains("--overlay")) return OverlayLauncher.RunHost();
            int ei = Array.IndexOf(args, "--etw-test");
            if (ei >= 0) return OverlayLauncher.EtwTest(ei + 1 < args.Length ? int.Parse(args[ei + 1]) : 5);
            int ri = Array.IndexOf(args, "--overlay-render");
            if (ri >= 0 && ri + 1 < args.Length)
            {
                using (var b = OverlayRenderer.Render(OverlayData.Sample(Display.RefreshRate()), OverlayItems.Parse(OverlayItems.Default + ",ram,clock"), Theme.Accent)) b.Save(args[ri + 1]);
                return 0;
            }
            if (args.Contains("--overlay-stats"))
            {
                var sys = new SysStats(); var net = new NetStats();
                for (int i = 0; i < 8; i++)
                {
                    var rb = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();
                    sys.Sample(rb != null ? rb.Id : -1); net.Poll();
                    Log.Write(string.Format("stats: cpu {0} gpu {1} mem {2}MB | server {3} loc {4} hop {5} ms {6} jitter {7} loss {8} session {9}",
                        sys.Cpu, sys.Gpu, sys.Mem >> 20, net.ServerIp, net.Location, net.Hop, net.Ms, net.Jitter, net.Loss, net.JoinedUtc.HasValue ? (DateTime.UtcNow - net.JoinedUtc.Value).ToString(@"hh\:mm\:ss") : "-"));
                    Thread.Sleep(1500);
                }
                return 0;
            }

            // ---- headless switches (testing / scripting)
            if (args.Contains("--update"))
            {
                Deployment.NoCopy = args.Contains("--no-copy");
                bool small = args.Contains("--small-only");
                string target = null;
                int ti = Array.IndexOf(args, "--target");
                if (ti >= 0 && ti + 1 < args.Length) target = args[ti + 1];
                try
                {
                    string lastLine = null;
                    string dir = Deployment.EnsureInstalled((t, p) =>
                    {
                        string line = t.Split('\n')[0] + (p >= 0 ? " " + p + "%" : "");
                        if (line != lastLine) { lastLine = line; Log.Write("[progress] " + line); }
                    }, small, target);
                    Log.Write("OK " + dir);
                    return 0;
                }
                catch (Exception ex) { Log.Write("FAILED " + ex); return 1; }
            }
            if (args.Contains("--write-flags"))
            {
                string v = Deployment.InstalledVersion();
                if (v == null) { Log.Write("FAILED no install"); return 1; }
                var s = Settings.Load();
                string cs = Path.Combine(Paths.Versions, v, "ClientSettings");
                Directory.CreateDirectory(cs);
                File.WriteAllText(Path.Combine(cs, "ClientAppSettings.json"), Json.FlagsToJson(Flags.Build(s)), new UTF8Encoding(false));
                Log.Write("OK flags written");
                return 0;
            }
            int fi = Array.IndexOf(args, "--fps-test");
            if (fi >= 0 && fi + 2 < args.Length)
                return Tweaks.SetFramerateCap(args[fi + 1], int.Parse(args[fi + 2])) ? 0 : 1;
            if (args.Contains("--progress-demo"))
            {
                var demo = new ProgressForm { Subtitle = "alt  ·  @builderman" };
                demo.Shown += (o, e) => demo.Report("Downloading Roblox  (212 / 340 MB)\ncontent-textures3.zip  ·  48211 KB", 62);
                Application.Run(demo);
                return 0;
            }
            // --render-loading <file.png> <pct> <step> <detail>: screenshot of the loading screen (testing)
            int rli = Array.IndexOf(args, "--render-loading");
            if (rli >= 0 && rli + 4 < args.Length)
            {
                var lf = new ProgressForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false, Subtitle = "alt  ·  @builderman" };
                lf.Report(args[rli + 3] + "\n" + args[rli + 4], int.Parse(args[rli + 2]));
                var tm = new Timer { Interval = 1200 };
                tm.Tick += (o, e) =>
                {
                    tm.Stop();
                    using (var bmp = new Bitmap(lf.Width, lf.Height)) { lf.DrawToBitmap(bmp, new Rectangle(0, 0, lf.Width, lf.Height)); bmp.Save(args[rli + 1]); }
                    lf.Close();
                };
                lf.Shown += (o, e) => tm.Start();
                Application.Run(lf);
                return 0;
            }
            int nti = Array.IndexOf(args, "--names-test");
            if (nti >= 0 && nti + 2 < args.Length) { try { return NameFinder.SelfTest(args[nti + 1], int.Parse(args[nti + 2])); } catch (Exception ex) { Log.Write("FAILED " + ex); return 1; } }
            if (args.Contains("--activity-test"))
            {
                foreach (var rp in Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    var lf = RobloxLogs.LogFor(rp); var st = RobloxLogs.Read(lf);
                    Log.Write("activity-test: pid " + rp.Id + " log " + (lf == null ? "-" : Path.GetFileName(lf)) + " place " + (st == null ? 0 : st.PlaceId) + " disconnected " + (st != null && st.Disconnected) + " " + (st == null ? "" : st.DisconnectReason));
                    if (st != null && st.PlaceId > 0) { var g = RobloxGames.Info(st.PlaceId); Log.Write("activity-test: game \"" + g.Name + "\" icon " + (g.IconUrl ?? "-")); }
                }
                try { Log.Write("activity-test: server pick (empty) for 606849621 = " + RobloxGames.PickServer(606849621, "empty", 3)); } catch (Exception ex) { Log.Write("activity-test: server pick failed " + ex.Message); }
                Log.Write("activity-test: discord pipe " + (File.Exists(@"\.pipediscord-ipc-0") ? "found" : "not found"));
                return 0;
            }
            int dti = Array.IndexOf(args, "--discord-test");
            if (dti >= 0 && dti + 1 < args.Length) { var rpc = new DiscordRpc(args[dti + 1]); Log.Write("discord-test: connected=" + rpc.Connect()); rpc.Dispose(); return 0; }
            int lgi = Array.IndexOf(args, "--logo-sheet");
            if (lgi >= 0 && lgi + 1 < args.Length)
            {
                using (var sheet = new Bitmap(860, 330))
                using (var g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(40, 40, 70));
                    for (int st = 0; st < 3; st++)
                    {
                        Theme.LogoStyle = st; int x = 10 + st * 285;
                        Theme.DrawLogo(g, new RectangleF(x, 10, 200, 200), Color.FromArgb(255, 111, 174));
                        int sx = x; foreach (int sz in new[] { 64, 48, 32, 24, 16 }) { using (var bm = new Bitmap(sz, sz)) { using (var gg = Graphics.FromImage(bm)) Theme.DrawLogo(gg, new RectangleF(0, 0, sz - 1, sz - 1), Color.FromArgb(255, 111, 174)); g.DrawImageUnscaled(bm, sx, 230); } sx += sz + 8; }
                    }
                    sheet.Save(args[lgi + 1]);
                }
                return 0;
            }
            if (args.Contains("--version-test")) { try { Log.Write("channel=" + (Deployment.Channel() ?? "(normal)") + " picks " + Deployment.LatestVersion()); return 0; } catch (Exception ex) { Log.Write("FAILED " + ex); return 1; } }
            if (args.Contains("--pc-test")) { try { var pc = PcReader.Read(); var why = PcReader.Recommend(pc, new Settings()); Log.Write("pc-test picks: " + string.Join(" / ", why)); return 0; } catch (Exception ex) { Log.Write("FAILED " + ex); return 1; } }
            int mti = Array.IndexOf(args, "--mods-test");
            if (mti >= 0) { try { return ModKit.SelfTest(mti + 1 < args.Length ? args[mti + 1] : null); } catch (Exception ex) { Log.Write("FAILED " + ex); return 1; } }
            if (args.Contains("--singleton-scan"))
            {
                MultiInstance.DebugNames = args.Contains("--all");
                Log.Write("singleton active (held by CloveStrap): " + MultiInstance.Active());
                try { Log.Write("OK singleton scan found " + MultiInstance.FreeRunning(d => Log.Write("scan: " + d), true)); return 0; }
                catch (Exception ex) { Log.Write("FAILED " + ex); return 1; }
            }
            if (args.Contains("--error-demo"))
            {
                VDialog.Info(null, Cute.Wont("open Roblox", "an internet connection"), "Here's what went wrong, if you want the nerdy details:\nCouldn't download Roblox and no installed copy was found.");
                return 0;
            }

            // ---- headless account checks (testing)
            if (args.Contains("--login-test"))
            {
                try { string r = BrowserLogin.Run(() => false, "https://www.roblox.com/login", 8); Log.Write("OK login plumbing " + (r != null)); return r != null ? 0 : 1; }
                catch (Exception ex) { Log.Write("FAILED " + ex); return 1; }
            }
            int li = Array.IndexOf(args, "--lookup-test");
            if (li >= 0 && li + 1 < args.Length)
            {
                try { Log.Write("OK lookup " + args[li + 1] + " = " + RobloxApi.UserIdFromName(args[li + 1])); return 0; }
                catch (Exception ex) { Log.Write("FAILED " + ex); return 1; }
            }
            if (args.Contains("--alt-test"))
            {
                try
                {
                    string v = Deployment.InstalledVersion();
                    if (v == null) { Log.Write("FAILED no install"); return 1; }
                    Log.Write("OK alt dir " + AltMode.Prepare(Path.Combine(Paths.Versions, v)));
                    return 0;
                }
                catch (Exception ex) { Log.Write("FAILED " + ex); return 1; }
            }

            // ---- launch a saved account (Accounts page)
            int ai = Array.IndexOf(args, "--account");
            long accountId = 0;
            JoinTarget joinTarget = null;
            if (ai >= 0 && ai + 1 < args.Length && long.TryParse(args[ai + 1], out accountId))
            {
                string err;
                int pi2 = Array.IndexOf(args, "--place"), fi2 = Array.IndexOf(args, "--follow"), fid = Array.IndexOf(args, "--follow-id");
                if (fid >= 0 && fid + 1 < args.Length) joinTarget = new JoinTarget { Mode = "follow", FollowUserId = long.Parse(args[fid + 1]), FollowName = "user " + args[fid + 1] };
                else if (fi2 >= 0 && fi2 + 1 < args.Length) joinTarget = JoinTarget.Parse("follow", args[fi2 + 1], out err);
                else if (pi2 >= 0 && pi2 + 1 < args.Length) joinTarget = JoinTarget.Parse("place", args[pi2 + 1], out err);
                if (joinTarget != null) { int jbi = Array.IndexOf(args, "--job"); if (jbi >= 0 && jbi + 1 < args.Length && Regex.IsMatch(args[jbi + 1], "^[0-9a-fA-F-]{36}$")) joinTarget.JobId = args[jbi + 1]; }
                if (joinTarget == null) { VDialog.Info(null, Cute.Wont("launch that account", "a place ID or someone to follow"), "Pick one on the Accounts page."); return 1; }
                int di = Array.IndexOf(args, "--delay");
                int delay;
                if (di >= 0 && di + 1 < args.Length && int.TryParse(args[di + 1], out delay)) Thread.Sleep(Math.Min(60, delay) * 1000);
            }

            // ---- launch Roblox
            if (uri != null || args.Contains("--launch") || joinTarget != null)
            {
                ClaimPlayButton();
                LaunchResult result = null;
                Exception error = null;
                var pf = new ProgressForm();
                if (joinTarget != null)
                {
                    var acc = AccountStore.Get(accountId);
                    if (acc != null) pf.Subtitle = (acc.AltMode ? "alt  ·  " : "") + "@" + acc.Username;
                }
                pf.Shown += (o, e) =>
                {
                    var t = new Thread(() =>
                    {
                        try { result = joinTarget != null ? AccountLaunch.Run(accountId, joinTarget, pf.Report) : Launcher.Run(uri, pf.Report); }
                        catch (Exception ex) { error = ex; }
                        try { pf.BeginInvoke((Action)pf.FadeClose); } catch { }
                    });
                    t.IsBackground = true;
                    t.Start();
                };
                Application.Run(pf);

                if (error != null)
                {
                    Log.Write("Launch failed: " + error);
                    if (error is RobloxAuthException)
                        VDialog.Info(null, Cute.Wont("sign in", "a fresh login"), error.Message);
                    else
                        VDialog.Info(null, Cute.Wont("open Roblox", Cute.Reason(error)),
                            "Here's what went wrong, if you want the nerdy details:\n" + error.Message + "\n\nLog: " + Log.FilePath);
                    return 1;
                }
                if (result == null) return 1;

                if (!result.Alt) { try { OverlayLauncher.Start(Settings.Load()); } catch (Exception ex) { Log.Write("Overlay start failed: " + ex.Message); } }

                if (result.ReRegister)
                {
                    Thread.Sleep(8000);
                    try { Protocol.Register(); } catch (Exception ex) { Log.Write("Re-register failed: " + ex.Message); }
                }

                var held = new List<List<Mutex>> { result.SingletonMutexes };
                if (joinTarget != null)
                {
                    // account launch: watch for kicks/disconnects and rejoin (Auto-rejoin)
                    var acc0 = AccountStore.Get(accountId);
                    string label = acc0 != null ? acc0.Username : "account";
                    DateTime launchedAt = DateTime.UtcNow;
                    while (true)
                    {
                        if (result.Alt)
                        {
                            AltWindows.Register(result.Process.Id, label);
                            var r0 = result;
                            new Thread(() => AltMode.Watch(r0.Process)) { IsBackground = true }.Start();
                        }
                        string reason = RobloxLogs.WatchForKick(result.Process);
                        if (reason == null || !Settings.Load().AutoRejoin) break;
                        Log.Write("Auto-rejoin: " + label + " was disconnected (" + reason + "), rejoining");
                        try { result.Process.Kill(); } catch { }
                        // a picked server that kicks us straight away is probably gone or full: fall back to any server
                        if (joinTarget.JobId != null && (DateTime.UtcNow - launchedAt).TotalSeconds < 120) joinTarget.JobId = null;
                        LaunchResult again = null;
                        for (int tries = 0; tries < 6 && again == null; tries++)
                        {
                            Thread.Sleep(tries == 0 ? 3000 : 20000);
                            try { again = AccountLaunch.Run(accountId, joinTarget, (t, pc) => { }); }
                            catch (Exception ex) { Log.Write("Auto-rejoin attempt " + (tries + 1) + " failed: " + ex.Message); if (ex is RobloxAuthException) break; }
                        }
                        if (again == null) break;
                        held.Add(again.SingletonMutexes);
                        again.KeepAlive = true;
                        result = again;
                        launchedAt = DateTime.UtcNow;
                    }
                }
                else if (result.Alt) AltMode.Watch(result.Process);   // returns when the alt client closes

                if (result.KeepAlive)
                {
                    try { result.Process.WaitForExit(); } catch { }
                    while (Deployment.RobloxRunning()) Thread.Sleep(3000);
                    foreach (var m in held) MultiInstance.Release(m);
                }
                return 0;
            }

            // ---- screenshot of a page (testing): --render-page <page> <file.png> [--demo-accounts]
            if (args.Contains("--quiet-test")) { try { QuietRobloxTest.Run(); } catch (Exception ex) { Log.Write("quiet-test FAILED " + ex); } return 0; }
            int cti = Array.IndexOf(args, "--color-test");
            if (cti >= 0 && cti + 1 < args.Length) { foreach (var f in args.Skip(cti + 1)) { using (var im = Image.FromFile(f)) { Color ac; bool lt; ThemeColors.FromPicture(im, ThemeColors.Others(null, null), out ac, out lt); Log.Write("color-test: " + Path.GetFileName(f) + " -> " + Theme.Hex(ac) + (lt ? " light" : " dark") + " (closest clash: " + (ThemeColors.TooCloseTo(ac, ThemeColors.Others(null, null)) ?? "none") + ")"); } } return 0; }
            int spi = Array.IndexOf(args, "--render-splash");
            if (spi >= 0 && spi + 2 < args.Length)
            {
                var sp = new SplashForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false, TopMost = false, Size = new Size(1600, 900) };
                double wait = double.Parse(args[spi + 2], System.Globalization.CultureInfo.InvariantCulture);
                var stm = new Timer { Interval = (int)(wait * 1000) };
                stm.Tick += (o, e) => { stm.Stop(); using (var bmp = new Bitmap(sp.Width, sp.Height)) { sp.DrawToBitmap(bmp, new Rectangle(0, 0, sp.Width, sp.Height)); bmp.Save(args[spi + 1]); } sp.Close(); };
                sp.Shown += (o, e) => stm.Start();
                Application.Run(sp);
                return 0;
            }
            int rpi = Array.IndexOf(args, "--render-page");
            if (rpi >= 0 && rpi + 2 < args.Length)
            {
                AccountStore.Demo = args.Contains("--demo-accounts");
                DemoThemes = args.Contains("--demo-themes");
                int dfi = Array.IndexOf(args, "--flash"); if (dfi >= 0 && dfi + 1 < args.Length) DemoFlash = args[dfi + 1];
                int dsi = Array.IndexOf(args, "--search"); if (dsi >= 0 && dsi + 1 < args.Length) DemoSearch = args[dsi + 1];
                int dgi = Array.IndexOf(args, "--goto"); if (dgi >= 0 && dgi + 1 < args.Length) DemoGoTo = args[dgi + 1];
                int dai = Array.IndexOf(args, "--ask"); if (dai >= 0 && dai + 1 < args.Length) DemoAsk = args[dai + 1];
                var f = new MainForm(args[rpi + 1]) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
                var tm = new Timer { Interval = DemoAsk != null && NatsukiBrain.Installed ? 60000 + 25000 * DemoAsk.Split((char)124).Length : 1500 };   // give the brain time to answer
                tm.Tick += (o, e) =>
                {
                    tm.Stop();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        foreach (Control c in f.Controls.Cast<Control>().Reverse()) if ((c is SearchPopup || c is ChatPanel || c is AskButton) && c.Visible) c.DrawToBitmap(bmp, c.Bounds);   // DrawToBitmap gets overlay order wrong
                        bmp.Save(args[rpi + 2]);
                    }
                    f.Close();
                };
                f.Shown += (o, e) => tm.Start();
                Application.Run(f);
                return 0;
            }

            // ---- settings window
            if (Updater.HandOff(args)) return 0;   // install a waiting update, or open a newer installed CloveStrap
            JustUpdated = args.Contains("--after-update");
            BrowserLogin.CleanupStale();
            Paths.RemoveOldFolder();
            ClaimPlayButton();
            int pi = Array.IndexOf(args, "--page");
            Protocol.InstallSelf();          // keep the copy the Play button and Start with Windows use up to date
            Startup.Sync(Settings.Load());
            return TrayApp.Run(pi >= 0 && pi + 1 < args.Length ? args[pi + 1] : "Presets", args.Contains("--tray"));
        }
    }
}
