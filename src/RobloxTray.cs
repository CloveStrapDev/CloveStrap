// Roblox's own "wait in the tray" helper (RobloxPlayerBeta --launch-to-tray) normally only runs once, but with
// multiple Roblox windows unlocked it can't tell that one is already running, so copies pile up, each with a tray
// icon. CloveStrap keeps the oldest one and closes the rest: politely first (so the tray icon goes away properly),
// and only forces it if it doesn't listen. Roblox game windows and alts are never touched.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace CloveStrap
{
    static class RobloxTray
    {
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr lParam);
        const uint WM_CLOSE = 0x0010;

        public static void Run(Func<bool> stop)
        {
            while (!stop())
            {
                try { Tidy(); } catch (Exception ex) { Log.Write("Roblox tray tidy: " + ex.Message); }
                for (int i = 0; i < 150 && !stop(); i++) Thread.Sleep(100);   // every 15 s
            }
        }

        static readonly object KnownLock = new object();
        static readonly HashSet<int> KnownTray = new HashSet<int>(), KnownGame = new HashSet<int>();

        /// <summary>True when a real Roblox client (a game or an alt) is running. Roblox's own background tray helper
        /// doesn't count: it never closes, so anything that waits for "Roblox closed" would otherwise wait forever.</summary>
        public static bool GamesRunning()
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta");
            try
            {
                lock (KnownLock)
                {
                    var alive = new HashSet<int>(procs.Select(p => p.Id));
                    KnownTray.IntersectWith(alive); KnownGame.IntersectWith(alive);
                    foreach (var p in procs)
                    {
                        if (KnownGame.Contains(p.Id)) return true;
                        if (KnownTray.Contains(p.Id)) continue;
                        bool tray = false;
                        try
                        {
                            using (var q = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + p.Id))
                                foreach (ManagementObject mo in q.Get())
                                    tray = Convert.ToString(mo["CommandLine"] ?? "").IndexOf("--launch-to-tray", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        catch { }   // can't tell: count it as a game, the safe side
                        if (tray) KnownTray.Add(p.Id);
                        else { KnownGame.Add(p.Id); return true; }
                    }
                    return false;
                }
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        /// <summary>Keeps one Roblox tray helper and closes the extra copies. Returns how many were closed.</summary>
        public static int Tidy()
        {
            // cheap first look: only windowless Roblox processes can be tray helpers
            var windowless = Process.GetProcessesByName("RobloxPlayerBeta")
                .Where(p => { try { return p.MainWindowHandle == IntPtr.Zero; } catch { return false; } }).ToList();
            if (windowless.Count < 2) return 0;

            var helpers = new List<Process>();
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'RobloxPlayerBeta.exe'"))
                foreach (ManagementObject mo in q.Get())
                {
                    string cmd = Convert.ToString(mo["CommandLine"] ?? "");
                    int pid = Convert.ToInt32(mo["ProcessId"]);
                    if (cmd.IndexOf("--launch-to-tray", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var p = windowless.FirstOrDefault(w => w.Id == pid);
                    if (p != null) helpers.Add(p);
                }
            if (helpers.Count < 2) return 0;

            var extra = helpers.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }).Skip(1).ToList();
            // ask nicely: closing its windows lets Roblox remove its own tray icon (a killed process leaves a ghost icon)
            var ids = new HashSet<uint>(extra.Select(p => (uint)p.Id));
            EnumWindows((h, l) =>
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (ids.Contains(pid)) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            int closed = 0;
            foreach (var p in extra)
            {
                try
                {
                    if (!p.WaitForExit(3000)) p.Kill();
                    closed++;
                }
                catch { }
            }
            if (closed > 0) Log.Write("Closed " + closed + " extra Roblox tray helper(s); kept one");
            return closed;
        }
    }
}
