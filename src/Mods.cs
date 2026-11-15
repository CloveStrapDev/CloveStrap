// CloveStrap built-in mods: skybox, character sounds and mouse cursor.
// Every mod is a file in the Mods folder, copied over Roblox on launch (Tweaks.ApplyMods), and removing it restores Roblox's original.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CloveStrap
{
    static class ModKit
    {
        static readonly string NamesDir = Path.Combine(Paths.Base, "ModNames");
        public static readonly string PreviewDir = Path.Combine(Paths.Base, "ModPreviews");

        static string ModFile(string rel) { return Path.Combine(Paths.Mods, rel); }

        static void SetName(string id, string name)
        {
            Directory.CreateDirectory(NamesDir);
            File.WriteAllText(Path.Combine(NamesDir, id + ".txt"), name ?? "");
        }

        public static string GetName(string id)
        {
            try { string f = Path.Combine(NamesDir, id + ".txt"); return File.Exists(f) ? File.ReadAllText(f).Trim() : null; }
            catch { return null; }
        }

        static void ClearName(string id) { try { File.Delete(Path.Combine(NamesDir, id + ".txt")); } catch { } }

        static void DeleteMod(string rel)
        {
            try { File.Delete(ModFile(rel)); } catch { }
        }

        /// <summary>Roblox's own copy of a file (the backup if a mod replaced it), for previews.</summary>
        public static string OriginalOf(string rel)
        {
            string v = Deployment.InstalledVersion();
            if (v == null) return null;
            string dir = Path.Combine(Paths.Versions, v);
            string backup = Path.Combine(Tweaks.OriginalsDir(dir), rel);
            if (File.Exists(backup)) return backup;
            string f = Path.Combine(dir, rel);
            return File.Exists(f) ? f : null;
        }

        // ================================================================== sounds
        public class SoundSlot
        {
            public string Id, Label, Desc;
            public string[] Files;
        }

        public static readonly SoundSlot[] Sounds =
        {
            new SoundSlot { Id = "walk", Label = "Walking", Desc = "Footsteps", Files = new[] { @"content\sounds\action_footsteps_plastic.mp3" } },
            new SoundSlot { Id = "jump", Label = "Jumping", Desc = "Every jump", Files = new[] { @"content\sounds\action_jump.mp3" } },
            new SoundSlot { Id = "land", Label = "Landing", Desc = "Hitting the ground", Files = new[] { @"content\sounds\action_jump_land.mp3" } },
            new SoundSlot { Id = "death", Label = "Death", Desc = "The oof", Files = new[] { @"content\sounds\ouch.ogg", @"content\sounds\oof.ogg" } },
            new SoundSlot { Id = "fall", Label = "Falling", Desc = "Long falls", Files = new[] { @"content\sounds\action_falling.ogg" } },
            new SoundSlot { Id = "swim", Label = "Swimming", Desc = "In water", Files = new[] { @"content\sounds\action_swim.mp3" } },
            new SoundSlot { Id = "getup", Label = "Getting up", Desc = "After falling over", Files = new[] { @"content\sounds\action_get_up.mp3" } },
            new SoundSlot { Id = "splash", Label = "Splash", Desc = "Jumping into water", Files = new[] { @"content\sounds\impact_water.mp3" } },
            new SoundSlot { Id = "explosion", Label = "Explosion", Desc = "Default explosions", Files = new[] { @"content\sounds\impact_explosion_03.mp3" } },
        };

        public static bool IsCustom(SoundSlot s) { return File.Exists(ModFile(s.Files[0])); }

        public static void SetSound(SoundSlot s, string source)
        {
            // Roblox's audio engine reads the file by its contents, so an mp3 works under an .ogg name and the other way round
            foreach (var rel in s.Files)
            {
                string dest = ModFile(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source, dest, true);
            }
            SetName("sound_" + s.Id, Path.GetFileName(source));
            Log.Write("Mod: " + s.Id + " sound = " + Path.GetFileName(source));
        }

        public static void ResetSound(SoundSlot s)
        {
            foreach (var rel in s.Files) DeleteMod(rel);
            ClearName("sound_" + s.Id);
        }

        public static string SoundFile(SoundSlot s)
        {
            return IsCustom(s) ? ModFile(s.Files[0]) : OriginalOf(s.Files[0]);
        }

        // ================================================================== cursor
        static readonly string[] CursorFiles = { @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png" };
        public static readonly string[] CursorPresets = { "dot", "cross", "circle" };

        public static bool HasCursor { get { return File.Exists(ModFile(CursorFiles[0])); } }
        public static string CursorFile { get { return HasCursor ? ModFile(CursorFiles[0]) : OriginalOf(CursorFiles[0]); } }

        public static Bitmap CursorPreset(string style, Color accent)
        {
            var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var c = OverlayRenderer.Crosshair(style, 2, accent))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(c, (64 - c.Width) / 2, (64 - c.Height) / 2, c.Width, c.Height);
            }
            return bmp;
        }

        /// <summary>Saves a 64x64 cursor (Roblox draws the hotspot at the centre).</summary>
        public static void SetCursor(Image img, string name)
        {
            using (var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float sc = Math.Min(64f / img.Width, 64f / img.Height);
                    float w = img.Width * sc, h = img.Height * sc;
                    g.DrawImage(img, (64 - w) / 2, (64 - h) / 2, w, h);
                }
                foreach (var rel in CursorFiles)
                {
                    string dest = ModFile(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    bmp.Save(dest, ImageFormat.Png);
                }
            }
            SetName("cursor", name);
            Log.Write("Mod: cursor = " + name);
        }

        public static void ResetCursor()
        {
            foreach (var rel in CursorFiles) DeleteMod(rel);
            ClearName("cursor");
        }

        // ================================================================== skybox
        static readonly string[] Faces = { "bk", "dn", "ft", "lf", "rt", "up" };
        static string SkyRel(string face) { return @"PlatformContent\pc\textures\sky\sky512_" + face + ".tex"; }
        public static bool HasSky { get { return File.Exists(ModFile(SkyRel("ft"))); } }
        public static string SkyPreviewFile { get { return Path.Combine(PreviewDir, "sky.png"); } }
        const int SkySize = 1024;

        public class SkyPreset
        {
            public string Id, Name;
            public float[] Stops;      // elevation in degrees, -90..90
            public Color[] Colors;
            public float Stars;        // chance per 2x2 cell
            public Color StarTint = Color.White;
        }

        static Color H(string hex) { return ColorTranslator.FromHtml(hex); }

        public static readonly SkyPreset[] SkyPresets =
        {
            new SkyPreset { Id = "void", Name = "Void", Stops = new float[] { -90, 0, 90 }, Colors = new[] { H("#000000"), H("#06040b"), H("#000000") }, Stars = 0.0016f },
            new SkyPreset { Id = "night", Name = "Night", Stops = new float[] { -90, 0, 25, 90 }, Colors = new[] { H("#04050c"), H("#1d2a5c"), H("#0e1538"), H("#03040c") }, Stars = 0.0022f },
            new SkyPreset { Id = "sunset", Name = "Sunset", Stops = new float[] { -90, -5, 3, 14, 40, 90 }, Colors = new[] { H("#2a1630"), H("#5b2a4a"), H("#ff9a4d"), H("#ff6f91"), H("#7c5cd6"), H("#2b1b5a") } },
            new SkyPreset { Id = "sakura", Name = "Sakura", Stops = new float[] { -90, 0, 35, 90 }, Colors = new[] { H("#f7b6d2"), H("#ffd1e6"), H("#ffe6f2"), H("#fff4fa") } },
            new SkyPreset { Id = "ocean", Name = "Ocean", Stops = new float[] { -90, 0, 30, 90 }, Colors = new[] { H("#0b3d6b"), H("#c4ebff"), H("#6fbaf0"), H("#2a6bb8") } },
            new SkyPreset { Id = "blood", Name = "Blood moon", Stops = new float[] { -90, 0, 20, 90 }, Colors = new[] { H("#080000"), H("#6a0d10"), H("#2a0406"), H("#070000") }, Stars = 0.0012f, StarTint = H("#ffb3b3") },
        };

        public static Color PresetColor(SkyPreset p, float elevDeg)
        {
            if (elevDeg <= p.Stops[0]) return p.Colors[0];
            for (int i = 1; i < p.Stops.Length; i++)
            {
                if (elevDeg <= p.Stops[i])
                {
                    float t = (elevDeg - p.Stops[i - 1]) / (p.Stops[i] - p.Stops[i - 1]);
                    t = t * t * (3 - 2 * t);
                    return Lerp(p.Colors[i - 1], p.Colors[i], t);
                }
            }
            return p.Colors[p.Colors.Length - 1];
        }

        static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static int Rgb(Color c) { return (c.R << 16) | (c.G << 8) | c.B; }

        // elevation of a pixel on a cube face; u, v in -1..1 (v down)
        static float Elevation(string face, float u, float v)
        {
            if (face == "up") return (float)(Math.Atan2(1, Math.Sqrt(u * u + v * v)) * 180 / Math.PI);
            if (face == "dn") return -(float)(Math.Atan2(1, Math.Sqrt(u * u + v * v)) * 180 / Math.PI);
            return (float)(Math.Atan2(-v, Math.Sqrt(u * u + 1)) * 180 / Math.PI);
        }

        static int[] PresetFace(SkyPreset p, string face, int fi)
        {
            var px = new int[SkySize * SkySize];
            for (int y = 0; y < SkySize; y++)
            {
                float v = (y + 0.5f) / SkySize * 2 - 1;
                for (int x = 0; x < SkySize; x++)
                {
                    float u = (x + 0.5f) / SkySize * 2 - 1;
                    float e = Elevation(face, u, v);
                    Color c = PresetColor(p, e);
                    if (p.Stars > 0 && e > 4)
                    {
                        uint h = (uint)((x >> 1) * 73856093) ^ (uint)((y >> 1) * 19349663) ^ (uint)(fi * 83492791);
                        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                        if ((h % 100000) < p.Stars * 100000)
                        {
                            float b = 0.35f + (h >> 20) % 100 / 154f;
                            b *= Math.Min(1f, (e - 4) / 20f);
                            c = Lerp(c, p.StarTint, b);
                        }
                    }
                    px[y * SkySize + x] = Rgb(c);
                }
            }
            return px;
        }

        public static void SetSkyPreset(SkyPreset p)
        {
            var faces = new Dictionary<string, int[]>();
            for (int i = 0; i < Faces.Length; i++) faces[Faces[i]] = PresetFace(p, Faces[i], i);
            WriteSky(faces, p.Name);
        }

        static void WriteSky(Dictionary<string, int[]> faces, string name)
        {
            foreach (var kv in faces)
            {
                string dest = ModFile(SkyRel(kv.Key));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                Dds.WriteDxt1(dest, kv.Value, SkySize);
            }
            // preview: the front, with a strip of sky above
            Directory.CreateDirectory(PreviewDir);
            using (var b = new Bitmap(SkySize, SkySize, PixelFormat.Format32bppRgb))
            {
                var data = b.LockBits(new Rectangle(0, 0, SkySize, SkySize), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                Marshal.Copy(faces["ft"], 0, data.Scan0, faces["ft"].Length);
                b.UnlockBits(data);
                using (var prev = new Bitmap(440, 248))
                {
                    using (var g = Graphics.FromImage(prev))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(b, new Rectangle(0, 0, 440, 248), 0, SkySize * 8 / 100, SkySize, SkySize * 248 / 440, GraphicsUnit.Pixel);
                    }
                    prev.Save(SkyPreviewFile, ImageFormat.Png);
                }
            }
            SetName("sky", name);
            Log.Write("Mod: sky = " + name);
        }

        public static void ResetSky()
        {
            foreach (var f in Faces) DeleteMod(SkyRel(f));
            try { File.Delete(SkyPreviewFile); } catch { }
            ClearName("sky");
        }

        // ================================================================== your own sky from colours
        public static SkyPreset Mix(Color top, Color horizon, bool stars)
        {
            Color ground = Lerp(horizon, Color.Black, 0.55f);
            return new SkyPreset
            {
                Id = "mix", Name = "Your mix",
                Stops = new float[] { -90, -4, 0, 14, 45, 90 },
                Colors = new[] { Lerp(ground, Color.Black, 0.3f), ground, horizon, Lerp(horizon, top, 0.45f), top, Lerp(top, Color.Black, 0.12f) },
                Stars = stars ? 0.002f : 0f
            };
        }

        public static void SetSkyMix(Color top, Color horizon, bool stars)
        {
            SetSkyPreset(Mix(top, horizon, stars));
            SetName("skymix", ColorTranslator.ToHtml(top) + "," + ColorTranslator.ToHtml(horizon) + "," + (stars ? "1" : "0"));
        }

        /// <summary>The colours last used for "Your mix" (or a starting pair).</summary>
        public static void GetMix(out Color top, out Color horizon, out bool stars)
        {
            top = H("#2B6CB0"); horizon = H("#FF9A4D"); stars = false;
            try
            {
                var parts = (GetName("skymix") ?? "").Split(',');
                if (parts.Length == 3) { top = H(parts[0]); horizon = H(parts[1]); stars = parts[2] == "1"; }
            }
            catch { }
        }

        // ================================================================== Roblox's own sky, for the Default preview
        static Bitmap defaultSky;

        /// <summary>A 440x248 view of Roblox's default sky (decoded from its own texture), or null if Roblox isn't installed.</summary>
        public static Bitmap DefaultSkyPreview()
        {
            if (defaultSky != null) return defaultSky;
            try
            {
                string cache = Path.Combine(PreviewDir, "default_sky_" + (Deployment.InstalledVersion() ?? "none") + ".png");
                if (File.Exists(cache))
                {
                    using (var fs = File.OpenRead(cache)) using (var tmp = Image.FromStream(fs)) defaultSky = new Bitmap(tmp);
                    return defaultSky;
                }
                string src = OriginalOf(SkyRel("ft"));
                if (src == null) return null;
                using (var face = Dds.ReadDxt1(src))
                {
                    var prev = new Bitmap(440, 248);
                    using (var g = Graphics.FromImage(prev))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(face, new Rectangle(0, 0, 440, 248), 0, face.Height * 8 / 100, face.Width, face.Width * 248 / 440, GraphicsUnit.Pixel);
                    }
                    Directory.CreateDirectory(PreviewDir);
                    prev.Save(cache, ImageFormat.Png);
                    defaultSky = prev;
                }
            }
            catch (Exception ex) { Log.Write("Default sky preview: " + ex.Message); }
            return defaultSky;
        }

        public static Bitmap LoadPicture(string path)
        {
            using (var fs = File.OpenRead(path)) using (var img = Image.FromStream(fs)) return new Bitmap(img);
        }

        // ================================================================== headless test: --mods-test
        public static int SelfTest(string unused)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string dir = Path.Combine(Path.GetTempPath(), "vs-modtest");
            Directory.CreateDirectory(dir);
            var px = PresetFace(Mix(H("#6B46C1"), H("#FF6F91"), true), "ft", 2);
            Dds.WriteDxt1(Path.Combine(dir, "sky512_ft.tex"), px, SkySize);
            Log.Write("mods-test: mix face + DXT1 in " + sw.ElapsedMilliseconds + " ms, " + new FileInfo(Path.Combine(dir, "sky512_ft.tex")).Length + " bytes");
            using (var dec = Dds.ReadDxt1(Path.Combine(dir, "sky512_ft.tex"))) dec.Save(Path.Combine(dir, "decoded_mix.png"));
            sw.Restart();
            var d = DefaultSkyPreview();
            Log.Write("mods-test: default sky preview " + (d == null ? "missing" : d.Width + "x" + d.Height) + " in " + sw.ElapsedMilliseconds + " ms");
            if (d != null) d.Save(Path.Combine(dir, "default_sky.png"));
            return 0;
        }
    }

    // ------------------------------------------------------------------ DXT1 (BC1) .dds writer, matching Roblox's own sky textures
    static class Dds
    {
        public static void WriteDxt1(string file, int[] rgb, int size)
        {
            using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                int mips = 1;
                for (int s = size; s > 1; s >>= 1) mips++;
                w.Write(0x20534444);              // "DDS "
                w.Write(124);
                w.Write(0x000A1007);              // caps | height | width | pixelformat | mipmapcount | linearsize
                w.Write(size); w.Write(size);
                w.Write(Math.Max(1, size / 4) * Math.Max(1, size / 4) * 8);
                w.Write(0);                       // depth
                w.Write(mips);
                for (int i = 0; i < 11; i++) w.Write(0);
                w.Write(32); w.Write(4); w.Write(0x31545844);   // pixel format: FOURCC "DXT1"
                for (int i = 0; i < 5; i++) w.Write(0);
                w.Write(0x00401008);              // complex | texture | mipmap
                for (int i = 0; i < 4; i++) w.Write(0);

                int[] level = rgb; int ls = size;
                for (int m = 0; m < mips; m++)
                {
                    EncodeLevel(w, level, ls);
                    if (ls > 1) { level = Half(level, ls); ls >>= 1; }
                }
            }
        }

        static int[] Half(int[] px, int s)
        {
            int h = s / 2;
            var o = new int[h * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                {
                    int a = px[(y * 2) * s + x * 2], b = px[(y * 2) * s + x * 2 + 1], c = px[(y * 2 + 1) * s + x * 2], d = px[(y * 2 + 1) * s + x * 2 + 1];
                    int r = (((a >> 16) & 255) + ((b >> 16) & 255) + ((c >> 16) & 255) + ((d >> 16) & 255) + 2) / 4;
                    int g = (((a >> 8) & 255) + ((b >> 8) & 255) + ((c >> 8) & 255) + ((d >> 8) & 255) + 2) / 4;
                    int bl = ((a & 255) + (b & 255) + (c & 255) + (d & 255) + 2) / 4;
                    o[y * h + x] = r << 16 | g << 8 | bl;
                }
            return o;
        }

        static void EncodeLevel(BinaryWriter w, int[] px, int s)
        {
            int blocks = Math.Max(1, (s + 3) / 4);
            var blk = new int[16];
            for (int by = 0; by < blocks; by++)
                for (int bx = 0; bx < blocks; bx++)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int x = Math.Min(s - 1, bx * 4 + (i & 3)), y = Math.Min(s - 1, by * 4 + (i >> 2));
                        blk[i] = px[y * s + x];
                    }
                    EncodeBlock(w, blk);
                }
        }

        static int To565(int c) { return (((c >> 16) & 255) >> 3) << 11 | (((c >> 8) & 255) >> 2) << 5 | ((c & 255) >> 3); }

        static int From565(int c)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            return ((r << 3) | (r >> 2)) << 16 | ((g << 2) | (g >> 4)) << 8 | ((b << 3) | (b >> 2));
        }

        static void EncodeBlock(BinaryWriter w, int[] blk)
        {
            // endpoints: the two colours furthest apart along the block's brightest/darkest axis, pulled in slightly
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            foreach (var c in blk)
            {
                int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                if (r < minR) minR = r; if (g < minG) minG = g; if (b < minB) minB = b;
                if (r > maxR) maxR = r; if (g > maxG) maxG = g; if (b > maxB) maxB = b;
            }
            int ir = (maxR - minR) >> 4, ig = (maxG - minG) >> 4, ib = (maxB - minB) >> 4;
            int hi = (maxR - ir) << 16 | (maxG - ig) << 8 | (maxB - ib);
            int lo = (minR + ir) << 16 | (minG + ig) << 8 | (minB + ib);
            // keep the diagonal that matches the block (choose per-channel direction by correlation with green)
            long cov = 0; int mg = (minG + maxG) / 2, mr = (minR + maxR) / 2, mb = (minB + maxB) / 2;
            long covB = 0;
            foreach (var c in blk) { cov += (long)(((c >> 16) & 255) - mr) * (((c >> 8) & 255) - mg); covB += (long)((c & 255) - mb) * (((c >> 8) & 255) - mg); }
            if (cov < 0) { int hr = (hi >> 16) & 255, lr = (lo >> 16) & 255; hi = (hi & 0x00FFFF) | lr << 16; lo = (lo & 0x00FFFF) | hr << 16; }
            if (covB < 0) { int hb = hi & 255, lb = lo & 255; hi = (hi & 0xFFFF00) | lb; lo = (lo & 0xFFFF00) | hb; }

            int c0 = To565(hi), c1 = To565(lo);
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            uint idx = 0;
            if (c0 != c1)
            {
                int p0 = From565(c0), p1 = From565(c1);
                var pal = new[] { p0, p1, MixThird(p0, p1), MixThird(p1, p0) };
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        int dr = ((blk[i] >> 16) & 255) - ((pal[k] >> 16) & 255), dg = ((blk[i] >> 8) & 255) - ((pal[k] >> 8) & 255), db = (blk[i] & 255) - (pal[k] & 255);
                        int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    idx |= (uint)best << (i * 2);
                }
            }
            w.Write((ushort)c0); w.Write((ushort)c1); w.Write(idx);
        }

        static int MixThird(int a, int b)
        {
            int r = (((a >> 16) & 255) * 2 + ((b >> 16) & 255)) / 3, g = (((a >> 8) & 255) * 2 + ((b >> 8) & 255)) / 3, bl = ((a & 255) * 2 + (b & 255)) / 3;
            return r << 16 | g << 8 | bl;
        }

        /// <summary>Decodes the top mip of a DXT1 .dds (for checking our own output).</summary>
        public static Bitmap ReadDxt1(string file)
        {
            var bytes = File.ReadAllBytes(file);
            int h = BitConverter.ToInt32(bytes, 12), wdt = BitConverter.ToInt32(bytes, 16);
            if (bytes.Length < 128 || BitConverter.ToInt32(bytes, 84) != 0x31545844) throw new Exception("not a DXT1 texture");
            var px = new int[wdt * h];
            int off = 128;
            for (int by = 0; by < h / 4; by++)
                for (int bx = 0; bx < wdt / 4; bx++)
                {
                    int c0 = BitConverter.ToUInt16(bytes, off), c1 = BitConverter.ToUInt16(bytes, off + 2);
                    uint idx = BitConverter.ToUInt32(bytes, off + 4);
                    off += 8;
                    int p0 = From565(c0), p1 = From565(c1);
                    var pal = c0 > c1 ? new[] { p0, p1, MixThird(p0, p1), MixThird(p1, p0) } : new[] { p0, p1, MixHalf(p0, p1), 0 };
                    for (int i = 0; i < 16; i++)
                        px[(by * 4 + (i >> 2)) * wdt + bx * 4 + (i & 3)] = pal[(idx >> (i * 2)) & 3] | unchecked((int)0xFF000000);
                }
            var bmp = new Bitmap(wdt, h, PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, wdt, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * wdt, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), wdt);
            bmp.UnlockBits(data);
            return bmp;
        }

        static int MixHalf(int a, int b)
        {
            return ((((a >> 16) & 255) + ((b >> 16) & 255)) / 2) << 16 | ((((a >> 8) & 255) + ((b >> 8) & 255)) / 2) << 8 | (((a & 255) + (b & 255)) / 2);
        }
    }
}
