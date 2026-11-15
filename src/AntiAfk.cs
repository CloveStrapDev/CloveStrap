// Anti-AFK: Roblox kicks you after 20 minutes without input. Every ~14 minutes each Roblox window gets one tiny jump
// (a Space press). Windows you're actively playing in are left alone; background and minimised ones (alts) get
// focused for a split second and you're put straight back where you were.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CloveStrap
{
    static class AntiAfk
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        public const int IntervalMinutes = 14;
        static readonly Dictionary<int, DateTime> lastNudge = new Dictionary<int, DateTime>();
        public static int Nudges;

        static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return (unchecked((uint)Environment.TickCount - li.dwTime)) / 1000.0;
        }

        /// <summary>Runs until stop() is true. Checks every 15 seconds.</summary>
        public static void Run(Func<bool> stop)
        {
            while (!stop())
            {
                try { if (Settings.Load().AntiAfk) Tick(); }
                catch (Exception ex) { Log.Write("Anti-AFK: " + ex.Message); }
                for (int i = 0; i < 150 && !stop(); i++) Thread.Sleep(100);
            }
        }

        static void Tick()
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta");
            var alive = new HashSet<int>(procs.Select(p => p.Id));
            foreach (var k in lastNudge.Keys.Where(k => !alive.Contains(k)).ToList()) lastNudge.Remove(k);
            foreach (var p in procs)
            {
                IntPtr h;
                try { h = p.MainWindowHandle; } catch { continue; }
                if (h == IntPtr.Zero) continue;
                DateTime last;
                if (!lastNudge.TryGetValue(p.Id, out last)) { lastNudge[p.Id] = DateTime.UtcNow; continue; }   // start the clock when we first see it
                if ((DateTime.UtcNow - last).TotalMinutes < IntervalMinutes) continue;

                // you're playing in this one right now: your own input already keeps it awake
                if (GetForegroundWindow() == h && IdleSeconds() < 60) { lastNudge[p.Id] = DateTime.UtcNow; continue; }
                // don't cut into typing or a fight: wait for a short pause in your input
                if (IdleSeconds() < 2) continue;

                if (Nudge(h)) { lastNudge[p.Id] = DateTime.UtcNow; Nudges++; Log.Write("Anti-AFK: nudged Roblox " + p.Id); }
            }
        }

        static void Focus(IntPtr h)
        {
            IntPtr fg = GetForegroundWindow();
            uint dummy;
            uint fgThread = GetWindowThreadProcessId(fg, out dummy), me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            try { BringWindowToTop(h); SetForegroundWindow(h); }
            finally { if (attached) AttachThreadInput(me, fgThread, false); }
        }

        static void Key(ushort vk, bool up)
        {
            var inp = new INPUT { type = 1 };
            inp.u.ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? 2u : 0u };
            SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>One Space press in the given Roblox window, then everything goes back as it was.</summary>
        public static bool Nudge(IntPtr h)
        {
            if (!IsWindow(h)) return false;
            IntPtr before = GetForegroundWindow();
            bool wasMin = IsIconic(h);
            try
            {
                if (wasMin) ShowWindow(h, 4);   // SW_SHOWNOACTIVATE
                Focus(h);
                Thread.Sleep(120);
                if (GetForegroundWindow() != h) return false;   // Windows refused the switch; try again next round
                Key(0x20, false); Thread.Sleep(60); Key(0x20, true);
                Thread.Sleep(120);
                return true;
            }
            finally
            {
                if (wasMin) ShowWindow(h, 7);   // SW_SHOWMINNOACTIVE
                if (before != IntPtr.Zero && before != h && IsWindow(before)) Focus(before);
            }
        }
    }
}
