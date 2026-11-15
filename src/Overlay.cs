// CloveStrap in-game overlay: FPS (ETW, no injection), network quality, system stats, crosshair.
// Nothing here touches Roblox's memory - it only reads Windows counters, Roblox's log files and DXGI present events.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CloveStrap
{
    static class Elevation
    {
        public static bool IsAdmin()
        {
            try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------ FPS via ETW (DXGI Present events), like PresentMon
    static class FrameCounter
    {
        static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        const string SessionName = "CloveStrapFrames";
        const ushort PresentStart = 42;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void RecordCallback(IntPtr record);
        static RecordCallback callback;
        static IntPtr props, logfile, loggerName;
        static ulong session, trace;
        static long dxgiLo, dxgiHi;
        static readonly object Lock = new object();
        static readonly Dictionary<int, Queue<long>> frames = new Dictionary<int, Queue<long>>();
        public static string Status = "off";
        public static bool Running { get; private set; }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")] static extern int StartTrace(out ulong h, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")] static extern int ControlTrace(ulong h, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern int EnableTraceEx2(ulong h, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr param);
        [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true)] static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern int CloseTrace(ulong h);

        // EVENT_TRACE_PROPERTIES (x64: 120 bytes) followed by room for the session name
        static IntPtr MakeProps()
        {
            const int size = 120 + 512;
            var p = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size);         // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);           // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, 0x20000);     // WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 8);           // BufferSize (KB)
            Marshal.WriteInt32(p, 52, 4);           // MinimumBuffers
            Marshal.WriteInt32(p, 56, 32);          // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x100);       // EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);           // FlushTimer (s)
            Marshal.WriteInt32(p, 116, 120);        // LoggerNameOffset
            return p;
        }

        public static bool Start()
        {
            if (Running) return true;
            if (IntPtr.Size != 8) { Status = "needs 64-bit Windows"; return false; }
            if (!Elevation.IsAdmin()) { Status = "needs admin"; return false; }
            try
            {
                byte[] gb = DxgiProvider.ToByteArray();
                dxgiLo = BitConverter.ToInt64(gb, 0);
                dxgiHi = BitConverter.ToInt64(gb, 8);

                var stale = MakeProps();
                ControlTrace(0, SessionName, stale, 1);   // stop a session left over from a crash
                Marshal.FreeHGlobal(stale);

                props = MakeProps();
                int err = StartTrace(out session, SessionName, props);
                if (err != 0) { Status = "StartTrace error " + err; return false; }

                Guid g = DxgiProvider;
                err = EnableTraceEx2(session, ref g, 1, 4, 0x8000000000000001UL, 0, 0, IntPtr.Zero);
                if (err != 0) { Status = "EnableTrace error " + err; Stop(); return false; }

                // EVENT_TRACE_LOGFILEW (x64: 448 bytes)
                callback = OnRecord;
                logfile = Marshal.AllocHGlobal(448);
                Marshal.Copy(new byte[448], 0, logfile, 448);
                loggerName = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(logfile, 8, loggerName);                                   // LoggerName
                Marshal.WriteInt32(logfile, 28, 0x100 | 0x10000000);                           // REAL_TIME | EVENT_RECORD
                Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(callback)); // EventRecordCallback
                trace = OpenTrace(logfile);
                if (trace == ulong.MaxValue) { Status = "OpenTrace error " + Marshal.GetLastWin32Error(); Stop(); return false; }

                var t = new Thread(() =>
                {
                    try { ProcessTrace(new[] { trace }, 1, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception ex) { Log.Write("ProcessTrace failed: " + ex.Message); }
                    Running = false;
                });
                t.IsBackground = true;
                t.Start();
                Running = true;
                Status = "on";
                return true;
            }
            catch (Exception ex) { Status = ex.Message; Log.Write("FrameCounter: " + ex); return false; }
        }

        public static void Stop()
        {
            try
            {
                if (session != 0)
                {
                    var p = MakeProps();
                    ControlTrace(0, SessionName, p, 1);
                    Marshal.FreeHGlobal(p);
                }
            }
            catch { }
            try { if (trace != 0 && trace != ulong.MaxValue) CloseTrace(trace); } catch { }
            Running = false;
        }

        static void OnRecord(IntPtr r)
        {
            if ((ushort)Marshal.ReadInt16(r, 40) != PresentStart) return;
            if (Marshal.ReadInt64(r, 24) != dxgiLo || Marshal.ReadInt64(r, 32) != dxgiHi) return;
            int pid = Marshal.ReadInt32(r, 12);
            long ts = Marshal.ReadInt64(r, 16);   // FILETIME (100 ns)
            lock (Lock)
            {
                Queue<long> q;
                if (!frames.TryGetValue(pid, out q)) { q = new Queue<long>(); frames[pid] = q; }
                q.Enqueue(ts);
                while (q.Count > 0 && ts - q.Peek() > 60000000L) q.Dequeue();   // keep 6 s
                if (q.Count > 6000) q.Dequeue();
            }
        }

        public static int[] Pids()
        {
            lock (Lock) return frames.Keys.ToArray();
        }

        /// <summary>FPS over the last second, 1% low over the last 5 s and recent frame times (ms).</summary>
        public static bool Get(int pid, out double fps, out double low1, out float[] recent)
        {
            fps = 0; low1 = 0; recent = new float[0];
            long[] a;
            lock (Lock)
            {
                Queue<long> q;
                if (!frames.TryGetValue(pid, out q) || q.Count < 3) return false;
                a = q.ToArray();
            }
            long last = a[a.Length - 1];
            if (DateTime.UtcNow.ToFileTimeUtc() - last > 30000000L) return true;   // nothing for 3 s: paused/minimised

            int i = a.Length - 1;
            while (i > 0 && last - a[i - 1] <= 10000000L) i--;
            int n = a.Length - 1 - i;
            if (n > 0) fps = n / ((last - a[i]) / 1e7);

            var ft = new List<float>(a.Length);
            for (int k = 1; k < a.Length; k++) ft.Add((a[k] - a[k - 1]) / 10000f);
            if (ft.Count > 0)
            {
                var sorted = ft.OrderByDescending(x => x).ToList();
                int worst = Math.Max(1, sorted.Count / 100);
                double avg = sorted.Take(worst).Average();
                if (avg > 0) low1 = 1000.0 / avg;
            }
            recent = ft.Skip(Math.Max(0, ft.Count - 120)).ToArray();
            return true;
        }
    }

    // ------------------------------------------------------------------ CPU / GPU / RAM
    class SysStats
    {
        PerformanceCounter cpuTotal;
        readonly Dictionary<string, PerformanceCounter> gpu = new Dictionary<string, PerformanceCounter>();
        DateTime gpuRefresh = DateTime.MinValue;
        int gpuPid = -1, lastPid = -1;
        TimeSpan lastCpu;
        DateTime lastSample;
        public volatile int Cpu = -1, RobloxCpu = -1, Gpu = -1;
        public long Mem = -1;

        public SysStats()
        {
            try { cpuTotal = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpuTotal.NextValue(); }
            catch
            {
                try { cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpuTotal.NextValue(); }
                catch { cpuTotal = null; }
            }
        }

        public void Sample(int pid)
        {
            try { if (cpuTotal != null) Cpu = (int)Math.Min(100, Math.Round(cpuTotal.NextValue())); } catch { Cpu = -1; }
            if (pid <= 0) { RobloxCpu = Gpu = -1; Mem = -1; return; }

            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    Mem = p.WorkingSet64;
                    var now = DateTime.UtcNow;
                    var cpu = p.TotalProcessorTime;
                    if (pid == lastPid && lastSample != DateTime.MinValue)
                    {
                        double ms = (now - lastSample).TotalMilliseconds;
                        if (ms > 0) RobloxCpu = (int)Math.Min(100, Math.Round((cpu - lastCpu).TotalMilliseconds / ms / Environment.ProcessorCount * 100));
                    }
                    lastPid = pid; lastCpu = cpu; lastSample = now;
                }
            }
            catch { Mem = -1; RobloxCpu = -1; }

            try
            {
                if (pid != gpuPid || (DateTime.UtcNow - gpuRefresh).TotalSeconds > 5)
                {
                    var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                        .Where(n => n.StartsWith("pid_" + pid + "_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var k in gpu.Keys.Where(k => !names.Contains(k)).ToList()) { gpu[k].Dispose(); gpu.Remove(k); }
                    foreach (var n in names.Where(n => !gpu.ContainsKey(n)))
                    {
                        var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n);
                        c.NextValue();
                        gpu[n] = c;
                    }
                    gpuPid = pid; gpuRefresh = DateTime.UtcNow;
                }
                float sum = 0;
                foreach (var c in gpu.Values) { try { sum += c.NextValue(); } catch { } }
                Gpu = gpu.Count == 0 ? -1 : (int)Math.Min(100, Math.Round(sum));
            }
            catch { Gpu = -1; }
        }
    }

    // ------------------------------------------------------------------ server, route latency, jitter, packet loss
    class NetStats
    {
        public volatile string ServerIp, Location, Hop;
        public DateTime? JoinedUtc;
        public volatile int Ms = -1, Jitter = -1;
        public double Loss = -1;
        string logFile;
        long logPos;
        readonly List<int> samples = new List<int>();   // -1 = lost
        static readonly Dictionary<string, string> locCache = new Dictionary<string, string>();
        static readonly Regex Udmux = new Regex(@"UDMUX Address = ([0-9\.]+), Port");
        static readonly Regex Joining = new Regex(@"! Joining game '[^']+' place ([0-9]+)");
        bool probing;

        public void Poll()
        {
            ReadLog();
            string ip = ServerIp;
            if (ip == null) { Ms = Jitter = -1; Loss = -1; return; }
            if (Hop == null) return;
            int r = -1;
            try
            {
                using (var p = new Ping())
                {
                    var rep = p.Send(Hop, 1000);
                    if (rep.Status == IPStatus.Success) r = (int)rep.RoundtripTime;
                }
            }
            catch { }
            lock (samples)
            {
                samples.Add(r);
                if (samples.Count > 30) samples.RemoveAt(0);
                var ok = samples.Where(x => x >= 0).ToList();
                Loss = samples.Count == 0 ? -1 : 100.0 * samples.Count(x => x < 0) / samples.Count;
                if (ok.Count > 0)
                {
                    var last5 = ok.Skip(Math.Max(0, ok.Count - 5)).OrderBy(x => x).ToList();
                    Ms = last5[last5.Count / 2];
                    if (ok.Count > 1)
                    {
                        double j = 0;
                        for (int i = 1; i < ok.Count; i++) j += Math.Abs(ok[i] - ok[i - 1]);
                        Jitter = (int)Math.Round(j / (ok.Count - 1));
                    }
                }
            }
        }

        void ReadLog()
        {
            try
            {
                string dir = Path.Combine(Paths.RobloxData, "logs");
                if (!Directory.Exists(dir)) return;
                var newest = new DirectoryInfo(dir).GetFiles("*.log")
                    .Where(f => f.Name.IndexOf("_Player_", StringComparison.OrdinalIgnoreCase) >= 0 && f.Name.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (newest == null) return;
                if (newest.FullName != logFile) { logFile = newest.FullName; logPos = 0; SetServer(null, null); }
                using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < logPos) logPos = 0;
                    fs.Position = logPos;
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.IndexOf("Joining game", StringComparison.Ordinal) >= 0 && Joining.IsMatch(line)) SetServer(null, null);
                            else if (line.IndexOf("UDMUX Address", StringComparison.Ordinal) >= 0)
                            {
                                var m = Udmux.Match(line);
                                if (m.Success) SetServer(m.Groups[1].Value, LineTime(line));
                            }
                            else if (line.IndexOf("Time to disconnect replication data", StringComparison.Ordinal) >= 0) SetServer(null, null);
                        }
                        logPos = fs.Length;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Overlay log read: " + ex.Message); }
        }

        static DateTime? LineTime(string line)
        {
            DateTime t;
            if (line.Length > 24 && DateTime.TryParse(line.Substring(0, 24), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return t;
            return DateTime.UtcNow;
        }

        void SetServer(string ip, DateTime? joined)
        {
            if (ip == ServerIp) return;
            ServerIp = ip; JoinedUtc = joined; Hop = null; Location = null;
            lock (samples) samples.Clear();
            Ms = Jitter = -1; Loss = -1;
            if (ip == null) return;
            string target = ip;
            new Thread(() => Probe(target)) { IsBackground = true }.Start();
        }

        void Probe(string ip)
        {
            if (probing) return;
            probing = true;
            try
            {
                string loc;
                lock (locCache) locCache.TryGetValue(ip, out loc);
                if (loc == null)
                {
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "CloveStrap/1.0";
                            var o = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString("https://ipinfo.io/" + ip + "/json"));
                            object city, country;
                            o.TryGetValue("city", out city); o.TryGetValue("country", out country);
                            loc = string.Join(", ", new[] { city as string, country as string }.Where(x => !string.IsNullOrEmpty(x)));
                        }
                    }
                    catch { loc = ""; }
                    lock (locCache) locCache[ip] = loc;
                }
                if (ServerIp == ip) Location = loc;

                // Roblox servers drop ping, so find the last router on the way that answers.
                string hop = null; int misses = 0;
                using (var p = new Ping())
                {
                    var buf = new byte[32];
                    for (int ttl = 1; ttl <= 24 && ServerIp == ip; ttl++)
                    {
                        var rep = p.Send(ip, 700, buf, new PingOptions(ttl, true));
                        if (rep.Status == IPStatus.Success) { hop = ip; break; }
                        if (rep.Status == IPStatus.TtlExpired && rep.Address != null) { hop = rep.Address.ToString(); misses = 0; }
                        else if (++misses >= 3 && hop != null) break;
                    }
                }
                if (ServerIp == ip) Hop = hop;
                Log.Write("Overlay: server " + ip + " (" + loc + "), measuring via " + (hop ?? "nothing"));
            }
            catch (Exception ex) { Log.Write("Overlay probe: " + ex.Message); }
            finally { probing = false; }
        }
    }

    // ------------------------------------------------------------------ what the overlay shows
    class OverlayData
    {
        public bool FpsAvailable;
        public string FpsNote;
        public double Fps = -1, Low = -1;
        public float[] Frames = new float[0];
        public int Target = 60;
        public int NetMs = -1, Jitter = -1;
        public double Loss = -1;
        public string Server;
        public int Cpu = -1, Gpu = -1;
        public long Mem = -1;
        public TimeSpan? Session;

        public static OverlayData Sample(int hz)
        {
            var rnd = new Random(7);
            var f = new float[120];
            for (int i = 0; i < f.Length; i++) f[i] = 1000f / hz + (float)(rnd.NextDouble() - 0.5) * 1.6f + (i == 70 ? 9 : 0) + (i == 71 ? 4 : 0);
            return new OverlayData
            {
                FpsAvailable = true, Fps = hz - 2, Low = hz * 0.8, Frames = f, Target = hz,
                NetMs = 18, Jitter = 2, Loss = 0, Server = "Ashburn, US", Cpu = 24, Gpu = 61, Mem = 2900L << 20,
                Session = TimeSpan.FromMinutes(42.3)
            };
        }
    }

    static class OverlayItems
    {
        public static readonly string[] Keys = { "fps", "low", "graph", "net", "server", "cpu", "gpu", "ram", "session", "clock" };
        public static readonly string[] Labels = { "FPS", "1% low", "Frametimes", "Network", "Server", "CPU", "GPU", "RAM", "Session", "Clock" };
        public const string Default = "fps,low,graph,net,server,cpu,gpu,session";

        public static HashSet<string> Parse(string s)
        {
            return new HashSet<string>((s ?? Default).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()));
        }

        public static bool NeedsFps(HashSet<string> items)
        {
            return items.Contains("fps") || items.Contains("low") || items.Contains("graph");
        }
    }

    // ------------------------------------------------------------------ drawing the overlay panel
    static class OverlayRenderer
    {
        static readonly Font Big = new Font("Segoe UI Semibold", 19f, GraphicsUnit.Point);
        static readonly Font Val = new Font("Segoe UI Semibold", 9.5f, GraphicsUnit.Point);
        static readonly Font Lbl = MonoFont(7f);
        static readonly Font Sub = new Font("Segoe UI", 7.5f, GraphicsUnit.Point);

        static Font MonoFont(float size)
        {
            var f = new Font("Cascadia Mono", size, GraphicsUnit.Point);
            if (f.Name != "Cascadia Mono") { f.Dispose(); f = new Font("Consolas", size, GraphicsUnit.Point); }
            return f;
        }

        static readonly Color LabelC = Color.FromArgb(150, 150, 160);
        static readonly Color ValueC = Color.FromArgb(240, 240, 244);
        static readonly Color WarnC = Color.FromArgb(250, 190, 80);
        static readonly Color BadC = Color.FromArgb(250, 110, 110);

        public static Bitmap Render(OverlayData d, HashSet<string> items, Color accent)
        {
            const int W = 200, Pad = 12, RowH = 20;
            var rows = new List<Action<Graphics, int>>();
            var heights = new List<int>();
            var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

            Action<Graphics, int, string, string, Color> kv = (g, y, label, value, color) =>
            {
                using (var lb = new SolidBrush(LabelC)) g.DrawString(label, Lbl, lb, new RectangleF(Pad, y, 80, RowH), left);
                using (var vb = new SolidBrush(color)) g.DrawString(value, Val, vb, new RectangleF(Pad, y, W - Pad * 2, RowH), right);
            };

            bool fps = items.Contains("fps"), low = items.Contains("low");
            if (fps || low)
            {
                heights.Add(42);
                rows.Add((g, y) =>
                {
                    if (!d.FpsAvailable)
                    {
                        using (var lb = new SolidBrush(LabelC)) g.DrawString("FPS", Lbl, lb, new RectangleF(Pad, y, 80, 42), left);
                        using (var vb = new SolidBrush(WarnC)) g.DrawString(d.FpsNote ?? "unavailable", Val, vb, new RectangleF(Pad, y, W - Pad * 2, 42), right);
                        return;
                    }
                    Color fc = d.Fps >= 0 && d.Fps < d.Target * 0.5 ? WarnC : accent;
                    if (fps)
                    {
                        string v = d.Fps < 0 ? "--" : Math.Round(d.Fps).ToString();
                        using (var b = new SolidBrush(fc)) g.DrawString(v, Big, b, Pad - 3, y + 2);
                        float vw = g.MeasureString(v, Big).Width;
                        using (var lb = new SolidBrush(LabelC)) g.DrawString("FPS", Lbl, lb, Pad - 3 + vw - 4, y + 22);
                    }
                    if (low)
                    {
                        using (var lb = new SolidBrush(LabelC)) g.DrawString("1% LOW", Lbl, lb, new RectangleF(Pad, y + 4, W - Pad * 2, 14), right);
                        using (var vb = new SolidBrush(ValueC)) g.DrawString(d.Low < 0 ? "--" : Math.Round(d.Low).ToString(), Val, vb, new RectangleF(Pad, y + 18, W - Pad * 2, 20), right);
                    }
                });
            }
            if (items.Contains("graph") && d.FpsAvailable)
            {
                heights.Add(34);
                rows.Add((g, y) =>
                {
                    var r = new RectangleF(Pad, y + 4, W - Pad * 2, 24);
                    using (var bg = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(bg, r);
                    if (d.Frames.Length < 2) return;
                    float target = 1000f / Math.Max(1, d.Target);
                    float max = Math.Max(target * 2.2f, d.Frames.Max());
                    var pts = new PointF[d.Frames.Length];
                    for (int i = 0; i < pts.Length; i++)
                        pts[i] = new PointF(r.X + r.Width * i / (pts.Length - 1), r.Bottom - Math.Min(1f, d.Frames[i] / max) * r.Height);
                    using (var tp = new Pen(Color.FromArgb(60, 255, 255, 255)) { DashStyle = DashStyle.Dot })
                        g.DrawLine(tp, r.X, r.Bottom - target / max * r.Height, r.Right, r.Bottom - target / max * r.Height);
                    using (var p = new Pen(accent, 1.4f)) g.DrawLines(p, pts);
                    using (var lb = new SolidBrush(LabelC))
                        g.DrawString(d.Frames[d.Frames.Length - 1].ToString("0.0") + " ms", Lbl, lb, new RectangleF(r.X, r.Y - 1, r.Width - 2, 12), new StringFormat { Alignment = StringAlignment.Far });
                });
            }
            if (items.Contains("net"))
            {
                heights.Add(RowH);
                rows.Add((g, y) =>
                {
                    string v = d.NetMs < 0 ? "--" : d.NetMs + " ms";
                    Color c = d.NetMs > 180 || d.Loss > 2 ? BadC : d.NetMs > 100 || d.Jitter > 15 ? WarnC : ValueC;
                    kv(g, y, "NET", v, c);
                    if (d.NetMs >= 0)
                    {
                        string extra = "±" + Math.Max(0, d.Jitter) + "  " + (d.Loss < 0 ? "0" : Math.Round(d.Loss).ToString()) + "% loss";
                        float vw = g.MeasureString(v, Val).Width;
                        using (var sb = new SolidBrush(LabelC)) g.DrawString(extra, Sub, sb, new RectangleF(Pad, y, W - Pad * 2 - vw - 2, RowH), right);
                    }
                });
            }
            if (items.Contains("server")) { heights.Add(RowH); rows.Add((g, y) => kv(g, y, "SERVER", string.IsNullOrEmpty(d.Server) ? "--" : d.Server, ValueC)); }
            bool cpu = items.Contains("cpu"), gpu = items.Contains("gpu");
            if (cpu || gpu)
            {
                heights.Add(RowH);
                rows.Add((g, y) =>
                {
                    if (cpu && gpu)
                    {
                        int half = (W - Pad * 2) / 2;
                        using (var lb = new SolidBrush(LabelC)) { g.DrawString("CPU", Lbl, lb, new RectangleF(Pad, y, 40, RowH), left); g.DrawString("GPU", Lbl, lb, new RectangleF(Pad + half + 8, y, 40, RowH), left); }
                        using (var vb = new SolidBrush(ValueC))
                        {
                            g.DrawString(d.Cpu < 0 ? "--" : d.Cpu + "%", Val, vb, new RectangleF(Pad, y, half - 8, RowH), right);
                            g.DrawString(d.Gpu < 0 ? "--" : d.Gpu + "%", Val, vb, new RectangleF(Pad + half, y, half, RowH), right);
                        }
                    }
                    else if (cpu) kv(g, y, "CPU", d.Cpu < 0 ? "--" : d.Cpu + "%", ValueC);
                    else kv(g, y, "GPU", d.Gpu < 0 ? "--" : d.Gpu + "%", ValueC);
                });
            }
            if (items.Contains("ram")) { heights.Add(RowH); rows.Add((g, y) => kv(g, y, "RAM", d.Mem < 0 ? "--" : (d.Mem / 1073741824.0).ToString("0.0") + " GB", ValueC)); }
            if (items.Contains("session"))
            {
                heights.Add(RowH);
                rows.Add((g, y) => kv(g, y, "SESSION", d.Session.HasValue ? ((int)d.Session.Value.TotalHours).ToString("00") + ":" + d.Session.Value.Minutes.ToString("00") + ":" + d.Session.Value.Seconds.ToString("00") : "--", ValueC));
            }
            if (items.Contains("clock")) { heights.Add(RowH); rows.Add((g, y) => kv(g, y, "TIME", DateTime.Now.ToString("HH:mm"), ValueC)); }

            int h = Pad * 2 - 4 + heights.Sum();
            if (rows.Count == 0) h = 1;
            var bmp = new Bitmap(W, Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                if (rows.Count == 0) return bmp;
                var rr = new RectangleF(0.5f, 0.5f, W - 1.5f, h - 1.5f);
                using (var path = Theme.Round(rr, 10))
                {
                    using (var b = new SolidBrush(Color.FromArgb(205, 12, 12, 15))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(38, 255, 255, 255))) g.DrawPath(p, path);
                }
                using (var ab = new SolidBrush(accent)) g.FillRectangle(ab, 0, 12, 2.5f, Math.Min(26, h - 24));
                int yy = Pad - 2;
                for (int i = 0; i < rows.Count; i++) { rows[i](g, yy); yy += heights[i]; }
            }
            return bmp;
        }

        public static Bitmap Crosshair(string style, int size, Color c)
        {
            int s = new[] { 10, 16, 24 }[Math.Max(0, Math.Min(2, size))];
            int box = s * 2 + 8;
            var bmp = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float cx = box / 2f, cy = box / 2f;
                using (var outline = new Pen(Color.FromArgb(200, 0, 0, 0), 3.2f))
                using (var pen = new Pen(c, 1.6f))
                using (var fill = new SolidBrush(c))
                using (var ob = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                {
                    if (style == "cross")
                    {
                        float gap = s * 0.3f, len = s;
                        foreach (var pen2 in new[] { outline, pen })
                        {
                            g.DrawLine(pen2, cx - len, cy, cx - gap, cy); g.DrawLine(pen2, cx + gap, cy, cx + len, cy);
                            g.DrawLine(pen2, cx, cy - len, cx, cy - gap); g.DrawLine(pen2, cx, cy + gap, cx, cy + len);
                        }
                    }
                    else if (style == "circle")
                    {
                        float r = s * 0.7f;
                        g.DrawEllipse(outline, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.FillEllipse(ob, cx - 2f, cy - 2f, 4f, 4f);
                        g.FillEllipse(fill, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);
                    }
                    else
                    {
                        float r = s * 0.22f;
                        g.FillEllipse(ob, cx - r - 1.2f, cy - r - 1.2f, (r + 1.2f) * 2, (r + 1.2f) * 2);
                        g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                    }
                }
            }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ click-through layered window
    class LayeredWindow : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; public PT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; public SZ(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        public bool Shown2;

        public LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(1, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000 | 0x8;   // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Present(Bitmap bmp, Point pos)
        {
            if (!IsHandleCreated) CreateHandle();
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hb);
                var size = new SZ(bmp.Width, bmp.Height);
                var src = new PT(0, 0);
                var dst = new PT(pos.X, pos.Y);
                var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(mem, old);
                if (hb != IntPtr.Zero) DeleteObject(hb);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
            if (!Shown2) { ShowWindow(Handle, 4); Shown2 = true; }   // SW_SHOWNOACTIVATE
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // keep topmost
        }

        public void HideNow()
        {
            if (Shown2) { ShowWindow(Handle, 0); Shown2 = false; }
        }
    }

    // ------------------------------------------------------------------ global hotkey
    class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        readonly Action onPress;

        public HotkeyWindow(Action onPress)
        {
            this.onPress = onPress;
            CreateHandle(new CreateParams());
            RegisterHotKey(Handle, 1, 0x0002 | 0x0004 | 0x4000, (uint)Keys.O);   // Ctrl+Shift+O, no repeat
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) onPress();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { UnregisterHotKey(Handle, 1); DestroyHandle(); } catch { }
        }
    }

    // ------------------------------------------------------------------ overlay process
    class OverlayHost : ApplicationContext
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Point p);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        Settings s;
        HashSet<string> items;
        DateTime settingsTime;
        readonly LayeredWindow panel = new LayeredWindow(), cross = new LayeredWindow();
        readonly Timer timer = new Timer { Interval = 250 };
        readonly HotkeyWindow hotkey;
        readonly SysStats sys = new SysStats();
        readonly NetStats net = new NetStats();
        volatile int targetPid;
        volatile bool hidden, quit;
        DateTime lastRoblox = DateTime.UtcNow;
        Bitmap crossBmp;
        string crossKey;
        readonly Dictionary<int, bool> robloxPid = new Dictionary<int, bool>();
        readonly int hz = Display.RefreshRate();

        public OverlayHost()
        {
            LoadSettings();
            if (OverlayItems.NeedsFps(items)) FrameCounter.Start();
            Log.Write("Overlay started. FPS counter: " + FrameCounter.Status);
            hotkey = new HotkeyWindow(() => hidden = !hidden);
            timer.Tick += (o, e) => Tick();
            timer.Start();

            var worker = new Thread(() =>
            {
                while (!quit)
                {
                    try { sys.Sample(targetPid); net.Poll(); } catch { }
                    Thread.Sleep(1000);
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void LoadSettings()
        {
            s = Settings.Load();
            Theme.SetCustoms(s.CustomThemes);
            Theme.ApplyScheme(s.ColorScheme);
            items = OverlayItems.Parse(s.OverlayItems);
            try { settingsTime = File.GetLastWriteTimeUtc(Paths.SettingsFile); } catch { }
        }

        bool IsRoblox(int pid)
        {
            bool r;
            if (robloxPid.TryGetValue(pid, out r)) return r;
            try { using (var p = Process.GetProcessById(pid)) r = p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase); }
            catch { r = false; }
            if (robloxPid.Count > 200) robloxPid.Clear();
            robloxPid[pid] = r;
            return r;
        }

        int ticks;
        void Tick()
        {
            ticks++;
            if (ticks % 8 == 0)
            {
                try { if (File.GetLastWriteTimeUtc(Paths.SettingsFile) != settingsTime) { LoadSettings(); crossKey = null; } } catch { }
                if (Deployment.RobloxRunning()) lastRoblox = DateTime.UtcNow;
                else if ((DateTime.UtcNow - lastRoblox).TotalSeconds > 60) { Quit(); return; }
                if (!s.OverlayEnabled && !s.ScreenCrosshair) { Quit(); return; }
            }

            IntPtr fg = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (hidden || pid == 0 || !IsRoblox((int)pid) || IsIconic(fg)) { panel.HideNow(); cross.HideNow(); return; }
            targetPid = (int)pid;

            RECT rc;
            if (!GetClientRect(fg, out rc)) return;
            var tl = new Point(0, 0);
            ClientToScreen(fg, ref tl);
            int cw = rc.R - rc.L, ch = rc.B - rc.T;

            var d = new OverlayData { Target = hz, Cpu = sys.Cpu, Gpu = sys.Gpu, Mem = sys.Mem, NetMs = net.Ms, Jitter = net.Jitter, Loss = net.Loss, Server = net.Location };
            if (net.JoinedUtc.HasValue && net.ServerIp != null) d.Session = DateTime.UtcNow - net.JoinedUtc.Value;
            if (OverlayItems.NeedsFps(items))
            {
                if (FrameCounter.Running)
                {
                    double fps, low; float[] fr;
                    if (FrameCounter.Get((int)pid, out fps, out low, out fr)) { d.FpsAvailable = true; d.Fps = fps; d.Low = low; d.Frames = fr; }
                    else { d.FpsAvailable = true; d.Fps = -1; d.Low = -1; }
                }
                else { d.FpsAvailable = false; d.FpsNote = FrameCounter.Status == "needs admin" ? "needs admin" : "unavailable"; }
            }

            if (s.OverlayEnabled)
            {
                using (var bmp = OverlayRenderer.Render(d, items, Theme.Accent))
                {
                    int m = 12, top = 64;   // stay clear of Roblox's top bar
                    string corner = s.OverlayCorner ?? "tl";
                    int x = corner.EndsWith("r") ? tl.X + cw - bmp.Width - m : tl.X + m;
                    int y = corner.StartsWith("b") ? tl.Y + ch - bmp.Height - m : tl.Y + top;
                    if (bmp.Height > 2) panel.Present(bmp, new Point(x, y)); else panel.HideNow();
                }
            }
            else panel.HideNow();

            // screen crosshair: drawn on top of Roblox, so it covers any game's own crosshair and shows in screenshots
            if (s.ScreenCrosshair)
            {
                string key = (s.CrosshairStyle ?? "dot") + s.CrosshairSize + Theme.Accent.ToArgb();
                if (crossBmp == null || key != crossKey)
                {
                    if (crossBmp != null) crossBmp.Dispose();
                    crossBmp = OverlayRenderer.Crosshair(s.CrosshairStyle ?? "dot", s.CrosshairSize, Theme.Accent);
                    crossKey = key;
                }
                cross.Present(crossBmp, new Point(tl.X + (cw - crossBmp.Width) / 2, tl.Y + (ch - crossBmp.Height) / 2));
            }
            else cross.HideNow();
        }

        void Quit()
        {
            quit = true;
            timer.Stop();
            panel.HideNow(); cross.HideNow();
            hotkey.Dispose();
            FrameCounter.Stop();
            ExitThread();
        }
    }

    // ------------------------------------------------------------------ custom shift lock cursor
    static class ShiftLockCursor
    {
        /// <summary>Swaps Roblox's shift lock icon for the chosen crosshair (or restores the original).</summary>
        public static void Apply(Settings s, string versionDir)
        {
            try
            {
                string tex = Path.Combine(versionDir, "content", "textures");
                if (!Directory.Exists(tex)) return;
                string cur = Path.Combine(tex, "MouseLockedCursor.png");
                string bak = Path.Combine(tex, "MouseLockedCursor.voidstrapper-original.png");
                if (s.Crosshair)
                {
                    if (!File.Exists(bak) && File.Exists(cur)) File.Copy(cur, bak);
                    Theme.SetCustoms(s.CustomThemes);
                    using (var b = OverlayRenderer.Crosshair(s.CrosshairStyle, s.CrosshairSize, Theme.Get(s.ColorScheme).Accent))
                        b.Save(cur, System.Drawing.Imaging.ImageFormat.Png);
                }
                else if (File.Exists(bak))
                {
                    File.Copy(bak, cur, true);
                    File.Delete(bak);
                }
            }
            catch (Exception ex) { Log.Write("Shift lock cursor: " + ex.Message); }
        }
    }

    static class OverlayLauncher
    {
        public const string MutexName = "Local\\CloveStrapOverlay";

        public static bool IsRunning()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        /// <summary>Starts the overlay process. Asks Windows for admin only when the FPS counter is on.</summary>
        public static string Start(Settings s, bool force = false)
        {
            if ((!s.OverlayEnabled && !s.ScreenCrosshair && !force) || IsRunning()) return null;
            string exe = Application.ExecutablePath;
            bool wantAdmin = (s.OverlayEnabled || force) && OverlayItems.NeedsFps(OverlayItems.Parse(s.OverlayItems)) && !Elevation.IsAdmin();
            if (wantAdmin)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exe, "--overlay") { UseShellExecute = true, Verb = "runas" });
                    return null;
                }
                catch (Win32Exception) { Log.Write("Overlay: admin declined, starting without FPS"); }
            }
            Process.Start(new ProcessStartInfo(exe, "--overlay") { UseShellExecute = false });
            return wantAdmin ? "declined" : null;
        }

        public static int RunHost()
        {
            bool created;
            using (var m = new Mutex(true, MutexName, out created))
            {
                if (!created) return 0;
                try { Application.Run(new OverlayHost()); }
                finally { FrameCounter.Stop(); }
            }
            return 0;
        }

        /// <summary>Headless check of the FPS counter: writes what it sees to the log.</summary>
        public static int EtwTest(int seconds)
        {
            bool ok = FrameCounter.Start();
            Log.Write("ETW test: start=" + ok + " status=" + FrameCounter.Status + " admin=" + Elevation.IsAdmin());
            if (!ok) return 1;
            for (int i = 0; i < seconds; i++)
            {
                Thread.Sleep(1000);
                foreach (var pid in FrameCounter.Pids())
                {
                    double fps, low; float[] fr;
                    if (FrameCounter.Get(pid, out fps, out low, out fr))
                    {
                        string name = "?";
                        try { using (var p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { }
                        Log.Write(string.Format("ETW test: pid {0} ({1}) fps {2:0.0} low {3:0.0} frames {4}", pid, name, fps, low, fr.Length));
                    }
                }
            }
            FrameCounter.Stop();
            return 0;
        }
    }
}
