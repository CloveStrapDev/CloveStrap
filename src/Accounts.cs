// CloveStrap accounts: sign in once, launch any saved account (multi-instance), follow a player or join a place,
// and "alt mode" which runs that account's Roblox as cheaply as possible.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CloveStrap
{
    // ------------------------------------------------------------------ saved accounts
    class Account
    {
        public long UserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Cookie { get; set; }        // .ROBLOSECURITY, encrypted with Windows DPAPI (only this Windows user can read it)
        public bool AltMode { get; set; }
        public DateTime AddedUtc { get; set; }
    }

    static class AccountStore
    {
        static readonly string FilePath = Path.Combine(Paths.Base, "Accounts.json");
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Voidstrapper.Accounts.v1");
        static readonly object Lock = new object();

        public static bool Demo;   // screenshots only: fake accounts, nothing read or written

        public static List<Account> Load()
        {
            if (Demo)
                return new List<Account>
                {
                    new Account { UserId = 1, Username = "Roblox", DisplayName = "Roblox" },
                    new Account { UserId = 156, Username = "builderman", DisplayName = "builderman", AltMode = true },
                    new Account { UserId = 261, Username = "Shedletsky", DisplayName = "Shedletsky", AltMode = true },
                };
            lock (Lock)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var list = new JavaScriptSerializer().Deserialize<List<Account>>(File.ReadAllText(FilePath));
                        if (list != null) return list.Where(a => a != null && a.UserId > 0).ToList();
                    }
                }
                catch (Exception ex) { Log.Write("Accounts load failed: " + ex.Message); }
                return new List<Account>();
            }
        }

        public static void Save(List<Account> list)
        {
            lock (Lock)
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Pretty(new JavaScriptSerializer().Serialize(list)));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
        }

        public static Account Get(long userId)
        {
            return Load().FirstOrDefault(a => a.UserId == userId);
        }

        public static string Protect(string cookie)
        {
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(cookie), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(Account a)
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(a.Cookie), Entropy, DataProtectionScope.CurrentUser));
        }

        /// <summary>Adds the account, or refreshes its cookie and name if it's already saved. Keeps its alt mode setting.</summary>
        public static Account AddOrUpdate(RobloxUser u, string cookie)
        {
            var list = Load();
            var a = list.FirstOrDefault(x => x.UserId == u.Id);
            if (a == null) { a = new Account { UserId = u.Id, AddedUtc = DateTime.UtcNow }; list.Add(a); }
            a.Username = u.Name;
            a.DisplayName = u.DisplayName;
            a.Cookie = Protect(cookie);
            Save(list);
            return a;
        }

        public static void Remove(long userId)
        {
            var list = Load();
            list.RemoveAll(a => a.UserId == userId);
            Save(list);
            Log.Write("Removed account " + userId);
        }

        public static void SetAlt(long userId, bool on)
        {
            var list = Load();
            var a = list.FirstOrDefault(x => x.UserId == userId);
            if (a == null) return;
            a.AltMode = on;
            Save(list);
        }
    }

    // ------------------------------------------------------------------ roblox web api
    class RobloxUser
    {
        public long Id;
        public string Name, DisplayName;
    }

    class RobloxAuthException : Exception
    {
        public RobloxAuthException(string msg) : base(msg) { }
    }

    static class RobloxApi
    {
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CloveStrap/2.0";

        static HttpWebRequest Request(string method, string url, string cookie)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = UserAgent;
            req.Timeout = 15000;
            req.Accept = "application/json";
            if (cookie != null) req.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;
            return req;
        }

        static string ReadBody(WebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        static void WriteBody(HttpWebRequest req, string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json ?? "");
            req.ContentType = "application/json";
            req.ContentLength = b.Length;
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
        }

        static Dictionary<string, object> Parse(string json)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        }

        static long ToLong(object o)
        {
            return Convert.ToInt64(o, CultureInfo.InvariantCulture);
        }

        /// <summary>Who this cookie belongs to. Throws RobloxAuthException when the cookie is expired or signed out.</summary>
        public static RobloxUser Authenticated(string cookie)
        {
            var req = Request("GET", "https://users.roblox.com/v1/users/authenticated", cookie);
            try
            {
                using (var resp = req.GetResponse())
                {
                    var d = Parse(ReadBody(resp));
                    return new RobloxUser { Id = ToLong(d["id"]), Name = d["name"] as string, DisplayName = d["displayName"] as string };
                }
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.Unauthorized)
                    throw new RobloxAuthException("This account was signed out. Remove it and sign in again.");
                throw;
            }
        }

        /// <summary>User id for a username (or display-less exact name). Returns 0 when nobody has that name.</summary>
        public static long UserIdFromName(string name)
        {
            var req = Request("POST", "https://users.roblox.com/v1/usernames/users", null);
            WriteBody(req, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "usernames", new[] { name } }, { "excludeBannedUsers", true } }));
            using (var resp = req.GetResponse())
            {
                var d = Parse(ReadBody(resp));
                var data = d["data"] as System.Collections.ArrayList;
                if (data == null || data.Count == 0) return 0;
                return ToLong(((Dictionary<string, object>)data[0])["id"]);
            }
        }

        public static string UserName(long id)
        {
            try
            {
                using (var resp = Request("GET", "https://users.roblox.com/v1/users/" + id, null).GetResponse())
                    return Parse(ReadBody(resp))["name"] as string;
            }
            catch { return null; }
        }

        /// <summary>One-time ticket Roblox exchanges for a signed-in game session.</summary>
        public static string AuthTicket(string cookie)
        {
            string csrf = "";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var req = Request("POST", "https://auth.roblox.com/v1/authentication-ticket", cookie);
                req.Referer = "https://www.roblox.com/";
                req.Headers["Origin"] = "https://www.roblox.com";
                req.Headers["RBXAuthenticationNegotiation"] = "1";
                if (csrf.Length > 0) req.Headers["x-csrf-token"] = csrf;
                WriteBody(req, "");
                try
                {
                    using (var resp = req.GetResponse())
                    {
                        string t = resp.Headers["rbx-authentication-ticket"];
                        if (string.IsNullOrEmpty(t)) throw new Exception("Roblox didn't hand out a login ticket.");
                        return t;
                    }
                }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse;
                    if (hr == null) throw;
                    string token = hr.Headers["x-csrf-token"];
                    if (hr.StatusCode == HttpStatusCode.Forbidden && !string.IsNullOrEmpty(token) && csrf.Length == 0) { csrf = token; continue; }
                    if (hr.StatusCode == HttpStatusCode.Unauthorized) throw new RobloxAuthException("This account was signed out. Remove it and sign in again.");
                    string body = "";
                    try { body = ReadBody(hr); } catch { }
                    throw new Exception("Roblox refused the login ticket (" + (int)hr.StatusCode + "). " + body, ex);
                }
            }
            throw new Exception("Roblox refused the login ticket.");
        }

        /// <summary>Headshot for a user, cached for 12 hours. Null when offline.</summary>
        public static Image Headshot(long userId)
        {
            string cacheFile = Path.Combine(Paths.Base, "avatar_" + userId + ".png");
            try
            {
                if (!File.Exists(cacheFile) || (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalHours > 12)
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                        string json = wc.DownloadString("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=" + userId + "&size=150x150&format=Png&isCircular=false");
                        var mu = Regex.Match(json, "\"imageUrl\"\\s*:\\s*\"(https://[^\"]+)\"");
                        if (mu.Success) File.WriteAllBytes(cacheFile, wc.DownloadData(mu.Groups[1].Value));
                    }
                }
                if (File.Exists(cacheFile))
                    using (var fs = File.OpenRead(cacheFile)) using (var tmp = Image.FromStream(fs)) return new Bitmap(tmp);
            }
            catch (Exception ex) { Log.Write("Headshot " + userId + ": " + ex.Message); }
            return null;
        }
    }

    // ------------------------------------------------------------------ sign in through a real browser window
    // Opens Roblox's own login page in a private Edge (or Chrome) window with its own temporary profile, so the user signs in
    // on roblox.com itself (2-step verification and captchas work as normal). CloveStrap reads the session cookie
    // through the browser's local debugging port, closes the window and deletes the temporary profile.
    static class BrowserLogin
    {
        public static string FindBrowser()
        {
            foreach (var exe in new[] { "msedge.exe", "chrome.exe" })
            {
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    try
                    {
                        using (var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        {
                            string p = k == null ? null : k.GetValue("") as string;
                            if (!string.IsNullOrEmpty(p) && File.Exists(p.Trim('"'))) return p.Trim('"');
                        }
                    }
                    catch { }
                }
            }
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var p in new[] { Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"), Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
                                      Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"), Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe") })
                if (File.Exists(p)) return p;
            return null;
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        /// <summary>
        /// Blocks until the user signs in (returns the cookie) or closes the window / cancels (returns null).
        /// testSeconds &gt; 0: headless self-test that only checks the browser plumbing and closes after that many seconds.
        /// </summary>
        public static string Run(Func<bool> cancelled, string startUrl = "https://www.roblox.com/login", int testSeconds = 0)
        {
            string browser = FindBrowser();
            if (browser == null) throw new Exception("Signing in needs Microsoft Edge or Google Chrome, and neither was found.");

            string profile = Path.Combine(Paths.Base, "LoginProfile_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            int port = FreePort();
            string args = string.Format(
                "--user-data-dir=\"{0}\" --remote-debugging-port={1} --remote-allow-origins=http://127.0.0.1:{1} --no-first-run --no-default-browser-check " +
                "--disable-sync --disable-extensions --disable-features=msEdgeSidebarV2,msImplicitSignin,EdgeCollections --window-size=560,780 --app={2}",
                profile, port, startUrl);
            Log.Write("Login: starting " + Path.GetFileName(browser) + " on debug port " + port);
            var proc = Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false });
            var started = DateTime.UtcNow;
            try
            {
                string wsUrl = null;
                while (wsUrl == null)
                {
                    if (cancelled()) return null;
                    if ((DateTime.UtcNow - started).TotalSeconds > 30) throw new Exception("The sign-in window didn't open.");
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString("http://127.0.0.1:" + port + "/json/version"));
                            wsUrl = d["webSocketDebuggerUrl"] as string;
                        }
                    }
                    catch { Thread.Sleep(300); }
                }

                using (var ws = new ClientWebSocket())
                {
                    ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).Wait(10000);
                    if (ws.State != WebSocketState.Open) throw new Exception("Couldn't talk to the sign-in window.");
                    int id = 0;
                    while (true)
                    {
                        if (cancelled()) return null;
                        if (testSeconds > 0 && (DateTime.UtcNow - started).TotalSeconds > testSeconds) { Log.Write("Login test: plumbing OK, browser reported " + LastCookieCount + " cookies"); return ""; }
                        if (ProcessGone(proc, profile)) { Log.Write("Login: window closed by user"); return null; }

                        string cookie = ReadCookie(ws, ++id);
                        if (!string.IsNullOrEmpty(cookie))
                        {
                            try { Send(ws, "{\"id\":" + (++id) + ",\"method\":\"Browser.close\"}"); } catch { }
                            return cookie;
                        }
                        Thread.Sleep(800);
                    }
                }
            }
            finally
            {
                Cleanup(proc, profile, Path.GetFileNameWithoutExtension(browser));
            }
        }

        // Edge sometimes hands the window to a child process; treat the session as alive while anything uses our profile.
        static bool ProcessGone(Process proc, string profile)
        {
            try { if (!proc.HasExited) return false; } catch { }
            try { return !File.Exists(Path.Combine(profile, "lockfile")) || IsUnlocked(Path.Combine(profile, "lockfile")); }
            catch { return true; }
        }

        static bool IsUnlocked(string f)
        {
            try { using (File.Open(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
            catch (IOException) { return false; }
            catch { return true; }
        }

        static void Send(ClientWebSocket ws, string msg)
        {
            var b = Encoding.UTF8.GetBytes(msg);
            ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
        }

        static string Receive(ClientWebSocket ws)
        {
            var buf = new byte[1 << 16];
            var ms = new MemoryStream();
            while (true)
            {
                var t = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (!t.Wait(8000)) throw new TimeoutException("sign-in window stopped responding");
                ms.Write(buf, 0, t.Result.Count);
                if (t.Result.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        static int LastCookieCount = -1;

        static string ReadCookie(ClientWebSocket ws, int id)
        {
            Send(ws, "{\"id\":" + id + ",\"method\":\"Storage.getCookies\"}");
            for (int i = 0; i < 20; i++)
            {
                string msg = Receive(ws);
                if (!msg.Contains("\"id\":" + id + ",") && !msg.Contains("\"id\":" + id + "}")) continue;   // skip events
                var d = new JavaScriptSerializer() { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(msg);
                object r;
                if (!d.TryGetValue("result", out r)) return null;
                var cookies = ((Dictionary<string, object>)r)["cookies"] as System.Collections.ArrayList;
                LastCookieCount = cookies == null ? -1 : cookies.Count;
                if (cookies == null) return null;
                foreach (Dictionary<string, object> c in cookies)
                {
                    string domain = c["domain"] as string ?? "";
                    if ((c["name"] as string) == ".ROBLOSECURITY" && domain.EndsWith("roblox.com", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = c["value"] as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                return null;
            }
            return null;
        }

        static void Cleanup(Process proc, string profile, string exeName)
        {
            try
            {
                if (!proc.HasExited) proc.WaitForExit(2500);
                // kill anything still running from the temporary profile
                foreach (var p in Process.GetProcessesByName(exeName))
                {
                    try { if (CommandLineOf(p.Id).IndexOf(profile, StringComparison.OrdinalIgnoreCase) >= 0) p.Kill(); } catch { }
                }
            }
            catch { }
            new Thread(() =>
            {
                for (int i = 0; i < 20 && Directory.Exists(profile); i++)
                {
                    try { Directory.Delete(profile, true); } catch { Thread.Sleep(500); }
                }
                if (Directory.Exists(profile)) Log.Write("Login: couldn't delete temporary profile " + profile);
            }) { IsBackground = false }.Start();
        }

        static string CommandLineOf(int pid)
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                    foreach (System.Management.ManagementObject o in s.Get())
                        return Convert.ToString(o["CommandLine"]) ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>Removes leftover temporary profiles from a crash.</summary>
        public static void CleanupStale()
        {
            try
            {
                foreach (var d in Directory.GetDirectories(Paths.Base, "LoginProfile_*"))
                    if ((DateTime.Now - Directory.GetLastWriteTime(d)).TotalMinutes > 30) { try { Directory.Delete(d, true); } catch { } }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ what to join
    class JoinTarget
    {
        public string Mode;      // "place" | "follow"
        public long PlaceId;
        public string FollowName;
        public long FollowUserId;
        public string JobId;     // a specific server (server picker), or null

        public string Describe()
        {
            return Mode == "follow" ? "following " + FollowName : "place " + PlaceId + (JobId != null ? ", picked server" : "");
        }

        /// <summary>Parses what the user typed. Accepts a place id or a roblox.com/games/... link; a username or profile link.</summary>
        public static JoinTarget Parse(string mode, string value, out string error)
        {
            error = null;
            value = (value ?? "").Trim();
            var t = new JoinTarget { Mode = mode == "follow" ? "follow" : "place" };
            if (t.Mode == "place")
            {
                var m = Regex.Match(value, @"games/(\d+)");
                string digits = m.Success ? m.Groups[1].Value : value;
                long id;
                if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0) { error = "a place ID (the number in the game's link)"; return null; }
                t.PlaceId = id;
            }
            else
            {
                var m = Regex.Match(value, @"users/(\d+)");
                if (m.Success) { t.FollowUserId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); t.FollowName = "user " + t.FollowUserId; return t; }
                string name = value.TrimStart('@');
                if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,20}$")) { error = "a username to follow"; return null; }
                t.FollowName = name;
            }
            return t;
        }

        public string ToArgs()
        {
            return Mode == "follow"
                ? (FollowUserId > 0 ? "--follow-id " + FollowUserId : "--follow " + FollowName)
                : "--place " + PlaceId + (JobId != null ? " --job " + JobId : "");
        }
    }

    // ------------------------------------------------------------------ launching a saved account
    static class AccountLaunch
    {
        static readonly Random Rng = new Random();

        public static string BuildUri(string ticket, JoinTarget t)
        {
            string tracker = (Rng.Next(100000, 999999).ToString() + Rng.Next(100000, 999999).ToString());
            string join = Guid.NewGuid().ToString();
            string url = t.Mode == "follow"
                ? "https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestFollowUser&browserTrackerId=" + tracker + "&userId=" + t.FollowUserId + "&joinAttemptId=" + join + "&joinAttemptOrigin=followUser"
                : t.JobId != null
                ? "https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId=" + tracker + "&placeId=" + t.PlaceId + "&gameId=" + t.JobId + "&isPlayTogetherGame=false&joinAttemptId=" + join + "&joinAttemptOrigin=publicServerListJoin"
                : "https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=" + tracker + "&placeId=" + t.PlaceId + "&isPlayTogetherGame=false&joinAttemptId=" + join + "&joinAttemptOrigin=PlayButton";
            long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            return "roblox-player:1+launchmode:play+gameinfo:" + ticket + "+launchtime:" + ms + "+placelauncherurl:" + Uri.EscapeDataString(url) +
                   "+browsertrackerid:" + tracker + "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
        }

        /// <summary>Signs in as the account and starts Roblox. Runs on a worker thread of the launching process.</summary>
        public static LaunchResult Run(long userId, JoinTarget t, Action<string, int> progress)
        {
            var acc = AccountStore.Get(userId);
            if (acc == null) throw new Exception("That account isn't saved in CloveStrap any more.");
            string who = acc.DisplayName ?? acc.Username;

            if (t.Mode == "follow" && t.FollowUserId == 0)
            {
                progress("Finding " + t.FollowName + "\nusers.roblox.com  ·  username lookup", -1);
                t.FollowUserId = RobloxApi.UserIdFromName(t.FollowName);
                if (t.FollowUserId == 0) throw new Exception("No Roblox player is called \"" + t.FollowName + "\".");
            }

            progress("Signing in as " + who + "\nauth.roblox.com  ·  login ticket", -1);
            string cookie;
            try { cookie = AccountStore.Unprotect(acc); }
            catch { throw new RobloxAuthException("CloveStrap couldn't unlock this account's sign-in (it was saved by a different Windows user). Sign in again."); }
            string ticket = RobloxApi.AuthTicket(cookie);
            Log.Write("Account launch: " + acc.Username + " (" + acc.UserId + "), " + t.Describe() + (acc.AltMode ? ", alt mode" : ""));

            return Launcher.Run(BuildUri(ticket, t), progress, new LaunchOptions { ForceMultiInstance = true, Alt = acc.AltMode, Label = acc.Username });
        }
    }

    class LaunchOptions
    {
        public bool ForceMultiInstance;
        public bool Alt;
        public string Label;
    }

    // ------------------------------------------------------------------ multi-instance
    static class MultiInstance
    {
        // Roblox closes older clients through these named objects. Holding both as mutexes stops that.
        static readonly string[] Names = { "ROBLOX_singletonMutex", "ROBLOX_singletonEvent" };

        public static List<Mutex> Acquire(Action<string> detail = null)
        {
            if (Deployment.RobloxRunning() && !Active())
            {
                // a Roblox that was opened the normal way is holding the lock: take it over instead of letting it get closed
                try
                {
                    int n = FreeRunning(detail);
                    Log.Write("Multi-instance repair: " + (n < 0 ? "not allowed by Windows" : n + " handle(s) released"));
                }
                catch (Exception ex) { Log.Write("Multi-instance repair failed: " + ex.Message); }
            }
            var held = new List<Mutex>();
            foreach (var n in Names)
            {
                try
                {
                    bool created;
                    held.Add(new Mutex(true, n, out created));
                    Log.Write("Multi-instance: " + n + (created ? " acquired" : " already held"));
                }
                catch (Exception ex) { Log.Write("Multi-instance: " + n + " unavailable: " + ex.Message); }
            }
            return held;
        }

        public static void Release(List<Mutex> held)
        {
            if (held == null) return;
            foreach (var m in held) { try { m.ReleaseMutex(); } catch { } try { m.Dispose(); } catch { } }
        }

        /// <summary>True when a CloveStrap process is already holding the lock (so a new client won't close the others).</summary>
        public static bool Active()
        {
            Mutex m;
            try { if (Mutex.TryOpenExisting("ROBLOX_singletonEvent", out m)) { m.Dispose(); return true; } }
            catch { }
            return false;
        }

        // ---- self-repair: a Roblox that was opened without multi-instance owns the singleton objects itself, so the next
        // client would close it. Closing just those two handles inside that Roblox lets CloveStrap take over the lock
        // without touching the game.
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);
        [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr h, int cls, IntPtr buf, int len, out int retLen);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DuplicateHandle(IntPtr srcProc, IntPtr src, IntPtr dstProc, out IntPtr dst, int access, bool inherit, int options);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        const int SystemExtendedHandleInformation = 64, ObjectNameInformation = 1;
        const int PROCESS_DUP_HANDLE = 0x40, DUPLICATE_CLOSE_SOURCE = 1, DUPLICATE_SAME_ACCESS = 2;
        const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        public static bool DebugNames;
        struct HandleEntry { public long Pid; public IntPtr Value; public ushort Type; }

        static List<HandleEntry> AllHandles()
        {
            int len = 1 << 22;
            IntPtr buf = IntPtr.Zero;
            try
            {
                while (true)
                {
                    buf = Marshal.AllocHGlobal(len);
                    int ret, st = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, len, out ret);
                    if (st == STATUS_INFO_LENGTH_MISMATCH) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; len = Math.Max(len * 2, ret + (1 << 20)); if (len > (1 << 28)) return null; continue; }
                    if (st != 0) return null;
                    int P = IntPtr.Size, entry = P * 3 + 16;
                    long count = Marshal.ReadIntPtr(buf).ToInt64();
                    var list = new List<HandleEntry>((int)Math.Min(count, 1 << 20));
                    for (long i = 0; i < count; i++)
                    {
                        IntPtr e = new IntPtr(buf.ToInt64() + P * 2 + i * entry);
                        list.Add(new HandleEntry
                        {
                            Pid = Marshal.ReadIntPtr(e, P).ToInt64(),
                            Value = Marshal.ReadIntPtr(e, P * 2),
                            Type = (ushort)Marshal.ReadInt16(e, P * 3 + 6)
                        });
                    }
                    return list;
                }
            }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        static string ObjectName(IntPtr h)
        {
            IntPtr buf = Marshal.AllocHGlobal(2048);
            try
            {
                int ret;
                if (NtQueryObject(h, ObjectNameInformation, buf, 2048, out ret) != 0) return null;
                int bytes = (ushort)Marshal.ReadInt16(buf);
                IntPtr str = Marshal.ReadIntPtr(buf, IntPtr.Size);   // UNICODE_STRING.Buffer (aligned to pointer size)
                return bytes == 0 || str == IntPtr.Zero ? "" : Marshal.PtrToStringUni(str, bytes / 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// Releases the singleton lock held by Roblox clients that were started without multi-instance.
        /// Returns how many handles were closed (or found, when dryRun). -1 when Windows wouldn't allow it.
        /// </summary>
        public static int FreeRunning(Action<string> detail, bool dryRun = false)
        {
            var pids = new HashSet<long>(Process.GetProcessesByName("RobloxPlayerBeta").Select(p => (long)p.Id));
            if (pids.Count == 0) return 0;
            detail = detail ?? (d => { });

            // learn the kernel's type numbers for mutexes and events from two of our own
            using (var probeM = new Mutex(false))
            using (var probeE = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                var all = AllHandles();
                if (all == null) { Log.Write("Multi-instance repair: handle list unavailable"); return -1; }
                long me = Process.GetCurrentProcess().Id;
                IntPtr hm = probeM.SafeWaitHandle.DangerousGetHandle(), he = probeE.SafeWaitHandle.DangerousGetHandle();
                var types = new HashSet<ushort>(all.Where(x => x.Pid == me && (x.Value == hm || x.Value == he)).Select(x => x.Type));
                if (types.Count == 0) { Log.Write("Multi-instance repair: couldn't identify object types"); return -1; }

                int closed = 0;
                bool denied = false;
                foreach (var pid in pids)
                {
                    IntPtr proc = OpenProcess(PROCESS_DUP_HANDLE, false, (int)pid);
                    if (proc == IntPtr.Zero) { denied = true; Log.Write("Multi-instance repair: can't open Roblox " + pid + " (error " + Marshal.GetLastWin32Error() + ")"); continue; }
                    try
                    {
                        foreach (var hnd in all.Where(x => x.Pid == pid && types.Contains(x.Type)))
                        {
                            IntPtr dup;
                            if (!DuplicateHandle(proc, hnd.Value, GetCurrentProcess(), out dup, 0, false, DUPLICATE_SAME_ACCESS)) continue;
                            string name;
                            try { name = ObjectName(dup); } finally { CloseHandle(dup); }
                            if (DebugNames && name != null && name.Length > 0) Log.Write("scan name: pid " + pid + " " + name);
                            if (name == null || !Names.Any(n => name.EndsWith("\\" + n, StringComparison.OrdinalIgnoreCase))) continue;
                            detail("RobloxPlayerBeta.exe  ·  pid " + pid + "  ·  " + name.Substring(name.LastIndexOf('\\') + 1));
                            if (dryRun) { closed++; Log.Write("Multi-instance repair (dry run): pid " + pid + " holds " + name); continue; }
                            IntPtr ignored;
                            if (DuplicateHandle(proc, hnd.Value, IntPtr.Zero, out ignored, 0, false, DUPLICATE_CLOSE_SOURCE)) { closed++; Log.Write("Multi-instance repair: released " + name + " in pid " + pid); }
                        }
                    }
                    finally { CloseHandle(proc); }
                }
                return closed == 0 && denied ? -1 : closed;
            }
        }
    }

    // ------------------------------------------------------------------ alt mode
    // Alt accounts run from their own copy of Roblox (hard links, so no extra disk space) with the lowest possible graphics,
    // a low frame cap, Windows efficiency mode, idle priority, two CPU cores and their memory trimmed regularly.
    static class AltMode
    {
        public const int Fps = 20;
        static readonly string Root = Path.Combine(Paths.Base, "AltClient");
        const string Marker = ".voidstrapper-alt";

        public static readonly string[] Summary =
        {
            "Lowest graphics, no grass, gray sky, frozen lighting",
            "Never touches your main account's settings",
            "Windows efficiency mode and idle CPU priority",
            "Limited to 2 CPU cores",
            "Memory trimmed every 15 seconds",
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string newFile, string existingFile, IntPtr reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PowerThrottling info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref int info, int size);

        [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottling { public uint Version, ControlMask, StateMask; }

        public static Settings LowSettings()
        {
            var s = new Settings();
            Presets.Apply(s, "lowend");
            s.QualityOverride = 1;
            s.TextureQuality = "0";
            s.Msaa = "1";
            s.ExclusiveFullscreen = false;
            s.DisableGrassSway = true;
            s.RemoveGrass = true;
            s.GraySky = true;
            s.PauseVoxelizer = true;
            s.LowMeshDetail = true;
            s.CustomFlags = new Dictionary<string, string>();
            return s;
        }

        /// <summary>Makes (once per Roblox version) the alt copy of a version folder and writes its low-end flags.</summary>
        public static string Prepare(string versionDir, Action<string> detail = null)
        {
            string version = Path.GetFileName(versionDir.TrimEnd('\\'));
            string dir = Path.Combine(Root, version);
            string marker = Path.Combine(dir, Marker);
            if (!File.Exists(marker) || !File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")))
            {
                if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch (Exception ex) { Log.Write("Alt: couldn't clear old copy: " + ex.Message); } }
                Directory.CreateDirectory(dir);
                int linked = 0, copied = 0;
                string src = Path.GetFullPath(versionDir).TrimEnd('\\');
                foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\');
                    if (rel.StartsWith("ClientSettings\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.StartsWith(".voidstrapper", StringComparison.OrdinalIgnoreCase)) continue;
                    string dest = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    if (detail != null) detail("Linking " + rel);
                    if (CreateHardLink(dest, f, IntPtr.Zero)) linked++;
                    else { File.Copy(f, dest, true); copied++; }
                }
                File.WriteAllText(marker, version);
                Log.Write("Alt: prepared " + dir + " (" + linked + " linked, " + copied + " copied)");
            }

            string cs = Path.Combine(dir, "ClientSettings");
            Directory.CreateDirectory(cs);
            File.WriteAllText(Path.Combine(cs, "ClientAppSettings.json"), Json.FlagsToJson(Flags.Build(LowSettings())), new UTF8Encoding(false));

            // old alt copies (hard links, so this frees almost nothing, but keeps the folder tidy)
            if (!Deployment.RobloxRunning())
                foreach (var d in Directory.GetDirectories(Root))
                    if (!string.Equals(d.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { try { Directory.Delete(d, true); } catch { } }
            return dir;
        }

        /// <summary>Applies the low-resource process settings. Safe to call repeatedly.</summary>
        public static void Throttle(Process p, bool trim)
        {
            try
            {
                if (p.HasExited) return;
                IntPtr h = p.Handle;
                try { if (p.PriorityClass != ProcessPriorityClass.Idle) p.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                try
                {
                    // ProcessPowerThrottling (4): EcoQoS / Task Manager "Efficiency mode"
                    var pt = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 1 };
                    SetProcessInformation(h, 4, ref pt, Marshal.SizeOf(typeof(PowerThrottling)));
                    // ProcessMemoryPriority (0): low memory priority so Windows pages this one out first
                    int memPrio = 2;   // MEMORY_PRIORITY_LOW
                    SetProcessInformation(h, 0, ref memPrio, 4);
                }
                catch { }
                try
                {
                    int n = Environment.ProcessorCount;
                    if (n > 2 && n <= 64)
                    {
                        long mask = (1L << (n - 1)) | (1L << (n - 2));   // the last two logical cores, away from core 0
                        if (p.ProcessorAffinity.ToInt64() != mask) p.ProcessorAffinity = new IntPtr(mask);
                    }
                }
                catch { }
                if (trim) EmptyWorkingSet(h);
            }
            catch { }
        }

        /// <summary>
        /// Keeps the alt client throttled until it closes. Blocks. (No FPS-cap trick: Roblox keeps the FPS cap in one settings
        /// file shared by every Roblox window, so lowering it for the alt slowed the main account down too.)
        /// </summary>
        public static void Watch(Process p)
        {
            int ticks = 0;
            while (true)
            {
                try { if (p.HasExited) break; } catch { break; }
                Throttle(p, ticks % 15 == 14);   // trim memory every ~15 s, re-apply the rest every second
                ticks++;
                Thread.Sleep(1000);
            }
            Log.Write("Alt: client " + SafeId(p) + " closed");
        }

        static string SafeId(Process p) { try { return p.Id.ToString(); } catch { return "?"; } }
    }
}
