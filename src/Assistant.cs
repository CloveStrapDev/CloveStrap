// "Ask Natsuki": the sidebar button and the chat panel it opens, with profile pictures next to every message.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace CloveStrap
{
    class ChatMsg
    {
        public bool Me, Typing;
        public string Text;
        public List<KeyValuePair<string, Action>> Buttons = new List<KeyValuePair<string, Action>>();
    }

    static class ChatPics
    {
        static Image natsuki;
        static bool tried;

        /// <summary>Natsuki's face (built into the exe).</summary>
        public static Image NatsukiOld()
        {
            if (tried) return natsuki;
            tried = true;
            try
            {
                using (var s = typeof(ChatPics).Assembly.GetManifestResourceStream("CloveStrap.clove_avatar.png"))
                    if (s != null) using (var tmp = Image.FromStream(s)) natsuki = new Bitmap(tmp);
            }
            catch { }
            return natsuki;
        }

        /// <summary>A round profile picture; the first letter of the name if there's no picture.</summary>
        public static void Draw(Graphics g, RectangleF r, Image pic, string name)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                if (pic != null)
                {
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(pic, r);
                    g.Restore(state);
                }
                else
                {
                    using (var b = new SolidBrush(Theme.Mix(Theme.Row, Theme.Accent, 0.5f))) g.FillPath(b, path);
                    string ch = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                    Theme.Draw(g, ch, Theme.RowTitle, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (var p = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.4f), 1.2f)) g.DrawPath(p, path);
            }
        }
    }

    partial class MainForm
    {
        AskButton askButton;      // made in BuildNow, in the sidebar
        ChatPanel chatPanel;
        readonly List<ChatMsg> chat = new List<ChatMsg>();   // kept across theme changes / window rebuilds
        bool chatOpen;
        Queue<string> demoQueue;   // tests only: questions asked one after another

        Image MyPic() { return DiscordAccount.SignedIn(S) ? (discordAvatar ?? avatar) : avatar; }
        string MyName() { return DiscordAccount.SignedIn(S) ? S.DiscordName : (robloxName ?? Environment.UserName); }

        string chatFor;   // which character the conversation so far is with

        void SetUpAssistant()
        {
            // the helper follows the theme: its own character, or Natsuki for custom themes
            Personas.Current = Personas.For(Program.SchemeOverride ?? S.ColorScheme);
            if (chatFor != null && chatFor != Personas.Current.Id && !chat.Any(c => c.Me)) chat.Clear();   // nothing said yet: greet as the new character
            chatFor = Personas.Current.Id;
            askButton.Click += (o, e) => ToggleChat(!chatOpen);
            chatPanel = new ChatPanel(chat, MyPic, MyName) { Location = new Point(SideW + 12, H - 16 - 488), Visible = false };
            chatPanel.Asked += Ask;
            chatPanel.Closed += () => ToggleChat(false);
            Controls.Add(chatPanel);
            chatPanel.BringToFront();
            if (chat.Count == 0) Welcome();
            if (chatOpen) ToggleChat(true);
        }

        void ToggleChat(bool open)
        {
            chatOpen = open;
            chatPanel.Visible = open;
            askButton.Open = open;
            if (open) { chatPanel.BringToFront(); chatPanel.FocusInput(); chatPanel.ScrollToEnd(); WarmBrain(); }
        }

        void Welcome()
        {
            var m = new ChatMsg { Text = Personas.Current.Welcome };
            foreach (var q in new[] { "How does CloveStrap work?", "How do I add an account?", "Why isn't my sky showing?", "How do I get more FPS?" })
            {
                string qq = q;
                m.Buttons.Add(new KeyValuePair<string, Action>(q, () => Ask(qq)));
            }
            chat.Add(m);
            if (!NatsukiBrain.Installed) OfferBrain();
        }

        // ------------------------------------------------------------------ the brain download
        void OfferBrain()
        {
            long mb = NatsukiBrain.TotalBytes / 1000000;
            var m = new ChatMsg
            {
                Text = NatsukiBrain.HasOldBrain
                    ? "Ooh, I've got a much smarter brain waiting for me! It understands questions way better. It's a one-time download of about " + mb +
                      " MB and replaces my old one, and uses about 3.5 GB of memory while I'm thinking. Until then I'll just match your question to answers I know."
                    : "Right now I can only match your question to answers I already know. Give me a real brain and I can think about your " +
                      "questions myself! It's a small AI that runs on your PC: free, private, no account. It's a one-time download of about " +
                      mb + " MB and uses about 3.5 GB of memory while I'm thinking."
            };
            m.Buttons.Add(new KeyValuePair<string, Action>((NatsukiBrain.HasOldBrain ? "Upgrade my brain (" : "Download my brain (") + mb + " MB)", () => DownloadBrain(m)));
            chat.Add(m);
        }

        void DownloadBrain(ChatMsg offer)
        {
            if (NatsukiBrain.Downloading) return;
            offer.Buttons.Clear();
            var m = new ChatMsg { Text = "Downloading my brain... 0%" };
            m.Buttons.Add(new KeyValuePair<string, Action>("Cancel", () => NatsukiBrain.CancelDownload()));
            chat.Add(m);
            if (chatPanel != null && !chatPanel.IsDisposed) chatPanel.Changed();
            int lastPct = -1;
            new Thread(() =>
            {
                string err = NatsukiBrain.Install((done, total) =>
                {
                    int pct = (int)(done * 100 / Math.Max(1, total));
                    if (pct == lastPct) return;
                    lastPct = pct;
                    m.Text = "Downloading my brain... " + pct + "%  (" + (done / 1000000) + " of " + (total / 1000000) + " MB)";
                    UiLater(() => chatPanel.Refresh2());
                });
                UiLater(() =>
                {
                    m.Buttons.Clear();
                    if (err == null) m.Text = "Done! I have a brain now >w< Ask me anything about CloveStrap.";
                    else if (err == "cancelled") { m.Text = "Okay, I stopped the download. It picks up where it left off next time."; OfferBrain(); }
                    else { m.Text = "Hmph, the download didn't work: " + err + ". You can try again."; OfferBrain(); }
                    chatPanel.Changed();
                });
            }) { IsBackground = true, Name = "natsuki-download" }.Start();
        }

        void UiLater(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (chatPanel != null && !chatPanel.IsDisposed) a(); })); } catch { }
        }

        // ------------------------------------------------------------------ answering
        void Ask(string question)
        {
            question = (question ?? "").Trim();
            if (question.Length == 0) return;
            // what the last question was about, for follow-ups like "where is it?"
            string previous = chat.Where(c => c.Me).Select(c => c.Text).LastOrDefault();
            // earlier questions that already have an answer (never a question still being answered)
            var history = new List<ChatMsg>();
            for (int i = 0; i + 1 < chat.Count; i++)
                if (chat[i].Me && !chat[i + 1].Me && !chat[i + 1].Typing && !string.IsNullOrWhiteSpace(chat[i + 1].Text))
                { history.Add(chat[i]); history.Add(chat[i + 1]); }
            chat.Add(new ChatMsg { Me = true, Text = question });
            var typing = new ChatMsg { Typing = true, Text = "..." };
            chat.Add(typing);
            chatPanel.Changed();

            // a follow-up ("where is it?", "yes, which one?") is about the previous question, but only when
            // this question has no clear topic of its own ("is this safe?" is its own question)
            var qWords = question.ToLowerInvariant().Split(new[] { ' ', '?', '!', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
            double ownScore; bool ownTopic;
            CloveBrain.Facts(question, searchIndex, out ownScore, out ownTopic);
            bool followUp = previous != null && !ownTopic && qWords.Length <= 8;
            string lookup = followUp ? question + " " + previous : question;

            // the matcher always runs: it answers if there's no brain, and knows where "Take me there" goes
            var quick = CloveBrain.Answer(question, searchIndex);
            var target = followUp && quick.Page == null ? CloveBrain.Answer(lookup, searchIndex) : quick;
            bool brainAnswered = false;
            double topScore; bool confident;
            CloveBrain.Facts(lookup, searchIndex, out topScore, out confident);
            if (UnknownTerms(question).Count > 0) confident = false;   // "drv1 error": nowhere to send you
            Action finish = () =>
            {
                typing.Typing = false;
                if (target.Page != null)
                {
                    string page = target.Page, row = target.Row;
                    string said = (typing.Text ?? "").ToLowerInvariant();
                    // no button on "sorry / I can't / not sure" replies: there's nowhere to go
                    bool declined = new[] { "sorry", "can't", "cannot", "can not", "not sure", "don't know", "only know", "unable", "nice try" }.Any(said.Contains);
                    bool named = said.Contains(page.ToLowerInvariant() + " tab") || (row != null && said.Contains(row.ToLowerInvariant()));
                    // the reply has to actually send you somewhere: it names that tab or setting, and CloveStrap is sure that's the topic
                    bool show = !declined && confident && (brainAnswered ? named : (said.Contains(page.ToLowerInvariant()) || row != null));
                    Log.Write("Natsuki: Take me there " + (show ? "shown -> " + page + " / " + row : "hidden") + " (declined " + declined + ", sure " + confident + ", named " + named + ")");
                    if (show) typing.Buttons.Add(new KeyValuePair<string, Action>("Take me there", () => { ToggleChat(false); GoToSetting(page, row); }));
                }
                if (!chatPanel.IsDisposed) chatPanel.Changed();
                if (demoQueue != null && demoQueue.Count > 0) { var next = demoQueue.Dequeue(); BeginInvoke((Action)(() => Ask(next))); }
            };

            if (!NatsukiBrain.Installed || NatsukiBrain.Downloading)
            {
                var t = new Timer { Interval = 420 };   // a short "typing..." so it doesn't feel robotic
                t.Tick += (o, e) =>
                {
                    t.Stop(); t.Dispose();
                    typing.Text = quick.Text;
                    foreach (var s in quick.Suggestions)
                    {
                        var ss = s;
                        typing.Buttons.Add(new KeyValuePair<string, Action>(s.Title + "  ·  " + s.Page, () => { ToggleChat(false); GoTo(ss); }));
                    }
                    target = quick;
                    finish();
                };
                t.Start();
                return;
            }

            var messages = BuildPrompt(question, lookup, history);
            if (!NatsukiBrain.Running) WarmBrain();   // wakes up (and loads her memory) first
            new Thread(() =>
            {
                string answer = null;
                try
                {
                    if (!warmDone.WaitOne(0))
                    {
                        typing.Text = "Waking up... (the very first time takes about a minute)";
                        UiLater(() => chatPanel.Refresh2());
                        warmDone.WaitOne(180000);
                    }
                    string startErr = NatsukiBrain.Start(st => { typing.Text = st; UiLater(() => chatPanel.Refresh2()); });
                    if (startErr == null)
                    {
                        typing.Text = "...";
                        DateTime lastDraw = DateTime.MinValue;
                        answer = NatsukiBrain.Chat(messages, sofar =>
                        {
                            typing.Text = Tidy(sofar);
                            if ((DateTime.UtcNow - lastDraw).TotalMilliseconds > 40) { lastDraw = DateTime.UtcNow; UiLater(() => chatPanel.Changed()); }
                        });
                    }
                    else Log.Write("Natsuki: " + startErr);
                }
                catch (Exception ex) { Log.Write("Natsuki: thinking failed: " + ex.Message); }
                UiLater(() =>
                {
                    if (string.IsNullOrWhiteSpace(answer)) typing.Text = quick.Text;   // brain trouble: fall back to what I know
                    else { brainAnswered = true; typing.Text = WholeSentences(Tidy(answer)); Log.Write("Natsuki answered \"" + question + "\": " + typing.Text.Replace('\n', ' ')); }
                    finish();
                });
            }) { IsBackground = true, Name = "natsuki-think" }.Start();
        }

        /// <summary>Plain chat text: no markdown symbols.</summary>
        /// <summary>If the answer got cut off by the length limit, end it at the last full sentence.</summary>
        static string WholeSentences(string s)
        {
            s = s.TrimEnd();
            if (s.Length == 0) return s;
            char last = s[s.Length - 1];
            if (".!?~)^<>".IndexOf(last) >= 0) return s;
            int cut = Math.Max(s.LastIndexOf(". "), Math.Max(s.LastIndexOf("! "), s.LastIndexOf("? ")));
            return cut > 20 ? s.Substring(0, cut + 1) : s;
        }

        static string Tidy(string s)
        {
            s = (s ?? "").Replace("**", "").Replace("__", "").Replace("`", "").Replace("\r", "").Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"<think>[\s\S]*?(</think>|$)", "").Trim();   // just in case the model "thinks out loud"
            // a small model's first paragraph is the accurate part; anything after it tends to wander off
            int para = s.IndexOf("\n\n", StringComparison.Ordinal);
            if (para > 40) s = s.Substring(0, para);
            var lines = s.Split('\n').Select(l => l.TrimStart('#', ' ').TrimEnd());
            return string.Join("\n", lines).Trim();
        }

        string brainOpening, brainOpeningFor;   // the same every time (per character), so the brain only reads it once per wake-up

        /// <summary>The fixed start of every conversation: who they are, the rules, everything they know, and two examples of their voice.</summary>
        List<KeyValuePair<string, string>> Opening()
        {
            var who = Personas.Current;
            if (brainOpening == null || brainOpeningFor != who.Id)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("You are " + who.Name + ", the helper living inside CloveStrap, a Roblox launcher for Windows. Stay in character the whole time.");
                sb.AppendLine("Character and voice: " + who.Voice);
                sb.AppendLine("The character is the seasoning: the actual answer always comes first and stays clear and correct.");
                sb.AppendLine();
                sb.AppendLine("Rules:");
                sb.AppendLine("- Answer from the CLOVESTRAP KNOWLEDGE below. Think about what the user is really trying to do, then point them to the right feature, setting and tab, named exactly as written below.");
                sb.AppendLine("- Keep it short: 1 to 3 sentences of plain text. No markdown, no headings, no lists.");
                sb.AppendLine("- Always reply in the same language the user writes in (Japanese gets Japanese, Spanish gets Spanish), keeping CloveStrap's setting and tab names in English as written.");
                sb.AppendLine("- Never invent features, settings, buttons, tabs, steps or error explanations. If the knowledge doesn't cover it, say so honestly, and suggest the closest thing that does exist.");
                sb.AppendLine("- If they mention an error, code or problem you don't recognise, say you don't recognise it, then give the troubleshooting steps from the knowledge.");
                sb.AppendLine("- Use the conversation so far to understand follow-ups like \"which one?\", \"where is it?\" or \"yes\".");
                sb.AppendLine("- Only mention the user's own current settings when they give them to you and ask about them.");
                sb.AppendLine("- Questions about CloveStrap's features are always fine. Only refuse cheats, exploits, free Robux, account stealing, or anything against Roblox's rules.");
                sb.AppendLine("- If the question has nothing to do with CloveStrap or Roblox, say you only know about CloveStrap.");
                sb.AppendLine();
                sb.AppendLine("CLOVESTRAP KNOWLEDGE:");
                sb.Append(CloveBrain.Knowledge(searchIndex));
                brainOpening = sb.ToString();
                brainOpeningFor = who.Id;
                Log.Write("Natsuki: " + who.Name + "'s knowledge is " + brainOpening.Length + " characters (about " + brainOpening.Length / 4 + " tokens)");
            }
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("system", brainOpening),
                // two examples of their voice: models copy examples far better than they follow descriptions
                new KeyValuePair<string, string>("user", "how do i add an account?"),
                new KeyValuePair<string, string>("assistant", who.AccountAnswer),
                new KeyValuePair<string, string>("user", "can you get me free robux?"),
                new KeyValuePair<string, string>("assistant", who.RobuxAnswer),
            };
        }

        /// <summary>What Natsuki's brain is told: the fixed opening, the recent chat (for follow-ups), then this question.</summary>
        List<KeyValuePair<string, string>> BuildPrompt(string question, string lookup, List<ChatMsg> history)
        {
            var msgs = Opening();
            foreach (var h in history.Skip(Math.Max(0, history.Count - 4)))
                msgs.Add(new KeyValuePair<string, string>(h.Me ? "user" : "assistant", h.Text));

            string low = " " + question.ToLowerInvariant() + " ";
            bool aboutMe = new[] { " am i ", " my setting", " my setup", " my preset", " my fps", " current", " do i have", " is my ", " did i ", " i'm on", " im on", " i on ", " my account", " my discord", " my name", " my username", " who am i", " my roblox" }.Any(low.Contains);
            var unknown = UnknownTerms(question);
            var q = new System.Text.StringBuilder(question);
            if (aboutMe) q.Append("\n\n(My current settings: " + string.Join("; ", SetupFacts()) + ".)");
            if (unknown.Count > 0) q.Append("\n\n(Note: CloveStrap doesn't know anything called " + string.Join(", ", unknown.Select(u => "\"" + u + "\"")) + ".)");
            msgs.Add(new KeyValuePair<string, string>("user", q.ToString()));
            Log.Write("Natsuki: \"" + question + "\"" + (aboutMe ? " +settings" : "") + (unknown.Count > 0 ? " unknown: " + string.Join(",", unknown) : "") + ", " + history.Count / 2 + " earlier exchanges");
            return msgs;
        }

        /// <summary>Error codes and made-up names (letters mixed with digits, or long numbers) that appear nowhere in CloveStrap.</summary>
        static List<string> UnknownTerms(string question)
        {
            var found = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(question, @"\b(?=[A-Za-z0-9_-]*\d)(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]{3,}\b|\b\d{3,}\b"))
            {
                string t = m.Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(d3d11|fps\d*|\d+fps|\d+hz|\d+x|4x|8x|2x|\d+k|\d+gb|\d+mb|\d{2,3})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;   // things CloveStrap does talk about
                found.Add(t);
            }
            return found;
        }

        /// <summary>Wake the brain and let it read the opening in the background, so the first answer is quick.</summary>
        void WarmBrain()
        {
            if (!NatsukiBrain.Installed || NatsukiBrain.Running || NatsukiBrain.Downloading || warming) return;
            warming = true;
            warmDone.Reset();
            var opening = Opening();
            opening.Add(new KeyValuePair<string, string>("user", "hi"));
            // the saved memory belongs to exactly this opening (character + knowledge): a different one gets a different file
            string key;
            using (var sha = System.Security.Cryptography.SHA1.Create())
                key = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\u0001", opening.Select(m => m.Key + ":" + m.Value)))))
                      .Replace("-", "").Substring(0, 16).ToLowerInvariant();
            string file = "opening-" + key + ".bin";
            new Thread(() =>
            {
                try
                {
                    var t0 = DateTime.UtcNow;
                    if (NatsukiBrain.Start(null) != null) return;
                    if (System.IO.File.Exists(System.IO.Path.Combine(NatsukiBrain.CacheDir, file)) && NatsukiBrain.Memory("restore", file))
                        Log.Write("Natsuki: woke up from saved memory in " + (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0") + " s");
                    else
                    {
                        NatsukiBrain.Chat(opening, null, 1);   // read everything once...
                        Log.Write("Natsuki: read her knowledge in " + (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0") + " s");
                        if (NatsukiBrain.Memory("save", file))   // ...and remember it for next time (only the newest is kept)
                            foreach (var old in System.IO.Directory.GetFiles(NatsukiBrain.CacheDir, "opening-*.bin").Where(f => !f.EndsWith(file)))
                                try { System.IO.File.Delete(old); } catch { }
                    }
                }
                catch (Exception ex) { Log.Write("Natsuki: warm-up failed: " + ex.Message); }
                finally { warming = false; warmDone.Set(); }
            }) { IsBackground = true, Name = "natsuki-warm" }.Start();
        }
        volatile bool warming;
        readonly ManualResetEvent warmDone = new ManualResetEvent(true);

        IEnumerable<string> SetupFacts()
        {
            var p = Presets.Get(S.Preset ?? "custom");
            yield return "Preset: " + p.Name;
            yield return "FPS cap: " + (S.FpsCap > 0 ? S.FpsCap.ToString() : "matches the monitor (" + Display.RefreshRate() + ")");
            yield return "Gray sky: " + (S.GraySky ? "on" : "off");
            yield return "Anti-AFK: " + (S.AntiAfk ? "on" : "off") + ", Auto-rejoin: " + (S.AutoRejoin ? "on" : "off") + ", Multiple instances: " + (S.MultiInstance ? "on" : "off");
            yield return "Start with Windows: " + (S.StartWithWindows ? "on" : "off") + ", Discord status: " + (S.DiscordPresence ? "on" : "off");
            List<Account> accs = null;
            try { accs = AccountStore.Load(); } catch { }
            if (accs != null)
                yield return "Saved Roblox accounts: " + accs.Count + (accs.Count > 0 ? " (" + string.Join(", ", accs.Select(a => "@" + a.Username + (a.AltMode ? " in alt mode" : ""))) + ")" : "");
            if (DiscordAccount.SignedIn(S)) yield return "Signed in with Discord as " + S.DiscordName;
        }

        void GoToSetting(string page, string row)
        {
            var e = searchIndex.FirstOrDefault(x => x.Page == page && x.RowTitle == row && x.Card != null)
                 ?? (row != null ? searchIndex.FirstOrDefault(x => x.RowTitle == row && x.Card != null) : null);
            if (e != null) GoTo(e);
            else if (page != null && pages.ContainsKey(page)) ShowPage(page);
        }
    }

    /// <summary>"Ask Natsuki" in the sidebar, styled like the tabs, with her face.</summary>
    class AskButton : VControl
    {
        bool open;
        public bool Open { get { return open; } set { open = value; Invalidate(); } }

        public AskButton()
        {
            Size = new Size(212, 40);
            Cursor = Cursors.Hand;
            BackColor = Theme.Side;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (open)
            {
                Theme.Fill(g, r, 9, Theme.NavActive);
                Theme.Stroke(g, r, 9, Theme.Mix(Theme.Border, Theme.Accent, 0.45f));
            }
            else if (Hover) Theme.Fill(g, r, 9, Theme.NavHover);
            ChatPics.Draw(g, new RectangleF(10, 7, 26, 26), Personas.Current.Pic, Personas.Current.Name);
            Color fg = open || Hover ? Theme.Text : Theme.Muted;
            Theme.Draw(g, "Ask " + Personas.Current.Name, open ? Theme.RowTitle : Theme.Body, new Rectangle(44, 0, Width - 70, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "", Theme.IconSmall, new Rectangle(Width - 30, 0, 20, Height), open ? Theme.Accent : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>The chat window: header, conversation, and a box to type in.</summary>
    class ChatPanel : Panel
    {
        public event Action<string> Asked;
        public event Action Closed;
        readonly ChatView view;
        readonly VTextBox input;
        const int HeadH = 58;

        public ChatPanel(List<ChatMsg> msgs, Func<Image> myPic, Func<string> myName)
        {
            Size = new Size(400, 488);
            BackColor = Theme.Bg;
            // cut the panel itself to its rounded shape, so no square corner shows over lighter things behind it
            using (var shape = Theme.Round(new RectangleF(0, 0, Width, Height), 14)) Region = new Region(shape);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            var close = new IconButton("") { BackColor = Theme.Card };
            close.Location = new Point(Width - close.Width - 8, (HeadH - close.Height) / 2);
            close.Click += (o, e) => { if (Closed != null) Closed(); };

            view = new ChatView(msgs, myPic, myName) { Location = new Point(1, HeadH), Size = new Size(Width - 2, Height - HeadH - 58) };

            input = new VTextBox { Location = new Point(12, Height - 48), Size = new Size(Width - 24 - 92, 36), BackColor = Theme.Card };
            input.Placeholder = "Ask " + Personas.Current.Name + " something...";
            var send = new VButton("Ask", "", true) { BackColor = Theme.Card, Size = new Size(84, 36) };
            send.Location = new Point(Width - 12 - send.Width, Height - 48);
            Action go = () => { string q = input.Text; input.Text = ""; if (Asked != null) Asked(q); input.Box.Focus(); };
            send.Click += (o, e) => go();
            input.Box.KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; go(); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; if (Closed != null) Closed(); }
            };
            Controls.AddRange(new Control[] { close, view, input, send });
        }

        public void FocusInput() { input.Box.Focus(); }
        public void Changed() { view.Relayout(); view.ScrollToEnd(); }
        public void Refresh2() { view.Relayout(); view.Invalidate(); }
        public void ScrollToEnd() { view.Relayout(); view.ScrollToEnd(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, 14, Theme.Card);
            Theme.Stroke(g, r, 14, Theme.Mix(Theme.Border, Theme.Accent, 0.35f));
            ChatPics.Draw(g, new RectangleF(14, 11, 36, 36), Personas.Current.Pic, Personas.Current.Name);
            Theme.Draw(g, Personas.Current.Name, Theme.RowTitle, new Rectangle(60, 11, 200, 20), Theme.Text, TextFormatFlags.Left);
            Theme.Draw(g, "Knows everything about CloveStrap", Theme.Small, new Rectangle(60, 31, 260, 18), Theme.Muted, TextFormatFlags.Left);
            using (var p = new Pen(Theme.Line)) { g.DrawLine(p, 1, HeadH - 1, Width - 2, HeadH - 1); g.DrawLine(p, 1, Height - 58, Width - 2, Height - 58); }
        }
    }

    /// <summary>The conversation: profile pictures, speech bubbles, Natsuki's buttons, mouse-wheel scrolling.</summary>
    class ChatView : VControl
    {
        readonly List<ChatMsg> msgs;
        readonly Func<Image> myPic;
        readonly Func<string> myName;
        readonly List<KeyValuePair<Rectangle, Action>> hits = new List<KeyValuePair<Rectangle, Action>>();
        int scroll, total, hoverHit = -1;
        const int Pic = 30, PicGap = 8, Side = 12;
        const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        public ChatView(List<ChatMsg> msgs, Func<Image> myPic, Func<string> myName)
        {
            this.msgs = msgs; this.myPic = myPic; this.myName = myName;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.Selectable, false);
        }

        public void Relayout() { total = Measure(); Invalidate(); }
        public void ScrollToEnd() { scroll = Math.Max(0, total - Height); Invalidate(); }

        int BubbleMax { get { return Width - (Side + Pic + PicGap) - 40; } }

        int Measure()
        {
            int y = 14;
            foreach (var m in msgs)
            {
                var sz = TextRenderer.MeasureText(m.Text, Theme.Body, new Size(BubbleMax - 24, 0), Wrap);
                y += Math.Max(Pic, sz.Height + 18) + 8;
                y += m.Buttons.Count * 32;
                if (m.Buttons.Count > 0) y += 4;
            }
            return y + 6;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, total - Height), scroll - e.Delta / 3));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = hits.FindIndex(x => x.Key.Contains(e.Location));
            if (h != hoverHit) { hoverHit = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var hit = hits.FirstOrDefault(x => x.Key.Contains(e.Location));
            if (hit.Value != null) hit.Value();
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            hits.Clear();
            int y = 14 - scroll, max = BubbleMax;
            Image mine = myPic != null ? myPic() : null;
            string name = myName != null ? myName() : "";
            foreach (var m in msgs)
            {
                var sz = TextRenderer.MeasureText(m.Text, Theme.Body, new Size(max - 24, 0), Wrap);
                int bw = Math.Min(max, sz.Width + 26), bh = sz.Height + 18;
                // your messages on the right with your picture; Natsuki's on the left with hers
                int picX = m.Me ? Width - Side - Pic : Side;
                int x = m.Me ? picX - PicGap - bw : Side + Pic + PicGap;
                ChatPics.Draw(g, new RectangleF(picX, y, Pic, Pic), m.Me ? mine : Personas.Current.Pic, m.Me ? name : Personas.Current.Name);
                var bubble = new RectangleF(x, y, bw, bh);
                Theme.Fill(g, bubble, 12, m.Me ? Theme.Accent : Theme.Row);
                if (!m.Me) Theme.Stroke(g, bubble, 12, Theme.Border);
                Color fg = m.Me ? Theme.OnAccent : (m.Typing ? Theme.Muted : Theme.Text);
                TextRenderer.DrawText(g, m.Text, Theme.Body, new Rectangle(x + 13, y + 9, bw - 24, bh - 12), fg, Wrap);
                y += Math.Max(Pic, bh) + 8;
                foreach (var b in m.Buttons)
                {
                    int tw = Math.Min(max, TextRenderer.MeasureText(b.Key, Theme.Small).Width + 28);
                    var br = new Rectangle(Side + Pic + PicGap, y, tw, 26);
                    bool hv = hits.Count == hoverHit;
                    Theme.Fill(g, br, 13, hv ? Theme.Mix(Theme.Row, Theme.Accent, 0.25f) : Theme.Bg);
                    Theme.Stroke(g, br, 13, Theme.Mix(Theme.Border, Theme.Accent, 0.6f));
                    Theme.Draw(g, b.Key, Theme.Small, new Rectangle(br.X + 12, br.Y, br.Width - 20, br.Height), Theme.Accent,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    hits.Add(new KeyValuePair<Rectangle, Action>(br, b.Value));
                    y += 32;
                }
                if (m.Buttons.Count > 0) y += 4;
            }
        }
    }
}
