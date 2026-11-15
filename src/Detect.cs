// "Read your PC": looks at the graphics card, processor, memory and screen, then picks settings to match.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CloveStrap
{
    class PcInfo
    {
        public string Gpu = "Unknown graphics", Cpu = "Unknown processor";
        public long VramMB;
        public bool Integrated;
        public int Cores, Threads, Hz;
        public long RamMB;
        public bool Laptop, OnBattery;
        public string Tier;      // potato | low | mid | high | ultra

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append("Graphics:  ").Append(Gpu);
            if (VramMB > 0) sb.Append("  (").Append(Gb(VramMB)).Append(Integrated ? " shared)" : ")");
            else if (Integrated) sb.Append("  (built-in)");
            sb.Append("\nProcessor:  ").Append(Cpu).Append("  (").Append(Cores).Append(" cores)");
            sb.Append("\nMemory:  ").Append(Gb(RamMB)).Append(" RAM");
            sb.Append("\nScreen:  ").Append(Hz).Append(" Hz");
            if (Laptop) sb.Append("\nLaptop").Append(OnBattery ? ", on battery" : ", plugged in");
            return sb.ToString();
        }

        static string Gb(long mb) { return mb >= 1024 ? Math.Round(mb / 1024.0, mb % 1024 == 0 ? 0 : 1) + " GB" : mb + " MB"; }
    }

    static class PcReader
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        public static PcInfo Read()
        {
            var pc = new PcInfo { Threads = Environment.ProcessorCount, Cores = Environment.ProcessorCount, Hz = Display.RefreshRate() };

            // graphics: the display adapter with the most dedicated memory (registry has the real 64-bit size; WMI caps at 4 GB)
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
                        {
                            using (var k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = k.GetValue("DriverDesc") as string;
                                if (string.IsNullOrEmpty(name) || name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Parsec", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                long bytes = 0;
                                object q = k.GetValue("HardwareInformation.qwMemorySize");
                                if (q is long) bytes = (long)q;
                                else if (q is byte[] && ((byte[])q).Length >= 8) bytes = BitConverter.ToInt64((byte[])q, 0);
                                if (bytes == 0)
                                {
                                    object m = k.GetValue("HardwareInformation.MemorySize");
                                    if (m is int) bytes = (uint)(int)m;
                                    else if (m is byte[] && ((byte[])m).Length >= 4) bytes = BitConverter.ToUInt32((byte[])m, 0);
                                }
                                long mb = bytes >> 20;
                                if (pc.Gpu == "Unknown graphics" || mb > pc.VramMB) { pc.Gpu = name.Trim(); pc.VramMB = mb; }
                            }
                        }
                }
            }
            catch (Exception ex) { Log.Write("PC read (gpu): " + ex.Message); }

            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get())
                    {
                        pc.Cpu = Convert.ToString(o["Name"]).Trim();
                        pc.Cores = Convert.ToInt32(o["NumberOfCores"]);
                        pc.Threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                        break;
                    }
            }
            catch (Exception ex) { Log.Write("PC read (cpu): " + ex.Message); }

            try { var m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) pc.RamMB = (long)(m.ullTotalPhys >> 20); } catch { }
            try
            {
                var ps = SystemInformation.PowerStatus;
                pc.Laptop = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
                pc.OnBattery = pc.Laptop && ps.PowerLineStatus == PowerLineStatus.Offline;
            }
            catch { }

            string g = pc.Gpu.ToLowerInvariant();
            bool intelIgpu = g.Contains("intel") && !g.Contains("arc");
            bool amdIgpu = g.Contains("amd") && (g.Contains("radeon(tm) graphics") || g.Contains("radeon graphics") || g.Contains("vega")) && !g.Contains(" rx ");
            pc.Integrated = intelIgpu || amdIgpu || (pc.VramMB > 0 && pc.VramMB < 1024);

            // tier from graphics first, then held back by memory and processor
            int tier;   // 0 potato .. 4 ultra
            if (pc.Integrated) tier = g.Contains("iris xe") || g.Contains("780m") || g.Contains("680m") || g.Contains("arc") ? 1 : 0;
            else if (pc.VramMB < 3000) tier = 1;
            else if (pc.VramMB < 6000) tier = 2;
            else if (pc.VramMB < 10000) tier = 3;
            else tier = 4;
            if (pc.RamMB > 0 && pc.RamMB < 6000) tier = Math.Min(tier, 0);
            else if (pc.RamMB > 0 && pc.RamMB < 10000) tier = Math.Min(tier, 2);
            if (pc.Cores <= 2) tier = Math.Min(tier, 0);
            else if (pc.Cores <= 4) tier = Math.Min(tier, 2);
            pc.Tier = new[] { "potato", "low", "mid", "high", "ultra" }[tier];
            Log.Write("PC read: " + pc.Summary().Replace("\n", " | ") + " | tier " + pc.Tier);
            return pc;
        }

        /// <summary>Fills in the settings that fit this PC and returns what was picked, line by line.</summary>
        public static List<string> Recommend(PcInfo pc, Settings s)
        {
            var why = new List<string>();
            s.FpsCap = 0;
            why.Add("FPS cap: your screen's " + pc.Hz + " Hz");
            s.GraphicsApi = "D3D11";
            why.Add("Renderer: D3D11, the steadiest on Windows");
            s.ExclusiveFullscreen = true;
            s.DisableGrassSway = true;
            s.CustomFlags = s.CustomFlags ?? new Dictionary<string, string>();

            switch (pc.Tier)
            {
                case "potato":
                    s.QualityOverride = 1; s.TextureQuality = "0"; s.Msaa = "1";
                    s.RemoveGrass = true; s.LowMeshDetail = true; s.ViewDistance = "near"; s.GraySky = true; s.PauseVoxelizer = true; s.DisableDpiScale = false;
                    why.Add("Graphics: level 1 of 21 with lowest textures, so it stays playable");
                    why.Add("Distant detail: near. Extras: no grass, gray sky, frozen lighting");
                    break;
                case "low":
                    s.QualityOverride = 4; s.TextureQuality = "1"; s.Msaa = "1";
                    s.RemoveGrass = true; s.LowMeshDetail = false; s.ViewDistance = "short"; s.GraySky = false; s.PauseVoxelizer = false; s.DisableDpiScale = false;
                    why.Add("Graphics: level 4 of 21, low textures, anti-aliasing off");
                    why.Add("Distant detail: short. Extras: no grass");
                    break;
                case "mid":
                    s.QualityOverride = 10; s.TextureQuality = "2"; s.Msaa = "2";
                    s.RemoveGrass = false; s.LowMeshDetail = false; s.ViewDistance = "default"; s.GraySky = false; s.PauseVoxelizer = false; s.DisableDpiScale = false;
                    why.Add("Graphics: level 10 of 21, medium textures, 2x anti-aliasing");
                    break;
                case "high":
                    s.QualityOverride = 16; s.TextureQuality = "3"; s.Msaa = "4";
                    s.RemoveGrass = false; s.LowMeshDetail = false; s.ViewDistance = "far"; s.GraySky = false; s.PauseVoxelizer = false; s.DisableDpiScale = false;
                    why.Add("Graphics: level 16 of 21, high textures, 4x anti-aliasing");
                    why.Add("Distant detail: far");
                    break;
                default:
                    s.QualityOverride = 21; s.TextureQuality = "3"; s.Msaa = "4";
                    s.RemoveGrass = false; s.LowMeshDetail = false; s.ViewDistance = "max"; s.GraySky = false; s.PauseVoxelizer = false; s.DisableDpiScale = true;
                    why.Add("Graphics: maxed, level 21 of 21, high textures, 4x anti-aliasing");
                    why.Add("Distant detail: max");
                    why.Add("Native resolution on scaled screens");
                    break;
            }

            if (pc.OnBattery)
            {
                s.Priority = "Normal";
                why.Add("CPU priority: normal, kinder to your battery and fans");
            }
            else if (pc.Laptop)
            {
                s.Priority = "AboveNormal";
                why.Add("CPU priority: above normal (plugged-in laptop)");
            }
            else if (pc.Cores >= 8)
            {
                s.Priority = "High";
                why.Add("CPU priority: high, you have " + pc.Cores + " cores to spare");
            }
            else
            {
                s.Priority = "AboveNormal";
                why.Add("CPU priority: above normal");
            }
            s.Preset = "custom";
            return why;
        }
    }

    partial class MainForm
    {
        bool reading;

        void FindBestSettings()
        {
            if (reading) return;
            reading = true;
            pill.Set("Reading your PC...", Theme.Accent);
            flashTimer.Stop();
            new Thread(() =>
            {
                PcInfo pc = null; Exception err = null;
                try { pc = PcReader.Read(); } catch (Exception ex) { err = ex; Log.Write("PC read failed: " + ex); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        reading = false;
                        if (pc == null) { Flash(Cute.Wont("read your PC", Cute.Reason(err)), Theme.Bad); return; }
                        var trial = new Settings();
                        var why = PcReader.Recommend(pc, trial);
                        string tierName = new Dictionary<string, string> { { "potato", "a gentle little PC" }, { "low", "a budget PC" }, { "mid", "a solid mid-range PC" }, { "high", "a strong gaming PC" }, { "ultra", "a beast of a PC" } }[pc.Tier];
                        string msg = pc.Summary() + "\n\nThat's " + tierName + " " + Cute.Face() + " Here's what fits it best:\n\n•  " + string.Join("\n•  ", why) +
                                     "\n\nYour own FFlags, overlay and mods stay as they are.";
                        if (!VDialog.Confirm(this, "Found your best settings!! >_<", msg, "Apply", "Not now")) { Flash("Kept your current settings", Theme.Accent); return; }
                        var keepFlags = S.CustomFlags;
                        PcReader.Recommend(pc, S);
                        S.CustomFlags = keepFlags;
                        LoadIntoUi();
                        SaveNow(false);
                        Flash("Best settings applied for " + tierName.Substring(tierName.IndexOf(' ') + 1), Theme.Accent);
                    }));
                }
                catch { reading = false; }
            }) { IsBackground = true }.Start();
        }
    }
}
