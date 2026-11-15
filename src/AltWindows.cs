// Alt windows: remembers which Roblox windows are alt-mode accounts and, when docked, keeps them in a small column at the
// right edge of the screen with your main Roblox window fitted beside them, so they never sit on top of your game.
// (Fullscreen main: they tuck into the bottom-right corner instead, since the main can't be made narrower.)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CloveStrap
{
    static class AltWindows
    {
        static readonly string FilePath = Path.Combine(Paths.Base, "alts.txt");
        static readonly object Lock = new object();

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr h, string s);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        static readonly IntPtr TOP = IntPtr.Zero, TOPMOST = new IntPtr(-1), NOTOPMOST = new IntPtr(-2);
        const uint NOACTIVATE = 0x0010, NOMOVE = 0x0002, NOSIZE = 0x0001, NOZORDER = 0x0004;

        public static void Register(int pid, string name)
        {
            lock (Lock)
            {
                try
                {
                    var lines = Read().Where(l => l.Key != pid).Select(l => l.Key + "|" + l.Value).ToList();
                    lines.Add(pid + "|" + (name ?? "alt"));
                    File.WriteAllLines(FilePath, lines);
                }
                catch (Exception ex) { Log.Write("Alt register: " + ex.Message); }
            }
        }

        static List<KeyValuePair<int, string>> Read()
        {
            var list = new List<KeyValuePair<int, string>>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                    {
                        var p = l.Split('|');
                        int id;
                        if (p.Length >= 2 && int.TryParse(p[0], out id)) list.Add(new KeyValuePair<int, string>(id, p[1]));
                    }
            }
            catch { }
            return list;
        }

        /// <summary>Running alt Roblox processes: pid -> account name.</summary>
        public static Dictionary<int, string> Running()
        {
            var alive = new HashSet<int>(Process.GetProcessesByName("RobloxPlayerBeta").Select(p => p.Id));
            var d = new Dictionary<int, string>();
            foreach (var kv in Read()) if (alive.Contains(kv.Key)) d[kv.Key] = kv.Value;
            return d;
        }

        public static bool IsAlt(int pid) { return Running().ContainsKey(pid); }

        /// <summary>The Roblox window you're actually playing in: the foreground one if it's not an alt, else the newest non-alt.</summary>
        public static Process MainProcess(Dictionary<int, string> alts)
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta").Where(p => !alts.ContainsKey(p.Id)).ToList();
            uint fgPid;
            GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
            var fg = procs.FirstOrDefault(p => p.Id == fgPid);
            if (fg != null) return fg;
            return procs.Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                        .OrderByDescending(p => { try { return p.StartTime; } catch { return DateTime.MinValue; } }).FirstOrDefault();
        }

        static readonly Dictionary<IntPtr, Rectangle> placed = new Dictionary<IntPtr, Rectangle>();
        static readonly Dictionary<IntPtr, int> originalStyle = new Dictionary<IntPtr, int>();
        static readonly HashSet<int> minimisedOnce = new HashSet<int>();
        static readonly HashSet<IntPtr> unframed = new HashSet<IntPtr>();   // checked once each
        static bool wasTop, wasFront;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
        const uint FRAMECHANGED = 0x0020;

        public static void Run(Func<bool> stop)
        {
            while (!stop())
            {
                try { Tick(); } catch (Exception ex) { Log.Write("Alt windows: " + ex.Message); }
                for (int i = 0; i < 4 && !stop(); i++) Thread.Sleep(100);
            }
            try { RestoreFrames(); PipTags.Hide(); } catch { }
        }

        /// <summary>Picture-in-picture look: no title bar or border, just the game.</summary>
        static void MakePip(IntPtr h)
        {
            int st = GetWindowLong(h, GWL_STYLE);
            if ((st & (WS_CAPTION | WS_THICKFRAME)) == 0) return;
            if (!originalStyle.ContainsKey(h)) originalStyle[h] = st;
            SetWindowLong(h, GWL_STYLE, st & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU));
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | FRAMECHANGED);
        }

        /// <summary>Gives the alt windows their normal frame back (docking turned off, or CloveStrap closing).</summary>
        static void RestoreFrames()
        {
            foreach (var kv in originalStyle)
                if (IsWindow(kv.Key))
                {
                    SetWindowLong(kv.Key, GWL_STYLE, kv.Value);
                    SetWindowPos(kv.Key, NOTOPMOST, 0, 0, 0, 0, NOMOVE | NOSIZE | NOACTIVATE | FRAMECHANGED);
                }
            originalStyle.Clear();
        }

        static void Tick()
        {
            string mode = Settings.Load().AltWindows ?? "normal";
            if (mode == "dock") mode = "normal";   // docking was removed: windows that were docked get their frame back
            var alts = Running();
            if (alts.Count == 0) { placed.Clear(); originalStyle.Clear(); PipTags.Hide(); return; }

            var wins = new List<KeyValuePair<IntPtr, string>>();
            foreach (var kv in alts.OrderBy(a => a.Value))
            {
                try
                {
                    var p = Process.GetProcessById(kv.Key);
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    wins.Add(new KeyValuePair<IntPtr, string>(p.MainWindowHandle, kv.Value));
                    // name the window after the account, so you can tell them apart in the taskbar
                    var title = new StringBuilder(64);
                    GetWindowText(p.MainWindowHandle, title, 64);
                    if (title.ToString() == "Roblox") SetWindowText(p.MainWindowHandle, "Roblox  ·  @" + kv.Value);
                    if (mode == "minimized" && minimisedOnce.Add(kv.Key)) ShowWindow(p.MainWindowHandle, 7);   // SW_SHOWMINNOACTIVE
                }
                catch { }
            }
            if (mode != "dock" || wins.Count == 0)
            {
                if (originalStyle.Count > 0) RestoreFrames();
                // alts docked by an older CloveStrap: give them their title bar back and take them off always-on-top
                foreach (var w in wins)
                {
                    if (unframed.Contains(w.Key)) continue;
                    int st = GetWindowLong(w.Key, GWL_STYLE);
                    if ((st & WS_CAPTION) == 0)
                    {
                        SetWindowLong(w.Key, GWL_STYLE, st | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
                        SetWindowPos(w.Key, NOTOPMOST, 0, 0, 0, 0, NOMOVE | NOSIZE | NOACTIVATE | FRAMECHANGED);
                    }
                    unframed.Add(w.Key);
                }
                PipTags.Hide();
                return;
            }

            var main = MainProcess(alts);
            if (main == null) { PipTags.Hide(); return; }
            IntPtr mh;
            try { mh = main.MainWindowHandle; } catch { return; }
            if (mh == IntPtr.Zero || IsIconic(mh)) { PipTags.Hide(); return; }
            RECT mr; if (!GetWindowRect(mh, out mr)) return;
            var mainRect = Rectangle.FromLTRB(mr.L, mr.T, mr.R, mr.B);
            var screen = Screen.FromRectangle(mainRect);
            Rectangle wa = screen.WorkingArea, scr = screen.Bounds;

            uint fgPid; GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
            bool robloxFront = fgPid == (uint)main.Id || alts.ContainsKey((int)fgPid);
            bool fullscreen = mr.L <= scr.Left && mr.T <= scr.Top && mr.R >= scr.Right && mr.B >= scr.Bottom;
            bool dragging = (GetAsyncKeyState(0x01) & 0x8000) != 0;   // don't fight you while you're moving a window
            const int gap = 10, margin = 12;
            var tags = new List<KeyValuePair<Rectangle, string>>();

            if (!fullscreen)
            {
                // little picture-in-picture tiles stacked at the right edge of the screen...
                int w = Math.Max(256, Math.Min(384, wa.Width / 6)), h = w * 9 / 16;
                int perCol = Math.Max(1, (wa.Height - 2 * margin + gap) / (h + gap));
                int cols = (wins.Count + perCol - 1) / perCol;
                int colLeft = wa.Right - margin - cols * w - (cols - 1) * gap;

                // ...and the main window fitted beside them, never underneath
                int room = colLeft - margin - wa.Left;
                if (!dragging && mainRect.Right > colLeft - margin && room >= 480)
                {
                    bool max = IsZoomed(mh);
                    if (max) ShowWindow(mh, 4);   // un-maximise without stealing focus
                    int nw = Math.Min(max ? room : mainRect.Width, room);
                    int nh = max ? wa.Height : Math.Min(mainRect.Height, wa.Height);
                    int nx = max ? wa.Left : Math.Max(wa.Left, colLeft - margin - nw);
                    int ny = max ? wa.Top : Math.Max(wa.Top, Math.Min(mainRect.Top, wa.Bottom - nh));
                    SetWindowPos(mh, IntPtr.Zero, nx, ny, nw, nh, NOZORDER | NOACTIVATE);
                }

                bool raise = robloxFront && !wasFront;   // you just clicked into Roblox: lift the tiles above other apps (not always-on-top)
                bool clearTop = wasTop;
                for (int i = 0; i < wins.Count; i++)
                {
                    int col = i / perCol, row = i % perCol;
                    var want = new Rectangle(wa.Right - margin - (col + 1) * w - col * gap, wa.Top + margin + row * (h + gap), w, h);
                    Place(wins[i].Key, want, first => first || clearTop ? NOTOPMOST : raise ? TOP : (IntPtr?)null, dragging);
                    tags.Add(new KeyValuePair<Rectangle, string>(want, wins[i].Value));
                }
                wasTop = false;
            }
            else
            {
                // fullscreen main can't be made narrower: tiles float in the bottom-right corner while you're in Roblox
                int w = Math.Max(224, Math.Min(320, scr.Width / 8)), h = w * 9 / 16;
                int perCol = Math.Max(1, (scr.Height / 2) / (h + gap));
                bool changed = robloxFront != wasTop, front = robloxFront;
                for (int i = 0; i < wins.Count; i++)
                {
                    int col = i / perCol, row = i % perCol;
                    var want = new Rectangle(scr.Right - margin - (col + 1) * w - col * gap, scr.Bottom - margin - (row + 1) * h - row * gap, w, h);
                    Place(wins[i].Key, want, first => first || changed ? (front ? TOPMOST : NOTOPMOST) : (IntPtr?)null, false);
                    tags.Add(new KeyValuePair<Rectangle, string>(want, wins[i].Value));
                }
                wasTop = robloxFront;
            }
            wasFront = robloxFront;
            if (robloxFront) PipTags.Show(tags); else PipTags.Hide();   // name tags only while you're in Roblox
        }

        static void Place(IntPtr ah, Rectangle want, Func<bool, IntPtr?> zorder, bool dragging)
        {
            if (IsIconic(ah)) ShowWindow(ah, 4);   // SW_SHOWNOACTIVATE
            MakePip(ah);
            RECT cur; GetWindowRect(ah, out cur);
            bool moved = cur.L != want.X || cur.T != want.Y || cur.R - cur.L != want.Width || cur.B - cur.T != want.Height;
            var z = zorder(!placed.ContainsKey(ah));
            if (z.HasValue) SetWindowPos(ah, z.Value, want.X, want.Y, want.Width, want.Height, NOACTIVATE);
            else if (moved && !dragging) SetWindowPos(ah, IntPtr.Zero, want.X, want.Y, want.Width, want.Height, NOZORDER | NOACTIVATE);
            placed[ah] = want;
        }
    }

    /// <summary>The small "@name" pills on the picture-in-picture tiles. Click-through, never take focus.</summary>
    static class PipTags
    {
        static Control ui;
        static readonly List<Tag> tags = new List<Tag>();
        static string lastKey = "";
        static readonly Font TagFont = new Font("Segoe UI Semibold", 8.5f);

        class Tag : Form
        {
            public string Label = "";
            public Tag()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.FromArgb(20, 16, 24);
                Opacity = 0.88;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000;   // NOACTIVATE | TOOLWINDOW | TRANSPARENT | LAYERED
                    return cp;
                }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, 8, Height / 2 - 3, 6, 6);
                TextRenderer.DrawText(g, Label, TagFont, new Rectangle(18, 0, Width - 22, Height), Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        static void Ensure()
        {
            if (ui != null) return;
            var ready = new ManualResetEvent(false);
            var t = new Thread(() =>
            {
                var c = new Control();
                c.CreateControl();
                var h = c.Handle;
                ui = c;
                ready.Set();
                Application.Run();
            }) { IsBackground = true, Name = "pip-tags" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            ready.WaitOne(3000);
        }

        public static void Show(List<KeyValuePair<Rectangle, string>> where)
        {
            string key = string.Join(";", where.Select(w => w.Key + w.Value));
            if (key == lastKey) return;
            lastKey = key;
            Ensure();
            if (ui == null) return;
            ui.BeginInvoke((Action)(() =>
            {
                while (tags.Count < where.Count) tags.Add(new Tag());
                for (int i = 0; i < tags.Count; i++)
                {
                    var tg = tags[i];
                    if (i >= where.Count) { tg.Hide(); continue; }
                    var r = where[i].Key;
                    tg.Label = "@" + where[i].Value;
                    int tw = TextRenderer.MeasureText(tg.Label, TagFont).Width + 30;
                    tg.Bounds = new Rectangle(r.X + 8, r.Bottom - 30, Math.Min(tw, r.Width - 16), 22);
                    using (var path = Rounded(new Rectangle(0, 0, tg.Width, tg.Height), 11)) tg.Region = new Region(path);
                    if (!tg.Visible) tg.Show();
                    tg.Invalidate();
                }
            }));
        }

        public static void Hide()
        {
            if (lastKey == "" || ui == null) return;
            lastKey = "";
            ui.BeginInvoke((Action)(() => { foreach (var tg in tags) tg.Hide(); }));
        }

        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
