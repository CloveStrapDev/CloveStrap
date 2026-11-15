// Start with Windows + tray: one CloveStrap app process that waits in the tray. Opening CloveStrap again (Desktop icon,
// Start menu, double-clicking the tray icon, however many times) just brings the same window forward.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CloveStrap
{
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "CloveStrap";

        public static bool IsOn()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    Protocol.InstallSelf();
                    k.SetValue(Name, "\"" + Protocol.ExePath() + "\" --tray");
                }
                else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
            }
            Log.Write("Start with Windows " + (on ? "on" : "off"));
        }

        /// <summary>Keeps the Windows setting in line with ours (it's on by default).</summary>
        public static void Sync(Settings s)
        {
            // the old Voidstrapper start-up entry
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null && k.GetValue("Voidstrapper") != null) k.DeleteValue("Voidstrapper", false); } catch { }
            try
            {
                if (s.StartWithWindows)
                {
                    string want = "\"" + Protocol.ExePath() + "\" --tray";
                    string have;
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) have = k == null ? null : k.GetValue(Name) as string;
                    if (have != want) Set(true);
                }
                else if (IsOn()) Set(false);
            }
            catch (Exception ex) { Log.Write("Start with Windows sync failed: " + ex.Message); }
        }
    }

    class TrayApp : ApplicationContext
    {
        public const string MutexName = "Local\\CloveStrapApp", ShowEventName = "Local\\CloveStrapShow";

        readonly NotifyIcon icon;
        readonly Control marshal = new Control();
        readonly EventWaitHandle showEvent;
        readonly string startPage;
        MainForm form;
        bool exiting;

        public TrayApp(EventWaitHandle showEvent, string startPage, bool startHidden)
        {
            Current = this;
            this.showEvent = showEvent;
            this.startPage = startPage;
            marshal.CreateControl();
            var h = marshal.Handle;   // so BeginInvoke works before any window exists

            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add("Open CloveStrap", null, (o, e) => ShowWindow());
            menu.Items.Add("Launch Roblox", null, (o, e) => LaunchRoblox());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit CloveStrap", null, (o, e) => Quit());
            menu.Items[0].Font = new Font(menu.Font, FontStyle.Bold);

            icon = new NotifyIcon { Text = "CloveStrap", ContextMenuStrip = menu, Visible = true };
            try { icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { icon.Icon = SystemIcons.Application; }
            icon.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

            new Thread(() =>
            {
                while (!exiting)
                {
                    try { if (!showEvent.WaitOne(1000)) continue; } catch { return; }
                    try { marshal.BeginInvoke((Action)ShowWindow); } catch { }
                }
            }) { IsBackground = true, Name = "show-listener" }.Start();

            // keep the official Roblox install modded too (Roblox opened from its own shortcut or app runs from there)
            new Thread(() =>
            {
                string last = null;
                while (!exiting)
                {
                    try
                    {
                        string fp = Tweaks.Fingerprint();
                        if (fp != last)
                        {
                            Tweaks.ApplyToOfficial(Settings.Load());
                            last = Tweaks.Fingerprint();
                        }
                    }
                    catch (Exception ex) { Log.Write("Official install watcher: " + ex.Message); }
                    for (int i = 0; i < 50 && !exiting; i++) Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "official-watcher" }.Start();

            new Thread(() => AntiAfk.Run(() => exiting)) { IsBackground = true, Name = "anti-afk" }.Start();
            new Thread(() => AltWindows.Run(() => exiting)) { IsBackground = true, Name = "alt-windows" }.Start();
            new Thread(() => QuietRoblox.Run(() => exiting)) { IsBackground = true, Name = "quiet-roblox" }.Start();
            new Thread(() => DiscordPresence.Run(() => exiting)) { IsBackground = true, Name = "discord" }.Start();

            // CloveStrap updates itself from GitHub Releases
            Updater.Downloaded += v => { try { marshal.BeginInvoke((Action)(() => UpdateReady(v))); } catch { } };
            new Thread(() => Updater.Run(() => exiting)) { IsBackground = true, Name = "updater" }.Start();

            if (!startHidden) ShowWindow();
        }

        ToolStripItem updateItem;

        /// <summary>A new version is downloaded: say so, and offer to restart into it (it also installs itself next start).</summary>
        void UpdateReady(Version v)
        {
            if (exiting) return;
            string ver = v.Major + "." + v.Minor + "." + v.Build;
            if (updateItem == null)
            {
                updateItem = icon.ContextMenuStrip.Items.Add("Restart to update to v" + ver, null, (o, e) => RestartForUpdate());
                icon.ContextMenuStrip.Items.Insert(0, updateItem);
                updateItem.Font = new Font(icon.ContextMenuStrip.Font, FontStyle.Bold);
            }
            else updateItem.Text = "Restart to update to v" + ver;
            icon.BalloonTipClicked -= OnUpdateBalloon;
            icon.BalloonTipClicked += OnUpdateBalloon;
            icon.ShowBalloonTip(8000, "CloveStrap " + ver + " is ready", "Click here to restart and update. It also updates by itself the next time CloveStrap starts.", ToolTipIcon.Info);
            if (form != null && !form.IsDisposed) form.Flash("CloveStrap " + ver + " is ready: restart from the tray icon to update", Theme.Accent);
        }

        void OnUpdateBalloon(object sender, EventArgs e) { RestartForUpdate(); }

        void RestartForUpdate()
        {
            bool windowOpen = form != null && !form.IsDisposed && form.Visible;
            if (Updater.Apply(windowOpen ? "" : "--tray")) Quit();
        }

        /// <summary>Shows the one window (creating it if needed) and brings it to the front. Safe to call any number of times.</summary>
        bool splashDone, restarting;
        SplashForm splash;
        public static TrayApp Current;

        /// <summary>After signing out: close the window and go back through the splash and sign-in.</summary>
        public void RequireSignIn()
        {
            splashDone = false;
            restarting = true;
            if (form != null && !form.IsDisposed) form.Close();
            restarting = false;
            ShowWindow();
        }

        /// <summary>A quick, smooth fade-in for the window (it's small, so this is cheap).</summary>
        static void FadeIn(Form f)
        {
            var tm = new System.Windows.Forms.Timer { Interval = 15 };
            double o = f.Opacity;
            tm.Tick += (s, e) =>
            {
                if (f.IsDisposed) { tm.Dispose(); return; }
                o = Math.Min(1, o + 0.12);
                f.Opacity = o >= 1 ? 1.0 : o;
                if (o >= 1) { tm.Stop(); tm.Dispose(); }
            };
            tm.Start();
        }

        void Track(MainForm f)
        {
            f.FormClosed += (o, e) =>
            {
                if (form == f) form = null;
                if (!restarting && !Settings.Load().StartWithWindows && !exiting) Quit();   // tray only stays when Start with Windows is on
            };
        }

        void ShowWindow()
        {
            if (exiting) return;
            // the first time CloveStrap is opened in this session: full-screen splash that loads everything and signs you in
            if (!splashDone)
            {
                if (splash != null && !splash.IsDisposed) { splash.Activate(); return; }
                splash = new SplashForm(() => new MainForm(startPage));
                // handoff: draw the ready-built window (invisible) behind the splash, so it can fade straight in afterwards
                splash.Ready += () =>
                {
                    var sp = splash;
                    if (sp == null || sp.Window == null || (form != null && !form.IsDisposed)) return;
                    form = sp.Window;
                    Track(form);
                    form.ReloadSettings();
                    form.Opacity = 0;
                    form.Show();
                    form.Update();
                };
                splash.FormClosed += (o, e) =>
                {
                    var sp = splash;
                    splash = null;
                    if (!sp.Completed) { Quit(); return; }   // closed before signing in: no way into the app
                    splashDone = true;
                    if (form != null && !form.IsDisposed && form.Opacity < 1) FadeIn(form);
                    ShowWindow();
                };
                splash.Show();
                return;
            }
            if (form == null || form.IsDisposed)
            {
                form = new MainForm(startPage);
                Track(form);
                form.Show();
            }
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }

        void LaunchRoblox()
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--launch") { UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Tray launch failed: " + ex.Message); }
        }

        void Quit()
        {
            if (exiting) return;
            exiting = true;
            NatsukiBrain.Stop();
            if (form != null && !form.IsDisposed) form.Close();
            icon.Visible = false;
            icon.Dispose();
            ExitThread();
        }

        /// <summary>Settings window / tray entry point. Returns false if another copy is already running (it was asked to show itself).</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

        public static int Run(string page, bool tray)
        {
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                if (!created)
                {
                    if (!tray) { AllowSetForegroundWindow(-1); show.Set(); }   // already running: just bring its window forward
                    return 0;
                }
                Application.Run(new TrayApp(show, page, tray));
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
