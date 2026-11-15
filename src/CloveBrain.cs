// "Ask Clove": a small built-in helper that knows CloveStrap. No internet, no API key: it matches your question against
// hand-written answers about how the app works and against every setting on every tab, then answers and can take you there.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CloveStrap
{
    class CloveReply
    {
        public string Text;
        public string Page, Row;            // where "Take me there" goes (Row may be null: just the tab)
        public List<SearchEntry> Suggestions = new List<SearchEntry>();
    }

    static class CloveBrain
    {
        class Faq
        {
            public string[] Keys;   // phrases; longer phrases count for more
            public string Answer, Page, Row;
            public Faq(string keys, string answer, string page = null, string row = null)
            {
                Keys = keys.Split(new[] { (char)124 }).Select(k => Core(k)).Where(k => k.Trim().Length > 0).Distinct().ToArray();
                Answer = answer; Page = page; Row = row;
            }
        }

        static readonly HashSet<string> Stop = new HashSet<string>(("a an the to do does did i im i'm me my mine you your it its is are was be been can could would should will " +
            "how what why where when which who whom that this these those there here of on in at for with and or but so if then than " +
            "get got make turn set change use using way want wanna need please pls plz just like really also still not dont don't doesnt " +
            "about any some all from into up out off by as am have has had keep keeps kept").Split(' '));

        // words that say little about what you're asking: they count for almost nothing when finding facts
        static readonly HashSet<string> Weak = new HashSet<string>("work thing stuff help know tell good best better bad lot much many one thank ok okay yes no hi hey roblox clovestrap app game".Split(' '));

        static readonly Dictionary<string, string> Same = new Dictionary<string, string>
        {
            { "acc", "account" }, { "accs", "account" }, { "accounts", "account" }, { "alts", "alt" }, { "grey", "gray" },
            { "skybox", "sky" }, { "skies", "sky" }, { "frames", "frame" }, { "framerate", "fps" }, { "laggy", "lag" }, { "lagging", "lag" },
            { "stuttering", "stutter" }, { "multiinstance", "multi instance" }, { "dc", "discord" }, { "pfp", "avatar" },
            { "usernames", "username" }, { "names", "name" }, { "crosshairs", "crosshair" }, { "settings", "setting" }, { "colour", "color" },
            { "colours", "color" }, { "themes", "theme" }, { "flags", "flag" }, { "fflags", "fflag" }, { "kicked", "kick" }, { "rejoining", "rejoin" },
        };

        /// <summary>What CloveStrap is, in a nutshell (Natsuki's brain always gets this).</summary>
        public const string Overview =
            "CloveStrap is a free launcher (a \"bootstrapper\") for Roblox on Windows. When you press Launch Roblox in CloveStrap, or Play on roblox.com, " +
            "CloveStrap starts Roblox with your settings applied: FPS cap, graphics options, FFlags, and mods (custom sky, character sounds, cursor). " +
            "It keeps Roblox up to date on your update channel and also applies your mods to the normal Roblox install. " +
            "Extras: saved Roblox accounts with multi-instance (several Roblox windows at once), alt mode (an account uses as little of your PC as possible), " +
            "an Alt windows setting (open alts normally or minimized), joining a place ID or following a player, picking a server, auto-rejoin, anti-AFK, " +
            "a screen crosshair and in-game overlay, Discord status (\"Playing <game> with CloveStrap\"), a username finder for free Roblox names, " +
            "themes, quietening Roblox when you switch away, and a search box (Ctrl+F). It waits in the system tray and starts with Windows. " +
            "Tabs: Presets, Accounts, Usernames, Performance, Graphics, Overlay, Mods, FFlag Editor, Launcher, Appearance. " +
            "Everything saves automatically. It only uses things Roblox allows (no injecting into the game).";

        static readonly Faq[] Faqs =
        {
            new Faq("how clovestrap work|what clovestrap|what clovestrap do|what do you do|what can you do|what this app|what is this|explain clovestrap|feature|bootstrapper|is free|cost money",
                "CloveStrap is a launcher for Roblox. It opens Roblox with your settings, FPS cap, FFlags and mods (sky, sounds, cursor) already applied and keeps it updated. On top of that: multiple accounts, alt mode, Discord status, anti-AFK, auto-rejoin, a crosshair and a username finder. Pick a tab on the left to explore, or ask me about any of them!", null, null),
            new Faq("add account|sign in account|log in account|login roblox|add alt|new account|save account|roblox account",
                "Go to Accounts and press Add account. A small roblox.com window opens; log in there once and CloveStrap remembers the account (locked to your Windows user). Your password never touches CloveStrap.", "Accounts", "Sign in once"),
            new Faq("launch all|same server|all account same server|launch multiple account|launch every account|join together",
                "On the Accounts tab, set where to go (a place ID or a player to follow), then press Launch all. With a place ID, every account goes into the same server.", "Accounts", "Server"),
            new Faq("alt mode|low resource|alt lag|lag pc|alt slow|alt use less|alt use much|alt cpu|alt ram|alt memory|alt heavy|potato alt|alt performance|make alt lighter|run many alt|lot alt",
                "Alt mode makes an account's Roblox use as little of your PC as possible: lowest graphics, efficiency mode, idle priority, 2 CPU cores and trimmed memory. Switch it on per account on the Accounts tab. It never touches your main account.", "Accounts", "Sign in once"),
            new Faq("place id|join game|join place|game link|join server",
                "Paste a game link or its place ID on the Accounts tab under Where to go, then launch. You can also pick the emptiest, fullest or best-ping server.", "Accounts", "Every account you launch goes here"),
            new Faq("follow player|join friend|join player|follow user|join someone",
                "On the Accounts tab, switch Where to go to Follow player and type their username. Your accounts join the server they're in, as long as their joins are open.", "Accounts", "Every account you launch goes here"),
            new Faq("afk|idle|kick idle|20 minute|away kick",
                "Anti-AFK (Launcher tab) gives every Roblox window one tiny jump every 14 minutes so Roblox doesn't kick you for idling. Alts too. CloveStrap needs to be running in the tray.", "Launcher", "Anti-AFK"),
            new Faq("rejoin|kick|kicked|disconnect|reconnect",
                "Auto-rejoin (Accounts tab) reopens an account into the same game if it gets kicked or disconnected. It won't rejoin if you logged in from another device.", "Accounts", "Auto-rejoin"),
            new Faq("multi instance|2 account|two account|account same time|account at once|multiple account|run account|more than one account|both account|multiple roblox|two roblox|several roblox|more than one roblox|multiple window",
                "Yes! You can run several accounts at the same time: add them on the Accounts tab, then press Launch on each one or Launch all. This works because Multiple instances is on by default (that setting is on the Launcher tab). If Roblox was already open, CloveStrap offers to fix it for you.", "Launcher", "Multiple instances"),
            new Faq("already open|roblox already|close roblox|fix now",
                "If Roblox is already open when you launch, CloveStrap asks \"O-oh, it appears Roblox is already open\". Press Fix now and it frees Roblox's lock so more windows can open, without closing your game.", "Launcher", "Multiple instances"),
            new Faq("sky|skybox|custom sky|change sky|blood moon|sky color",
                "Make a sky on the Mods tab: pick a preset like Blood moon or mix your own colors, then press Make. It shows in every game that uses Roblox's default sky.", "Mods", null),
            new Faq("sky show|isnt sky|sky game|cant see|see sky|sky missing|sky gone|sky some|sky work|sky not working|sky not show|sky override|game sky|sky every game|force sky|sky not apply",
                "Your custom sky only shows in games that use Roblox's normal sky. Some games have their own sky, and theirs always wins there; CloveStrap can't replace it without breaking Roblox's rules. The only setting that covers every game's sky is Gray sky (Graphics tab).", "Graphics", "Gray sky"),
            new Faq("gray sky|grey sky|remove gray|sky gray",
                "Gray sky turns every game's sky plain gray. It's a little faster and makes players easier to spot. Turn it off on the Graphics tab or in the FFlag quick flags.", "Graphics", "Gray sky"),
            new Faq("view distance|render distance|draw distance|see far|fog|haze|far away",
                "How far you can see is set by Graphics quality: higher shows more. Fog and haze come from the game itself and can't be changed. Distant model detail only changes when far-away unions and meshes simplify.", "Graphics", "Graphics quality"),
            new Faq("fps|framerate|uncap|fps cap|fps limit|more frame|frame rate|good fps|best fps|what fps",
                "The best FPS cap is usually your monitor's refresh rate (for example 144 on a 144 Hz screen); every preset sets that for you. Change it on the Performance tab: pick 60 to 360 or type a custom number.", "Performance", "FPS cap"),
            new Faq("lag|stutter|better performance|more fps|boost|optimize|faster|low end|slow pc|bad pc|roblox lag|game lag|less lag|reduce lag|fix lag|stop lag|smoother",
                "Try a preset: Competitive for every frame, or Low-End PC for old hardware. Or press Find on the Graphics tab and CloveStrap reads your PC and picks settings for you.", "Presets", null),
            new Faq("read pc|best setting|find setting|recommend setting|auto setting|what setting",
                "On the Graphics tab, press Find. CloveStrap checks your graphics card, processor, memory and screen, then picks the best settings for you.", "Graphics", "Read your PC to find the best settings for you?? >_<!!"),
            new Faq("preset|competitive|balanced|quality preset|laptop|which preset|best preset|what preset|preset fps|preset should",
                "Presets (Presets tab) set everything at once. Pick by goal: most FPS / lowest lag = Competitive; faster but looks the same = Balanced; " +
                "best looks on a strong graphics card = Quality; old or weak PC = Low-End PC; laptop on battery, cooler and quieter = Laptop. " +
                "Change anything by hand and you're on Custom.", "Presets", null),
            new Faq("error|crash|crashes|crashing|wont launch|wont open|not open|doesnt open|wont start|not start|stuck|freeze|frozen|bug|broken|fail|failed|black screen|white screen|not working",
                "For launch problems or errors CloveStrap doesn't recognise: close Roblox completely and launch again; on the Launcher tab press Check for updates, " +
                "or press Reinstall under Tools (downloads a fresh Roblox, your settings are kept); if it still happens, the Logs button under Tools opens CloveStrap's " +
                "log files, which say what went wrong.", "Launcher", "Installed version"),
            new Faq("username|name finder|free name|available name|3 letter|4 letter|rare name|og name",
                "The Usernames tab finds free Roblox names: real words in English or other languages, or 3 and 4 letter names. It keeps going, remembers what it already checked, and lists every free name it finds.", "Usernames", null),
            new Faq("discord status|rich presence|playing on discord|discord activity|show game discord",
                "Show what you're playing puts \"Playing <game> with CloveStrap\" on your Discord profile with the CloveStrap logo. Discord has to be open.", "Launcher", "Show what you're playing"),
            new Faq("discord sign|discord login|sign out|log out|switch discord|discord account",
                "You sign in with Discord once, through Discord's own Authorize popup in the Discord app. After that CloveStrap remembers you and logs you in by itself every launch.", null, null),
            new Faq("discord server|my server|join discord|discord permission|discord join",
                "When you sign in, Discord asks you to allow CloveStrap to see your servers and join servers. Nothing in the app uses those yet; they're there for future features.", null, null),
            new Faq("theme|color|custom theme|dark mode|light mode|change look|character",
                "The Appearance tab has character themes and your own custom themes. Upload a picture and CloveStrap picks matching colors for you.", "Appearance", null),
            new Faq("start with windows|startup|tray|run on boot|background",
                "Start with Windows keeps CloveStrap waiting in the tray so Roblox opens instantly. Right-click the tray icon to quit.", "Launcher", "Start with Windows"),
            new Faq("sound|oof|death sound|walk sound|jump sound|custom sound",
                "Change Roblox's character sounds on the Mods tab with your own mp3, ogg or wav files. Press play to hear the current one.", "Mods", "Character sounds"),
            new Faq("cursor|mouse|shift lock|pointer",
                "The Mods tab has custom mouse cursors, and the Overlay tab has a custom shift lock cursor.", "Mods", null),
            new Faq("crosshair|aim|reticle|dot",
                "Screen crosshair draws your crosshair over the Roblox window, on top of any game's own. Set its style, size and color on the Overlay tab.", "Overlay", "Screen crosshair"),
            new Faq("overlay|fps counter|ping|hud",
                "The in-game overlay shows FPS and network info while you play. Ctrl+Shift+O hides it. It doesn't show in exclusive fullscreen.", "Overlay", "Show overlay"),
            new Faq("tabbed out|tab out|alt tab|mute|volume|quiet|sound background|alt tab sound|loud",
                "Quieten Roblox mutes (or turns down) any Roblox window you're not looking at, and brings it back when you click in.", "Performance", "Quieten Roblox when you switch away"),
            new Faq("fflag|fast flag|flag|quick flag|allowlist|custom flag",
                "The FFlag Editor has quick flags you can just switch on, plus your own flags. Roblox only accepts flags on its allowlist; anything else shows yellow and is ignored.", "FFlag Editor", null),
            new Faq("update|roblox version|channel|out of date|new version",
                "CloveStrap follows your Roblox update channel and installs updates by itself. Check it on the Launcher tab.", "Launcher", "Installed version"),
            new Faq("ban|banned|safe|is safe|safe use|virus|malware|allowed|against rule|tos|detect|legit|trust",
                "CloveStrap only uses things Roblox allows: settings on its FFlag allowlist, its own launcher options, and swapping Roblox's default sounds, sky and cursor files. It doesn't inject into or modify the game, and won't do anything that breaks Roblox's rules.", null, null),
            new Faq("setting save|setting reset|lost setting|setting not save",
                "Everything saves by itself as you change it and stays between launches. You don't need to press save.", null, null),
            new Faq("search|find setting|where is",
                "Press Ctrl+F or click the search box at the top to search every setting on every tab.", null, null),
            new Faq("texture|blurry|vram|video memory",
                "Textures on the Graphics tab: lower saves video memory on 2-4 GB cards, higher looks sharper.", "Graphics", "Textures"),
            new Faq("grass|remove grass|static grass",
                "Remove grass takes it out completely for more FPS; Static grass just stops it swaying. Both are on the Performance tab.", "Performance", "Remove grass"),
            new Faq("fullscreen|input lag|alt enter",
                "Exclusive fullscreen makes Alt+Enter use real fullscreen, for less input lag.", "Performance", "Exclusive fullscreen"),
            new Faq("vulkan|directx|d3d11|opengl|graphics api|renderer",
                "Pick Roblox's graphics API on the Graphics tab (or in quick flags). Try DirectX 11 if you crash or stutter.", "Graphics", "Graphics API"),
            new Faq("uninstall|remove clovestrap|delete clovestrap",
                "To remove CloveStrap, turn off Start with Windows and Use for the Play button on the Launcher tab, quit it from the tray, then delete it. Roblox keeps working on its own.", "Launcher", "Use for the Play button"),
            new Faq("play button|roblox website|browser launch|play roblox.com",
                "Use for the Play button makes Play on roblox.com start through CloveStrap, so your settings and mods apply.", "Launcher", "Use for the Play button"),
        };

        static string Stem(string w)
        {
            string s;
            if (Same.TryGetValue(w, out s)) return s;
            if (w.Length > 5 && w.EndsWith("ing")) return w.Substring(0, w.Length - 3);
            if (w.Length > 4 && w.EndsWith("s") && !w.EndsWith("ss")) return w.Substring(0, w.Length - 1);
            return w;
        }

        public static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) || c == '\'' ? c : ' ');
            var words = sb.ToString().Replace("'", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Stem);
            return " " + string.Join(" ", words) + " ";
        }

        /// <summary>The meaningful words in order, filler words dropped: "how do I add an account" -> " add account ".</summary>
        static string Core(string s)
        {
            return " " + string.Join(" ", Norm(s).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !Stop.Contains(w))) + " ";
        }

        static List<string> Words(string q)
        {
            return Norm(q).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !Stop.Contains(w)).Distinct().ToList();
        }

        static readonly string[] Hellos = { "hi", "hello", "hey", "hii", "yo", "sup", "hiya", "heya", "howdy" };
        static readonly string[] Thanks = { "thank", "thanks", "ty", "thx", "tysm", "thankyou" };

        /// <summary>Everything Natsuki knows, as one document: what CloveStrap is, how-tos, the presets, and every setting with its tab.</summary>
        public static string Knowledge(List<SearchEntry> settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ABOUT CLOVESTRAP:");
            sb.AppendLine(Overview);
            sb.AppendLine();
            sb.AppendLine("HOW-TOS AND ANSWERS:");
            foreach (var f in Faqs.Skip(1)) sb.AppendLine("- " + f.Answer);
            sb.AppendLine();
            sb.AppendLine("PRESETS (Presets tab):");
            foreach (var p in Presets.All) sb.AppendLine("- " + p.Name + ": " + p.Desc + (p.Tags != null && p.Tags.Length > 0 ? " (" + string.Join(", ", p.Tags) + ")" : ""));
            sb.AppendLine();
            sb.AppendLine("EVERY SETTING (name, tab: what it does):");
            var seen = new HashSet<string>();
            foreach (var e in settings.Where(x => x.Card != null))
            {
                string key = e.Title + "|" + e.Page;
                if (!seen.Add(key) || e.Title.StartsWith("Read your PC")) continue;
                string desc = (e.Desc ?? "").Trim();
                if (desc == "Off." || desc.Length == 0) desc = "";
                sb.AppendLine("- " + e.Title + ", " + e.Page + " tab" + (desc.Length > 0 ? ": " + desc : ""));
            }
            sb.AppendLine("- Find (Read your PC to find the best settings), Graphics tab: checks your PC and picks the best settings for you.");
            return sb.ToString();
        }

        /// <summary>
        /// The facts most related to a question, for Natsuki's brain: hand-written answers first, then settings
        /// ("Title (Page tab): description").
        /// </summary>
        public static List<string> Facts(string question, List<SearchEntry> settings, int faqCount = 3, int settingCount = 3)
        {
            double top; bool sure;
            return Facts(question, settings, out top, out sure, faqCount, settingCount);
        }

        /// <summary>Same, best first. sure: the best fact clearly answers the question (strong match, well ahead of the rest).</summary>
        public static List<string> Facts(string question, List<SearchEntry> settings, out double topScore, out bool sure, int faqCount = 3, int settingCount = 3)
        {
            topScore = 0; sure = false;
            // every piece of knowledge as a "document": its strong words (keys / setting name) and its other words
            var docs = new List<Doc>();
            foreach (var f in Faqs)
                docs.Add(new Doc { Faq = f, Strong = WordSet(string.Join(" ", f.Keys)), Rest = WordSet(f.Answer) });
            foreach (var e in settings.Where(x => x.Card != null))
                docs.Add(new Doc { Setting = e, Strong = WordSet(e.Title), Rest = WordSet(e.Hay) });

            // rare words matter more than common ones (a word in 2 answers says more than one in 30)
            var vocab = new HashSet<string>(docs.SelectMany(d => d.Strong.Concat(d.Rest)));
            var df = new Dictionary<string, int>();
            foreach (var d in docs) foreach (var w in d.Strong.Union(d.Rest)) { int c; df.TryGetValue(w, out c); df[w] = c + 1; }
            Func<string, double> idf = w => { int c; df.TryGetValue(w, out c); return Math.Log(1.0 + docs.Count / (double)Math.Max(1, c)); };

            var words = Words(question).Select(w => Fix(w, vocab)).Distinct().ToList();
            string core = Core(string.Join(" ", words));
            foreach (var d in docs)
            {
                double s = 0;
                foreach (var w in words)
                {
                    double weight = Weak.Contains(w) ? 0.15 : 1.0;
                    if (d.Strong.Contains(w)) s += 2.0 * idf(w) * weight;
                    else if (d.Rest.Contains(w)) s += 0.8 * idf(w) * weight;
                }
                if (d.Faq != null) foreach (var k in d.Faq.Keys) { int n = k.Trim().Split(' ').Length; if (n > 1 && core.Contains(k)) s += 2.5 * n; }
                d.Score = s;
            }
            double top = docs.Count == 0 ? 0 : docs.Max(d => d.Score);
            topScore = top;
            if (top < 1.5) return new List<string>();   // nothing clearly related
            var keep = docs.Where(d => d.Score >= Math.Max(1.5, top * 0.45)).OrderByDescending(d => d.Score).ToList();
            double second = keep.Count > 1 ? keep[1].Score : 0;
            sure = top >= 4.0 && (second < top * 0.8 || keep[0].Faq != null);
            // best first, then the rest (answers before settings), within the limits
            var facts = new List<string>();
            int faqs = 0, sets = 0;
            foreach (var d in keep)
            {
                if (d.Faq != null && faqs < faqCount) { facts.Add(d.Faq.Answer); faqs++; }
                else if (d.Setting != null && sets < settingCount)
                {
                    facts.Add("Setting \"" + d.Setting.Title + "\" (" + d.Setting.Page + " tab): " + (string.IsNullOrEmpty(d.Setting.Desc) ? "" : d.Setting.Desc));
                    sets++;
                }
            }
            return facts;
        }

        class Doc { public Faq Faq; public SearchEntry Setting; public HashSet<string> Strong, Rest; public double Score; }

        static HashSet<string> WordSet(string s)
        {
            return new HashSet<string>(Norm(s).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !Stop.Contains(w)));
        }

        /// <summary>Small typos: a word CloveStrap doesn't know becomes the known word one letter away ("acount" -> "account").</summary>
        static string Fix(string w, HashSet<string> vocab)
        {
            if (w.Length < 5 || vocab.Contains(w)) return w;   // short words are usually real words ("safe" isn't a typo of "save")
            int allowed = w.Length >= 8 ? 2 : 1;
            string best = null; int bestD = int.MaxValue;
            foreach (var v in vocab)
            {
                if (Math.Abs(v.Length - w.Length) > allowed) continue;
                int d = Edit(w, v, allowed);
                if (d <= allowed && d < bestD) { bestD = d; best = v; }
            }
            return best ?? w;
        }

        static int Edit(string a, string b, int cap)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    rowMin = Math.Min(rowMin, cur[j]);
                }
                if (rowMin > cap) return cap + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        public static CloveReply Answer(string question, List<SearchEntry> settings)
        {
            string n = Norm(question);
            var all = n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var words = Words(question);

            if (all.Length <= 4 && all.Any(w => Hellos.Contains(w)))
                return new CloveReply { Text = Personas.Current.Hello };
            if (all.Any(w => Thanks.Contains(w)) && all.Length <= 6)
                return new CloveReply { Text = Personas.Current.Thanks };
            if (n.Contains(" who are you ") || n.Contains(" what are you ") || n.Contains(" your name "))
                return new CloveReply { Text = Personas.Current.WhoAmI };
            if (words.Count == 0)
                return new CloveReply { Text = "Ask me about any CloveStrap feature, like alt mode, the sky, FPS or Discord status." };

            // hand-written answers: whole phrases count for more than single words
            string core = Core(question);
            Faq best = null; int bestScore = 0;
            foreach (var f in Faqs)
            {
                int score = 0;
                foreach (var k in f.Keys)
                    if (core.Contains(k)) score += k.Trim().Split(' ').Length * 3;
                if (score > bestScore) { best = f; bestScore = score; }
            }

            // settings: words found in the setting's name count most
            SearchEntry bestSet = null; int setScore = 0;
            var scored = new List<KeyValuePair<int, SearchEntry>>();
            foreach (var e in settings.Where(x => x.Card != null))
            {
                string title = Norm(e.Title), hay = Norm(e.Hay);
                int s = 0, inTitle = 0;
                foreach (var w in words)
                {
                    if (w == "roblox" || w == "clovestrap") continue;   // in almost everything, says nothing
                    if (title.Contains(" " + w + " ")) { s += 4; inTitle++; }
                    else if (hay.Contains(" " + w + " ")) s += 1;
                }
                if (inTitle == 0 && s < 3) continue;
                scored.Add(new KeyValuePair<int, SearchEntry>(s, e));
                if (s > setScore) { setScore = s; bestSet = e; }
            }

            if (best != null && bestScore >= 3 && bestScore + 2 >= setScore)   // a hand-written answer explains more than a setting name
                return new CloveReply { Text = best.Answer, Page = best.Page, Row = best.Row };

            if (bestSet != null && setScore >= 4)
                return new CloveReply
                {
                    Text = bestSet.Title + " is on the " + bestSet.Page + " tab. " +
                           (string.IsNullOrEmpty(bestSet.Desc) ? "" : bestSet.Desc),
                    Page = bestSet.Page, Row = bestSet.Title
                };

            var r = new CloveReply { Text = Personas.Current.NotSure };
            r.Suggestions = scored.OrderByDescending(x => x.Key).Select(x => x.Value).Take(3).ToList();
            if (r.Suggestions.Count > 0) r.Text += " These might be what you're looking for:";
            else r.Text += " Try asking about accounts, alt mode, the sky, FPS, Discord status or the username finder.";
            return r;
        }
    }
}
