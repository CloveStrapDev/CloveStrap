// Discord sign-in tokens without any secret in the app: the CloveStrap Discord app is a "Public Client", so sign-in
// uses PKCE (a one-time random check code made fresh for every sign-in) instead of a client secret.
// The token is kept encrypted for your Windows user only (DPAPI), like the saved Roblox accounts.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CloveStrap
{
    static class DiscordOAuth
    {
        // must match a Redirect saved on the Discord app (OAuth2 > Redirects)
        public const string RedirectUri = "http://127.0.0.1";
        const string Api = "https://discord.com/api/v10";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CloveStrap.Discord.v1");
        static string TokenFile { get { return Path.Combine(Paths.Base, "discord_token.bin"); } }

        /// <summary>A fresh PKCE pair: the verifier stays here, the challenge goes to Discord.</summary>
        public static void NewPkce(out string verifier, out string challenge)
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            verifier = B64Url(bytes);
            using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        /// <summary>Swaps Discord's one-time code for an access token and saves it. Returns null, or what went wrong.</summary>
        public static string Exchange(string clientId, string code, string verifier)
        {
            try
            {
                string body = "client_id=" + Uri.EscapeDataString(clientId) +
                              "&grant_type=authorization_code" +
                              "&code=" + Uri.EscapeDataString(code) +
                              "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                              "&code_verifier=" + Uri.EscapeDataString(verifier);
                string json;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    json = wc.UploadString(Api + "/oauth2/token", body);
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (!d.ContainsKey("access_token")) return "no token in Discord's answer";
                File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser));
                Log.Write("Discord: token saved (scopes: " + (d.ContainsKey("scope") ? d["scope"] : "?") + ")");
                return null;
            }
            catch (WebException ex)
            {
                string why = ex.Message;
                try { using (var r = new StreamReader(ex.Response.GetResponseStream())) why = r.ReadToEnd(); } catch { }
                Log.Write("Discord token exchange failed: " + why);
                return why;
            }
            catch (Exception ex) { Log.Write("Discord token exchange failed: " + ex.Message); return ex.Message; }
        }

        static string AccessToken()
        {
            try
            {
                if (!File.Exists(TokenFile)) return null;
                var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), Entropy, DataProtectionScope.CurrentUser));
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                return d.ContainsKey("access_token") ? Convert.ToString(d["access_token"]) : null;
            }
            catch { return null; }
        }

        /// <summary>Names of the servers you're in (needs the "guilds" permission). Null if not available.</summary>
        public static List<string> Servers()
        {
            string token = AccessToken();
            if (token == null) return null;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                    var list = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(wc.DownloadString(Api + "/users/@me/guilds"));
                    return list.Select(g => Convert.ToString(g["name"])).ToList();
                }
            }
            catch (Exception ex) { Log.Write("Discord servers: " + ex.Message); return null; }
        }

        public static void Forget() { try { File.Delete(TokenFile); } catch { } }
    }
}
