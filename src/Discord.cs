// Discord status: "Playing <game> with CloveStrap", with the game's icon and the CloveStrap logo.
// Talks to the Discord app on this PC through its local pipe (Discord Rich Presence). Needs a Discord application ID:
// Discord only shows names and pictures that belong to a registered app.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CloveStrap
{
    class DiscordRpc : IDisposable
    {
        NamedPipeClientStream pipe;
        readonly string clientId;

        public DiscordRpc(string clientId) { this.clientId = clientId; }

        // who is signed in to the Discord app (sent by Discord itself when we connect; no password involved)
        public string UserId, UserName, AvatarHash, LastError;

        void ReadUser(string readyJson)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(readyJson);
                var data = d["data"] as Dictionary<string, object>;
                var user = data == null ? null : data["user"] as Dictionary<string, object>;
                if (user == null) return;
                UserId = Convert.ToString(user["id"]);
                object g, u, a;
                string global = user.TryGetValue("global_name", out g) && g != null ? g.ToString() : null;
                UserName = !string.IsNullOrEmpty(global) ? global : (user.TryGetValue("username", out u) && u != null ? u.ToString() : "Discord user");
                AvatarHash = user.TryGetValue("avatar", out a) && a != null ? a.ToString() : null;
            }
            catch { }
        }

        public bool Connected { get { return pipe != null && pipe.IsConnected; } }

        public bool Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(300);
                    pipe = p;
                    Send(0, new Dictionary<string, object> { { "v", 1 }, { "client_id", clientId } });
                    var reply = Receive();
                    if (reply == null || reply.IndexOf("READY", StringComparison.Ordinal) < 0)
                    {
                        Log.Write("Discord: handshake refused: " + (reply ?? "(nothing)"));
                        LastError = reply;
                        Dispose();
                        return false;
                    }
                    ReadUser(reply);
                    if (!string.IsNullOrEmpty(UserId)) { SeenUserId = UserId; SeenUserName = UserName; SeenAvatar = AvatarHash; }
                    Log.Write("Discord: connected on discord-ipc-" + i + (UserName != null ? " as " + UserName : ""));
                    return true;
                }
                catch (TimeoutException) { }
                catch (IOException) { }
                catch (Exception ex) { Log.Write("Discord: " + ex.Message); }
            }
            return false;
        }

        void Send(int op, object payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));
            var head = new byte[8];
            BitConverter.GetBytes(op).CopyTo(head, 0);
            BitConverter.GetBytes(body.Length).CopyTo(head, 4);
            pipe.Write(head, 0, 8);
            pipe.Write(body, 0, body.Length);
            pipe.Flush();
        }

        /// <summary>One message from Discord, or null. Never waits more than a few seconds (Discord sometimes just doesn't answer).</summary>
        string Receive(int timeoutMs = 6000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var head = new byte[8];
            if (!ReadAll(head, deadline)) return null;
            int len = BitConverter.ToInt32(head, 4);
            if (len <= 0 || len > 1 << 20) return null;
            var body = new byte[len];
            if (!ReadAll(body, deadline)) return null;
            return Encoding.UTF8.GetString(body);
        }

        bool ReadAll(byte[] buf, DateTime deadline)
        {
            int got = 0;
            while (got < buf.Length)
            {
                if (pipe == null) return false;
                var ar = pipe.BeginRead(buf, got, buf.Length - got, null, null);
                int wait = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!ar.AsyncWaitHandle.WaitOne(wait))
                {
                    LastError = "Discord didn't answer";
                    Log.Write("Discord: no answer, giving up on this connection");
                    Dispose();   // closing the pipe cancels the pending read
                    return false;
                }
                int n = pipe.EndRead(ar);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        // the last Discord user any connection was told about (the status feature is usually already connected)
        public static string SeenUserId, SeenUserName, SeenAvatar;

        /// <summary>
        /// Shows Discord's own "Authorize CloveStrap?" popup in the Discord app and waits for the answer.
        /// Returns null if you clicked Authorize, otherwise why not (cancelled, timed out...).
        /// </summary>
        public string Authorize(string[] scopes, int timeoutMs) { string code; return Authorize(scopes, timeoutMs, null, out code); }

        /// <summary>Same, also handing back Discord's one-time code. codeChallenge: PKCE (S256), so no secret is ever needed.</summary>
        public string Authorize(string[] scopes, int timeoutMs, string codeChallenge, out string code)
        {
            code = null;
            string nonce = Guid.NewGuid().ToString();
            var args = new Dictionary<string, object> { { "client_id", clientId }, { "scopes", scopes } };
            if (codeChallenge != null) { args["code_challenge"] = codeChallenge; args["code_challenge_method"] = "S256"; }
            Send(1, new Dictionary<string, object>
            {
                { "cmd", "AUTHORIZE" },
                { "args", args },
                { "nonce", nonce }
            });
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                string reply = Receive((int)Math.Max(1000, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (reply == null) return LastError == "Discord didn't answer" ? "timed out" : "Discord closed the connection";
                Dictionary<string, object> d;
                try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply); } catch { continue; }
                object n; if (!d.TryGetValue("nonce", out n) || Convert.ToString(n) != nonce) continue;   // not the answer to this
                var data = d.ContainsKey("data") ? d["data"] as Dictionary<string, object> : null;
                object evt; d.TryGetValue("evt", out evt);
                if (Convert.ToString(evt) == "ERROR")
                {
                    string msg = data != null && data.ContainsKey("message") ? Convert.ToString(data["message"]) : "error";
                    Log.Write("Discord authorize: " + msg);
                    return msg;
                }
                if (data != null && data.ContainsKey("code")) { code = Convert.ToString(data["code"]); return null; }   // you clicked Authorize
                return "unexpected answer";
            }
            return "timed out";
        }

        /// <summary>Sets (or clears, when activity is null) this app's Discord status.</summary>
        public void SetActivity(Dictionary<string, object> activity)
        {
            Send(1, new Dictionary<string, object>
            {
                { "cmd", "SET_ACTIVITY" },
                { "args", new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id }, { "activity", activity } } },
                { "nonce", Guid.NewGuid().ToString() }
            });
            Receive();
        }

        public void Dispose()
        {
            try { if (pipe != null) pipe.Dispose(); } catch { }
            pipe = null;
        }
    }

    static class DiscordPresence
    {
        public const string PortalUrl = "https://discord.com/developers/applications";

        /// <summary>Set while signing in: the status lets go of its Discord connection so sign-in can use its own.</summary>
        public static volatile bool Paused;
        public static volatile bool Idle = true;

        public static void Run(Func<bool> stop)
        {
            DiscordRpc rpc = null;
            string shownKey = null, usedId = null;
            while (!stop())
            {
                if (Paused)
                {
                    if (rpc != null) { try { rpc.SetActivity(null); } catch { } rpc.Dispose(); rpc = null; }
                    shownKey = null;
                    Idle = true;
                    Thread.Sleep(200);
                    continue;
                }
                Idle = rpc == null;
                try
                {
                    var s = Settings.Load();
                    string id = DiscordAccount.AppId(s);
                    bool want = s.DiscordPresence && id.Length >= 15 && id.All(char.IsDigit);
                    if (!want || id != usedId)
                    {
                        if (rpc != null) { try { rpc.SetActivity(null); } catch { } rpc.Dispose(); rpc = null; shownKey = null; }
                        usedId = want ? id : null;
                    }
                    if (want)
                    {
                        var alts = AltWindows.Running();
                        var main = AltWindows.MainProcess(alts);
                        RobloxState st = main == null ? null : RobloxLogs.Read(RobloxLogs.LogFor(main));
                        string key = st == null || st.PlaceId == 0 || st.Disconnected ? "none" : st.PlaceId + "|" + st.JobId + "|" + alts.Count;
                        if (key != shownKey)
                        {
                            if (rpc == null || !rpc.Connected) { if (rpc != null) rpc.Dispose(); rpc = new DiscordRpc(id); if (!rpc.Connect()) { rpc = null; Wait(stop, 15); continue; } }
                            if (key == "none") rpc.SetActivity(null);
                            else
                            {
                                var g = RobloxGames.Info(st.PlaceId);
                                var assets = new Dictionary<string, object>
                                {
                                    { "large_image", "logo" },   // the CloveStrap logo (Art Asset named "logo" on the Discord app)
                                    { "large_text", "CloveStrap" }
                                };
                                var act = new Dictionary<string, object>
                                {
                                    { "details", Trim("Playing " + g.Name) },
                                    { "state", alts.Count > 0 ? "with CloveStrap  ·  " + alts.Count + " alt" + (alts.Count == 1 ? "" : "s") : "with CloveStrap" },
                                    { "timestamps", new Dictionary<string, object> { { "start", (long)(st.JoinedUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds } } },
                                    { "assets", assets },
                                    { "buttons", new object[] { new Dictionary<string, object> { { "label", "Open game" }, { "url", "https://www.roblox.com/games/" + st.PlaceId } } } }
                                };
                                rpc.SetActivity(act);
                                Log.Write("Discord: playing " + g.Name);
                            }
                            shownKey = key;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Discord status: " + ex.Message);
                    if (rpc != null) { rpc.Dispose(); rpc = null; }
                    shownKey = null;
                }
                Wait(stop, 5);
            }
            if (rpc != null) { try { rpc.SetActivity(null); } catch { } rpc.Dispose(); }
        }

        static string Trim(string s) { return s.Length > 127 ? s.Substring(0, 124) + "..." : s; }

        static void Wait(Func<bool> stop, int seconds)
        {
            for (int i = 0; i < seconds * 10 && !stop(); i++) Thread.Sleep(100);
        }

        /// <summary>Saves the CloveStrap logo (512x512 PNG) for uploading to the Discord app.</summary>
        public static string SaveLogo()
        {
            string f = Path.Combine(Paths.Base, "clovestrap-logo.png");
            using (var b = new System.Drawing.Bitmap(512, 512))
            {
                using (var g = System.Drawing.Graphics.FromImage(b))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    Theme.DrawLogo(g, new System.Drawing.RectangleF(0, 0, 511, 511));
                }
                b.Save(f, System.Drawing.Imaging.ImageFormat.Png);
            }
            return f;
        }
    }
}
