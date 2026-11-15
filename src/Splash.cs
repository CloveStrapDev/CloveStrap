// Full-screen splash when CloveStrap opens: Clove on the right, the logo and "Loading" in the middle,
// Roblox fun facts along the bottom. The first time (once a Discord app ID is set up) it also asks you to sign in
// with Discord, which reads your name and picture from the Discord app on this PC.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CloveStrap
{
    static class DiscordAccount
    {
        // The CloveStrap Discord application. Set once by the developer; everyone who uses CloveStrap shares it.
        public const string BuiltInAppId = "1571498679788114010";   // the CloveStrap app on Discord

        public static string AppId(Settings s)
        {
            string own = new string((s.DiscordAppId ?? "").Where(char.IsDigit).ToArray());
            return own.Length >= 15 ? own : BuiltInAppId;
        }

        public static bool Available(Settings s) { return AppId(s).Length >= 15; }
        public static bool SignedIn(Settings s) { return !string.IsNullOrEmpty(Current.UserId); }

        // who's signed in lives in its own file, separate from the settings, so you only ever sign in once
        public class Identity { public string UserId { get; set; } public string Name { get; set; } public string Avatar { get; set; } }
        static string IdentityFile { get { return Path.Combine(Paths.Base, "Discord.json"); } }
        static Identity current;

        public static Identity Current
        {
            get
            {
                if (current != null) return current;
                try
                {
                    if (File.Exists(IdentityFile))
                        current = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Identity>(File.ReadAllText(IdentityFile));
                }
                catch { }
                return current ?? (current = new Identity());
            }
        }

        static void SaveIdentity(Identity id)
        {
            current = id;
            if (string.IsNullOrEmpty(id.UserId)) { try { File.Delete(IdentityFile); } catch { } return; }
            File.WriteAllText(IdentityFile, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(id));
        }

        static string AvatarFile { get { return Path.Combine(Paths.Base, "discord_avatar.png"); } }

        /// <summary>What CloveStrap asks Discord for: your name and avatar, the servers you're in, and joining servers.</summary>
        public static readonly string[] Scopes = { "identify", "guilds", "guilds.join" };

        /// <summary>
        /// Signs in through Discord's own Authorize popup (in the Discord app). Nothing is saved unless you click Authorize.
        /// Returns null on success, or a reason it didn't work.
        /// </summary>
        public static string SignIn()
        {
            var s = Settings.Load();
            if (!Available(s)) return "CloveStrap's Discord app isn't set up yet";
            // the Discord status lets go of its connection while we ask (Discord may ignore a second one)
            DiscordPresence.Paused = true;
            for (int i = 0; i < 30 && !DiscordPresence.Idle; i++) Thread.Sleep(100);
            var rpc = new DiscordRpc(AppId(s));
            try
            {
                if (!rpc.Connect())
                    return rpc.LastError == null ? "Discord isn't open"
                         : rpc.LastError.Contains("4000") ? "the Discord app ID is wrong"
                         : rpc.LastError == "Discord didn't answer" ? "Discord didn't answer, try again" : "Discord isn't open";
                if (string.IsNullOrEmpty(rpc.UserId)) return "Discord didn't say who you are";
                string verifier, challenge, code;
                DiscordOAuth.NewPkce(out verifier, out challenge);
                string no = rpc.Authorize(Scopes, 180000, challenge, out code);
                if (no != null && no.ToLowerInvariant().Contains("code_challenge"))
                    no = rpc.Authorize(Scopes, 180000, null, out code);   // this Discord doesn't take a check code: plain authorize
                if (no != null)
                {
                    Log.Write("Discord sign-in not approved: " + no);
                    string low = no.ToLowerInvariant();
                    return low.Contains("denied") || low.Contains("cancel") || low.Contains("closed") ? "You didn't allow CloveStrap. Try again when you're ready"
                         : low == "timed out" ? "No answer from the Discord popup. Try again"
                         : "Discord said: " + no;
                }
                SaveIdentity(new Identity { UserId = rpc.UserId, Name = rpc.UserName, Avatar = rpc.AvatarHash });
                DownloadAvatar(Current);
                Log.Write("Signed in with Discord as " + rpc.UserName);
                // the token behind "servers you're in" / "join servers": never blocks signing in if it doesn't work
                if (!string.IsNullOrEmpty(code) && DiscordOAuth.Exchange(AppId(s), code, verifier) == null)
                {
                    var servers = DiscordOAuth.Servers();
                    if (servers != null) Log.Write("Discord: can see " + servers.Count + " servers");
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { rpc.Dispose(); DiscordPresence.Paused = false; }
        }

        public static void SignOut()
        {
            SaveIdentity(new Identity());
            DiscordOAuth.Forget();
            try { File.Delete(AvatarFile); } catch { }
        }

        static void DownloadAvatar(Identity s)
        {
            try
            {
                string url;
                if (!string.IsNullOrEmpty(s.Avatar)) url = "https://cdn.discordapp.com/avatars/" + s.UserId + "/" + s.Avatar + ".png?size=128";
                else { ulong id; ulong.TryParse(s.UserId, out id); url = "https://cdn.discordapp.com/embed/avatars/" + ((id >> 22) % 6) + ".png"; }
                using (var wc = new WebClient()) File.WriteAllBytes(AvatarFile, wc.DownloadData(url));
            }
            catch (Exception ex) { Log.Write("Discord avatar: " + ex.Message); }
        }

        public static Image Avatar()
        {
            try { if (File.Exists(AvatarFile)) using (var fs = File.OpenRead(AvatarFile)) using (var t = Image.FromStream(fs)) return new Bitmap(t); }
            catch { }
            return null;
        }
    }

    class SplashForm : Form
    {
        // the character on the splash screen
        const string MascotId = "natsuki";

        static readonly string[] Facts =
        {
            "Roblox was first called DynaBlocks before it was renamed in 2004.",
            "Roblox was founded by David Baszucki and Erik Cassel.",
            "The account with ID 1 is Roblox itself. Builderman's is 156.",
            "Before Robux took over, Roblox also had Tix. They were removed in 2016.",
            "Roblox games are scripted in Luau, a fast language that grew out of Lua 5.1.",
            "R15 avatars, with 15 body parts, arrived in 2016. R6 has just 6.",
            "Roblox Premium replaced Builders Club in 2019.",
            "Creators can turn earned Robux into real money through the Developer Exchange.",
            "The classic \"oof\" death sound was replaced in 2022.",
            "Roblox became a public company on the New York Stock Exchange in 2021.",
            "Roblox Studio, the tool every Roblox game is made in, is free.",
            "The classic Roblox look: yellow head, blue torso, green legs."
        };

        enum Phase { Loading, SignIn, LoggingIn, Welcome, Out }
        Phase phase = Phase.Loading;
        readonly DateTime born = DateTime.UtcNow;
        DateTime phaseAt = DateTime.UtcNow;
        // frames are paced to the monitor itself: one frame per screen refresh (60, 144, 240 Hz...), synced with Windows' compositor
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmFlush();
        [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
        [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern int timeEndPeriod(int ms);
        volatile bool pumping, framePending;
        int hz = 60;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastStep;
        int frames, bucketFrames;
        double bucketAt;
        readonly List<int> perSecond = new List<int>();

        void StartFrames()
        {
            hz = Math.Max(30, Math.Min(500, Display.RefreshRate(Screen.FromControl(this).DeviceName)));
            Log.Write("Splash: " + hz + " fps (monitor refresh rate)");
            pumping = true;
            new Thread(() =>
            {
                timeBeginPeriod(1);
                try
                {
                    double period = 1000.0 / hz, last = clock.Elapsed.TotalMilliseconds;
                    while (pumping)
                    {
                        // wait for the next screen refresh; if Windows returns early (or can't), sleep out the rest of the frame
                        try { DwmFlush(); } catch { }
                        while (pumping && clock.Elapsed.TotalMilliseconds - last < period * 0.8) Thread.Sleep(1);
                        last = clock.Elapsed.TotalMilliseconds;
                        if (!pumping) break;
                        if (framePending) continue;   // previous frame still drawing: skip rather than pile up
                        framePending = true;
                        try { BeginInvoke((Action)(() => { try { Step(); } finally { framePending = false; } })); }
                        catch { break; }
                    }
                }
                finally { timeEndPeriod(1); }
            }) { IsBackground = true, Name = "splash-frames", Priority = ThreadPriority.AboveNormal }.Start();
        }
        readonly Func<MainForm> buildWindow;

        // loading progress (written by the loader thread)
        volatile string step = "Starting up", detail = "";
        int stepsDone, stepsTotal = 1;
        float shownProgress;
        bool loaded;

        /// <summary>The finished main window, built while the splash was up.</summary>
        public MainForm Window;
        /// <summary>True once the user is signed in (or sign-in isn't set up yet). False means they closed it: quit.</summary>
        public bool Completed;

        Bitmap mascot, bgLayer, staticLayer;
        Image logo, avatar;

        int fact = new Random().Next(Facts.Length);
        DateTime factAt = DateTime.UtcNow;
        string signInMsg = "", userName;
        bool signingIn, welcomed;
        Rectangle discordBtn, centerArea, factArea;
        bool hoverBtn;
        readonly bool signInReady;

        static readonly Font Big = MakeFont("Segoe UI Variable Display Semib", 40f, "Segoe UI Semibold");
        static readonly Font Title = MakeFont("Segoe UI Variable Display Semib", 22f, "Segoe UI Semibold");
        static readonly Font Mid = MakeFont("Segoe UI Variable Text", 13f, "Segoe UI");
        static readonly Font Small = MakeFont("Segoe UI Variable Text", 10.5f, "Segoe UI");

        static Font MakeFont(string n, float s, string fb)
        {
            var f = new Font(n, s);
            if (!string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) { f.Dispose(); f = new Font(fb, s); }
            return f;
        }

        public SplashForm(Func<MainForm> buildWindow = null)
        {
            this.buildWindow = buildWindow;
            signInReady = DiscordAccount.Available(Settings.Load());
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;
            TopMost = true;
            ShowInTaskbar = true;
            Text = "CloveStrap";
            BackColor = Color.FromArgb(14, 10, 16);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            logo = Theme.BrandLogo();

            // Clove, the brand mascot, always; pre-scaled once in a fast pixel format
            var src = Characters.Get(MascotId);
            if (src != null)
            {
                int h = (int)(Height * 0.90), w = (int)(src.Width * (h / (float)src.Height));
                mascot = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(mascot))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);
                }
            }

            Shown += (o, e) => { StartFrames(); StartLoading(); };
            MouseMove += (o, e) => { bool hv = phase == Phase.SignIn && discordBtn.Contains(e.Location); if (hv != hoverBtn) { hoverBtn = hv; Cursor = hv ? Cursors.Hand : Cursors.Default; Invalidate(discordBtn); } };
            MouseClick += (o, e) => { if (phase == Phase.SignIn && discordBtn.Contains(e.Location)) SignInClicked(); };
            FormClosed += (o, e) =>
            {
                pumping = false;
                if (frames > 0) Log.Write("Splash: drew " + frames + " frames, " + Math.Round(frames / Math.Max(0.1, clock.Elapsed.TotalSeconds)) + " fps average; each second: " + string.Join(" ", perSecond));
                foreach (var b in new[] { mascot, bgLayer, staticLayer, logoCache }) if (b != null) b.Dispose();
                foreach (var b in textCache.Values) b.Dispose();
                if (avatar != null) avatar.Dispose();
                if (!Completed && Window != null) { Window.Dispose(); Window = null; }
            };
        }

        double Since(DateTime t) { return (DateTime.UtcNow - t).TotalSeconds; }

        // ------------------------------------------------------------- loading: everything the window needs, so nothing shows up late
        void Report(string s, string d) { step = s; detail = d ?? ""; }

        void StartLoading()
        {
            var steps = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Reading your settings", () => { Report("Reading your settings", "Settings.json"); Settings.Load(); AccountStore.Load(); }),
                new KeyValuePair<string, Action>("Checking for Roblox updates", () =>
                {
                    Report("Checking for Roblox updates", "clientsettingscdn.roblox.com");
                    try { Report("Checking for Roblox updates", Deployment.LatestVersion()); } catch { Report("Checking for Roblox updates", "offline, using installed Roblox"); }
                }),
                new KeyValuePair<string, Action>("Putting your mods into Roblox", () =>
                {
                    Report("Putting your mods into Roblox", "sky, sounds, cursor, FFlags");
                    try { Tweaks.ApplyToOfficial(Settings.Load()); } catch { }
                }),
                new KeyValuePair<string, Action>("Loading your accounts", () =>
                {
                    foreach (var a in AccountStore.Load())
                    {
                        Report("Loading your accounts", "@" + a.Username);
                        var img = RobloxApi.Headshot(a.UserId);
                        if (img != null) MainForm.PreloadHeadshot(a.UserId, img);
                    }
                }),
                new KeyValuePair<string, Action>("Loading themes", () =>
                {
                    foreach (var sc in Theme.Schemes.Where(x => x.Character)) { Report("Loading themes", sc.Name); Characters.Get(sc.Id); }
                }),
                new KeyValuePair<string, Action>("Loading the sky preview", () => { Report("Loading the sky preview", "sky512_ft.tex"); ModKit.DefaultSkyPreview(); }),
                new KeyValuePair<string, Action>("Getting Discord ready", () =>
                {
                    var s = Settings.Load();
                    if (DiscordAccount.SignedIn(s)) { Report("Getting Discord ready", s.DiscordName); avatar = DiscordAccount.Avatar(); userName = s.DiscordName; }
                    else Report("Getting Discord ready", "not signed in yet");
                }),
            };
            stepsTotal = steps.Count + 1;   // + building the window
            new Thread(() =>
            {
                foreach (var st in steps)
                {
                    try { st.Value(); } catch (Exception ex) { Log.Write("Splash step '" + st.Key + "': " + ex.Message); }
                    Interlocked.Increment(ref stepsDone);
                }
                try { BeginInvoke((Action)BuildWindowThenContinue); } catch { }
            }) { IsBackground = true, Name = "splash-loader" }.Start();
        }

        void BuildWindowThenContinue()
        {
            Report("Building your window", "pages, themes, accounts");
            Invalidate(centerArea);
            Update();
            if (buildWindow != null)
            {
                try { Window = buildWindow(); } catch (Exception ex) { Log.Write("Building window: " + ex); }
            }
            Interlocked.Increment(ref stepsDone);
            loaded = true;
        }

        /// <summary>Raised right before the splash closes after a successful sign-in, so the window can be drawn behind it first.</summary>
        public event Action Ready;

        void Step()
        {
            if (phase == Phase.Out)
            {
                // no full-screen fade (that's what stuttered): the menu is drawn behind us first, then we simply go
                pumping = false;
                if (Completed && Ready != null) { try { Ready(); } catch (Exception ex) { Log.Write("Splash handoff: " + ex.Message); } }
                Close();
                return;
            }

            // same speed at any frame rate: the bar eases by elapsed time, not per frame
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, Math.Max(0, now - lastStep));
            lastStep = now;
            frames++; bucketFrames++;
            if (now - bucketAt >= 1) { perSecond.Add(bucketFrames); bucketFrames = 0; bucketAt = now; }
            float target = stepsTotal > 0 ? stepsDone / (float)stepsTotal : 0;
            shownProgress += (target - shownProgress) * (float)(1 - Math.Pow(0.75, dt * 30));

            // when everything is loaded and the bar is full: sign in (first time) or log in
            if (phase == Phase.Loading && loaded && shownProgress > 0.985f && Since(born) > 1.4)
            {
                var s = Settings.Load();
                if (DiscordAccount.SignedIn(s)) { userName = s.DiscordName; GoTo(Phase.LoggingIn); }
                else GoTo(Phase.SignIn);
            }
            if (phase == Phase.LoggingIn && !signingIn && Since(phaseAt) > 1.3) { Completed = true; Finish(); }
            if (phase == Phase.Welcome && Since(phaseAt) > 1.5) { Completed = true; Finish(); }
            if (Since(factAt) > 4.5) { fact = (fact + 1) % Facts.Length; factAt = DateTime.UtcNow; Invalidate(factArea); }
            else if (Since(factAt) < 0.65 || Since(factAt) > 3.85) Invalidate(factArea);

            // the character slides in once (only the strip she moves through is repainted); after that only the middle moves
            if (staticLayer == null && mascot != null && slideStrip.Width > 0) Invalidate(slideStrip);
            // repaint the whole middle only when something in it changed; otherwise just the spinning ring
            float pr = Math.Min(1, shownProgress);
            string key = phase + "|" + step + "|" + detail + "|" + (int)Math.Round(pr * 100) + "|" + (int)(pr * 480) + "|" + hoverBtn + "|" +
                         signInMsg + "|" + userName + "|" + welcomed + "|" + (avatar != null) + "|" + signingIn;
            if (key != lastKey || spinRect.IsEmpty || Since(born) < 0.7) { lastKey = key; Invalidate(centerArea); }
            else if (phase == Phase.Loading || phase == Phase.LoggingIn) Invalidate(spinRect);
            Update();   // draw this frame now, inside its refresh, instead of whenever Windows gets round to it
        }

        Rectangle slideStrip, spinRect;
        string lastKey = "";
        Bitmap logoCache;
        readonly Dictionary<string, Bitmap> textCache = new Dictionary<string, Bitmap>();

        /// <summary>Text rendered once into a picture, then just copied each frame (drawing big text every frame is what was slow).</summary>
        Bitmap TextBmp(string s, Font f, Color c, int maxW)
        {
            string k = s + "\u0001" + f.Name + f.Size + "\u0001" + c.ToArgb() + "\u0001" + maxW;
            Bitmap b;
            if (textCache.TryGetValue(k, out b)) return b;
            if (textCache.Count > 120) { foreach (var x in textCache.Values) x.Dispose(); textCache.Clear(); }
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                SizeF sz;
                using (var tmp = new Bitmap(1, 1)) using (var mg = Graphics.FromImage(tmp)) { mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; sz = mg.MeasureString(s ?? "", f, maxW, sf); }
                b = new Bitmap(Math.Max(1, Math.Min(maxW, (int)Math.Ceiling(sz.Width) + 4)), Math.Max(1, (int)Math.Ceiling(sz.Height) + 2), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                using (var br = new SolidBrush(c))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(s ?? "", f, br, new RectangleF(0, 0, b.Width, b.Height), sf);
                }
            }
            textCache[k] = b;
            return b;
        }

        Bitmap LogoAt(int size)
        {
            if (logo == null) return null;
            if (logoCache == null || logoCache.Width != size)
            {
                if (logoCache != null) logoCache.Dispose();
                logoCache = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(logoCache))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Theme.DrawContain(g, logo, new RectangleF(0, 0, size, size), false);
                }
            }
            return logoCache;
        }

        void GoTo(Phase p) { phase = p; phaseAt = DateTime.UtcNow; Invalidate(centerArea); }
        void Finish() { if (phase != Phase.Out) GoTo(Phase.Out); }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        /// <summary>Brings the Discord app to the front (from the tray too), where its Authorize popup is.</summary>
        static void ShowDiscord()
        {
            try
            {
                var win = System.Diagnostics.Process.GetProcessesByName("Discord").Select(p => { try { return p.MainWindowHandle; } catch { return IntPtr.Zero; } })
                                 .FirstOrDefault(h => h != IntPtr.Zero);
                if (win != IntPtr.Zero)
                {
                    if (IsIconic(win)) ShowWindow(win, 9);   // SW_RESTORE
                    SetForegroundWindow(win);
                    return;
                }
                // in the tray: Discord's own launcher brings the window back
                string upd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord", "Update.exe");
                if (File.Exists(upd)) System.Diagnostics.Process.Start(upd, "--processStart Discord.exe");
            }
            catch (Exception ex) { Log.Write("Show Discord: " + ex.Message); }
        }

        void SignInClicked()
        {
            if (signingIn) return;
            if (!signInReady)
            {
                // the developer hasn't built the Discord app ID in yet: don't lock anyone out
                Completed = true; Finish(); return;
            }
            signingIn = true;
            signInMsg = "";
            GoTo(Phase.LoggingIn);
            // step back so Discord's "Authorize" popup isn't hidden behind us, and bring Discord up
            TopMost = false;
            new Thread(() =>
            {
                Thread.Sleep(400);   // let Discord open the popup first
                ShowDiscord();
            }) { IsBackground = true }.Start();
            new Thread(() =>
            {
                string err = DiscordAccount.SignIn();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        signingIn = false;
                        TopMost = true;
                        Activate();
                        if (err == null)
                        {
                            var s = Settings.Load();
                            userName = s.DiscordName;
                            avatar = DiscordAccount.Avatar();
                            if (Window != null) Window.RefreshProfile();
                            GoTo(Phase.Welcome);
                        }
                        else
                        {
                            signInMsg = err == "Discord isn't open" ? "Open the Discord app and sign in there, then try again" : "Couldn't sign in: " + err;
                            GoTo(Phase.SignIn);
                        }
                    }));
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        static float Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return (float)(1 - Math.Pow(1 - t, 3)); }

        // ------------------------------------------------------------- drawing
        void BuildBackground(int W, int H)
        {
            bgLayer = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bgLayer))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(14, 10, 16), Color.FromArgb(28, 14, 28), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(bg, 0, 0, W, H);
                Glow(g, W * 0.70f, H * 0.45f, W * 0.55f, Color.FromArgb(70, Theme.Get(MascotId).Accent));
                Glow(g, W * 0.18f, H * 0.80f, W * 0.40f, Color.FromArgb(40, Theme.Get(MascotId).Accent));
            }
            float cx = W * 0.36f;
            centerArea = new Rectangle((int)(cx - 480), (int)(H * 0.30f - 150), 960, 560);
            factArea = new Rectangle((int)(cx - 310), H - 110, (int)Math.Min(800, W * 0.55f), 90);
        }

        Rectangle MascotRect(float k, int W, int H)
        {
            return new Rectangle((int)(W - mascot.Width - W * 0.06f + (1 - k) * 120), H - mascot.Height, mascot.Width, mascot.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int W = ClientSize.Width, H = ClientSize.Height;
            if (bgLayer == null || bgLayer.Width != W || bgLayer.Height != H) { if (bgLayer != null) bgLayer.Dispose(); BuildBackground(W, H); }
            double t = Since(born);
            Color acc = Theme.Get(MascotId).Accent;

            // still parts: background + Clove (composited once after her slide-in)
            g.CompositingMode = CompositingMode.SourceCopy;   // the background is solid: a straight copy, no blending
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (staticLayer != null)
                g.DrawImage(staticLayer, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            else
            {
                // copy just the dirty part of the background, then the character at her slide position (plain, fast blit)
                g.DrawImage(bgLayer, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                float k = Ease(t / 0.7);
                if (mascot != null)
                {
                    var r = MascotRect(k, W, H);
                    g.DrawImageUnscaled(mascot, r.X, r.Y);
                    var start = MascotRect(0, W, H);
                    slideStrip = Rectangle.Union(start, MascotRect(1, W, H));
                }
                if (k >= 1 || mascot == null)
                {
                    staticLayer = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var sg = Graphics.FromImage(staticLayer))
                    {
                        sg.DrawImageUnscaled(bgLayer, 0, 0);
                        if (mascot != null) sg.DrawImageUnscaled(mascot, MascotRect(1, W, H).Location);
                    }
                }
            }

            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.Bilinear;

            if (e.ClipRectangle.IntersectsWith(centerArea)) DrawCenter(g, W, H, t, acc);
            if (e.ClipRectangle.IntersectsWith(factArea)) DrawFact(g, acc);
        }

        void Spinner(Graphics g, RectangleF r, double t, Color acc, float width)
        {
            using (var track = new Pen(Color.FromArgb(45, acc), width)) g.DrawEllipse(track, r);
            using (var arc = new Pen(acc, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, r, (float)(t * 300 % 360), 80 + 50 * (float)Math.Sin(t * 2.4));
        }

        void Centered(Graphics g, string s, Font f, Color c, float cx, float y, float w)
        {
            var b = TextBmp(s, f, c, (int)w);
            g.DrawImageUnscaled(b, (int)Math.Round(cx - b.Width / 2f), (int)Math.Round(y + 1));
        }

        void LeftText(Graphics g, string s, Font f, Color c, float x, float y, int w)
        {
            g.DrawImageUnscaled(TextBmp(s, f, c, w), (int)Math.Round(x), (int)Math.Round(y));
        }

        void DrawCenter(Graphics g, int W, int H, double t, Color acc)
        {
            float cx = W * 0.36f;
            float lk = Ease(t / 0.6);
            float ls = 150 + 50 * lk;
            float ly = H * 0.30f - ls / 2;
            var lb = LogoAt(200);
            if (lb != null)
            {
                if (lk >= 1) g.DrawImageUnscaled(lb, (int)Math.Round(cx - 100), (int)Math.Round(ly));
                else g.DrawImage(lb, new RectangleF(cx - ls / 2, ly, ls, ls));
            }
            // load icon: a spinning ring around the logo while anything is happening
            var ring = new RectangleF(cx - ls / 2 - 16, ly - 16, ls + 32, ls + 32);
            spinRect = Rectangle.Inflate(Rectangle.Round(ring), 6, 6);
            if (phase == Phase.Loading || phase == Phase.LoggingIn) Spinner(g, ring, t, acc, 4f);
            var clip = g.ClipBounds;
            if (clip.Bottom <= spinRect.Bottom && clip.Top >= spinRect.Top) return;   // only the ring was repainted
            float y = ly + ls + 22;
            Centered(g, "CloveStrap", Title, Color.White, cx, y, 600);
            y += 64;

            if (phase == Phase.Loading || (phase == Phase.Out && !Completed))
            {
                Centered(g, step, Big, Color.White, cx, y, 900);
                y += Big.Height + 14;
                var bar = new RectangleF(cx - 240, y, 480, 8);
                Theme.Fill(g, bar, 4, Color.FromArgb(45, 255, 255, 255));
                if (shownProgress > 0.01f) Theme.Fill(g, new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(1, shownProgress), bar.Height), 4, acc);
                y += 22;
                LeftText(g, detail, Small, Color.FromArgb(170, 255, 255, 255), bar.X, y, 400);
                string pct = (int)Math.Round(Math.Min(1, shownProgress) * 100) + "%";
                var pb = TextBmp(pct, Small, acc, 80);
                g.DrawImageUnscaled(pb, (int)(bar.Right - pb.Width), (int)y);
            }
            else if (phase == Phase.SignIn)
            {
                Centered(g, "Sign in to get started", Big, Color.White, cx, y, 900);
                y += Big.Height + 6;
                Centered(g, signInReady ? "Discord will ask you to allow CloveStrap. You can review everything it asks for there."
                                        : "Discord sign-in isn't set up in this build yet.", Mid, Color.FromArgb(200, 255, 255, 255), cx, y, 900);
                y += 50;
                discordBtn = new Rectangle((int)(cx - 170), (int)y, 340, 58);
                Theme.Fill(g, discordBtn, 14, hoverBtn ? Color.FromArgb(108, 121, 255) : Color.FromArgb(88, 101, 242));
                var bt = TextBmp(signInReady ? "Sign in with Discord" : "Continue", Mid, Color.White, 320);
                g.DrawImageUnscaled(bt, discordBtn.X + (discordBtn.Width - bt.Width) / 2, discordBtn.Y + (discordBtn.Height - bt.Height) / 2);
                y += 76;
                if (signInMsg.Length > 0) Centered(g, signInMsg, Small, Color.FromArgb(230, 255, 190, 205), cx, y, 900);
            }
            else if (phase == Phase.LoggingIn || phase == Phase.Welcome || phase == Phase.Out)
            {
                float a = 76;
                var av = new RectangleF(cx - a / 2, y, a, a);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(av);
                    if (avatar != null) { g.SetClip(path, CombineMode.Intersect); g.DrawImage(avatar, av); g.ResetClip(); g.SetClip(centerArea); }
                    else using (var b = new SolidBrush(Color.FromArgb(90, acc))) g.FillPath(b, path);
                    using (var p = new Pen(acc, 3)) g.DrawPath(p, path);
                }
                y += a + 14;
                if (phase == Phase.Welcome) welcomed = true;
                string line = welcomed ? "Welcome, " + (userName ?? "friend") + "!" : "Logging you in... OwO";
                Centered(g, line, Big, Color.White, cx, y, 900);
                if (signingIn) Centered(g, "Click Authorize in the Discord window", Mid, Color.FromArgb(200, 255, 255, 255), cx, y + Big.Height + 6, 900);
                else if (userName != null && !welcomed) Centered(g, userName, Mid, Color.FromArgb(200, 255, 255, 255), cx, y + Big.Height + 6, 900);
            }
        }

        void DrawFact(Graphics g, Color acc)
        {
            // each fact drifts up into place, rests, then drifts on and fades out: no label, it just flows
            double since = Since(factAt);
            float inK = Ease(since / 0.6), outK = Ease((since - 3.9) / 0.6);
            double fa = inK * (1 - outK);
            int alpha = (int)(Math.Max(0, Math.Min(1, fa)) * 225);
            float dy = (1 - inK) * 14 - outK * 14;
            using (var b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.DrawString(Facts[fact], Mid, b, new RectangleF(factArea.X + 10, factArea.Y + 22 + dy, factArea.Width - 20, 56));
        }

        static void Glow(Graphics g, float x, float y, float r, Color c)
        {
            using (var path = new GraphicsPath())
            {
                var rect = new RectangleF(x - r, y - r * 0.8f, r * 2, r * 1.6f);
                path.AddEllipse(rect);
                using (var br = new PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } })
                    g.FillEllipse(br, rect);
            }
        }
    }

    partial class MainForm
    {
        /// <summary>The splash screen fetches account pictures before the window opens, so they're there right away.</summary>
        public static void PreloadHeadshot(long userId, Image img)
        {
            lock (headshots) headshots[userId] = img;
        }

        /// <summary>Picks up the newest saved settings (the window may have been built a moment before the last save).</summary>
        public void ReloadSettings() { RefreshProfile(); }

        /// <summary>Shows the Discord name and picture in the corner right after signing in.</summary>
        public void RefreshProfile()
        {
            S = Settings.Load();
            discordAvatar = DiscordAccount.Avatar();
            if (profile == null) return;
            if (DiscordAccount.SignedIn(S)) { profile.UserName = S.DiscordName; profile.Avatar = discordAvatar; }
            profile.Invalidate();
        }
    }
}
