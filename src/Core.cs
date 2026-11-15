// CloveStrap - a lightweight Roblox bootstrapper
// Build: csc /target:winexe /win32icon:void.ico /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll CloveStrap.cs
// Language level: C# 5 (works with the csc.exe that ships with Windows / .NET Framework 4.x)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("CloveStrap")]
[assembly: System.Reflection.AssemblyProduct("CloveStrap")]
[assembly: System.Reflection.AssemblyDescription("Roblox bootstrapper")]
[assembly: System.Reflection.AssemblyVersion("2.1.0.0")]   // the release workflow sets this from the git tag (v2.1.0)
[assembly: System.Reflection.AssemblyFileVersion("2.1.0.0")]

namespace CloveStrap
{
    // ------------------------------------------------------------------ paths
    static class Paths
    {
        public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Base = Path.Combine(LocalAppData, "CloveStrap");
        public static readonly string Versions = Path.Combine(Base, "Versions");
        public static readonly string Downloads = Path.Combine(Base, "Downloads");
        public static readonly string Mods = Path.Combine(Base, "Mods");
        public static readonly string Logs = Path.Combine(Base, "Logs");
        public static readonly string SettingsFile = Path.Combine(Base, "Settings.json");
        public static readonly string StateFile = Path.Combine(Base, "State.json");
        public static readonly string InstalledExe = Path.Combine(Base, "CloveStrap.exe");
        public static readonly string RobloxData = Path.Combine(LocalAppData, "Roblox");
        public static readonly string OfficialVersions = Path.Combine(RobloxData, "Versions");

        public static void Ensure()
        {
            foreach (var d in new[] { Base, Versions, Downloads, Mods, Logs })
                Directory.CreateDirectory(d);
        }

        public static readonly string OldBase = Path.Combine(LocalAppData, "Voidstrapper");

        /// <summary>
        /// CloveStrap used to be called Voidstrapper. The first time it runs, it brings over settings, accounts, mods,
        /// themes, finder progress and Roblox-original backups from the old folder. Returns a note for the log, or null.
        /// </summary>
        public static string MigrateFromVoidstrapper()
        {
            try
            {
                if (!Directory.Exists(OldBase) || File.Exists(SettingsFile)) return null;
                Directory.CreateDirectory(Base);
                int n = 0;
                foreach (var name in new[] { "Settings.json", "State.json", "Accounts.json", "alts.txt" })
                {
                    string src = Path.Combine(OldBase, name);
                    if (File.Exists(src)) { File.Copy(src, Path.Combine(Base, name), true); n++; }
                }
                foreach (var f in Directory.GetFiles(OldBase, "*.xml.bak")) { File.Copy(f, Path.Combine(Base, Path.GetFileName(f)), true); n++; }
                foreach (var f in Directory.GetFiles(OldBase, "avatar_*.png")) { try { File.Copy(f, Path.Combine(Base, Path.GetFileName(f)), true); } catch { } }
                foreach (var dir in new[] { "Mods", "ModNames", "ModPreviews", "Characters", "NameFinder", "Originals" })
                {
                    string src = Path.Combine(OldBase, dir);
                    if (!Directory.Exists(src)) continue;
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string dest = Path.Combine(Base, dir, f.Substring(src.Length).TrimStart('\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(f, dest, true);
                        n++;
                    }
                }
                // Roblox itself: move it if nothing is running from it, otherwise it gets copied again on the next launch
                string oldVersions = Path.Combine(OldBase, "Versions");
                if (Directory.Exists(oldVersions) && !Directory.Exists(Versions))
                {
                    try { Directory.Move(oldVersions, Versions); }
                    catch { }
                }
                return "Moved " + n + " files from the old Voidstrapper folder";
            }
            catch (Exception ex) { return "Moving data from Voidstrapper failed: " + ex.Message; }
        }

        /// <summary>Deletes the old Voidstrapper folder once nothing uses it any more (tries again on later starts).</summary>
        public static void RemoveOldFolder()
        {
            if (!Directory.Exists(OldBase) || !File.Exists(SettingsFile)) return;
            new Thread(() =>
            {
                try { Directory.Delete(OldBase, true); Log.Write("Removed the old Voidstrapper folder"); }
                catch { }   // something still runs from it (an old helper or Roblox): next time
            }) { IsBackground = true }.Start();
        }
    }

    // ------------------------------------------------------------------ logging
    static class Log
    {
        static readonly object Lock = new object();
        static string file;

        public static void Init()
        {
            try
            {
                file = Path.Combine(Paths.Logs, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_" + Process.GetCurrentProcess().Id + ".log");
                // keep the 15 newest logs
                var old = new DirectoryInfo(Paths.Logs).GetFiles("*.log").OrderByDescending(f => f.CreationTimeUtc).Skip(15);
                foreach (var f in old) { try { f.Delete(); } catch { } }
            }
            catch { }
        }

        public static string FilePath { get { return file; } }

        public static void Write(string msg)
        {
            lock (Lock)
            {
                try { if (file != null) File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine); }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------ settings
    class Settings
    {
        public int FpsCap { get; set; }                // 0 = match monitor refresh rate
        public string GraphicsApi { get; set; }        // Automatic | D3D11 | Vulkan | OpenGL
        public string Msaa { get; set; }               // Automatic | 1 | 2 | 4 | 8
        public string TextureQuality { get; set; }     // Automatic | 0 | 1 | 2 | 3
        public int QualityOverride { get; set; }       // 0 = off, 1..21
        public bool ExclusiveFullscreen { get; set; }
        public bool DisableGrassSway { get; set; }
        public bool RemoveGrass { get; set; }
        public bool DisableDpiScale { get; set; }
        public bool GraySky { get; set; }
        public bool LowMeshDetail { get; set; }        // same as ViewDistance "near"
        public string ViewDistance { get; set; }       // near | short | default | far | max
        public bool PauseVoxelizer { get; set; }
        public string ColorScheme { get; set; }
        public string CustomAccent { get; set; }
        public bool CustomLight { get; set; }
        public bool OverlayEnabled { get; set; }
        public string OverlayItems { get; set; }      // comma list, see OverlayItems.Keys
        public string OverlayCorner { get; set; }     // tl | tr | bl | br
        public bool Crosshair { get; set; }
        public bool ScreenCrosshair { get; set; }      // draw the crosshair on top of Roblox (covers any game's crosshair)
        public string CrosshairStyle { get; set; }    // dot | cross | circle
        public int CrosshairSize { get; set; }        // 0..2
        public string Preset { get; set; }             // balanced | competitive | quality | lowend | laptop | custom
        public string Priority { get; set; }           // Normal | AboveNormal | High
        public bool RegisterProtocol { get; set; }
        public bool PlayButtonDefaultApplied { get; set; }   // one-time switch-on for settings saved when the default was off
        public bool OverlayDefaultOffApplied { get; set; }   // one-time switch-off for settings saved when the overlay defaulted on
        public bool StartWithWindows { get; set; }           // sits in the tray from sign-in
        public bool StartupDefaultApplied { get; set; }
        public bool MultiDefaultApplied { get; set; }
        public bool AntiAfk { get; set; }                   // keep Roblox windows from the 20 minute idle kick
        public bool DiscordPresence { get; set; }           // "Playing <game> with CloveStrap" on Discord
        public string DiscordAppId { get; set; }            // optional override; normally the built-in CloveStrap app
        // signed in with Discord: kept in its own file (Discord.json), so no other save can ever wipe it
        [ScriptIgnore] public string DiscordUserId { get { return DiscordAccount.Current.UserId; } }
        [ScriptIgnore] public string DiscordName { get { return DiscordAccount.Current.Name; } }
        [ScriptIgnore] public string DiscordAvatar { get { return DiscordAccount.Current.Avatar; } }
        public bool AutoRejoin { get; set; }                // reopen an account that got kicked / disconnected
        public string AltWindows { get; set; }              // minimized | normal (the old "dock" counts as normal)
        public string ServerPick { get; set; }              // any | empty | full | ping
        public bool QuietRoblox { get; set; }               // quieten Roblox windows you switch away from
        public string QuietMode { get; set; }               // mute | lower
        public bool AntiAfkDefaultApplied { get; set; }
        public bool MultiInstance { get; set; }
        public bool CleanupOldVersions { get; set; }
        public bool ApplyMods { get; set; }
        public List<CustomTheme> CustomThemes { get; set; }
        public string JoinMode { get; set; }           // place | follow (Accounts page)
        public string JoinValue { get; set; }          // place id / link, or username to follow
        public Dictionary<string, string> CustomFlags { get; set; }

        public Settings()
        {
            Presets.Apply(this, "balanced");
            RegisterProtocol = true;    // Play on roblox.com goes through CloveStrap unless the user turns it off
            PlayButtonDefaultApplied = true;
            MultiInstance = true;       // several Roblox windows at once, on by default
            MultiDefaultApplied = true;
            AntiAfk = true;
            AntiAfkDefaultApplied = true;
            DiscordPresence = true;
            DiscordAppId = "";
            AutoRejoin = true;
            AltWindows = "normal";
            ServerPick = "any";
            QuietRoblox = false;
            QuietMode = "mute";
            CleanupOldVersions = true;
            ApplyMods = true;
            CustomThemes = new List<CustomTheme>();
            JoinMode = "place";
            JoinValue = "";
            ColorScheme = "clove";
            CustomAccent = "#FF6FAE";
            CustomLight = false;
            OverlayEnabled = false;     // the in-game overlay is opt-in (Overlay > Show overlay)
            OverlayDefaultOffApplied = true;
            StartWithWindows = true;
            StartupDefaultApplied = true;
            OverlayItems = CloveStrap.OverlayItems.Default;
            OverlayCorner = "tl";
            Crosshair = false;
            CrosshairStyle = "dot";
            CrosshairSize = 1;
            CustomFlags = new Dictionary<string, string>();
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(Paths.SettingsFile))
                {
                    Settings s = null;
                    for (int attempt = 0; s == null; attempt++)
                    {
                        try { s = new JavaScriptSerializer().Deserialize<Settings>(ReadSettingsText()); break; }
                        catch { if (attempt >= 4) throw; Thread.Sleep(30); }   // caught mid-save: try again rather than fall back to defaults
                    }
                    if (s != null)
                    {
                        if (s.CustomFlags == null) s.CustomFlags = new Dictionary<string, string>();
                        if (string.IsNullOrEmpty(s.GraphicsApi)) s.GraphicsApi = "Automatic";
                        if (string.IsNullOrEmpty(s.Msaa)) s.Msaa = "Automatic";
                        if (string.IsNullOrEmpty(s.TextureQuality)) s.TextureQuality = "Automatic";
                        if (string.IsNullOrEmpty(s.Priority)) s.Priority = "AboveNormal";
                        if (string.IsNullOrEmpty(s.Preset)) s.Preset = "custom";
                        if (string.IsNullOrEmpty(s.ViewDistance)) s.ViewDistance = s.LowMeshDetail ? "near" : "default";
                        if (!s.PlayButtonDefaultApplied) { s.RegisterProtocol = true; s.PlayButtonDefaultApplied = true; }
                        if (!s.OverlayDefaultOffApplied) { s.OverlayEnabled = false; s.OverlayDefaultOffApplied = true; }
                        if (!s.StartupDefaultApplied) { s.StartWithWindows = true; s.StartupDefaultApplied = true; }
                        if (!s.MultiDefaultApplied) { s.MultiInstance = true; s.MultiDefaultApplied = true; }
                        if (!s.AntiAfkDefaultApplied) { s.AntiAfk = true; s.AntiAfkDefaultApplied = true; }
                        if (s.CustomThemes == null)
                        {
                            // older versions had a single custom theme; keep it (and its picture) as the first of the list
                            s.CustomThemes = new List<CustomTheme>();
                            if (s.ColorScheme == "custom" || File.Exists(Path.Combine(Paths.Base, "Characters", "custom.png")))
                                s.CustomThemes.Add(new CustomTheme { Id = "custom", Name = "My theme", Accent = s.CustomAccent ?? "#FF6FAE", Light = s.CustomLight });
                        }
                        s.CustomThemes.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Id));
                        // plain color schemes were removed: only character themes and the user's own themes remain
                        bool known = s.CustomThemes.Any(t => t.Id == s.ColorScheme) || Theme.Schemes.Any(x => x.Id == s.ColorScheme && x.Character);
                        if (!known) s.ColorScheme = "clove";
                        // named presets always follow their current definition (e.g. FPS cap = monitor)
                        if (s.Preset != "custom" && Presets.All.Any(p => p.Id == s.Preset)) Presets.Apply(s, s.Preset);
                        return s;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Settings load failed, using defaults: " + ex.Message); }
            return new Settings();
        }

        /// <summary>
        /// Saves atomically (write a temp file, then swap it in) and retries if the file is busy. The tray's background helpers
        /// read the settings several times a second, so a plain overwrite could fail silently or be read half-written,
        /// which is how changes used to get lost between launches.
        /// </summary>
        public void Save()
        {
            string text = Json.Pretty(new JavaScriptSerializer().Serialize(this));
            string dest = Paths.SettingsFile, tmp = dest + ".tmp";
            Exception last = null;
            lock (SaveLock)
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        File.WriteAllText(tmp, text, new UTF8Encoding(false));
                        if (File.Exists(dest)) File.Replace(tmp, dest, null, true);
                        else File.Move(tmp, dest);
                        return;
                    }
                    catch (Exception ex) { last = ex; Thread.Sleep(25); }
                }
            Log.Write("Settings save failed: " + (last != null ? last.Message : "?"));
            throw last ?? new IOException("Couldn't save settings");
        }

        static readonly object SaveLock = new object();

        static string ReadSettingsText()
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    using (var fs = new FileStream(Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                    {
                        string t = r.ReadToEnd();
                        if (t.Trim().Length > 0 || i >= 9) return t;
                    }
                }
                catch (IOException) { if (i >= 9) throw; }
                Thread.Sleep(20);
            }
        }
    }

    class CustomTheme
    {
        public string Id { get; set; }        // "custom" or "custom-xxxxxxxx"; picture lives in Characters\<Id>.png
        public string Name { get; set; }
        public string Accent { get; set; }    // #RRGGBB
        public bool Light { get; set; }
    }

    class State
    {
        public string CurrentVersion { get; set; }
        public string PrevPlayerCommand { get; set; }
        public string PrevRobloxCommand { get; set; }

        public static State Load()
        {
            try
            {
                if (File.Exists(Paths.StateFile))
                {
                    var s = new JavaScriptSerializer().Deserialize<State>(File.ReadAllText(Paths.StateFile));
                    if (s != null) return s;
                }
            }
            catch { }
            return new State();
        }

        public void Save()
        {
            try { File.WriteAllText(Paths.StateFile, new JavaScriptSerializer().Serialize(this)); }
            catch (Exception ex) { Log.Write("State save failed: " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ json helpers
    static class Json
    {
        // Indents compact JSON produced by JavaScriptSerializer.
        public static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0; bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{': case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        indent++; sb.Append('\n').Append(new string(' ', indent * 2)); break;
                    case '}': case ']':
                        indent--; sb.Append('\n').Append(new string(' ', indent * 2)).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(new string(' ', indent * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string FlagsToJson(List<KeyValuePair<string, string>> flags)
        {
            var ser = new JavaScriptSerializer();
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < flags.Count; i++)
            {
                sb.Append("  ").Append(ser.Serialize(flags[i].Key)).Append(": ").Append(ser.Serialize(flags[i].Value));
                sb.Append(i < flags.Count - 1 ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }

        public static Dictionary<string, string> ParseFlags(string json)
        {
            var raw = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var result = new Dictionary<string, string>();
            foreach (var kv in raw)
            {
                object v = kv.Value;
                string s;
                if (v == null) s = "";
                else if (v is bool) s = ((bool)v) ? "True" : "False";
                else if (v is IFormattable) s = ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
                else s = v.ToString();
                result[kv.Key] = s;
            }
            return result;
        }
    }

    // ------------------------------------------------------------------ display
    static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        public static int RefreshRate() { return RefreshRate(null); }

        /// <summary>Refresh rate (Hz) of one monitor, e.g. Screen.DeviceName; null = the main monitor.</summary>
        public static int RefreshRate(string device)
        {
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(device, -1, ref dm) && dm.dmDisplayFrequency > 1)
                    return dm.dmDisplayFrequency;
            }
            catch { }
            return 60;
        }
    }

    // ------------------------------------------------------------------ fast flags
    static class Flags
    {
        // Roblox only honours these client flags (FastFlag allowlist, Sept 2025). Everything else is ignored.
        public static readonly HashSet<string> Allowlist = new HashSet<string>(StringComparer.Ordinal)
        {
            "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12",
            "DFIntCSGLevelOfDetailSwitchingDistanceL23", "DFIntCSGLevelOfDetailSwitchingDistanceL34",
            "FFlagHandleAltEnterFullscreenManually", "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride",
            "FIntDebugForceMSAASamples", "DFFlagDisableDPIScale", "FFlagDebugGraphicsPreferD3D11", "FFlagDebugSkyGray",
            "DFFlagDebugPauseVoxelizer", "DFIntDebugFRMQualityLevelOverride", "FIntFRMMaxGrassDistance",
            "FIntFRMMinGrassDistance", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL",
            "FIntGrassMovementReducedMotionFactor"
        };

        public static List<KeyValuePair<string, string>> Build(Settings s)
        {
            var d = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, v) =>
            {
                d.RemoveAll(p => p.Key == k);
                d.Add(new KeyValuePair<string, string>(k, v));
            };

            if (s.ExclusiveFullscreen) add("FFlagHandleAltEnterFullscreenManually", "False");

            switch (s.GraphicsApi)
            {
                case "D3D11": add("FFlagDebugGraphicsPreferD3D11", "True"); break;
                case "Vulkan": add("FFlagDebugGraphicsPreferVulkan", "True"); break;
                case "OpenGL": add("FFlagDebugGraphicsPreferOpenGL", "True"); break;
            }

            if (s.Msaa != "Automatic") add("FIntDebugForceMSAASamples", s.Msaa);

            if (s.TextureQuality != "Automatic")
            {
                add("DFFlagTextureQualityOverrideEnabled", "True");
                add("DFIntTextureQualityOverride", s.TextureQuality);
            }

            if (s.QualityOverride >= 1 && s.QualityOverride <= 21)
                add("DFIntDebugFRMQualityLevelOverride", s.QualityOverride.ToString(CultureInfo.InvariantCulture));

            if (s.DisableGrassSway) add("FIntGrassMovementReducedMotionFactor", "0");
            if (s.RemoveGrass) { add("FIntFRMMinGrassDistance", "0"); add("FIntFRMMaxGrassDistance", "0"); }
            if (s.DisableDpiScale) add("DFFlagDisableDPIScale", "True");
            if (s.GraySky) add("FFlagDebugSkyGray", "True");
            if (s.PauseVoxelizer) add("DFFlagDebugPauseVoxelizer", "True");
            // view distance: how far away models keep their detail, separate from the graphics quality level
            int[] lod = null;
            switch (s.LowMeshDetail ? "near" : (s.ViewDistance ?? "default"))
            {
                case "near": lod = new[] { 0, 0, 0, 0 }; break;
                case "short": lod = new[] { 100, 200, 300, 400 }; break;
                case "far": lod = new[] { 500, 1000, 1500, 2000 }; break;
                case "max": lod = new[] { 1500, 3000, 4500, 6000 }; break;
            }
            if (lod != null)
            {
                add("DFIntCSGLevelOfDetailSwitchingDistance", lod[0].ToString(CultureInfo.InvariantCulture));
                add("DFIntCSGLevelOfDetailSwitchingDistanceL12", lod[1].ToString(CultureInfo.InvariantCulture));
                add("DFIntCSGLevelOfDetailSwitchingDistanceL23", lod[2].ToString(CultureInfo.InvariantCulture));
                add("DFIntCSGLevelOfDetailSwitchingDistanceL34", lod[3].ToString(CultureInfo.InvariantCulture));
            }

            // user's own flags win
            foreach (var kv in s.CustomFlags)
                if (!string.IsNullOrWhiteSpace(kv.Key)) add(kv.Key.Trim(), kv.Value ?? "");

            return d;
        }
    }

    // ------------------------------------------------------------------ presets
    class PresetInfo
    {
        public string Id, Name, Glyph, Desc;
        public string[] Tags;
        public Color Tint;
        public bool Recommended;
    }

    static class Presets
    {
        public static readonly PresetInfo[] All =
        {
            new PresetInfo { Id = "balanced", Name = "Balanced", Glyph = "", Recommended = true, Tint = Color.FromArgb(167, 139, 250),
                Desc = "Faster, and looks identical.", Tags = new[] { "FPS +", "Looks the same" } },
            new PresetInfo { Id = "competitive", Name = "Competitive", Glyph = "", Tint = Color.FromArgb(251, 146, 60),
                Desc = "Every frame you can get.", Tags = new[] { "FPS +++", "Lowest lag" } },
            new PresetInfo { Id = "quality", Name = "Quality", Glyph = "", Tint = Color.FromArgb(34, 211, 238),
                Desc = "Cleaner edges and sharper textures.", Tags = new[] { "Best visuals", "Strong GPU" } },
            new PresetInfo { Id = "lowend", Name = "Low-End PC", Glyph = "", Tint = Color.FromArgb(74, 222, 128),
                Desc = "Strips detail so old hardware keeps up.", Tags = new[] { "FPS ++++", "Looks worse" } },
            new PresetInfo { Id = "laptop", Name = "Laptop", Glyph = "", Tint = Color.FromArgb(96, 165, 250),
                Desc = "Runs cooler and quieter.", Tags = new[] { "Cooler", "Battery +" } },
            new PresetInfo { Id = "custom", Name = "Custom", Glyph = "", Tint = Color.FromArgb(232, 121, 249),
                Desc = "Whatever you set by hand.", Tags = new[] { "Your settings" } },
        };

        public static PresetInfo Get(string id)
        {
            return All.FirstOrDefault(p => p.Id == id) ?? All[All.Length - 1];
        }

        // Every preset only uses flags on Roblox's allowlist, and every preset caps FPS at the monitor's refresh rate.
        public static void Apply(Settings s, string id)
        {
            // shared baseline
            s.FpsCap = 0;                       // 0 = match monitor refresh rate
            s.GraphicsApi = "Automatic";
            s.Msaa = "Automatic";
            s.TextureQuality = "Automatic";
            s.QualityOverride = 0;
            s.ExclusiveFullscreen = true;       // FFlagHandleAltEnterFullscreenManually
            s.DisableGrassSway = true;          // FIntGrassMovementReducedMotionFactor
            s.RemoveGrass = false;
            s.LowMeshDetail = false;
            s.ViewDistance = "default";
            s.DisableDpiScale = false;
            s.GraySky = false;
            s.PauseVoxelizer = false;
            s.Priority = "AboveNormal";

            switch (id)
            {
                case "competitive":
                    s.GraphicsApi = "D3D11";    // steadiest frame times on Windows
                    s.Msaa = "1";
                    s.RemoveGrass = true;
                    s.GraySky = true;           // cleaner background, easier to spot players
                    s.Priority = "High";
                    break;
                case "quality":
                    s.Msaa = "4";
                    s.TextureQuality = "3";
                    s.QualityOverride = 21;     // highest graphics level
                    s.ViewDistance = "far";
                    s.DisableGrassSway = false;
                    s.DisableDpiScale = true;   // native resolution
                    break;
                case "lowend":
                    s.GraphicsApi = "D3D11";
                    s.QualityOverride = 1;
                    s.TextureQuality = "0";
                    s.Msaa = "1";
                    s.RemoveGrass = true;
                    s.LowMeshDetail = true; s.ViewDistance = "near";
                    s.GraySky = true;
                    s.PauseVoxelizer = true;    // stop lighting recalculation
                    break;
                case "laptop":
                    s.GraphicsApi = "D3D11";
                    s.QualityOverride = 8;
                    s.TextureQuality = "2";
                    s.Msaa = "1";
                    s.Priority = "Normal";
                    break;
            }
            s.Preset = id == "custom" ? "custom" : Get(id).Id;
        }
    }

    // ------------------------------------------------------------------ roblox deployment (install / update)
    class Package
    {
        public string Name, Md5;
        public long PackedSize, Size;
    }

    static class Deployment
    {
        static readonly string[] Mirrors = { "https://setup.rbxcdn.com/", "https://setup-aws.rbxcdn.com/", "https://setup-ak.rbxcdn.com/" };
        const string VersionApi = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";
        const string Marker = ".voidstrapper";
        const string UserAgent = "CloveStrap/1.0";

        // zip name -> folder inside the version directory
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RobloxApp.zip", "" },
            { "Libraries.zip", "" },
            { "redist.zip", "" },
            { "WebView2.zip", "" },
            { "WebView2RuntimeInstaller.zip", @"WebView2RuntimeInstaller\" },
            { "shaders.zip", @"shaders\" },
            { "ssl.zip", @"ssl\" },
            { "content-avatar.zip", @"content\avatar\" },
            { "content-configs.zip", @"content\configs\" },
            { "content-fonts.zip", @"content\fonts\" },
            { "content-sky.zip", @"content\sky\" },
            { "content-sounds.zip", @"content\sounds\" },
            { "content-textures2.zip", @"content\textures\" },
            { "content-models.zip", @"content\models\" },
            { "content-platform-fonts.zip", @"PlatformContent\pc\fonts\" },
            { "content-platform-dictionaries.zip", @"PlatformContent\pc\shared_compression_dictionaries\" },
            { "content-terrain.zip", @"PlatformContent\pc\terrain\" },
            { "content-textures3.zip", @"PlatformContent\pc\textures\" },
            { "extracontent-luapackages.zip", @"ExtraContent\LuaPackages\" },
            { "extracontent-translations.zip", @"ExtraContent\translations\" },
            { "extracontent-models.zip", @"ExtraContent\models\" },
            { "extracontent-textures.zip", @"ExtraContent\textures\" },
            { "extracontent-places.zip", @"ExtraContent\places\" },
        };

        const string AppSettingsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<ContentFolder>content</ContentFolder>\r\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n";

        public static bool NoCopy;     // testing switch: always download from Roblox's CDN

        static string Get(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        static string QueryVersion(string url)
        {
            var obj = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Get(url));
            string v = obj["clientVersionUpload"] as string;
            if (string.IsNullOrEmpty(v) || !Regex.IsMatch(v, "^version-[0-9a-f]+$")) throw new Exception("Unexpected version response from Roblox");
            return v;
        }

        /// <summary>The update channel Roblox put this PC on (e.g. a test group), or null for the normal one.</summary>
        public static string Channel()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\ROBLOX Corporation\Environments\RobloxPlayer\Channel"))
                {
                    string c = k == null ? null : (k.GetValue("www.roblox.com") as string);
                    if (string.IsNullOrWhiteSpace(c)) return null;
                    c = c.Trim();
                    if (c.Equals("production", StringComparison.OrdinalIgnoreCase) || c.Equals("live", StringComparison.OrdinalIgnoreCase)) return null;
                    return Regex.IsMatch(c, "^[A-Za-z0-9_-]+$") ? c : null;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// The version Roblox itself will insist on. Roblox checks the PC's channel and updates itself (through the official
        /// launcher, losing every mod) when it isn't on that version, so CloveStrap follows the channel too. Channel builds
        /// can't be downloaded, only copied from the official install, so until Roblox has installed one we stay on the public build.
        /// </summary>
        static string cachedLatest;
        static DateTime cachedLatestAt;

        public static string LatestVersion()
        {
            // the splash screen checks first; reuse that answer for a minute and a half
            if (cachedLatest != null && (DateTime.UtcNow - cachedLatestAt).TotalSeconds < 90) return cachedLatest;
            string v = LatestVersionUncached();
            cachedLatest = v; cachedLatestAt = DateTime.UtcNow;
            return v;
        }

        static string LatestVersionUncached()
        {
            string live = QueryVersion(VersionApi);
            string ch = Channel();
            if (ch == null) return live;
            string v;
            try { v = QueryVersion(VersionApi + "/channel/" + ch); }
            catch (Exception ex) { Log.Write("Channel " + ch + " lookup failed (" + ex.Message + "), using " + live); return live; }
            if (v == live) return v;
            if (IsComplete(Path.Combine(Paths.Versions, v)) ||
                (File.Exists(Path.Combine(Paths.OfficialVersions, v, "RobloxPlayerBeta.exe")) && File.Exists(Path.Combine(Paths.OfficialVersions, v, "AppSettings.xml"))))
            {
                Log.Write("Roblox channel " + ch + " is on " + v + " (public is " + live + "), following it");
                return v;
            }
            Log.Write("Roblox channel " + ch + " wants " + v + " but Roblox hasn't installed it yet; using " + live + " for now");
            return live;
        }

        public static bool IsComplete(string dir)
        {
            return File.Exists(Path.Combine(dir, Marker)) && File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe"));
        }

        public static List<Package> Manifest(string version)
        {
            Exception last = null;
            foreach (var m in Mirrors)
            {
                try
                {
                    var lines = Get(m + version + "-rbxPkgManifest.txt").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    if (lines.Count == 0 || lines[0] != "v0") throw new Exception("Unknown manifest format: " + (lines.Count > 0 ? lines[0] : "(empty)"));
                    var list = new List<Package>();
                    for (int i = 1; i + 3 < lines.Count; i += 4)
                    {
                        list.Add(new Package
                        {
                            Name = lines[i],
                            Md5 = lines[i + 1],
                            PackedSize = long.Parse(lines[i + 2], CultureInfo.InvariantCulture),
                            Size = long.Parse(lines[i + 3], CultureInfo.InvariantCulture)
                        });
                    }
                    return list;
                }
                catch (Exception ex) { last = ex; Log.Write("Manifest from " + m + " failed: " + ex.Message); }
            }
            throw new Exception("Could not download the Roblox package list", last);
        }

        static string TargetFolder(string zip)
        {
            string t;
            if (Map.TryGetValue(zip, out t)) return t;
            // unknown package: best guess from its name so new Roblox packages still land somewhere sensible
            string n = Path.GetFileNameWithoutExtension(zip);
            if (n.StartsWith("extracontent-", StringComparison.OrdinalIgnoreCase)) return @"ExtraContent\" + n.Substring(13) + @"\";
            if (n.StartsWith("content-platform-", StringComparison.OrdinalIgnoreCase)) return @"PlatformContent\pc\" + n.Substring(17) + @"\";
            if (n.StartsWith("content-", StringComparison.OrdinalIgnoreCase)) return @"content\" + n.Substring(8) + @"\";
            return "";
        }

        static string Md5Of(string file)
        {
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static void Extract(string zipFile, string root, string sub)
        {
            string rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string target = Path.Combine(rootFull, sub);
            using (var zip = ZipFile.OpenRead(zipFile))
            {
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('/', '\\').TrimStart('\\');
                    if (name.Length == 0) continue;
                    string dest = Path.GetFullPath(Path.Combine(target, name));
                    if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                    if (name.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
        }

        // Downloads + verifies + extracts one package. Adds downloaded bytes to 'counter'.
        static void Fetch(string version, Package p, string dir, ref long counter, Action onBytes)
        {
            string tmp = Path.Combine(Paths.Downloads, version + "-" + p.Name);
            Exception last = null;
            for (int attempt = 0; attempt < 3 * Mirrors.Length; attempt++)
            {
                string url = Mirrors[attempt % Mirrors.Length] + version + "-" + p.Name;
                long got = 0;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = UserAgent;
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 60000;
                    req.AutomaticDecompression = DecompressionMethods.None;
                    using (var resp = req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        var buf = new byte[1 << 16];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            got += n;
                            Interlocked.Add(ref counter, n);
                            onBytes();
                        }
                    }
                    string hash = Md5Of(tmp);
                    if (!hash.Equals(p.Md5, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("checksum mismatch for " + p.Name);
                    Extract(tmp, dir, TargetFolder(p.Name));
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Interlocked.Add(ref counter, -got);
                    Log.Write("Package " + p.Name + " attempt " + (attempt + 1) + " failed: " + ex.Message);
                    Thread.Sleep(500 * (attempt % 3 + 1));
                }
            }
            throw new Exception("Failed to download " + p.Name, last);
        }

        static void CopyDir(string src, string dst, Action<string> onFile = null)
        {
            Directory.CreateDirectory(dst);
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = d.Substring(src.Length).TrimStart('\\');
                if (rel.Equals("ClientSettings", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("ClientSettings\\", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                if (rel.StartsWith("ClientSettings\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.Equals("RobloxPlayerInstaller.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (onFile != null) onFile(rel);
                File.Copy(f, Path.Combine(dst, rel), true);
            }
        }

        public static bool RobloxRunning()
        {
            return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
        }

        /// <summary>Makes sure the latest Roblox is installed. Returns the version folder.</summary>
        public static string EnsureInstalled(Action<string, int> progress, bool smallOnlyTest = false, string overrideDir = null)
        {
            progress("Checking for Roblox updates\nclientsettingscdn.roblox.com  ·  WindowsPlayer", -1);
            string version = LatestVersion();
            Log.Write("Latest Roblox version: " + version);
            string dir = overrideDir ?? Path.Combine(Paths.Versions, version);

            if (!IsComplete(dir))
            {
                if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch (Exception ex) { Log.Write("Could not clear partial install: " + ex.Message); } }
                Directory.CreateDirectory(dir);

                string official = Path.Combine(Paths.OfficialVersions, version);
                if (!NoCopy && !smallOnlyTest && File.Exists(Path.Combine(official, "RobloxPlayerBeta.exe")) && File.Exists(Path.Combine(official, "AppSettings.xml")))
                {
                    // Same version already on disk from the official installer: copy instead of re-downloading ~400 MB
                    progress("Copying Roblox\n" + version, -1);
                    Log.Write("Copying from official install: " + official);
                    CopyDir(official, dir, rel => progress("Copying Roblox\n" + rel, -1));
                }
                else
                {
                    var pkgs = Manifest(version).Where(p => p.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (smallOnlyTest) pkgs = pkgs.Where(p => p.PackedSize < 3000000).ToList();
                    foreach (var p in pkgs.Where(p => !Map.ContainsKey(p.Name)))
                        Log.Write("Note: unknown package " + p.Name + " -> " + TargetFolder(p.Name));

                    long total = Math.Max(1, pkgs.Sum(p => p.PackedSize));
                    long done = 0;
                    int lastPct = -1;
                    string currentPkg = "", shownPkg = null;
                    Action tick = () =>
                    {
                        int pct = (int)Math.Min(100, Interlocked.Read(ref done) * 100 / total);
                        string pkg = currentPkg;
                        if (pct != lastPct || pkg != shownPkg)
                        {
                            lastPct = pct; shownPkg = pkg;
                            progress(string.Format("Downloading Roblox  ({0} / {1} MB)\n{2}", Interlocked.Read(ref done) >> 20, total >> 20, currentPkg), pct);
                        }
                    };

                    var queue = new Queue<Package>(pkgs.OrderByDescending(p => p.PackedSize));
                    Exception error = null;
                    var workers = new List<Thread>();
                    for (int w = 0; w < 5; w++)
                    {
                        var t = new Thread(() =>
                        {
                            while (true)
                            {
                                Package p;
                                lock (queue)
                                {
                                    if (queue.Count == 0 || error != null) return;
                                    p = queue.Dequeue();
                                    currentPkg = p.Name + "  ·  " + (p.PackedSize >> 10) + " KB";
                                }
                                try { Fetch(version, p, dir, ref done, tick); }
                                catch (Exception ex) { lock (queue) { if (error == null) error = ex; } return; }
                            }
                        });
                        t.IsBackground = true;
                        t.Start();
                        workers.Add(t);
                    }
                    foreach (var t in workers) t.Join();
                    if (error != null) throw error;

                    File.WriteAllText(Path.Combine(dir, "AppSettings.xml"), AppSettingsXml);
                }

                File.WriteAllText(Path.Combine(dir, Marker), version);
                Log.Write("Installed " + version + " to " + dir);
            }

            if (overrideDir == null)
            {
                var st = State.Load();
                st.CurrentVersion = version;
                st.Save();
            }
            return dir;
        }

        public static void CleanupOld(string keepDir)
        {
            if (RobloxRunning()) return; // never delete files from under a running client
            foreach (var d in Directory.GetDirectories(Paths.Versions))
            {
                if (string.Equals(Path.GetFullPath(d).TrimEnd('\\'), Path.GetFullPath(keepDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(d, true); Log.Write("Removed old version " + Path.GetFileName(d)); }
                catch (Exception ex) { Log.Write("Could not remove " + d + ": " + ex.Message); }
            }
            foreach (var f in Directory.GetFiles(Paths.Downloads)) { try { File.Delete(f); } catch { } }
        }

        /// <summary>Used when offline: newest working install we can find.</summary>
        public static string Fallback()
        {
            var st = State.Load();
            if (!string.IsNullOrEmpty(st.CurrentVersion))
            {
                string d = Path.Combine(Paths.Versions, st.CurrentVersion);
                if (IsComplete(d)) return d;
            }
            var mine = Directory.GetDirectories(Paths.Versions).Where(IsComplete).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
            if (mine != null) return mine;
            if (Directory.Exists(Paths.OfficialVersions))
                return Directory.GetDirectories(Paths.OfficialVersions)
                    .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                    .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "RobloxPlayerBeta.exe")))
                    .FirstOrDefault();
            return null;
        }

        public static string InstalledVersion()
        {
            var st = State.Load();
            if (!string.IsNullOrEmpty(st.CurrentVersion) && IsComplete(Path.Combine(Paths.Versions, st.CurrentVersion))) return st.CurrentVersion;
            return null;
        }
    }

    // ------------------------------------------------------------------ applying tweaks
    static class Tweaks
    {
        public static void Apply(Settings s, string versionDir, Action<string> detail = null)
        {
            detail = detail ?? (d => { });
            string cs = Path.Combine(versionDir, "ClientSettings");
            Directory.CreateDirectory(cs);
            var flags = Flags.Build(s);
            detail(@"ClientSettings\ClientAppSettings.json  ·  " + flags.Count + " flags");
            File.WriteAllText(Path.Combine(cs, "ClientAppSettings.json"), Json.FlagsToJson(flags), new UTF8Encoding(false));
            Log.Write("Wrote " + flags.Count + " fast flags");

            int fps = s.FpsCap > 0 ? s.FpsCap : Display.RefreshRate();
            foreach (var f in GlobalSettingsFiles())
            {
                detail(Path.GetFileName(f) + "  ·  FPS cap " + fps);
                SetFramerateCap(f, fps);
            }

            if (s.Crosshair) detail(@"content\textures\MouseLockedCursor.png");
            ShiftLockCursor.Apply(s, versionDir);
            if (s.ApplyMods) ApplyMods(versionDir, detail);
        }

        /// <summary>
        /// Roblox opened from its own shortcut, the Start menu or the Roblox app runs from the official install, not CloveStrap's.
        /// Putting the same FFlags and mods there means every way of opening Roblox gets them.
        /// </summary>
        public static int ApplyToOfficial(Settings s)
        {
            int n = 0;
            if (!Directory.Exists(Paths.OfficialVersions)) return 0;
            foreach (var d in Directory.GetDirectories(Paths.OfficialVersions))
            {
                if (!File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe"))) continue;
                try
                {
                    string cs = Path.Combine(d, "ClientSettings");
                    Directory.CreateDirectory(cs);
                    File.WriteAllText(Path.Combine(cs, "ClientAppSettings.json"), Json.FlagsToJson(Flags.Build(s)), new UTF8Encoding(false));
                    ShiftLockCursor.Apply(s, d);
                    if (s.ApplyMods) ApplyMods(d, x => { });
                    n++;
                }
                catch (Exception ex) { Log.Write("Official install " + Path.GetFileName(d) + ": " + ex.Message); }
            }
            if (n > 0) Log.Write("Applied settings and mods to " + n + " official Roblox install(s)");
            return n;
        }

        /// <summary>A fingerprint of everything that decides what goes into an install (settings, mods, official versions).</summary>
        public static string Fingerprint()
        {
            var sb = new StringBuilder();
            try { sb.Append(File.GetLastWriteTimeUtc(Paths.SettingsFile).Ticks); } catch { }
            try
            {
                if (Directory.Exists(Paths.Mods))
                    foreach (var f in Directory.GetFiles(Paths.Mods, "*", SearchOption.AllDirectories)) sb.Append('|').Append(f.Length).Append(File.GetLastWriteTimeUtc(f).Ticks);
            }
            catch { }
            try
            {
                if (Directory.Exists(Paths.OfficialVersions))
                    foreach (var d in Directory.GetDirectories(Paths.OfficialVersions)) sb.Append('|').Append(Path.GetFileName(d)).Append(File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")));
            }
            catch { }
            return sb.ToString();
        }

        public static IEnumerable<string> GlobalSettingsFiles()
        {
            if (!Directory.Exists(Paths.RobloxData)) return new string[0];
            return Directory.GetFiles(Paths.RobloxData, "GlobalBasicSettings_*.xml")
                .Where(f => !Path.GetFileName(f).ToLowerInvariant().Contains("studio"));
        }

        // Roblox's own in-game "Maximum Frame Rate" setting. This is the FPS cap that still works after the flag allowlist.
        public static bool SetFramerateCap(string file, int fps)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                var re = new Regex("(<int name=\"FramerateCap\">)(-?\\d+)(</int>)");
                string updated;
                if (re.IsMatch(text))
                {
                    updated = re.Replace(text, "${1}" + fps.ToString(CultureInfo.InvariantCulture) + "${3}", 1);
                }
                else
                {
                    // older settings file without the entry: add it to the UserGameSettings properties
                    var props = new Regex("(<Item class=\"UserGameSettings\"[^>]*>\\s*<Properties>)");
                    if (!props.IsMatch(text)) { Log.Write("FramerateCap entry not found in " + file); return false; }
                    updated = props.Replace(text, "${1}\n\t\t\t<int name=\"FramerateCap\">" + fps.ToString(CultureInfo.InvariantCulture) + "</int>", 1);
                }
                if (updated != text)
                {
                    string backup = Path.Combine(Paths.Base, Path.GetFileName(file) + ".bak");
                    if (!File.Exists(backup)) File.Copy(file, backup);
                    var enc = new UTF8Encoding(bom);
                    File.WriteAllText(file, updated, enc);
                }
                Log.Write("FPS cap set to " + fps + " in " + Path.GetFileName(file));
                return true;
            }
            catch (Exception ex) { Log.Write("Could not set FPS cap: " + ex.Message); return false; }
        }

        // Roblox's own copy of every file a mod replaces, so removing the mod puts the original back
        public static string OriginalsDir(string versionDir)
        {
            return Path.Combine(Paths.Base, "Originals", Path.GetFileName(versionDir.TrimEnd('\\')));
        }

        static void ApplyMods(string versionDir, Action<string> detail)
        {
            if (!Directory.Exists(Paths.Mods)) return;
            string origRoot = OriginalsDir(versionDir);
            var modRels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(Paths.Mods, "*", SearchOption.AllDirectories))
                modRels.Add(f.Substring(Paths.Mods.Length).TrimStart('\\'));

            // mods that were removed: put Roblox's original file back
            if (Directory.Exists(origRoot))
            {
                foreach (var o in Directory.GetFiles(origRoot, "*", SearchOption.AllDirectories))
                {
                    string rel = o.Substring(origRoot.Length).TrimStart('\\');
                    if (modRels.Contains(rel)) continue;
                    try
                    {
                        detail("Restoring " + rel);
                        File.Copy(o, Path.Combine(versionDir, rel), true);
                        File.Delete(o);
                        Log.Write("Restored original " + rel);
                    }
                    catch (Exception ex) { Log.Write("Restore " + rel + " failed: " + ex.Message); }
                }
            }

            int n = 0;
            foreach (var rel in modRels)
            {
                if (rel.Equals("README.txt", StringComparison.OrdinalIgnoreCase)) continue;
                string f = Path.Combine(Paths.Mods, rel);
                detail(@"Mods\" + rel);
                string dest = Path.Combine(versionDir, rel);
                try
                {
                    string orig = Path.Combine(origRoot, rel);
                    if (!File.Exists(orig) && File.Exists(dest))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(orig));
                        File.Copy(dest, orig);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(f, dest, true);
                    n++;
                }
                catch (Exception ex) { Log.Write("Mod " + rel + " failed: " + ex.Message); }
            }
            if (n > 0) Log.Write("Applied " + n + " mod file(s)");
        }

        public static void WriteModsReadme()
        {
            try
            {
                string p = Path.Combine(Paths.Mods, "README.txt");
                if (!File.Exists(p))
                    File.WriteAllText(p,
                        "CloveStrap mods folder\r\n\r\n" +
                        "Anything you put here is copied over the Roblox install every time you launch,\r\n" +
                        "using the same folder layout as the Roblox version folder.\r\n\r\n" +
                        "Examples:\r\n" +
                        "  content\\sounds\\ouch.ogg            -> custom death sound\r\n" +
                        "  content\\textures\\Cursors\\KeyboardMouse\\ArrowFarCursor.png -> custom cursor\r\n" +
                        "  content\\fonts\\...                   -> custom fonts\r\n\r\n" +
                        "To undo a mod: delete it here, then use Launcher > Reinstall Roblox.\r\n");
            }
            catch { }
        }

        public static int ClearCache()
        {
            int n = 0;
            string temp = Path.Combine(Path.GetTempPath(), "Roblox");
            if (!Directory.Exists(temp)) return 0;
            foreach (var f in Directory.GetFiles(temp, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(f); n++; } catch { }
            }
            return n;
        }
    }

    // ------------------------------------------------------------------ protocol handler (roblox-player:// and roblox://)
    static class Protocol
    {
        static readonly string[] Schemes = { "roblox-player", "roblox" };

        static string ReadCommand(string scheme)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + scheme + @"\shell\open\command"))
                return k == null ? null : k.GetValue("") as string;
        }

        public static bool IsRegistered()
        {
            string c = ReadCommand("roblox-player");
            return c != null && c.IndexOf("CloveStrap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string ExePath()
        {
            return File.Exists(Paths.InstalledExe) ? Paths.InstalledExe : Application.ExecutablePath;
        }

        // Keeps a stable copy in %LOCALAPPDATA%\CloveStrap so the Desktop file can be moved or deleted safely.
        public static void InstallSelf()
        {
            string me = Path.GetFullPath(Application.ExecutablePath);
            try { InstallSelfCopy(me); } finally { StartMenuShortcut(); }
        }

        // A "CloveStrap" entry in the Start menu (and so in Windows search), pointing at the installed copy.
        public static void StartMenuShortcut()
        {
            try
            {
                string exe = ExePath();
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "CloveStrap.lnk");
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    var t = sc.GetType();
                    string current = t.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
                    if (File.Exists(lnk) && string.Equals(current, exe, StringComparison.OrdinalIgnoreCase)) return;   // already right
                    t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
                    t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
                    t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                    t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "The smooth Roblox launcher" });
                    t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                    Log.Write("Start menu shortcut -> " + exe);
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
            }
            catch (Exception ex) { Log.Write("Start menu shortcut skipped: " + ex.Message); }
        }

        static void InstallSelfCopy(string me)
        {
            if (string.Equals(me, Path.GetFullPath(Paths.InstalledExe), StringComparison.OrdinalIgnoreCase)) return;
            // tidy up copies that were moved aside while they were running
            try
            {
                foreach (var old in Directory.GetFiles(Paths.Base, "CloveStrap.old-*.exe"))
                    try { File.Delete(old); } catch { }
            }
            catch { }
            try
            {
                var a = new FileInfo(me);
                var b = new FileInfo(Paths.InstalledExe);
                // never replace a newer installed CloveStrap with an older copy (e.g. an old download)
                var mine = Updater.FileVer(me); var theirs = b.Exists ? Updater.FileVer(Paths.InstalledExe) : null;
                if (mine != null && theirs != null && theirs > mine) { Log.Write("Not installing " + mine + " over newer " + theirs); return; }
                if (!b.Exists || a.Length != b.Length || a.LastWriteTimeUtc != b.LastWriteTimeUtc)
                {
                    try { File.Copy(me, Paths.InstalledExe, true); }
                    catch (IOException)
                    {
                        // an older CloveStrap is still running from it (e.g. keeping Roblox windows side by side):
                        // a running exe can be renamed, so move it aside and put the new one in its place
                        File.Move(Paths.InstalledExe, Path.Combine(Paths.Base, "CloveStrap.old-" + DateTime.Now.ToString("HHmmssfff") + ".exe"));
                        File.Copy(me, Paths.InstalledExe, true);
                    }
                    Log.Write("Installed this version to " + Paths.InstalledExe);
                }
            }
            catch (Exception ex) { Log.Write("Self-install skipped: " + ex.Message); }
        }

        public static void Register()
        {
            InstallSelf();
            string exe = ExePath();
            var st = State.Load();
            foreach (var scheme in Schemes)
            {
                string prev = ReadCommand(scheme);
                if (prev != null && prev.IndexOf("CloveStrap", StringComparison.OrdinalIgnoreCase) < 0 && prev.IndexOf("Voidstrapper", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (scheme == "roblox-player") st.PrevPlayerCommand = prev; else st.PrevRobloxCommand = prev;
                }
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme))
                {
                    k.SetValue("", "URL: Roblox Protocol");
                    k.SetValue("URL Protocol", "");
                    using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                    using (var cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" %1");
                }
            }
            st.Save();
            Log.Write("Registered as Roblox launcher: " + exe);
        }

        public static void Unregister()
        {
            if (!IsRegistered()) return;
            var st = State.Load();
            string official = null;
            if (Directory.Exists(Paths.OfficialVersions))
                official = Directory.GetDirectories(Paths.OfficialVersions)
                    .Select(d => Path.Combine(d, "RobloxPlayerBeta.exe")).Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

            foreach (var scheme in Schemes)
            {
                string prev = scheme == "roblox-player" ? st.PrevPlayerCommand : st.PrevRobloxCommand;
                string prevExe = prev == null ? null : Regex.Match(prev, "^\"([^\"]+)\"").Groups[1].Value;
                string restore = null;
                if (!string.IsNullOrEmpty(prevExe) && File.Exists(prevExe)) restore = prev;
                else if (official != null) restore = "\"" + official + "\" %1";

                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme))
                {
                    if (restore != null)
                    {
                        string exe = Regex.Match(restore, "^\"([^\"]+)\"").Groups[1].Value;
                        using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                        using (var cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue("", restore);
                    }
                }
                if (restore == null)
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + scheme, false);
            }
            Log.Write("Unregistered; restored previous Roblox launcher");
        }
    }

    // ------------------------------------------------------------------ launching
    class LaunchResult
    {
        public Process Process;
        public List<Mutex> SingletonMutexes;
        public bool KeepAlive;
        public bool ReRegister;
        public bool Alt;
    }

    static class Launcher
    {
        public static LaunchResult Run(string uri, Action<string, int> progress, LaunchOptions opt = null)
        {
            var s = Settings.Load();
            opt = opt ?? new LaunchOptions();
            // never log the sign-in ticket
            Log.Write("Launch: " + (uri == null ? "(app)" : Regex.Replace(uri, "gameinfo:[^+]+", "gameinfo:<hidden>")));

            string dir;
            try
            {
                dir = Deployment.EnsureInstalled(progress);
            }
            catch (Exception ex)
            {
                Log.Write("Update failed: " + ex);
                dir = Deployment.Fallback();
                if (dir == null) throw new Exception("Couldn't download Roblox and no installed copy was found.\n\n" + ex.Message, ex);
                progress("Couldn't check for updates, using installed Roblox\n" + Path.GetFileName(dir), -1);
                Thread.Sleep(800);
            }

            progress("Applying settings\n" + Path.GetFileName(dir), -1);
            Tweaks.Apply(s, dir, d => progress("Applying settings\n" + d, -1));
            try { Tweaks.ApplyToOfficial(s); } catch (Exception ex) { Log.Write("Official apply: " + ex.Message); }
            if (s.CleanupOldVersions) { progress("Tidying up\nold Roblox versions", -1); Deployment.CleanupOld(dir); }

            var result = new LaunchResult();
            if (opt.Alt)
            {
                progress("Setting up alt mode\n" + Path.GetFileName(dir), -1);
                dir = AltMode.Prepare(dir, d => progress("Setting up alt mode\n" + d, -1));
                result.Alt = true;
            }

            if (s.MultiInstance || opt.ForceMultiInstance)
            {
                // Owning Roblox's singleton objects stops new clients from closing the existing ones.
                progress("Allowing multiple Roblox windows\nROBLOX_singletonMutex  ·  ROBLOX_singletonEvent", -1);
                result.SingletonMutexes = MultiInstance.Acquire(d => progress("Keeping your open Roblox running\n" + d, -1));
                result.KeepAlive = true;
            }

            progress("Starting Roblox\nRobloxPlayerBeta.exe", -1);
            string args = string.IsNullOrEmpty(uri) ? "--app" : (uri.Contains(" ") ? "\"" + uri + "\"" : uri);
            var psi = new ProcessStartInfo(Path.Combine(dir, "RobloxPlayerBeta.exe"), args)
            {
                WorkingDirectory = dir,
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            result.Process = p;
            Log.Write("Started RobloxPlayerBeta pid " + p.Id + " from " + dir);

            try
            {
                if (opt.Alt) AltMode.Throttle(p, false);
                else if (s.Priority == "High") p.PriorityClass = ProcessPriorityClass.High;
                else if (s.Priority == "AboveNormal") p.PriorityClass = ProcessPriorityClass.AboveNormal;
            }
            catch (Exception ex) { Log.Write("Priority not set: " + ex.Message); }

            // keep the loading screen up until Roblox's own window shows, so there's no empty gap
            var waitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - waitStart).TotalSeconds < 25)
            {
                try
                {
                    if (p.HasExited) break;
                    p.Refresh();
                    if (p.MainWindowHandle != IntPtr.Zero) break;
                }
                catch { break; }
                progress("Starting Roblox\nRobloxPlayerBeta.exe  ·  pid " + p.Id + "  ·  " + (int)(DateTime.UtcNow - waitStart).TotalSeconds + "s", -1);
                Thread.Sleep(200);
            }

            result.ReRegister = s.RegisterProtocol;
            return result;
        }
    }
}
