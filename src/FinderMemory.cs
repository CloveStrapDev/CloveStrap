// Username finder memory: which names were already checked (per mode), saved across restarts, so nothing is checked twice
// until every possible name in that mode has been. Also keeps every free name ever found.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CloveStrap
{
    class FinderMemory
    {
        static readonly string Dir = Path.Combine(Paths.Base, "NameFinder");
        public static readonly string FoundFile = Path.Combine(Dir, "found.txt");

        readonly string file;
        readonly bool letters;
        readonly string cs;
        readonly int len;
        readonly BitArray bits;            // 3 / 4 letters: one bit per possible name
        readonly HashSet<string> words;    // word modes
        public readonly long Total;
        public long Done;
        readonly object gate = new object();
        bool dirty;

        public FinderMemory(string mode, bool digits, int wordCount)
        {
            Directory.CreateDirectory(Dir);
            letters = mode == "3" || mode == "4";
            if (letters)
            {
                len = mode == "3" ? 3 : 4;
                cs = "abcdefghijklmnopqrstuvwxyz" + (digits ? "0123456789" : "");
                long n = 1; for (int i = 0; i < len; i++) n *= cs.Length;
                Total = n;
                file = Path.Combine(Dir, "checked_" + mode + (digits ? "_digits" : "") + ".bin");
                bits = new BitArray((int)n);
                try
                {
                    if (File.Exists(file))
                    {
                        var b = File.ReadAllBytes(file);
                        if (b.Length == (n + 7) / 8) bits = new BitArray(b) { Length = (int)n };
                    }
                }
                catch { }
                foreach (bool v in bits) if (v) Done++;
            }
            else
            {
                Total = wordCount;
                file = Path.Combine(Dir, "checked_" + mode + ".txt");
                words = new HashSet<string>();
                try { if (File.Exists(file)) foreach (var l in File.ReadAllLines(file)) if (l.Length > 0) words.Add(l.Trim()); } catch { }
                Done = words.Count;
            }
        }

        long Index(string name)
        {
            long idx = 0;
            foreach (char c in name) { int k = cs.IndexOf(c); if (k < 0) return -1; idx = idx * cs.Length + k; }
            return idx;
        }

        public bool IsChecked(string name)
        {
            lock (gate)
            {
                if (letters) { long i = Index(name); return i >= 0 && i < bits.Length && bits[(int)i]; }
                return words.Contains(name);
            }
        }

        public void Mark(IEnumerable<string> names)
        {
            lock (gate)
            {
                foreach (var n in names)
                {
                    if (letters)
                    {
                        long i = Index(n);
                        if (i >= 0 && i < bits.Length && !bits[(int)i]) { bits[(int)i] = true; Done++; dirty = true; }
                    }
                    else if (words.Add(n)) { Done++; dirty = true; }
                }
            }
        }

        /// <summary>Every name in this mode has been checked: forget them all and start a fresh cycle.</summary>
        public void Reset()
        {
            lock (gate)
            {
                if (letters) bits.SetAll(false); else words.Clear();
                Done = 0; dirty = true;
            }
            Save();
        }

        public void Save()
        {
            lock (gate)
            {
                if (!dirty) return;
                try
                {
                    string tmp = file + ".tmp";
                    if (letters)
                    {
                        var b = new byte[(bits.Length + 7) / 8];
                        bits.CopyTo(b, 0);
                        File.WriteAllBytes(tmp, b);
                    }
                    else File.WriteAllLines(tmp, words);
                    if (File.Exists(file)) File.Delete(file);
                    File.Move(tmp, file);
                    dirty = false;
                }
                catch (Exception ex) { Log.Write("Finder memory save: " + ex.Message); }
            }
        }

        public static List<string> LoadFound()
        {
            try
            {
                if (File.Exists(FoundFile))
                    return File.ReadAllLines(FoundFile).Select(l => l.Split('\t')[0].Trim()).Where(l => l.Length > 0).Distinct().ToList();
            }
            catch { }
            return new List<string>();
        }

        public static void AddFound(string name)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(FoundFile, name + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + Environment.NewLine); }
            catch { }
        }

        public static void ClearFound()
        {
            try { File.Delete(FoundFile); } catch { }
        }
    }
}
