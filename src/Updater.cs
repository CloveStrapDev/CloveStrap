// CloveStrap updates itself from GitHub Releases: it checks the repo's latest release, downloads the new
// CloveStrap.exe in the background, checks it against GitHub's SHA-256 fingerprint, and swaps it in on the next restart.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CloveStrap
{
    static class Updater
    {
        /// <summary>Where releases come from: "owner/repo" on GitHub. Empty = updates off.</summary>
        public const string Repo = "CloveStrapDev/CloveStrap";

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string CurrentText { get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }

        static string Dir { get { return Path.Combine(Paths.Base, "Update"); } }
        static string Staged { get { return Path.Combine(Dir, "CloveStrap.exe"); } }

        /// <summary>A downloaded, checked update waiting to be installed (null if none).</summary>
        public static Version Ready
        {
            get
            {
                var v = FileVer(Staged);
                return v != null && v > Current ? v : null;
            }
        }

        /// <summary>Raised (on a background thread) when an update has been downloaded and is ready.</summary>
        public static event Action<Version> Downloaded;

        public static Version FileVer(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var fv = FileVersionInfo.GetVersionInfo(path).FileVersion;
                Version v;
                return Version.TryParse(fv, out v) ? v : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ checking and downloading
        public static void Run(Func<bool> stop)
        {
            if (string.IsNullOrEmpty(Repo)) return;
            Wait(stop, 20);   // let CloveStrap finish starting first
            while (!stop())
            {
                try { CheckOnce(); } catch (Exception ex) { Log.Write("Update check failed: " + ex.Message); }
                Wait(stop, 6 * 60 * 60);
            }
        }

        static void Wait(Func<bool> stop, int seconds) { for (int i = 0; i < seconds * 10 && !stop(); i++) Thread.Sleep(100); }

        public static void CheckOnce()
        {
            if (string.IsNullOrEmpty(Repo)) return;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "CloveStrap";
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
            }
            var rel = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            string tag = Convert.ToString(rel["tag_name"]).TrimStart('v', 'V');
            Version latest;
            if (!Version.TryParse(tag.Contains('.') ? tag : tag + ".0", out latest)) return;
            latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build), Math.Max(0, latest.Revision));
            if (latest <= Current) { Log.Write("Up to date (" + CurrentText + ")"); return; }
            if (Ready != null && Ready >= latest) { if (Downloaded != null) Downloaded(Ready); return; }

            var asset = (rel["assets"] as ArrayList ?? new ArrayList()).Cast<Dictionary<string, object>>()
                .FirstOrDefault(a => string.Equals(Convert.ToString(a["name"]), "CloveStrap.exe", StringComparison.OrdinalIgnoreCase));
            if (asset == null) { Log.Write("Release " + tag + " has no CloveStrap.exe"); return; }
            string url = Convert.ToString(asset["browser_download_url"]);
            long size = Convert.ToInt64(asset["size"]);
            object d; string digest = asset.TryGetValue("digest", out d) && d != null ? Convert.ToString(d) : null;   // "sha256:..."

            Log.Write("Downloading CloveStrap " + tag);
            Directory.CreateDirectory(Dir);
            string part = Staged + ".part";
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "CloveStrap";
                wc.DownloadFile(url, part);
            }
            var fi = new FileInfo(part);
            if (fi.Length != size) { File.Delete(part); Log.Write("Update download was the wrong size"); return; }
            if (digest != null && digest.StartsWith("sha256:"))
            {
                string got;
                using (var fs = File.OpenRead(part)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (got != digest.Substring(7).ToLowerInvariant()) { File.Delete(part); Log.Write("Update failed its fingerprint check"); return; }
            }
            var pv = FileVer(part);
            if (pv == null || pv <= Current) { File.Delete(part); Log.Write("Downloaded update isn't newer (" + pv + ")"); return; }
            if (File.Exists(Staged)) File.Delete(Staged);
            File.Move(part, Staged);
            Log.Write("CloveStrap " + pv + " is ready to install");
            if (Downloaded != null) Downloaded(pv);
        }

        // ------------------------------------------------------------------ installing
        /// <summary>
        /// Puts the waiting update in place of the installed CloveStrap and starts it (with args), which waits for this
        /// one to close. Returns true if the caller should now exit.
        /// </summary>
        public static bool Apply(string args)
        {
            try
            {
                var ready = Ready;
                if (ready == null) return false;
                string target = Paths.InstalledExe;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    // a running exe can't be overwritten, but it can be renamed out of the way
                    try { File.Delete(target); }
                    catch { File.Move(target, Path.Combine(Paths.Base, "CloveStrap.old-" + DateTime.Now.ToString("HHmmssfff") + ".exe")); }
                }
                File.Copy(Staged, target, true);
                File.Delete(Staged);
                Log.Write("Updated to " + ready);
                Process.Start(new ProcessStartInfo(target, (args ?? "") + " --after-update " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                return true;
            }
            catch (Exception ex) { Log.Write("Update install failed: " + ex.Message); return false; }
        }

        /// <summary>
        /// At start-up: install a waiting update, or hand over to a newer installed CloveStrap (so an old copy, like one in
        /// Downloads, never runs over a newer version). Returns true if this process should exit.
        /// </summary>
        public static bool HandOff(string[] args)
        {
            // the previous version is closing: wait for it (it holds the "only one CloveStrap" lock)
            int ai = Array.IndexOf(args, "--after-update");
            if (ai >= 0 && ai + 1 < args.Length)
            {
                int pid;
                if (int.TryParse(args[ai + 1], out pid))
                    try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
                return false;
            }
            string rest = string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
            if (Ready != null) return Apply(rest);
            string me = Path.GetFullPath(Application.ExecutablePath);
            if (!string.Equals(me, Path.GetFullPath(Paths.InstalledExe), StringComparison.OrdinalIgnoreCase))
            {
                var installed = FileVer(Paths.InstalledExe);
                if (installed != null && installed > Current)
                {
                    Log.Write("A newer CloveStrap (" + installed + ") is installed; opening that one");
                    Process.Start(new ProcessStartInfo(Paths.InstalledExe, rest) { UseShellExecute = false });
                    return true;
                }
            }
            return false;
        }
    }
}
