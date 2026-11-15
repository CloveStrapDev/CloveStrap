// What a Roblox window is doing, read from its own log file: which game it joined, and whether it got disconnected.
// Also: game names and icons, and Roblox's public server list (for the server picker).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CloveStrap
{
    class RobloxState
    {
        public long PlaceId;
        public string JobId;
        public DateTime JoinedUtc;
        public bool Disconnected;
        public string DisconnectReason;
        public bool Leaving;        // left on purpose / teleporting
    }

    static class RobloxLogs
    {
        static readonly string Dir = Path.Combine(Paths.RobloxData, "logs");
        static readonly Regex Join = new Regex(@"! Joining game '([0-9a-fA-F\-]{36})' place (\d+)");
        static readonly Regex Lost = new Regex(@"(?:Client has been disconnected with reason|Lost connection with reason)\s*:\s*(.+)$");
        static readonly Dictionary<int, string> logOf = new Dictionary<int, string>();

        /// <summary>The log file a Roblox process writes to (the player log created right after it started).</summary>
        public static string LogFor(Process p)
        {
            lock (logOf)
            {
                string f;
                if (logOf.TryGetValue(p.Id, out f) && File.Exists(f)) return f;
                DateTime start;
                try { start = p.StartTime; } catch { return null; }
                if (!Directory.Exists(Dir)) return null;
                var best = new DirectoryInfo(Dir).GetFiles("*_Player_*_last.log")
                    .Where(x => x.Name.IndexOf("CrashHandler", StringComparison.OrdinalIgnoreCase) < 0)
                    .Select(x => new { x, d = (x.CreationTime - start).TotalSeconds })
                    .Where(t => t.d > -2 && t.d < 30)
                    .OrderBy(t => Math.Abs(t.d)).FirstOrDefault();
                if (best == null) return null;
                if (logOf.Count > 100) logOf.Clear();
                logOf[p.Id] = best.x.FullName;
                return best.x.FullName;
            }
        }

        class Tail { public long Pos; public RobloxState State = new RobloxState(); public string Partial = ""; }
        static readonly Dictionary<string, Tail> tails = new Dictionary<string, Tail>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns the latest join / disconnect state. Only reads what was added since the last call.</summary>
        public static RobloxState Read(string file)
        {
            if (file == null || !File.Exists(file)) return null;
            Tail t;
            lock (tails)
            {
                if (!tails.TryGetValue(file, out t)) { if (tails.Count > 50) tails.Clear(); t = new Tail(); tails[file] = t; }
            }
            lock (t)
            {
                var st = t.State;
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (fs.Length < t.Pos) { t.Pos = 0; t.State = st = new RobloxState(); t.Partial = ""; }
                        fs.Seek(t.Pos, SeekOrigin.Begin);
                        string chunk;
                        using (var r = new StreamReader(fs, Encoding.UTF8, false, 1 << 16, true)) chunk = r.ReadToEnd();
                        t.Pos = fs.Length;
                        string text = t.Partial + chunk;
                        int lastNl = text.LastIndexOf('\n');
                        t.Partial = lastNl >= 0 ? text.Substring(lastNl + 1) : text;
                        if (lastNl < 0) return Copy(st);
                        foreach (var raw in text.Substring(0, lastNl).Split('\n'))
                            Apply(st, raw.TrimEnd('\r'));
                    }
                }
                catch { }
                return Copy(st);
            }
        }

        static RobloxState Copy(RobloxState s)
        {
            return new RobloxState { PlaceId = s.PlaceId, JobId = s.JobId, JoinedUtc = s.JoinedUtc, Disconnected = s.Disconnected, DisconnectReason = s.DisconnectReason, Leaving = s.Leaving };
        }

        static void Apply(RobloxState st, string line)
        {
            var m = Join.Match(line);
            if (m.Success)
            {
                st.JobId = m.Groups[1].Value; st.PlaceId = long.Parse(m.Groups[2].Value);
                int comma = line.IndexOf(',');
                DateTime t;
                st.JoinedUtc = comma > 0 && DateTime.TryParse(line.Substring(0, comma), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out t) ? t : DateTime.UtcNow;
                st.Disconnected = false; st.DisconnectReason = null; st.Leaving = false;
                return;
            }
            if (line.Contains("initiateTeleport") || line.Contains("leaveUGCGameInternal")) { st.Leaving = true; return; }
            var d = Lost.Match(line);
            if (d.Success && !st.Leaving) { st.Disconnected = true; st.DisconnectReason = d.Groups[1].Value.Trim(); }
        }

        /// <summary>
        /// Blocks while the Roblox window runs. Returns the disconnect reason if it got kicked/disconnected and is just sitting
        /// on Roblox's error screen; null if it was closed normally (or kicked by the same account joining elsewhere).
        /// </summary>
        public static string WatchForKick(Process p)
        {
            DateTime? since = null;
            while (true)
            {
                try { if (p.HasExited) return null; } catch { return null; }
                var st = Read(LogFor(p));
                if (st != null && st.Disconnected)
                {
                    string why = st.DisconnectReason ?? "disconnected";
                    if (why.IndexOf("another device", StringComparison.OrdinalIgnoreCase) >= 0) { Log.Write("Kicked because the account joined elsewhere; not rejoining"); return null; }
                    if (!since.HasValue) since = DateTime.UtcNow;
                    else if ((DateTime.UtcNow - since.Value).TotalSeconds >= 5) return why;   // still open on the error screen
                }
                else since = null;
                System.Threading.Thread.Sleep(2000);
            }
        }
    }

    class GameInfo
    {
        public long PlaceId, UniverseId;
        public string Name, IconUrl;
    }

    static class RobloxGames
    {
        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CloveStrap/2.0";
        static readonly Dictionary<long, GameInfo> cache = new Dictionary<long, GameInfo>();

        static Dictionary<string, object> GetJson(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = UA;
                wc.Encoding = Encoding.UTF8;
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString(url));
            }
        }

        public static GameInfo Info(long placeId)
        {
            lock (cache) { GameInfo g; if (cache.TryGetValue(placeId, out g)) return g; }
            var info = new GameInfo { PlaceId = placeId, Name = "a Roblox game" };
            try
            {
                var u = GetJson("https://apis.roblox.com/universes/v1/places/" + placeId + "/universe");
                info.UniverseId = Convert.ToInt64(u["universeId"]);
                var gd = GetJson("https://games.roblox.com/v1/games?universeIds=" + info.UniverseId);
                var data = gd["data"] as System.Collections.ArrayList;
                if (data != null && data.Count > 0) info.Name = ((Dictionary<string, object>)data[0])["name"] as string ?? info.Name;
                var ic = GetJson("https://thumbnails.roblox.com/v1/games/icons?universeIds=" + info.UniverseId + "&size=512x512&format=Png&isCircular=false");
                var idata = ic["data"] as System.Collections.ArrayList;
                if (idata != null && idata.Count > 0) info.IconUrl = ((Dictionary<string, object>)idata[0])["imageUrl"] as string;
            }
            catch (Exception ex) { Log.Write("Game info " + placeId + ": " + ex.Message); }
            lock (cache) cache[placeId] = info;
            return info;
        }

        public class Server { public string Id; public int Playing, Max; public double Ping; }

        /// <summary>Public servers, fewest players first ("Asc") or most first ("Desc"). Up to 100.</summary>
        public static List<Server> Servers(long placeId, bool fullestFirst)
        {
            var d = GetJson("https://games.roblox.com/v1/games/" + placeId + "/servers/Public?sortOrder=" + (fullestFirst ? "Desc" : "Asc") + "&limit=100&excludeFullGames=true");
            var list = new List<Server>();
            var data = d["data"] as System.Collections.ArrayList;
            if (data != null)
                foreach (Dictionary<string, object> s in data)
                {
                    object ping;
                    list.Add(new Server
                    {
                        Id = s["id"] as string,
                        Playing = Convert.ToInt32(s["playing"]),
                        Max = Convert.ToInt32(s["maxPlayers"]),
                        Ping = s.TryGetValue("ping", out ping) && ping != null ? Convert.ToDouble(ping) : 9999
                    });
                }
            return list;
        }

        /// <summary>Picks a server: "empty" (fewest players), "full" (most players with room for everyone), "ping" (lowest ping). Null = let Roblox pick.</summary>
        public static string PickServer(long placeId, string how, int seatsNeeded)
        {
            if (how == null || how == "any") return null;
            var list = Servers(placeId, how == "full").Where(s => s.Id != null && s.Max - s.Playing >= Math.Max(1, seatsNeeded)).ToList();
            if (list.Count == 0) return null;
            Server pick;
            if (how == "empty") pick = list.OrderBy(s => s.Playing).First();
            else if (how == "full") pick = list.OrderByDescending(s => s.Playing).First();
            else pick = list.Where(s => s.Ping > 0).OrderBy(s => s.Ping).FirstOrDefault() ?? list[0];
            Log.Write("Server picker (" + how + "): " + pick.Id + " " + pick.Playing + "/" + pick.Max + " ping " + pick.Ping);
            return pick.Id;
        }
    }
}
