// "Quieten Roblox when you switch away": any Roblox window you're not looking at is muted (or turned down), with a short
// fade, and comes back when you click into it. Uses Windows' own per-app volume, the same thing as the volume mixer.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CloveStrap
{
    static class QuietRoblox
    {
        // ---- Windows Core Audio
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr ctx);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute(bool mute, ref Guid ctx);
            [PreserveSig] int GetMute(out bool mute);
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public const float LowerTo = 0.25f;
        public static bool DebugLog;

        class Quieted { public float Original; public float Current; public bool WasMuted; }
        static readonly Dictionary<uint, Quieted> quieted = new Dictionary<uint, Quieted>();
        static Guid ctx = Guid.NewGuid();

        /// <summary>Every audio session that belongs to a Roblox process: pid -> volume control.</summary>
        public class VolumeInfo { readonly ISimpleAudioVolume v; internal VolumeInfo(object v) { this.v = (ISimpleAudioVolume)v; } public void Get(out float level, out bool muted) { v.GetMasterVolume(out level); v.GetMute(out muted); } }
        public static Dictionary<uint, VolumeInfo> Sessions(HashSet<uint> pids) { return RobloxSessions(pids).ToDictionary(k => k.Key, k => new VolumeInfo(k.Value)); }

        static Dictionary<uint, ISimpleAudioVolume> RobloxSessions(HashSet<uint> robloxPids)
        {
            var result = new Dictionary<uint, ISimpleAudioVolume>();
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return result;   // speakers, multimedia
            var iid = typeof(IAudioSessionManager2).GUID;
            object o;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return result;               // CLSCTX_ALL
            var mgr = (IAudioSessionManager2)o;
            IAudioSessionEnumerator sessions;
            if (mgr.GetSessionEnumerator(out sessions) != 0) return result;
            int n; sessions.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 s;
                if (sessions.GetSession(i, out s) != 0 || s == null) continue;
                uint pid;
                int hr = s.GetProcessId(out pid);
                if (DebugLog) { string pn = "?"; try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { } Log.Write("quiet-test: session " + i + " hr " + hr + " pid " + pid + " " + pn); }
                if (hr == 0 && robloxPids.Contains(pid) && !result.ContainsKey(pid))
                    result[pid] = (ISimpleAudioVolume)s;
            }
            return result;
        }

        // ---- self-healing
        // Windows remembers each app's mixer volume by its exe path. If a Roblox window is closed (or CloveStrap is
        // killed) while it's quietened, the 0% gets saved and every later game from that Roblox copy starts silent,
        // even in front. So a game that starts near 0% or muted, without us quietening it, is put back to 100%.
        const float StuckBelow = 0.05f;
        static readonly HashSet<uint> healChecked = new HashSet<uint>();
        static readonly object HealLock = new object();

        static bool HealSession(uint pid, ISimpleAudioVolume vol)
        {
            float level; bool muted;
            vol.GetMasterVolume(out level); vol.GetMute(out muted);
            if (level >= StuckBelow && !muted) return false;
            vol.SetMute(false, ref ctx);
            vol.SetMasterVolume(1f, ref ctx);
            Log.Write("Roblox pid " + pid + " started at " + (int)Math.Round(level * 100) + "% volume" + (muted ? " and muted" : "") +
                      " (left over from an earlier session), set it back to 100%");
            return true;
        }

        /// <summary>Checks Roblox games we haven't looked at yet. Returns how many were put back to full volume.</summary>
        public static int HealNew()
        {
            lock (HealLock)
            {
                var pids = new HashSet<uint>(Process.GetProcessesByName("RobloxPlayerBeta").Select(p => (uint)p.Id));
                healChecked.IntersectWith(pids);
                pids.ExceptWith(healChecked);
                if (pids.Count == 0) return 0;
                int n = 0;
                foreach (var kv in RobloxSessions(pids))
                {
                    healChecked.Add(kv.Key);
                    if (quieted.ContainsKey(kv.Key)) continue;   // that one is quiet on purpose
                    if (HealSession(kv.Key, kv.Value)) n++;
                }
                return n;
            }
        }

        /// <summary>For the launcher: waits (in the background) for this game's sound to start, then heals it if needed.</summary>
        public static void HealWhenReady(Process p)
        {
            new Thread(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        if (p.HasExited) return;
                        var one = RobloxSessions(new HashSet<uint> { (uint)p.Id });
                        ISimpleAudioVolume vol;
                        if (one.TryGetValue((uint)p.Id, out vol)) { lock (HealLock) { healChecked.Add((uint)p.Id); HealSession((uint)p.Id, vol); } return; }
                    }
                    catch (Exception ex) { Log.Write("Volume check: " + ex.Message); return; }
                }
            }) { IsBackground = true, Name = "volume-heal" }.Start();
        }

        public static void Run(Func<bool> stop)
        {
            int n = 0;
            while (!stop())
            {
                try { if (n++ % 7 == 0) HealNew(); } catch (Exception ex) { Log.Write("Volume check: " + ex.Message); }   // about every 2 s
                try { Tick(); } catch (Exception ex) { Log.Write("Quieten Roblox: " + ex.Message); Thread.Sleep(2000); }
                for (int i = 0; i < 3 && !stop(); i++) Thread.Sleep(100);
            }
            try { RestoreAll(); } catch { }
        }

        static void Tick()
        {
            var s = Settings.Load();
            var pids = new HashSet<uint>(Process.GetProcessesByName("RobloxPlayerBeta").Select(p => (uint)p.Id));
            foreach (var gone in quieted.Keys.Where(k => !pids.Contains(k)).ToList()) quieted.Remove(gone);
            if (pids.Count == 0) return;
            if (!s.QuietRoblox) { if (quieted.Count > 0) RestoreAll(); return; }
            lock (HealLock) if (pids.Any(p => !healChecked.Contains(p))) HealNew();   // never quieten a game before checking it

            uint fg; GetWindowThreadProcessId(GetForegroundWindow(), out fg);
            bool mute = (s.QuietMode ?? "mute") != "lower";
            foreach (var kv in RobloxSessions(pids))
            {
                var vol = kv.Value;
                bool away = kv.Key != fg;
                Quieted q;
                bool done = quieted.TryGetValue(kv.Key, out q);
                if (away && !done)
                {
                    q = new Quieted();
                    vol.GetMasterVolume(out q.Original);
                    vol.GetMute(out q.WasMuted);
                    if (q.Original < StuckBelow) { q.Original = 1f; q.WasMuted = false; }   // never "restore" to silence
                    q.Current = q.Original;
                    quieted[kv.Key] = q;
                    Fade(vol, q, mute ? 0f : q.Original * LowerTo);
                    if (mute) vol.SetMute(true, ref ctx);
                }
                else if (!away && done)
                {
                    if (!q.WasMuted) vol.SetMute(false, ref ctx);
                    Fade(vol, q, q.Original);
                    quieted.Remove(kv.Key);
                }
            }
        }

        /// <summary>A quick, smooth volume change instead of a hard cut.</summary>
        static void Fade(ISimpleAudioVolume vol, Quieted q, float to)
        {
            float from = q.Current;
            for (int i = 1; i <= 8; i++)
            {
                float v = from + (to - from) * i / 8f;
                vol.SetMasterVolume(Math.Max(0, Math.Min(1, v)), ref ctx);
                Thread.Sleep(22);
            }
            q.Current = to;
        }

        static void RestoreAll()
        {
            if (quieted.Count == 0) return;
            var pids = new HashSet<uint>(quieted.Keys);
            foreach (var kv in RobloxSessions(pids))
            {
                Quieted q;
                if (!quieted.TryGetValue(kv.Key, out q)) continue;
                if (!q.WasMuted) kv.Value.SetMute(false, ref ctx);
                kv.Value.SetMasterVolume(q.Original, ref ctx);
            }
            quieted.Clear();
        }
    }
}

namespace CloveStrap
{
    static class QuietRobloxTest
    {
        public static void Run()
        {
            var pids = new System.Collections.Generic.HashSet<uint>(System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Select(p => (uint)p.Id));
            QuietRoblox.DebugLog = true;
            Log.Write("quiet-test: " + pids.Count + " Roblox processes");
            foreach (var kv in QuietRoblox.Sessions(pids))
            {
                float v; bool m; kv.Value.Get(out v, out m);
                Log.Write("quiet-test: pid " + kv.Key + " volume " + v.ToString("0.00") + " muted " + m);
            }
        }
    }
}
