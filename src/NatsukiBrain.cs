// Natsuki's brain: a small AI model (Qwen3 4B Instruct 2507, Apache-2.0) run on this PC by llama.cpp (MIT).
// Nothing is downloaded until you ask for it. Both downloads are checked against their published SHA-256 before use.
// The model only runs while you're chatting, stops after 10 idle minutes, and is tied to CloveStrap so it never
// keeps running after CloveStrap closes.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CloveStrap
{
    static class NatsukiBrain
    {
        // pinned, tested versions
        const string LlamaTag = "b12413";
        const string LlamaUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b12413/llama-b12413-bin-win-cpu-x64.zip";
        const string LlamaSha = "4d2e557f15ecdde59ff1be87f7cd208e43075abde93de29d61e7bbd7cb78004c";
        const long LlamaSize = 25142557;
        const string ModelName = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
        const string ModelUrl = "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
        const string ModelSha = "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597";
        const long ModelSize = 2497281120;
        public const long TotalBytes = LlamaSize + ModelSize;

        static string Dir { get { return Path.Combine(Paths.Base, "Brain"); } }
        public static string CacheDir { get { string d = Path.Combine(Dir, "memory"); Directory.CreateDirectory(d); return d; } }
        static string LlamaDir { get { return Path.Combine(Dir, "llama-" + LlamaTag); } }
        static string ModelFile { get { return Path.Combine(Dir, ModelName); } }
        static string Marker { get { return Path.Combine(Dir, "ready.txt"); } }

        static string ServerExe()
        {
            try { return Directory.Exists(LlamaDir) ? Directory.GetFiles(LlamaDir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault() : null; }
            catch { return null; }
        }

        public static bool Installed { get { return File.Exists(Marker) && File.Exists(ModelFile) && ServerExe() != null; } }

        /// <summary>An older brain is on this PC (so the download offer can say "upgrade").</summary>
        public static bool HasOldBrain
        {
            get
            {
                try { return Directory.Exists(Dir) && Directory.GetFiles(Dir, "*.gguf").Any(f => !string.Equals(Path.GetFileName(f), ModelName, StringComparison.OrdinalIgnoreCase)); }
                catch { return false; }
            }
        }
        public static volatile bool Downloading;
        static volatile bool cancel;
        public static void CancelDownload() { cancel = true; }

        // ------------------------------------------------------------------ install
        /// <summary>Downloads and checks everything. progress(bytesDone, bytesTotal). Returns null when done, or what went wrong.</summary>
        public static string Install(Action<long, long> progress)
        {
            if (Downloading) return "already downloading";
            Downloading = true; cancel = false;
            try
            {
                Directory.CreateDirectory(Dir);
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                if (ServerExe() == null)
                {
                    string zip = Path.Combine(Dir, "llama.zip");
                    string err = Download(LlamaUrl, zip, LlamaSize, LlamaSha, (d) => progress(d, TotalBytes));
                    if (err != null) return err;
                    if (Directory.Exists(LlamaDir)) Directory.Delete(LlamaDir, true);
                    ZipFile.ExtractToDirectory(zip, LlamaDir);
                    File.Delete(zip);
                    if (ServerExe() == null) return "the AI engine download didn't contain llama-server";
                }
                if (!File.Exists(ModelFile))
                {
                    string err = Download(ModelUrl, ModelFile, ModelSize, ModelSha, (d) => progress(LlamaSize + d, TotalBytes));
                    if (err != null) return err;
                }
                File.WriteAllText(Marker, LlamaTag + "\n" + ModelName);
                // an older, smaller brain isn't needed any more
                foreach (var old in Directory.GetFiles(Dir, "*.gguf").Where(f => !string.Equals(Path.GetFileName(f), ModelName, StringComparison.OrdinalIgnoreCase)))
                    try { File.Delete(old); Log.Write("Natsuki: removed old brain " + Path.GetFileName(old)); } catch { }
                progress(TotalBytes, TotalBytes);
                Log.Write("Natsuki: brain installed");
                return null;
            }
            catch (Exception ex) { Log.Write("Natsuki: install failed: " + ex.Message); return ex.Message; }
            finally { Downloading = false; }
        }

        const int Pieces = 6;

        /// <summary>
        /// Big files come down as 6 pieces at once (much faster when one connection is slow), each resumable,
        /// then are joined and SHA-256 checked. Falls back to one connection if the server won't do pieces.
        /// </summary>
        static string Download(string url, string dest, long size, string sha, Action<long> progress)
        {
            if (size < 64L << 20) return DownloadSingle(url, dest, size, sha, progress);
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 16);
            // whatever an older single-connection download already has stays; only the rest is split into pieces
            string part = dest + ".part", baseFile = dest + ".pieces";
            long already = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (already > size) { File.Delete(part); already = 0; }
            long savedBase;
            if (!File.Exists(baseFile) || !long.TryParse(File.ReadAllText(baseFile), out savedBase) || savedBase != already)
            {
                for (int i = 0; i < Pieces; i++) try { File.Delete(dest + ".piece" + i); } catch { }
                File.WriteAllText(baseFile, already.ToString());
            }
            long chunk = (size - already + Pieces - 1) / Pieces;
            var have = new long[Pieces];
            string failure = null;
            var threads = new List<Thread>();
            Func<long> total = () => { long t = already; for (int i = 0; i < Pieces; i++) t += Interlocked.Read(ref have[i]); return t; };
            for (int i = 0; i < Pieces; i++)
            {
                int idx = i;
                long start = already + idx * chunk, end = Math.Min(size, start + chunk) - 1;
                if (start > end) continue;
                string seg = dest + ".piece" + idx;
                have[idx] = File.Exists(seg) ? new FileInfo(seg).Length : 0;
                if (have[idx] > end - start + 1) { File.Delete(seg); have[idx] = 0; }
                var t = new Thread(() =>
                {
                    for (int attempt = 0; attempt < 4 && failure == null && !cancel; attempt++)
                    {
                        try
                        {
                            long from = start + Interlocked.Read(ref have[idx]);
                            if (from > end) return;
                            var req = (HttpWebRequest)WebRequest.Create(url);
                            req.UserAgent = "CloveStrap"; req.AllowAutoRedirect = true;
                            req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                            req.AddRange(from, end);
                            using (var resp = (HttpWebResponse)req.GetResponse())
                            {
                                if (resp.StatusCode != HttpStatusCode.PartialContent) { failure = "no-pieces"; return; }
                                using (var src = resp.GetResponseStream())
                                using (var fs = new FileStream(seg, FileMode.Append, FileAccess.Write))
                                {
                                    var buf = new byte[1 << 16];
                                    int n;
                                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                                    {
                                        if (cancel) return;
                                        fs.Write(buf, 0, n);
                                        Interlocked.Add(ref have[idx], n);
                                    }
                                }
                            }
                            if (start + Interlocked.Read(ref have[idx]) > end) return;
                        }
                        catch (Exception ex) { Log.Write("Natsuki: piece " + idx + " retry: " + ex.Message); Thread.Sleep(1000); }
                    }
                    if (failure == null && !cancel && start + Interlocked.Read(ref have[idx]) <= end) failure = "a piece kept failing";
                }) { IsBackground = true, Name = "natsuki-piece" + idx };
                threads.Add(t);
                t.Start();
            }
            while (threads.Any(t => t.IsAlive)) { Thread.Sleep(250); progress(total()); }
            if (cancel) return "cancelled";
            if (failure == "no-pieces")
            {
                for (int i = 0; i < Pieces; i++) try { File.Delete(dest + ".piece" + i); } catch { }
                try { File.Delete(baseFile); } catch { }
                return DownloadSingle(url, dest, size, sha, progress);
            }
            if (failure != null) return failure;

            // join: what was already there, then the pieces in order, checking the fingerprint as we go
            string joined = dest + ".joining";
            string got;
            using (var outFs = new FileStream(joined, FileMode.Create, FileAccess.Write))
            using (var h = SHA256.Create())
            {
                var buf = new byte[1 << 20];
                var sources = new List<string>();
                if (already > 0) sources.Add(part);
                for (int i = 0; i < Pieces; i++) if (File.Exists(dest + ".piece" + i)) sources.Add(dest + ".piece" + i);
                foreach (var src in sources)
                    using (var fs = File.OpenRead(src))
                    {
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0) { outFs.Write(buf, 0, n); h.TransformBlock(buf, 0, n, null, 0); }
                    }
                h.TransformFinalBlock(new byte[0], 0, 0);
                got = BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
            }
            for (int i = 0; i < Pieces; i++) try { File.Delete(dest + ".piece" + i); } catch { }
            try { File.Delete(part); File.Delete(baseFile); } catch { }
            if (got != sha)
            {
                File.Delete(joined);
                Log.Write("Natsuki: " + Path.GetFileName(dest) + " failed its check (" + got + ")");
                return "the download was damaged; try again";
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(joined, dest);
            progress(size);
            return null;
        }

        /// <summary>One connection, resumable via dest.part, then a SHA-256 check. Wrong hash: the file is deleted.</summary>
        static string DownloadSingle(string url, string dest, long size, string sha, Action<long> progress)
        {
            string part = dest + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > size) { File.Delete(part); have = 0; }
            if (have < size)
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "CloveStrap";
                req.AllowAutoRedirect = true;
                req.Timeout = 30000; req.ReadWriteTimeout = 60000;
                if (have > 0) req.AddRange(have);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent) have = 0;   // server ignored the resume: start over
                    using (var src = resp.GetResponseStream())
                    using (var fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                    {
                        var buf = new byte[1 << 16];
                        long done = have, lastReport = 0;
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (cancel) return "cancelled";
                            fs.Write(buf, 0, n);
                            done += n;
                            if (done - lastReport > 1 << 20) { lastReport = done; progress(done); }
                        }
                    }
                }
            }
            progress(size);
            string got;
            using (var fs = File.OpenRead(part)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (got != sha)
            {
                File.Delete(part);
                Log.Write("Natsuki: " + Path.GetFileName(dest) + " failed its check (" + got + ")");
                return "the download was damaged; try again";
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return null;
        }

        public static void Remove()
        {
            Stop();
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch (Exception ex) { Log.Write("Natsuki: remove failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------ running
        static Process server;
        static int port;
        static DateTime lastUsed;
        static readonly object Gate = new object();
        static Thread idleWatch;

        public static bool Running { get { try { return server != null && !server.HasExited; } catch { return false; } } }

        /// <summary>Starts the model if needed and waits until it answers. Returns null when ready, or what went wrong.</summary>
        public static string Start(Action<string> status)
        {
            lock (Gate)
            {
                lastUsed = DateTime.UtcNow;
                if (Running) return null;
                string exe = ServerExe();
                if (exe == null || !File.Exists(ModelFile)) return "not installed";
                KillOrphans();
                port = FreePort();
                int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2));
                var psi = new ProcessStartInfo(exe,
                    "-m \"" + ModelFile + "\" --host 127.0.0.1 --port " + port + " -c 8192 -t " + threads + " -np 1 --slot-save-path \"" + CacheDir + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe),
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                var tail = new Queue<string>();
                DataReceivedEventHandler keep = (o, e) => { if (e.Data == null) return; lock (tail) { tail.Enqueue(e.Data); while (tail.Count > 12) tail.Dequeue(); } };
                server = new Process { StartInfo = psi };
                server.OutputDataReceived += keep;
                server.ErrorDataReceived += keep;
                server.Start();
                server.BeginOutputReadLine(); server.BeginErrorReadLine();
                Job.Add(server);   // dies with CloveStrap
                try { server.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                Log.Write("Natsuki: brain starting on port " + port + " with " + threads + " threads");
                if (status != null) status("Waking up...");

                var until = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < until)
                {
                    if (server.HasExited)
                    {
                        string why; lock (tail) why = string.Join(" | ", tail);
                        Log.Write("Natsuki: brain stopped while starting: " + why);
                        server = null;
                        return "my brain couldn't start";
                    }
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                        req.Timeout = 1500; req.Proxy = null;
                        using (var resp = (HttpWebResponse)req.GetResponse()) if (resp.StatusCode == HttpStatusCode.OK) break;
                    }
                    catch { }
                    Thread.Sleep(300);
                }
                if (DateTime.UtcNow >= until) { Stop(); return "my brain took too long to start"; }
                Log.Write("Natsuki: brain ready");
                if (idleWatch == null)
                {
                    idleWatch = new Thread(() =>
                    {
                        while (true)
                        {
                            Thread.Sleep(30000);
                            if (Running && (DateTime.UtcNow - lastUsed).TotalMinutes > 15) { Log.Write("Natsuki: resting after 15 idle minutes"); Stop(); }
                        }
                    }) { IsBackground = true, Name = "natsuki-idle" };
                    idleWatch.Start();
                }
                return null;
            }
        }

        /// <summary>
        /// Saves (or loads back) what the brain has already read, as a file in Brain\memory, so it doesn't have to read
        /// all of CloveStrap's knowledge again every time it wakes up. action: "save" or "restore". Returns true if it worked.
        /// </summary>
        public static bool Memory(string action, string fileName)
        {
            try
            {
                lastUsed = DateTime.UtcNow;
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/slots/0?action=" + action);
                req.Method = "POST"; req.ContentType = "application/json"; req.Proxy = null; req.Timeout = 120000;
                byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "filename", fileName } }));
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse()) return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception ex) { Log.Write("Natsuki: memory " + action + " failed: " + ex.Message); return false; }
        }

        public static void Stop()
        {
            try { if (server != null && !server.HasExited) server.Kill(); } catch { }
            server = null;
        }

        static void KillOrphans()
        {
            foreach (var p in Process.GetProcessesByName("llama-server"))
            {
                try { if (p.MainModule.FileName.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
            }
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        /// <summary>
        /// Asks the model. messages: role/content pairs (system first). onText gets the answer so far as it's written.
        /// Returns the full answer, or null if something went wrong.
        /// </summary>
        public static string Chat(List<KeyValuePair<string, string>> messages, Action<string> onText, int maxTokens = 180)
        {
            lastUsed = DateTime.UtcNow;
            var body = new Dictionary<string, object>
            {
                { "messages", messages.Select(m => new Dictionary<string, object> { { "role", m.Key }, { "content", m.Value } }).ToList() },
                { "stream", true }, { "max_tokens", maxTokens }, { "cache_prompt", true }, { "temperature", 0.3 }, { "top_p", 0.9 }, { "repeat_penalty", 1.1 }
            };
            var json = new JavaScriptSerializer();
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/v1/chat/completions");
            req.Method = "POST"; req.ContentType = "application/json"; req.Proxy = null;
            req.Timeout = 120000; req.ReadWriteTimeout = 120000;
            byte[] data = Encoding.UTF8.GetBytes(json.Serialize(body));
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            var sb = new StringBuilder();
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    string payload = line.Substring(5).Trim();
                    if (payload == "[DONE]") break;
                    try
                    {
                        var d = json.Deserialize<Dictionary<string, object>>(payload);
                        var choices = d["choices"] as ArrayList;
                        if (choices == null || choices.Count == 0) continue;
                        var delta = ((Dictionary<string, object>)choices[0])["delta"] as Dictionary<string, object>;
                        object c;
                        if (delta != null && delta.TryGetValue("content", out c) && c != null)
                        {
                            sb.Append(c);
                            lastUsed = DateTime.UtcNow;
                            if (onText != null) onText(sb.ToString());
                        }
                    }
                    catch { }
                }
            }
            lastUsed = DateTime.UtcNow;
            return sb.ToString();
        }

        // ------------------------------------------------------------------ "dies with CloveStrap"
        static class Job
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
            [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref ExtendedLimits info, int size);
            [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [StructLayout(LayoutKind.Sequential)]
            struct BasicLimits
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass, SchedulingClass;
            }
            [StructLayout(LayoutKind.Sequential)]
            struct IoCounters { public ulong a, b, c, d, e, f; }
            [StructLayout(LayoutKind.Sequential)]
            struct ExtendedLimits
            {
                public BasicLimits Basic;
                public IoCounters Io;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }

            static IntPtr handle;

            public static void Add(Process p)
            {
                try
                {
                    if (handle == IntPtr.Zero)
                    {
                        handle = CreateJobObject(IntPtr.Zero, null);
                        var info = new ExtendedLimits();
                        info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        SetInformationJobObject(handle, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimits)));
                    }
                    AssignProcessToJobObject(handle, p.Handle);
                }
                catch (Exception ex) { Log.Write("Natsuki: job: " + ex.Message); }
            }
        }
    }
}
