// Username finder: walks through short names / foreign words and stops at the first one Roblox says is free.
// Names are looked up 100 at a time; anything not taken is double-checked with Roblox's sign-up check.
// When Roblox answers "too many requests" it waits as long as Roblox asks, so the speed is whatever Roblox allows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CloveStrap
{
    class NameFinder
    {
        public volatile bool Running;
        public volatile string Current = "", Hit, State = "Ready";
        public readonly List<string> Hits = new List<string>();   // every free name found this run
        public bool StopOnHit;                                    // false: keep going and collect them all

        public string[] HitsCopy() { lock (gate) return Hits.ToArray(); }

        public NameFinder() { Hits.AddRange(FinderMemory.LoadFound()); }

        public void ClearFound() { lock (gate) Hits.Clear(); FinderMemory.ClearFound(); Hit = null; }
        public long Checked, Requests, Limited;
        public DateTime Started;
        readonly object gate = new object();
        IEnumerator<string> source;
        int batch = 100;
        int backoffMs;

        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CloveStrap/2.0";

        // short words from other languages (Japanese, Spanish, French, Latin, Italian, German, Finnish, Korean, Hawaiian, Norse, Greek...)
        public static readonly string[] Words = (
            "yami kage hoshi tsuki sora kaze yuki hana kumo ame umi mori kawa tori neko inu kitsune ryu tora kami oni yume kokoro hikari " +
            "akuma tenshi sakura momiji kiri shiro kuro aka ao midori gin kin tama kaji honoo mizu tsuchi ishi hagane katana yari yumi " +
            "sombra luna sol cielo fuego agua tierra viento nube rayo trueno lobo zorro gato perro oso aguila cuervo rosa lirio noche " +
            "alba ocaso brisa niebla hielo nieve llama chispa piedra plata oro hierro sangre alma sueno fiel bravo rapido feroz " +
            "ombre lune soleil ciel feu eau terre vent nuage foudre loup renard chat ours aigle corbeau rose nuit aube brume glace " +
            "neige flamme pierre argent fer sang ame reve rapide sauvage velours minuit esprit " +
            "umbra lux nox ignis aqua terra ventus nubes fulmen lupus vulpes felis ursus aquila corvus rosa stella astra caelum " +
            "anima somnium ferrum aurum argentum sanguis mors vita fortis celer ferox noctis aeternus " +
            "ombra luce fuoco acqua vento nuvola lupo volpe gatto orso aquila corvo notte alba nebbia ghiaccio fiamma pietra " +
            "argento ferro sangue anima sogno veloce selvaggio " +
            "schatten mond sonne himmel feuer wasser erde wind wolke blitz wolf fuchs katze baer adler rabe rose nacht nebel " +
            "eis schnee flamme stein silber eisen blut seele traum schnell wild " +
            "varjo kuu aurinko taivas tuli vesi maa tuuli pilvi salama susi kettu kissa karhu kotka korppi ruusu yo sumu jaa " +
            "lumi liekki kivi hopea rauta veri sielu uni nopea " +
            "byeol dal haneul bul mul baram gureum horangi yeou goyangi gom doksuri kkamagwi bam angae eoreum nun dol eun soe pi " +
            "hoku mahina la lani ahi wai honua makani ao uila ilio pueo manu pulelehua po ohu hau pohaku kala hao koko uhane moe " +
            "skuggi mani sol himinn eldr vatn jord vindr sky hrafn ulfr refr bjorn orn rosa nott thoka iss snjor logi steinn silfr " +
            "jarn blod sal draumr snarr " +
            "skia selene helios ouranos pyr hydor gaia anemos nefos keravnos lykos alepou gata arkos aetos korakas nyx omichli " +
            "pagos chioni floga petra argyros sidiros haima psyche oneiro tachys agrios"
        ).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && w.Length <= 20).Distinct().ToArray();

        // short English words people like as names
        public static readonly string[] English = (
            "void abyss aura blaze bolt chaos comet crow dawn doom dusk echo ember fang flare frost ghost glitch grim haze hex " +
            "inferno jinx karma knight lunar lyric maze mist moth myth nova onyx orbit phantom pixel prism pulse quartz raven rebel " +
            "relic rift rogue rune sage scar shade shard siren sky slate smoke solar spark specter spirit storm stray surge talon " +
            "tempest thorn tide titan torch toxic venom vex vibe vine viper vortex warp whisper wisp wraith zen zero zephyr " +
            "ace apex arrow ash atlas axe bane bash beast blade blink bliss bloom blur bone brave breeze brick bullet burst cage " +
            "candy canyon cedar charm chill cinder claw clover cobra coral cosmic crash crest crimson crypt cube cyber dagger dash " +
            "delta demon dream drift drone dune eagle eclipse edge elite emerald epic fable falcon fate feather fern fiend flame " +
            "flash flux forge fox frenzy fury galaxy gem giant glide glow golem grace gravity guard hawk heart hollow honey hunter " +
            "hyper icon idol iris ivory jade jet jewel jungle kite lace lance laser legend lime lion lotus lucky magic mango marble " +
            "mars mercy meteor mirage moon neon night ninja noble oasis ocean omen oracle panda panic peach pearl petal phoenix " +
            "pilot plasma plume polar poison quest quill rage rain reaper ripple river rocket rose ruby saber savage scythe shadow " +
            "shark shock silk silver skull slash slime snow sonic soul spike star steel sting stone sugar sunny swift sword tiger " +
            "token tornado toxin tulip twilight unity valor velvet venus vivid volt wave wild wolf wonder zap zest zinc"
        ).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && w.Length <= 20).Distinct().ToArray();

        public static IEnumerable<string> Candidates(string mode, bool digits)
        {
            var rnd = new Random();
            if (mode == "words" || mode == "english")
            {
                // one clean word each, nothing joined or added
                var src = mode == "english" ? WordLists.English.Concat(English) : WordLists.Foreign.Concat(Words);
                foreach (var w in src.Distinct().OrderBy(x => rnd.Next())) yield return w;
                yield break;
            }
            int len = mode == "3" ? 3 : 4;
            string cs = "abcdefghijklmnopqrstuvwxyz" + (digits ? "0123456789" : "");
            long n = 1; for (int i = 0; i < len; i++) n *= cs.Length;
            const long step = 7919;                 // prime, so i*step mod n visits every name once in a scrambled order
            long offset = rnd.Next((int)Math.Min(int.MaxValue, n));
            var sb = new StringBuilder(len);
            for (long i = 0; i < n; i++)
            {
                long idx = (i * step + offset) % n;
                sb.Clear();
                for (int k = 0; k < len; k++) { sb.Insert(0, cs[(int)(idx % cs.Length)]); idx /= cs.Length; }
                string s = sb.ToString();
                if (char.IsDigit(s[0]) && s.All(char.IsDigit)) continue;   // all-number names aren't allowed
                yield return s;
            }
        }

        public void Start(string mode, bool digits, Action<string> onHit, Action onDone)
        {
            if (Running) return;
            Running = true; Hit = null; Checked = 0; Requests = 0; Limited = 0; batch = 100; backoffMs = 0;
            lock (gate) { Hits.Clear(); Hits.AddRange(FinderMemory.LoadFound()); }
            int wordCount = mode == "english" ? WordLists.English.Concat(English).Distinct().Count() : WordLists.Foreign.Concat(Words).Distinct().Count();
            Memory = new FinderMemory(mode, digits, wordCount);
            Started = DateTime.UtcNow;
            State = "Looking";
            Round = 1;
            source = Endless(mode, digits).GetEnumerator();
            interval = 250;
            WaitUntil = DateTime.MinValue;
            // one steady worker paced to what Roblox allows: smoother (and in practice faster) than bursts that get blocked
            new Thread(() =>
            {
                try { Work(onHit); }
                catch (Exception ex) { Log.Write("Name finder worker: " + ex.Message); }
                if (Memory != null) Memory.Save();
                Running = false;
                if (Hit == null && State == "Looking") State = "Checked every name, none free";
                onDone();
            }) { IsBackground = true, Name = "name-finder" }.Start();
        }

        double interval;                      // ms between lookups, tuned to Roblox's limit as we go
        public DateTime WaitUntil;            // when Roblox said "slow down", we're waiting until this time

        /// <summary>What to show under the name; counts down live while waiting.</summary>
        public volatile int Round = 1;

        /// <summary>Goes through the list, then reshuffles and starts again, forever (names already found are skipped).</summary>
        public FinderMemory Memory;
        DateTime lastSave = DateTime.UtcNow;

        IEnumerable<string> Endless(string mode, bool digits)
        {
            bool resetOnce = false;
            while (true)
            {
                int n = 0;
                foreach (var s in Candidates(mode, digits))
                {
                    if (Memory != null && Memory.IsChecked(s)) continue;   // already checked this cycle (even before a restart)
                    bool known;
                    lock (gate) known = Hits.Contains(s);
                    if (known) continue;
                    n++;
                    yield return s;
                }
                if (n == 0)
                {
                    // every possible name has been checked: only now start over and recheck the taken ones
                    if (Memory == null || resetOnce) yield break;
                    Memory.Reset();
                    resetOnce = true;
                    Log.Write("Name finder: every " + mode + " name checked, starting a new cycle");
                }
                else resetOnce = false;
                Round++;
            }
        }

        public string StateText()
        {
            double left = (WaitUntil - DateTime.UtcNow).TotalSeconds;
            string round = Running && Round > 1 ? "round " + Round + "  ·  " : "";
            if (Running && left > 0.05) return round + "Roblox asked to slow down, back in " + Math.Ceiling(left) + "s";
            return round + State;
        }

        public void Stop() { if (Running) { Running = false; if (Hit == null) State = "Stopped"; } if (Memory != null) Memory.Save(); }

        List<string> Take(int count)
        {
            var list = new List<string>(count);
            lock (gate)
                while (list.Count < count && source.MoveNext()) list.Add(source.Current);
            return list;
        }

        void Work(Action<string> onHit)
        {
            var last = DateTime.UtcNow.AddSeconds(-10);
            while (Running)
            {
                // wait out a slow-down, then keep a steady gap between lookups
                while (Running && DateTime.UtcNow < WaitUntil) Thread.Sleep(100);
                double gap = interval - (DateTime.UtcNow - last).TotalMilliseconds;
                if (gap > 0) Thread.Sleep((int)gap);
                if (!Running) return;
                State = "Looking";
                var names = Take(batch);
                if (names.Count == 0) return;
                Current = names[0];
                last = DateTime.UtcNow;
                HashSet<string> taken;
                try { taken = Lookup(names); }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse;
                    int code = hr == null ? 0 : (int)hr.StatusCode;
                    lock (gate) Requeue(names);
                    if (code == 429)
                    {
                        Interlocked.Increment(ref Limited);
                        interval = Math.Min(6000, interval * 1.5);
                        int ra; string h = hr.Headers["Retry-After"];
                        int ms = int.TryParse(h, out ra) && ra > 0 ? Math.Min(60000, ra * 1000) : (int)Math.Max(1500, interval * 2);
                        WaitUntil = DateTime.UtcNow.AddMilliseconds(ms);
                    }
                    else if (code == 400 && batch > 10) batch /= 2;
                    else WaitUntil = DateTime.UtcNow.AddSeconds(3);
                    continue;
                }
                interval = Math.Max(150, interval * 0.93);   // creep back up to the fastest Roblox accepts
                Interlocked.Increment(ref Requests);
                Interlocked.Add(ref Checked, names.Count);
                if (Memory != null)
                {
                    Memory.Mark(names);
                    if ((DateTime.UtcNow - lastSave).TotalSeconds > 10) { Memory.Save(); lastSave = DateTime.UtcNow; }
                }
                // walk the name shown through this batch so you can see it moving
                var shown = names;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    int step = Math.Max(1, shown.Count / 20);
                    for (int i = 0; i < shown.Count && Running; i += step) { Current = shown[i]; Thread.Sleep(Math.Max(10, (int)(interval / 20))); }
                });

                foreach (var n in names.Where(x => !taken.Contains(x.ToLowerInvariant())))
                {
                    if (!Running) return;
                    Current = n;
                    int verdict = Validate(n);
                    if (verdict == 0)
                    {
                        lock (gate) { Hit = n; Hits.Add(n); }
                        FinderMemory.AddFound(n);
                        Log.Write("Name finder: " + n + " is available");
                        onHit(n);
                        if (StopOnHit)
                        {
                            Running = false;
                            State = "Found one!";
                            return;
                        }
                    }
                }
            }
        }

        readonly Queue<string> retry = new Queue<string>();
        void Requeue(List<string> names)
        {
            // put the batch back in front so nothing is skipped after a slow-down
            var rest = new List<string>(names);
            var old = source;
            source = Prepend(rest, old);
        }

        static IEnumerator<string> Prepend(List<string> first, IEnumerator<string> then)
        {
            foreach (var s in first) yield return s;
            while (then.MoveNext()) yield return then.Current;
        }

        static HashSet<string> Lookup(List<string> names)
        {
            var req = (HttpWebRequest)WebRequest.Create("https://users.roblox.com/v1/usernames/users");
            req.Method = "POST"; req.UserAgent = UA; req.Timeout = 15000; req.ContentType = "application/json"; req.Accept = "application/json";
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "usernames", names }, { "excludeBannedUsers", false } }));
            req.ContentLength = body.Length;
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                var taken = new HashSet<string>();
                var data = d["data"] as System.Collections.ArrayList;
                if (data != null)
                    foreach (Dictionary<string, object> u in data)
                    {
                        object rq; if (u.TryGetValue("requestedUsername", out rq) && rq != null) taken.Add(rq.ToString().ToLowerInvariant());
                        object nm; if (u.TryGetValue("name", out nm) && nm != null) taken.Add(nm.ToString().ToLowerInvariant());
                    }
                return taken;
            }
        }

        /// <summary>Roblox's own sign-up check: 0 = free, 1 = taken, 2 = not allowed, other = not usable. -1 on error.</summary>
        public static int Validate(string name)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://auth.roblox.com/v1/usernames/validate?request.username=" + Uri.EscapeDataString(name) + "&request.birthday=2000-01-01&request.context=Signup");
                    req.UserAgent = UA; req.Timeout = 15000; req.Accept = "application/json";
                    using (var resp = req.GetResponse())
                    using (var r = new StreamReader(resp.GetResponseStream()))
                    {
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                        return Convert.ToInt32(d["code"]);
                    }
                }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse;
                    if (hr != null && (int)hr.StatusCode == 429) { Thread.Sleep(1500 * (attempt + 1)); continue; }
                    return -1;
                }
                catch { return -1; }
            }
            return -1;
        }

        /// <summary>Headless check: --names-test &lt;mode&gt; &lt;seconds&gt;</summary>
        public static int SelfTest(string mode, int seconds)
        {
            Log.Write("names-test: validate 'builderman' = " + Validate("builderman") + ", word list " + Words.Length + " words");
            var f = new NameFinder();
            var done = new ManualResetEvent(false);
            f.Start(mode, false, n => Log.Write("names-test: HIT " + n), () => done.Set());
            done.WaitOne(seconds * 1000);
            f.Stop();
            double secs = Math.Max(0.1, (DateTime.UtcNow - f.Started).TotalSeconds);
            Log.Write(string.Format("names-test: {0} checked in {1:0.0}s = {2:0}/s, {3} requests, {4} slow-downs, state '{5}', hit {6}",
                f.Checked, secs, f.Checked / secs, f.Requests, f.Limited, f.State, f.Hit ?? "-"));
            return 0;
        }
    }
}

namespace CloveStrap
{
    class NameStatus : VControl
    {
        public NameFinder F;
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            int found = F == null ? 0 : F.HitsCopy().Length;
            bool stoppedOnHit = F != null && !F.Running && F.Hit != null && F.StopOnHit;
            Theme.Fill(g, r, 10, Theme.Input);
            Theme.Stroke(g, r, 10, found > 0 ? Theme.Accent : Theme.Border, found > 0 ? 2f : 1f);
            if (F == null) return;
            string big = F.Running ? F.Current : (F.Hit ?? (F.Checked > 0 ? F.Current : "—"));
            bool accent = !F.Running && F.Hit != null;
            using (var f = new Font("Cascadia Mono", 22f, FontStyle.Bold))
            {
                var ff = f.Name == "Cascadia Mono" ? f : new Font("Consolas", f.Size, FontStyle.Bold);
                Theme.Draw(g, big, ff, new Rectangle(0, 14, Width, 46), accent ? Theme.Accent : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            double secs = Math.Max(0.1, (DateTime.UtcNow - F.Started).TotalSeconds);
            string stats = F.Checked == 0 && !F.Running ? "Pick what to look for, then press Start" :
                string.Format("{0}{1:0} a second  ·  {2}{3}",
                    F.Memory != null ? string.Format("{0:N0} / {1:N0} done  ·  ", F.Memory.Done, F.Memory.Total) : string.Format("{0:N0} checked  ·  ", F.Checked),
                    F.Checked / secs,
                    found > 0 ? found + " free found  ·  " : "", stoppedOnHit ? "free right now!" : F.StateText());
            Theme.Draw(g, stats, Theme.Small, new Rectangle(10, 64, Width - 20, 18), found > 0 ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    partial class MainForm
    {
        static readonly NameFinder finder = new NameFinder();
        Chips nameMode, nameChars, nameOnHit;
        VButton nameStart, nameCopy;
        NameStatus nameStatus;
        VTextBox nameFound;
        Timer nameTimer;
        int nameShown = -1;

        void BuildNames()
        {
            var p = NewPage("Usernames");
            p.AutoScroll = true;
            p.HandleCreated += (o, e) => Native.Scrollbars(p);
            int nw = pageW - SystemInformation.VerticalScrollBarWidth - 6, y = 4;
            var card = new Card(nw, "Username finder", "");
            nameMode = new Chips("English words", "Other languages", "4 letters", "3 letters");
            nameChars = new Chips("Letters", "Letters + numbers");
            nameOnHit = new Chips("Keep going", "Stop");
            nameMode.SelectedIndex = 0; nameChars.SelectedIndex = 0; nameOnHit.SelectedIndex = finder.StopOnHit ? 1 : 0;
            nameMode.SelectedIndexChanged += (o, e) => { nameChars.Enabled = nameMode.SelectedIndex >= 2; nameChars.Invalidate(); };
            nameOnHit.SelectedIndexChanged += (o, e) => finder.StopOnHit = nameOnHit.SelectedIndex == 1;
            nameChars.Enabled = false;
            card.AddRow("Look for", "One clean word each, in English or other languages.", nameMode);
            card.AddRow("Characters", "For 3 and 4 letter names.", nameChars);
            card.AddRow("When one is free", "Keep going collects every free name in the list below.", nameOnHit);

            var box = new Panel { Size = new Size(card.InnerWidth, 140), BackColor = Theme.Row };
            nameStatus = new NameStatus { F = finder, Location = new Point(0, 0), Size = new Size(box.Width, 92), BackColor = Theme.Row };
            nameStart = new VButton(finder.Running ? "Stop" : "Start", finder.Running ? "" : "", true) { BackColor = Theme.Row, Size = new Size(140, 36) };
            nameStart.Location = new Point(0, 102);
            nameStart.Click += (o, e) => ToggleFinder();
            nameCopy = new VButton("Copy all", "") { BackColor = Theme.Row, Location = new Point(nameStart.Right + 8, 102) };
            nameCopy.Click += (o, e) =>
            {
                var all = finder.HitsCopy();
                if (all.Length == 0) { Flash("Nothing found yet", Theme.Accent); return; }
                try { Clipboard.SetText(string.Join(Environment.NewLine, all)); Flash("Copied " + all.Length + " name" + (all.Length == 1 ? "" : "s"), Theme.Accent); } catch { }
            };
            var change = new VButton("Change username on Roblox", "") { BackColor = Theme.Row };
            change.Location = new Point(nameCopy.Right + 8, 102);
            change.Click += (o, e) => { try { Process.Start("https://www.roblox.com/my/account#!/info"); } catch { } };
            var clear = new VButton("Clear list", "") { BackColor = Theme.Row };
            clear.Location = new Point(change.Right + 8, 102);
            clear.Click += (o, e) =>
            {
                if (finder.HitsCopy().Length == 0) return;
                if (!VDialog.Confirm(this, "Clear the found names?", "Removes the saved list of free names. What's been checked is kept.", "Clear")) return;
                finder.ClearFound(); nameShown = -1;
            };
            box.Controls.AddRange(new Control[] { nameStatus, nameStart, nameCopy, change, clear });
            card.AddRaw(box, true);

            nameFound = new VTextBox(true) { Size = new Size(card.InnerWidth, 150) };
            nameFound.Box.ReadOnly = true;
            nameFound.Text = string.Join("\r\n", finder.HitsCopy());
            card.AddFull("Free names found", "Free when checked. Grab one fast, anyone can take it.", nameFound);
            Place(p, card, ref y);

            var note = new Label
            {
                Text = "Checks 100 names at a time and double-checks every hit. Changing your username costs 1,000 Robux.",
                Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg, Location = new Point(2, y - 2), MaximumSize = new Size(nw - 4, 0), AutoSize = true, UseMnemonic = false
            };
            p.Controls.Add(note);
            nameShown = -1;

            if (nameTimer == null)
            {
                nameTimer = new Timer { Interval = 150 };
                nameTimer.Tick += (o, e) =>
                {
                    if (nameStatus != null && !nameStatus.IsDisposed && nameStatus.Visible) nameStatus.Invalidate();
                    var all = finder.HitsCopy();
                    if (nameFound != null && !nameFound.IsDisposed && all.Length != nameShown)
                    {
                        nameShown = all.Length;
                        nameFound.Text = string.Join("\r\n", all);
                        nameFound.Box.SelectionStart = nameFound.Box.TextLength;
                        nameFound.Box.ScrollToCaret();
                    }
                };
                nameTimer.Start();
                FormClosed += (o, e) => { nameTimer.Stop(); finder.Stop(); };
            }
        }

        void ToggleFinder()
        {
            if (finder.Running)
            {
                finder.Stop();
                nameStart.Text = "Start"; nameStart.Glyph = ""; nameStart.Invalidate();
                return;
            }
            string mode = new[] { "english", "words", "4", "3" }[Math.Max(0, nameMode.SelectedIndex)];
            finder.StopOnHit = nameOnHit.SelectedIndex == 1;
            nameStart.Text = "Stop"; nameStart.Glyph = ""; nameStart.Invalidate();
            finder.Start(mode, nameChars.SelectedIndex == 1,
                hit =>
                {
                    try { BeginInvoke((Action)(() => Flash("Free username: " + hit + " " + Cute.Face(), Theme.Accent))); } catch { }
                },
                () =>
                {
                    try { BeginInvoke((Action)(() => { nameStart.Text = "Start"; nameStart.Glyph = ""; nameStart.Invalidate(); })); } catch { }
                });
        }
    }
}
