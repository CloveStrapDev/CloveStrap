// Search: one box in the header that looks through every setting on every tab. Pick a result to jump straight to it.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CloveStrap
{
    class SearchEntry
    {
        public string Page, Title, Where, Desc, Hay;
        public Card Card;          // null: the result is a whole tab or a preset
        public string RowTitle;
    }

    partial class MainForm
    {
        VTextBox searchBox;
        SearchPopup searchPopup;
        List<SearchEntry> searchIndex = new List<SearchEntry>();

        // extra words people might type for a setting (row title -> keywords)
        static readonly Dictionary<string, string> SearchWords = new Dictionary<string, string>
        {
            { "FPS cap", "fps framerate frame rate limit uncap unlock refresh hz" },
            { "Graphics quality", "view distance render distance draw distance see far quality level" },
            { "Distant model detail", "view distance lod level of detail mesh union far" },
            { "Quieten Roblox when you switch away", "mute volume sound audio quiet background alt tab" },
            { "Anti-AFK", "afk idle kick 20 minutes" },
            { "Auto-rejoin", "kick kicked disconnect rejoin reconnect" },
            { "Multiple instances", "multi instance multiinstance alts several roblox" },
            { "Start with Windows", "startup boot tray autostart" },
            { "Screen crosshair", "crosshair aim reticle dot" },
            { "Show what you're playing", "discord rich presence status activity" },
            { "Alt windows", "alt window minimized minimize normal alts" },
            { "Use for the Play button", "play button website browser protocol" },
            { "Exclusive fullscreen", "fullscreen alt enter input lag" },
            { "Gray sky", "grey sky skybox" },
            { "CPU priority", "cpu priority performance" },
            { "Show overlay", "overlay fps counter ping hud" },
        };

        static string Norm(string s) { return (s ?? "").ToLowerInvariant().Replace("grey", "gray").Replace("-", " "); }

        void SetUpSearch()
        {
            BuildSearchIndex();
            searchPopup = new SearchPopup { Visible = false };
            Controls.Add(searchPopup);
            searchPopup.BringToFront();
            searchPopup.Picked += GoTo;

            var box = searchBox.Box;
            box.TextChanged += (o, e) => RunSearch();
            box.GotFocus += (o, e) => { if (box.Text.Trim().Length > 0) RunSearch(); };
            box.LostFocus += (o, e) => BeginInvoke((Action)(() => { if (!searchBox.Box.Focused) searchPopup.Visible = false; }));
            box.KeyDown += (o, e) =>
            {
                if (!searchPopup.Visible) { if (e.KeyCode == Keys.Escape) { box.Text = ""; ActiveControl = null; } return; }
                if (e.KeyCode == Keys.Down) { searchPopup.Step(1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { searchPopup.Step(-1); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { searchPopup.PickSelected(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { box.Text = ""; searchPopup.Visible = false; ActiveControl = null; e.Handled = e.SuppressKeyPress = true; }
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F) && searchBox != null && !searchBox.IsDisposed)
            {
                searchBox.Box.Focus();
                searchBox.Box.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void BuildSearchIndex()
        {
            searchIndex = new List<SearchEntry>();
            foreach (var kv in pages)
            {
                string page = kv.Key;
                searchIndex.Add(new SearchEntry { Page = page, Title = page, Where = "Tab", Desc = "Open the " + page + " tab", Hay = Norm(page) });
                foreach (var card in AllCards(kv.Value))
                {
                    string ct = card.CardTitle ?? "";
                    foreach (var row in card.SearchRows())
                    {
                        string words;
                        SearchWords.TryGetValue(row[0], out words);
                        searchIndex.Add(new SearchEntry
                        {
                            Page = page, Card = card, RowTitle = row[0], Title = row[0], Desc = row[1],
                            Where = ct.Length > 0 ? page + "  ›  " + ct.Replace("  ·  ", " · ") : page,
                            Hay = Norm(row[0] + " " + row[1] + " " + ct + " " + page + " " + words)
                        });
                    }
                }
            }
            foreach (var pc in presetCards)
                searchIndex.Add(new SearchEntry { Page = "Presets", Title = pc.Info.Name + " preset", Where = "Presets", Desc = pc.Info.Desc, Hay = Norm(pc.Info.Name + " preset " + pc.Info.Desc + " " + string.Join(" ", pc.Info.Tags ?? new string[0])) });
        }

        static IEnumerable<Card> AllCards(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                var card = c as Card;
                if (card != null) yield return card;
                foreach (var inner in AllCards(c)) yield return inner;
            }
        }

        void RunSearch()
        {
            string q = Norm(searchBox.Box.Text).Trim();
            if (q.Length == 0) { searchPopup.Visible = false; return; }
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<KeyValuePair<int, SearchEntry>>();
            foreach (var e in searchIndex)
            {
                if (!words.All(w => e.Hay.Contains(w))) continue;
                string t = Norm(e.Title);
                int score = 0;
                foreach (var w in words)
                {
                    if (t.StartsWith(w)) score += 30;
                    else if (t.Contains(" " + w)) score += 20;
                    else if (t.Contains(w)) score += 12;
                    else if (Norm(e.Desc).Contains(w)) score += 4;
                    else score += 2;
                }
                if (e.Card == null) score -= 5;   // settings first, whole tabs after
                hits.Add(new KeyValuePair<int, SearchEntry>(score, e));
            }
            var list = hits.OrderByDescending(h => h.Key).ThenBy(h => h.Value.Title.Length).Select(h => h.Value).Take(7).ToList();
            var at = searchBox.Parent.PointToScreen(new Point(searchBox.Left, searchBox.Bottom + 6));
            searchPopup.Location = PointToClient(at);
            searchPopup.Show(list, q);
            searchPopup.BringToFront();
        }

        void GoTo(SearchEntry e)
        {
            searchPopup.Visible = false;
            searchBox.Box.Text = "";
            ActiveControl = null;
            ShowPage(e.Page);
            if (e.Card == null) return;
            // scroll the tab so the setting sits near the top, then make it glow for a moment
            var page = pages[e.Page];
            int y = e.Card.RowY(e.RowTitle);
            Control c = e.Card;
            while (c != null && c != page) { y += c.Top; c = c.Parent; }
            if (page.AutoScroll)
            {
                int now = -page.AutoScrollPosition.Y;
                int target = Math.Max(0, now + y - 60);
                page.AutoScrollPosition = new Point(0, target);
            }
            e.Card.Highlight(e.RowTitle);
        }
    }

    /// <summary>The drop-down list of search results under the search box.</summary>
    class SearchPopup : VControl
    {
        List<SearchEntry> items = new List<SearchEntry>();
        int sel, hover = -1;
        string query = "";
        public event Action<SearchEntry> Picked;
        const int ItemH = 56, Pad = 6;

        public SearchPopup()
        {
            SetStyle(ControlStyles.Selectable, false);   // clicking a result keeps the typing in the search box
            Width = 460;
            Cursor = Cursors.Hand;
        }

        public void Show(List<SearchEntry> list, string q)
        {
            items = list; query = q; sel = 0; hover = -1;
            Height = items.Count == 0 ? 52 : items.Count * ItemH + Pad * 2;
            Visible = true;
            Invalidate();
        }

        public void Step(int d) { if (items.Count == 0) return; sel = (sel + d + items.Count) % items.Count; Invalidate(); }
        public void PickSelected() { if (sel >= 0 && sel < items.Count && Picked != null) Picked(items[sel]); }

        int At(Point p) { int i = (p.Y - Pad) / ItemH; return p.Y < Pad || i < 0 || i >= items.Count ? -1 : i; }

        protected override void OnMouseMove(MouseEventArgs e) { int i = At(e.Location); if (i != hover) { hover = i; if (i >= 0) sel = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int i = At(e.Location); if (i >= 0 && Picked != null) Picked(items[i]); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 10, Theme.Card);
            Theme.Stroke(g, r, 10, Theme.Mix(Theme.Border, Theme.Accent, 0.35f));
            if (items.Count == 0)
            {
                Theme.Draw(g, "Nothing matches \"" + query + "\"", Theme.Small, new Rectangle(16, 0, Width - 32, Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = new RectangleF(Pad, Pad + i * ItemH, Width - Pad * 2, ItemH - 2);
                if (i == sel) Theme.Fill(g, row, 8, Theme.Selected);
                int x = Pad + 12, y = (int)row.Y + 8;
                var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                Theme.Draw(g, it.Title, Theme.RowTitle, new Rectangle(x, y, Width - 200, 20), Theme.Text, flags);
                Theme.Draw(g, it.Where, Theme.Small, new Rectangle(Width - 190, y + 1, 172, 18), Theme.Accent, flags | TextFormatFlags.Right);
                Theme.Draw(g, it.Desc ?? "", Theme.Small, new Rectangle(x, y + 22, Width - x - 18, 18), Theme.Muted, flags);
            }
        }
    }
}
