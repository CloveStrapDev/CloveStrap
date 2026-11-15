// CloveStrap Mods page (skybox, sounds, cursor)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CloveStrap
{
    // plays a sound file through Windows (mp3, wav, and ogg where Windows has the codec)
    static class SoundPreview
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);

        public static bool Play(string file)
        {
            mciSendString("close vsprev", null, 0, IntPtr.Zero);
            if (file == null || !File.Exists(file)) return false;
            if (mciSendString("open \"" + file + "\" type mpegvideo alias vsprev", null, 0, IntPtr.Zero) != 0) return false;
            return mciSendString("play vsprev", null, 0, IntPtr.Zero) == 0;
        }

        public static void Stop() { mciSendString("close vsprev", null, 0, IntPtr.Zero); }
    }

    class SkyPreviewBox : VControl
    {
        static Image cached; static DateTime cachedTime;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 9))
            {
                Image img = null;
                string f = ModKit.SkyPreviewFile;
                if (ModKit.HasSky && File.Exists(f))
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (cached == null || t != cachedTime)
                    {
                        if (cached != null) cached.Dispose();
                        using (var fs = File.OpenRead(f)) using (var tmp = Image.FromStream(fs)) cached = new Bitmap(tmp);
                        cachedTime = t;
                    }
                    img = cached;
                }
                if (img == null) img = ModKit.DefaultSkyPreview();   // Roblox's real sky
                g.SetClip(path);
                if (img != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float sc = Math.Max(Width / (float)img.Width, Height / (float)img.Height);
                    g.DrawImage(img, (Width - img.Width * sc) / 2, (Height - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                }
                else
                {
                    using (var br = new LinearGradientBrush(r, Color.FromArgb(120, 170, 225), Color.FromArgb(196, 226, 250), LinearGradientMode.Vertical)) g.FillRectangle(br, r);
                    using (var cloud = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    {
                        g.FillEllipse(cloud, Width * 0.18f, Height * 0.30f, Width * 0.22f, Height * 0.14f);
                        g.FillEllipse(cloud, Width * 0.28f, Height * 0.24f, Width * 0.18f, Height * 0.16f);
                        g.FillEllipse(cloud, Width * 0.62f, Height * 0.46f, Width * 0.20f, Height * 0.11f);
                    }
                    using (var ground = new SolidBrush(Color.FromArgb(96, 140, 86))) g.FillRectangle(ground, 0, Height * 0.82f, Width, Height);
                }
                // horizon line of "ground" for a sense of scale
                using (var shade = new LinearGradientBrush(new RectangleF(0, Height - 40, Width, 40), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(90, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(shade, 0, Height - 40, Width, 40);
                g.ResetClip();
            }
            Theme.Stroke(g, r, 9, Theme.Border);
            string tag = ModKit.HasSky ? "CUSTOM" : "ROBLOX DEFAULT";
            Size ts = Theme.Measure(tag, Theme.Mono);
            var pill = new RectangleF(10, Height - 28, ts.Width + 16, 18);
            Theme.Fill(g, pill, 9, Color.FromArgb(150, 0, 0, 0));
            Theme.Draw(g, tag, Theme.Mono, Rectangle.Round(pill), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    class SkyTile : VControl
    {
        public readonly ModKit.SkyPreset Preset;   // null = Roblox default
        public bool Active;

        public SkyTile(ModKit.SkyPreset p) { Preset = p; Size = new Size(110, 58); Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 8))
            {
                g.SetClip(path);
                var real = Preset == null ? ModKit.DefaultSkyPreview() : null;
                if (real != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float sc = Math.Max(Width / (float)real.Width, Height / (float)real.Height);
                    g.DrawImage(real, (Width - real.Width * sc) / 2, (Height - real.Height * sc) / 2, real.Width * sc, real.Height * sc);
                }
                else
                    for (int y = 0; y < Height; y++)
                    {
                        float elev = 55 - y / (float)Height * 75;   // top of tile looks up, bottom looks a bit down
                        Color c = Preset != null ? ModKit.PresetColor(Preset, elev) : Color.FromArgb((int)(120 + y * 1.2f), (int)(170 + y * 0.9f), 235);
                        using (var pen = new Pen(c)) g.DrawLine(pen, 0, y, Width, y);
                    }
                if (Preset != null && Preset.Stars > 0)
                {
                    var rnd = new Random(Preset.Id.GetHashCode());
                    using (var b = new SolidBrush(Color.FromArgb(200, Preset.StarTint)))
                        for (int i = 0; i < 14; i++) g.FillEllipse(b, rnd.Next(Width), rnd.Next(Height / 2), 1.6f, 1.6f);
                }
                using (var shade = new LinearGradientBrush(new RectangleF(0, Height - 24, Width, 24), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(shade, 0, Height - 24, Width, 24);
                g.ResetClip();
            }
            Theme.Stroke(g, r, 8, Active ? Theme.Accent : Hover ? Theme.BorderHot : Theme.Border, Active ? 2f : 1f);
            Theme.Draw(g, Preset != null ? Preset.Name : "Default", Theme.Small, new Rectangle(8, Height - 20, Width - 16, 16), Color.White, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    class CursorTile : VControl
    {
        public readonly string Style;   // null = Roblox default, "custom" = the user's picture
        public bool Active;
        Image img;

        public CursorTile(string style, Image img) { Style = style; this.img = img; Size = new Size(76, 76); Cursor = Cursors.Hand; }

        protected override void Dispose(bool disposing) { if (disposing && img != null) img.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 9, Hover ? Theme.InputHot : Theme.Input);
            // checkerboard so transparent cursors read clearly
            using (var path = Theme.Round(new RectangleF(6, 6, Width - 12, Height - 26), 6))
            {
                g.SetClip(path);
                using (var a = new SolidBrush(Color.FromArgb(118, 160, 205))) g.FillRectangle(a, 0, 0, Width, Height);
                using (var b = new SolidBrush(Color.FromArgb(104, 146, 192)))
                    for (int y = 0; y < Height; y += 8) for (int x = (y / 8 % 2) * 8; x < Width; x += 16) g.FillRectangle(b, x, y, 8, 8);
                if (img != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float s = Math.Min(44f / img.Width, 44f / img.Height);
                    g.DrawImage(img, (Width - img.Width * s) / 2, 6 + (Height - 26 - img.Height * s) / 2, img.Width * s, img.Height * s);
                }
                g.ResetClip();
            }
            Theme.Stroke(g, r, 9, Active ? Theme.Accent : Hover ? Theme.BorderHot : Theme.Border, Active ? 2f : 1f);
            string label = Style == null ? "Default" : Style == "custom" ? "Yours" : char.ToUpper(Style[0]) + Style.Substring(1);
            Theme.Draw(g, label, Theme.Small, new Rectangle(0, Height - 19, Width, 16), Active ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }

    class SoundRow : VControl
    {
        public readonly ModKit.SoundSlot Slot;
        public readonly IconButton PlayB, ResetB;
        public readonly VButton ChooseB;

        public SoundRow(ModKit.SoundSlot slot, int width)
        {
            Slot = slot;
            Size = new Size(width, 54);
            BackColor = Theme.Card;
            bool custom = ModKit.IsCustom(slot);
            ResetB = new IconButton("") { Size = new Size(34, 34), BackColor = Theme.Row, Visible = custom };
            ResetB.Location = new Point(Width - 10 - 34, 10);
            ChooseB = new VButton(custom ? "Change" : "Choose") { BackColor = Theme.Row };
            ChooseB.Location = new Point((custom ? ResetB.Left - 4 : Width - 10) - ChooseB.Width, 10);
            PlayB = new IconButton("") { Size = new Size(34, 34), BackColor = Theme.Row };
            PlayB.Location = new Point(ChooseB.Left - 4 - 34, 10);
            Controls.AddRange(new Control[] { PlayB, ChooseB, ResetB });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            bool custom = ModKit.IsCustom(Slot);
            Theme.Fill(g, r, 9, Theme.Row);
            Theme.Stroke(g, r, 9, custom ? Theme.Mix(Theme.Border, Theme.Accent, 0.45f) : Theme.Border);
            int tw = PlayB.Left - 26;
            Theme.Draw(g, Slot.Label, Theme.RowTitle, new Rectangle(14, 9, tw, 18), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            string status = custom ? (ModKit.GetName("sound_" + Slot.Id) ?? "Custom sound") : Slot.Desc + "  ·  default";
            Theme.Draw(g, status, Theme.Small, new Rectangle(14, 29, tw, 16), custom ? Theme.Accent : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    partial class MainForm
    {
        bool modBusy;
        string mixTop, mixHorizon;
        bool mixStars;
        static readonly string[] SkyPalette = { "#0B1026", "#1E3A8A", "#2B6CB0", "#63B3ED", "#BEE3F8", "#6B46C1", "#D53F8C", "#FF6F91", "#FF9A4D", "#F6E05E", "#FFFFFF" };

        void BuildMods()
        {
            var p = NewPage("Mods");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int cw = pageW - SystemInformation.VerticalScrollBarWidth - 6, y = 4;

            // ---------------------------------------------------- skybox
            var sky = new Card(cw, "Skybox", "");
            var sp = new Panel { Size = new Size(sky.InnerWidth, 244), BackColor = Theme.Row };
            var prev = new SkyPreviewBox { Location = new Point(0, 0), Size = new Size(316, 172), BackColor = Theme.Row };
            int tx = prev.Right + 18, tw = sp.Width - tx;
            string skyName = ModKit.HasSky ? (ModKit.GetName("sky") ?? "Custom sky") : "Roblox's default sky";
            var title = new Label { Text = skyName, Font = Theme.H3, ForeColor = Theme.Text, BackColor = Theme.Row, Location = new Point(tx - 2, 2), AutoSize = true, MaximumSize = new Size(tw, 0), UseMnemonic = false };
            var hint = new Label
            {
                Text = "Pick a sky below, or mix your own colors. Shows in games that use Roblox's default sky.",
                Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Row, Location = new Point(tx - 1, 30), MaximumSize = new Size(tw, 0), AutoSize = true, UseMnemonic = false
            };
            sp.Controls.AddRange(new Control[] { prev, title, hint });

            // colour mixer
            if (mixTop == null) { Color t0, h0; bool s0; ModKit.GetMix(out t0, out h0, out s0); mixTop = Theme.Hex(t0); mixHorizon = Theme.Hex(h0); mixStars = s0; }
            int lw = 64;
            var topRow = new ColorRow(SkyPalette) { BackColor = Theme.Row, Location = new Point(tx + lw, 68), Selected = mixTop };
            var horRow = new ColorRow(SkyPalette) { BackColor = Theme.Row, Location = new Point(tx + lw, 100), Selected = mixHorizon };
            topRow.Picked += hex => { mixTop = hex; topRow.Selected = hex; topRow.Invalidate(); };
            horRow.Picked += hex => { mixHorizon = hex; horRow.Selected = hex; horRow.Invalidate(); };
            var lTop = new Label { Text = "Top", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Row, Location = new Point(tx - 1, 73), AutoSize = true, UseMnemonic = false };
            var lHor = new Label { Text = "Horizon", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Row, Location = new Point(tx - 1, 105), AutoSize = true, UseMnemonic = false };
            var stars = new Toggle { Checked = mixStars, BackColor = Theme.Row, Location = new Point(tx + lw, 142) };
            stars.CheckedChanged += (o, e) => mixStars = stars.Checked;
            var lStars = new Label { Text = "Stars", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Row, Location = new Point(tx - 1, 144), AutoSize = true, UseMnemonic = false };
            var make = new VButton("Make sky", "", true) { BackColor = Theme.Row };
            make.Location = new Point(tx + lw + topRow.Width - make.Width, 136);
            make.Click += (o, e) =>
            {
                Color t1 = Theme.ParseHex(mixTop) ?? Color.SteelBlue, h1 = Theme.ParseHex(mixHorizon) ?? Color.Orange;
                bool s1 = mixStars;
                RunModJob("Painting your sky", () => ModKit.SetSkyMix(t1, h1, s1), "Skybox: your mix");
            };
            sp.Controls.AddRange(new Control[] { lTop, topRow, lHor, horRow, lStars, stars, make });
            var presets = new List<ModKit.SkyPreset> { null };
            presets.AddRange(ModKit.SkyPresets);
            int gap = 8, tile = (sp.Width - gap * (presets.Count - 1)) / presets.Count;
            string current = ModKit.GetName("sky");
            for (int i = 0; i < presets.Count; i++)
            {
                var pr = presets[i];
                var t = new SkyTile(pr) { Location = new Point(i * (tile + gap), 186), Width = tile, BackColor = Theme.Row };
                t.Active = pr == null ? !ModKit.HasSky : ModKit.HasSky && current == pr.Name;
                t.Click += (o, e) =>
                {
                    if (pr == null) { ModKit.ResetSky(); ModsChanged("Default sky is back"); return; }
                    RunModJob("Painting the " + pr.Name + " sky", () => ModKit.SetSkyPreset(pr), "Skybox: " + pr.Name);
                };
                sp.Controls.Add(t);
            }
            sky.AddRaw(sp, true);
            Place(p, sky, ref y);

            // ---------------------------------------------------- sounds
            var snd = new Card(cw, "Sounds", "");
            int colW = (snd.InnerWidth - 10) / 2, rows = (ModKit.Sounds.Length + 1) / 2;
            var grid = new Panel { Size = new Size(snd.InnerWidth, rows * 54 + (rows - 1) * 8), BackColor = Theme.Card };
            for (int i = 0; i < ModKit.Sounds.Length; i++)
            {
                var slot = ModKit.Sounds[i];
                var row = new SoundRow(slot, colW) { Location = new Point((i % 2) * (colW + 10), (i / 2) * 62) };
                row.PlayB.Click += (o, e) =>
                {
                    string sf = ModKit.SoundFile(slot);
                    if (!SoundPreview.Play(sf))
                        Flash(sf != null && sf.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                              ? "Windows can't play .ogg previews >_< Roblox plays it fine in game"
                              : "Windows can't preview this one >_< Roblox still uses it", Theme.Warn);
                };
                row.ChooseB.Click += (o, e) => PickSound(slot);
                row.ResetB.Click += (o, e) => { SoundPreview.Stop(); ModKit.ResetSound(slot); ModsChanged(slot.Label + " sound reset"); };
                grid.Controls.Add(row);
            }
            snd.AddFull("Character sounds", "mp3, ogg or wav. ▶ plays the current one.", grid);
            Place(p, snd, ref y);

            // ---------------------------------------------------- cursor
            var cur = new Card(cw, "Mouse cursor", "");
            var cp = new Panel { Size = new Size(cur.InnerWidth, 76), BackColor = Theme.Row };
            string curName = ModKit.GetName("cursor");
            int cx = 0;
            Action<string, Image> addTile = (style, img) =>
            {
                var t = new CursorTile(style, img) { Location = new Point(cx, 0), BackColor = Theme.Row };
                t.Active = style == null ? !ModKit.HasCursor : ModKit.HasCursor && (style == "custom" ? !ModKit.CursorPresets.Contains(curName) : curName == style);
                t.Click += (o, e) =>
                {
                    if (style == null) { ModKit.ResetCursor(); ModsChanged("Default cursor is back"); }
                    else if (style == "custom") PickCursor();
                    else { using (var b = ModKit.CursorPreset(style, Theme.Accent)) ModKit.SetCursor(b, style); ModsChanged("Cursor: " + style); }
                };
                cp.Controls.Add(t);
                cx += t.Width + 8;
            };
            Image def = null;
            try { string f = ModKit.OriginalOf(@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png"); if (f != null) using (var fs = File.OpenRead(f)) using (var tmp = Image.FromStream(fs)) def = new Bitmap(tmp); } catch { }
            addTile(null, def);
            foreach (var st in ModKit.CursorPresets) addTile(st, ModKit.CursorPreset(st, Theme.Accent));
            Image mine = null;
            if (ModKit.HasCursor && !ModKit.CursorPresets.Contains(curName))
                try { using (var fs = File.OpenRead(ModKit.CursorFile)) using (var tmp = Image.FromStream(fs)) mine = new Bitmap(tmp); } catch { }
            addTile("custom", mine);
            var cpick = new VButton("Use picture", "", true) { BackColor = Theme.Row };
            cpick.Location = new Point(cp.Width - cpick.Width, (cp.Height - cpick.Height) / 2);
            cpick.Click += (o, e) => PickCursor();
            cp.Controls.Add(cpick);
            cur.AddRaw(cp, true);
            Place(p, cur, ref y);

            var note = MakeLabel("Mods apply when Roblox starts. Reset puts the original back.", Theme.Small, Theme.Dim, 2, y - 2);
            p.Controls.Add(note);
            p.Controls.Add(new Panel { Location = new Point(0, y + 20), Size = new Size(1, 8), BackColor = Theme.Bg });
        }

        void RebuildMods()
        {
            Panel old;
            Point scroll = Point.Empty;
            if (pages.TryGetValue("Mods", out old))
            {
                scroll = new Point(-old.AutoScrollPosition.X, -old.AutoScrollPosition.Y);
                pages.Remove("Mods");
                right.Controls.Remove(old);
                old.Dispose();
            }
            SuspendLayout();
            BuildMods();
            ShowPage(currentPage ?? "Mods");
            ResumeLayout();
            var np = pages["Mods"];
            np.AutoScrollPosition = scroll;
        }

        void ModsChanged(string msg)
        {
            if (!S.ApplyMods)
            {
                S.ApplyMods = true;
                loading = true; if (tMods != null) tMods.Checked = true; loading = false;
            }
            if (saveTimer.Enabled) saveTimer.Stop();
            SaveNow(false);
            RebuildMods();
            Flash(msg + (Deployment.RobloxRunning() ? ". Applies next time Roblox starts" : ". Ready for your next game"), Theme.Accent);
        }

        void RunModJob(string busyText, Action job, string doneText)
        {
            if (modBusy) return;
            modBusy = true;
            pill.Set(busyText + "...", Theme.Accent);
            flashTimer.Stop();
            new Thread(() =>
            {
                Exception err = null;
                try { job(); } catch (Exception ex) { err = ex; Log.Write("Mod failed: " + ex); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        modBusy = false;
                        if (err != null) Flash(Cute.Wont("make that", Cute.Reason(err)), Theme.Bad);
                        else ModsChanged(doneText);
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void PickSound(ModKit.SoundSlot slot)
        {
            using (var d = new OpenFileDialog { Title = "Pick a sound for " + slot.Label.ToLowerInvariant(), Filter = "Sounds|*.mp3;*.ogg;*.wav;*.flac|All files|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { SoundPreview.Stop(); ModKit.SetSound(slot, d.FileName); }
                catch (Exception ex) { Flash(Cute.Wont("use that sound", Cute.Reason(ex)), Theme.Bad); return; }
                ModsChanged(slot.Label + ": " + Path.GetFileName(d.FileName));
            }
        }

        void PickCursor()
        {
            using (var d = new OpenFileDialog { Title = "Pick a picture for your cursor", Filter = "Pictures|*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { using (var img = ModKit.LoadPicture(d.FileName)) ModKit.SetCursor(img, Path.GetFileName(d.FileName)); }
                catch (Exception ex) { Log.Write("Cursor: " + ex); Flash(Cute.Wont("use that", "a picture Windows can open"), Theme.Bad); return; }
                ModsChanged("Cursor: " + Path.GetFileName(d.FileName));
            }
        }
    }
}
