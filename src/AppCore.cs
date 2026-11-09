using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Chipmunk
{
    /// <summary>Hidden window that receives global hotkeys and clipboard-change notifications.</summary>
    class MessageWindow : NativeWindow
    {
        readonly AppCore core;

        public MessageWindow(AppCore core)
        {
            this.core = core;
            CreateHandle(new CreateParams { Caption = "Chipmunk.Messages" });
            if (!Native.AddClipboardFormatListener(Handle)) Log.Error("AddClipboardFormatListener failed");
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) core.OnHotkey(m.WParam.ToInt32());
            else if (m.Msg == Native.WM_CLIPBOARDUPDATE) core.OnClipboardUpdate();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Native.RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }
    }

    class AppCore
    {
        public readonly Settings Settings;
        public readonly Store Store;
        public readonly Icon AppIcon;
        public readonly Bitmap Logo;
        public readonly bool DevMode;
        public readonly ApplicationContext Context = new ApplicationContext();

        readonly MainWindow win;
        readonly Toast toast;
        readonly PetWindow pet;
        readonly MessageWindow messages;
        readonly NotifyIcon tray;
        readonly PipeServer pipe;
        ToolStripMenuItem trayOpen, trayCollect, trayPet;

        static readonly string[] HotkeyNames = { "toggle", "snip", "grab", "paste" };
        bool hotkeysSuspended;
        bool collect;
        bool flushed;
        IntPtr lastTarget = IntPtr.Zero;
        string lastTargetSig;
        bool panelWasLastForeground;
        readonly int myPid = Process.GetCurrentProcess().Id;

        class SnipState { public string DraftId; public DateTime Until, Started, OverlayGoneAt; public bool PanelWasVisible, SawOverlay; }
        class GrabState { public string DraftId, Source; public uint Seq; public DateTime Until; }
        SnipState snip;
        GrabState grab;
        string forceDraft;
        DateTime forceUntil;

        readonly System.Windows.Forms.Timer fgTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly System.Windows.Forms.Timer snipTimer = new System.Windows.Forms.Timer { Interval = 200 };
        readonly System.Windows.Forms.Timer grabTimer = new System.Windows.Forms.Timer { Interval = 60 };
        readonly System.Windows.Forms.Timer clipTimer = new System.Windows.Forms.Timer { Interval = 70 };

        static readonly HashSet<string> ShellClasses = new HashSet<string>
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
            "Windows.UI.Core.CoreWindow", "Progman", "WorkerW", "#32768", "XamlExplorerHostIslandWindow", "ForegroundStaging",
            "MultitaskingViewFrame", "TaskListThumbnailWnd", "Xaml_WindowedPopupClass",
        };
        static readonly HashSet<string> SnipProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ScreenClippingHost", "SnippingTool", "ScreenSketch" };
        static readonly HashSet<string> TerminalClasses = new HashSet<string> { "CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass", "mintty", "VirtualConsoleClass", "PuTTY" };
        static readonly HashSet<string> TerminalProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WindowsTerminal", "wezterm-gui", "alacritty", "Tabby", "Hyper", "kitty", "warp" };
        static readonly string[] DictationApps = { "Wispr Flow", "WisprFlow", "Aqua", "AquaVoice", "Willow", "Superwhisper", "Spokenly", "VoiceInk" };
        static readonly Dictionary<string, string> AppNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "chrome", "Chrome" }, { "msedge", "Edge" }, { "firefox", "Firefox" }, { "brave", "Brave" }, { "opera", "Opera" }, { "vivaldi", "Vivaldi" },
            { "arc", "Arc" }, { "claude", "Claude" }, { "chatgpt", "ChatGPT" }, { "code", "VS Code" }, { "cursor", "Cursor" }, { "windsurf", "Windsurf" },
            { "WindowsTerminal", "Terminal" }, { "notepad", "Notepad" }, { "explorer", "File Explorer" }, { "slack", "Slack" }, { "discord", "Discord" },
            { "winword", "Word" }, { "outlook", "Outlook" }, { "olk", "Outlook" }, { "obsidian", "Obsidian" }, { "notion", "Notion" }, { "teams", "Teams" },
            { "ms-teams", "Teams" }, { "devenv", "Visual Studio" }, { "idea64", "IntelliJ" }, { "pycharm64", "PyCharm" }, { "codex", "Codex" },
        };

        public AppCore(EventWaitHandle showEvent, string[] args)
        {
            DevMode = args.Contains("--dev");
            string uiDir = ArgValue(args, "--ui") ?? Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui");
            Settings = Settings.Load();
            Store = new Store(Settings.dataDir);
            bool firstRun = Store.Count == 0;
            if (firstRun) Settings.currentDraft = Store.Create("").id;
            if (!Store.Exists(Settings.currentDraft)) Settings.currentDraft = MostRecent().id;
            Settings.Save();

            Logo = Brand.Draw(64);
            AppIcon = Brand.MakeIcon(32);
            toast = new Toast(Logo);
            toast.Activated2 += (s, e) => ShowPanel();

            messages = new MessageWindow(this);
            var hotkeyNotes = new List<string>();
            var errors = RegisterHotkeys(true, hotkeyNotes);
            foreach (var kv in errors) Log.Error("Hotkey " + kv.Key + ": " + kv.Value);
            if (hotkeyNotes.Count > 0) { Settings.Save(); foreach (var n in hotkeyNotes) Log.Info(n); }

            tray = BuildTray();
            win = new MainWindow(this);
            var _ = win.Handle;

            pet = new PetWindow(this);
            pet.Place(Settings.petPos);
            UpdatePet();
            pet.SetWanted(Settings.showPet);

            Bridge.Register();
            pipe = new PipeServer(win, OnPipe);
            pipe.Start();

            fgTimer.Tick += (s, e) => TrackForeground();
            snipTimer.Tick += (s, e) => SnipTick();
            grabTimer.Tick += (s, e) => GrabTick();
            clipTimer.Tick += (s, e) => { clipTimer.Stop(); ProcessClipboard(); };
            fgTimer.Start();

            // Index past sessions once in the background, so Past chats opens instantly later.
            var warm = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(8000);
                    ClaudeImport.Sessions();
                    LocalAgents.CodexSessions();
                    LocalAgents.AntigravitySessions();
                }
                catch (Exception e) { Log.Error("Warm history: " + e.Message); }
            }) { IsBackground = true, Priority = ThreadPriority.Lowest };
            warm.Start();

            ThreadPool.RegisterWaitForSingleObject(showEvent, (st, to) => { try { win.BeginInvoke((Action)ShowPanel); } catch { } }, null, -1, false);

            StartUi(uiDir, args.Contains("--background") && !firstRun, errors, hotkeyNotes);
        }

        static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        async void StartUi(string uiDir, bool background, Dictionary<string, string> hotkeyErrors, List<string> hotkeyNotes)
        {
            try
            {
                await win.InitAsync(uiDir);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show("Chipmunk needs the Microsoft Edge WebView2 Runtime, which is built into Windows 11.\n\n" +
                    "On Windows 10, install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start Chipmunk again.",
                    "Chipmunk", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Quit();
                return;
            }
            catch (Exception e)
            {
                Log.Error(e);
                MessageBox.Show("Chipmunk couldn't start its window:\n\n" + e.Message, "Chipmunk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Quit();
                return;
            }
            if (background)
                toast.Show("Chipmunk is running. Press " + Settings.hotkeys["toggle"] + " to open it.");
            else
                ShowPanel();
            if (hotkeyErrors.Count > 0)
                toast.Show("Some shortcuts are taken by other apps. Pick new ones in Chipmunk settings.", true, 6000);
            else if (hotkeyNotes.Count > 0)
                toast.Show(string.Join(". ", hotkeyNotes) + ". You can change it in settings.", false, 6000);
        }

        Draft Current { get { return Store.Get(Settings.currentDraft); } }

        Draft MostRecent()
        {
            var d = Store.All.OrderByDescending(x => x.updated, StringComparer.Ordinal).FirstOrDefault();
            return d ?? Store.Create("");
        }

        // ---------------- tray ----------------

        NotifyIcon BuildTray()
        {
            var menu = new ContextMenuStrip();
            trayOpen = new ToolStripMenuItem("Open Chipmunk", null, (s, e) => ShowPanel()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            menu.Items.Add(trayOpen);
            menu.Items.Add("New draft", null, (s, e) => { ShowPanel(); NewDraftFromOutside(); });
            menu.Items.Add("Past chats", null, (s, e) => ShowView("history"));
            menu.Items.Add("Snip into draft", null, (s, e) => StartSnip(Settings.currentDraft));
            trayCollect = new ToolStripMenuItem("Collect screenshots") { CheckOnClick = true };
            trayCollect.Click += (s, e) => SetCollect(trayCollect.Checked);
            menu.Items.Add(trayCollect);
            trayPet = new ToolStripMenuItem("Show Chip") { CheckOnClick = true };
            trayPet.Click += (s, e) => SetPetVisible(trayPet.Checked);
            menu.Items.Add(trayPet);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings", null, (s, e) => ShowView("settings"));
            menu.Items.Add("Open drafts folder", null, (s, e) => OpenFolder(Store.Root));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit Chipmunk", null, (s, e) => Quit());
            var t = new NotifyIcon { Icon = AppIcon, Text = "Chipmunk", Visible = true, ContextMenuStrip = menu };
            t.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
            UpdateTrayText();
            return t;
        }

        void UpdateTrayText()
        {
            if (trayOpen != null) trayOpen.ShortcutKeyDisplayString = Settings.hotkeys["toggle"];
            if (trayCollect != null) trayCollect.Checked = collect;
            if (trayPet != null) trayPet.Checked = Settings.showPet;
        }

        void NewDraftFromOutside()
        {
            var d = Store.Create("");
            Settings.currentDraft = d.id;
            Settings.Save();
            win.Emit("openDraft", DraftDto(d));
            UpdatePet();
        }

        // ---------------- pet ----------------

        void UpdatePet()
        {
            if (pet == null) return;
            try
            {
                var d = Current;
                pet.SetCount(d.attachments.Count, d.title);
            }
            catch { pet.SetCount(0, null); }
        }

        public void SetPetVisible(bool on)
        {
            Settings.showPet = on;
            Settings.Save();
            pet.SetWanted(on);
            UpdateTrayText();
            win.Emit("settings", SettingsDto());
            if (!on) Notify("Chip is hiding. Bring it back from the tray icon or settings.");
        }

        public void SavePetPosition(int[] pos)
        {
            Settings.petPos = pos;
            Settings.Save();
        }

        // ---------------- panel ----------------

        bool PanelIsForeground()
        {
            return win.Visible && Native.Root(Native.GetForegroundWindow()) == win.Handle;
        }

        public void TogglePanel()
        {
            if (win.Visible && (PanelIsForeground() || panelWasLastForeground)) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel()
        {
            var fg = Native.GetForegroundWindow();
            if (IsExternal(fg)) SetTarget(fg);
            if (!win.Visible) win.Show();
            if (win.WindowState == FormWindowState.Minimized) win.WindowState = FormWindowState.Normal;
            win.Activate();
            Native.Focus(win.Handle);
            panelWasLastForeground = true;
            win.Emit("target", TargetDto(DefaultTarget()));
            win.FocusPage();
        }

        public void ShowView(string view)
        {
            ShowPanel();
            win.Emit("showView", view);
        }

        public void HidePanel()
        {
            panelWasLastForeground = false;
            win.Hide();
        }

        void TrackForeground()
        {
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            bool changed = fg != lastFgRaw;
            lastFgRaw = fg;
            if (!changed && ++quietTicks < 4) return;
            quietTicks = 0;
            pet.SetSuppressed(IsFullscreen(fg));
            if (Native.Root(fg) == win.Handle) { panelWasLastForeground = true; return; }
            if (!IsExternal(fg)) return;
            panelWasLastForeground = false;
            SetTarget(fg);
            var root = Native.Root(fg);
            if (root != lastForeground)
            {
                lastForeground = root;
                if (SnipProcesses.Contains(Native.ProcessName(root))) snipSeenAt = DateTime.UtcNow;
                else OfferSend(root);
            }
        }

        IntPtr lastForeground, lastFgRaw;
        int quietTicks;
        DateTime snipSeenAt = DateTime.MinValue, lastImageOffer = DateTime.MinValue;
        readonly Dictionary<string, DateTime> sendOffers = new Dictionary<string, DateTime>();

        /// <summary>You switched into ChatGPT, Claude, Codex... and your draft has something unsent:
        /// Chip offers to paste it right there, so sending is one click instead of a trip to the panel.</summary>
        void OfferSend(IntPtr h)
        {
            if (!pet.IsShowing || !Settings.petHints || snip != null || grab != null) return;
            var info = Describe(h);
            if (!(bool)info["ai"]) return;
            var d = Current;
            if (d == null || d.status == "sent") return;
            int items = d.attachments.Count(a => a.include);
            if (items == 0 && string.IsNullOrWhiteSpace(Store.ReadText(d))) return;
            string key = d.id + "|" + h.ToInt64();
            DateTime last;
            if (sendOffers.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(15)) return;
            sendOffers[key] = DateTime.UtcNow;
            string what = items > 0 ? Plural(items, "item") + " ready" : "Draft ready";
            pet.Say(what + " for " + info["label"] + ".", 6500, () => SendCurrentTo(h), "Click me to paste it in");
        }

        /// <summary>A screenshot or image just landed on the clipboard and Collect is off:
        /// Chip offers to keep it, so you don't have to come back to the panel.</summary>
        void OfferImage()
        {
            if (!pet.IsShowing || !Settings.petHints || PanelIsForeground()) return;
            if (!Native.IsClipboardFormatAvailable(2) && !Native.IsClipboardFormatAvailable(8)) return; // CF_BITMAP / CF_DIB
            if (DateTime.UtcNow - lastImageOffer < TimeSpan.FromSeconds(8)) return;
            var c = ClipboardService.Read();
            if (c.ImagePng == null || (c.Files != null && c.Files.Length > 0)) return;
            lastImageOffer = DateTime.UtcNow;
            bool shot = DateTime.UtcNow - snipSeenAt < TimeSpan.FromSeconds(20);
            var png = c.ImagePng;
            string source = shot ? "Screenshot" : "Copied" + (lastTarget != IntPtr.Zero ? " · " + FriendlyApp(lastTarget) : "");
            var d = Current;
            pet.Say((shot ? "Nice screenshot." : "Copied an image.") + " Keep it for " + Quote(d.title) + "?", 6500, () =>
            {
                var target = Store.Exists(d.id) ? Store.Get(d.id) : Current;
                var a = Store.AddImage(target, png, "image/png", shot ? Store.ScreenshotName() : "Copied image " + DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture), source, null);
                Collected(target);
                pet.Say(a == null ? "Already have that one." : "Got it · " + Plural(target.attachments.Count(x => x.kind == "image"), "image") + " in " + Quote(target.title), 2600);
            }, "Click to add it");
        }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        /// <summary>Games, fullscreen video and presentations: Chip steps out of the way.
        /// Asks Windows rather than comparing window sizes, which maximized windows can fool.</summary>
        bool IsFullscreen(IntPtr h)
        {
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return false;
            return state == 2 || state == 3 || state == 4; // busy fullscreen app, D3D fullscreen, presentation mode
        }

        bool IsExternal(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h) || !Native.IsWindowVisible(h)) return false;
            if (Native.ProcessId(h) == myPid) return false;
            if (ShellClasses.Contains(Native.WindowClass(h))) return false;
            string proc = Native.ProcessName(h);
            if (SnipProcesses.Contains(proc) || proc.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        void SetTarget(IntPtr h)
        {
            lastTarget = Native.Root(h);
            NoteRecent(lastTarget);
            var dto = TargetDto(DefaultTarget());
            string sig = dto == null ? "" : dto["app"] + "|" + dto["title"];
            if (sig != lastTargetSig)
            {
                lastTargetSig = sig;
                if (win.Visible) win.Emit("target", dto);
            }
        }

        string FriendlyApp(IntPtr h)
        {
            string p = Native.ProcessName(h);
            string name;
            if (AppNames.TryGetValue(p, out name)) return name;
            if (string.IsNullOrEmpty(p)) return "that app";
            return char.ToUpperInvariant(p[0]) + p.Substring(1);
        }

        bool IsTerminal(IntPtr h)
        {
            return TerminalClasses.Contains(Native.WindowClass(h)) || TerminalProcesses.Contains(Native.ProcessName(h));
        }

        Dictionary<string, object> TargetDto(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return null;
            return Describe(h);
        }

        string SourceLabel(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            string title = Native.WindowTitle(h);
            string app = FriendlyApp(h);
            if (string.IsNullOrWhiteSpace(title)) return app;
            return title.Length > 70 ? title.Substring(0, 68) + "…" : title;
        }

        // ---------------- hotkeys ----------------

        // Alternatives tried, in order, when a default shortcut is already taken by another app.
        static readonly Dictionary<string, string[]> HotkeyFallbacks = new Dictionary<string, string[]>
        {
            { "toggle", new[] { "Win+Alt+P", "Ctrl+Shift+Alt+P" } },
            { "snip", new[] { "Win+Alt+S", "Ctrl+Shift+Alt+S" } },
            { "grab", new[] { "Win+Alt+C", "Ctrl+Shift+Alt+C" } },
            { "paste", new[] { "Win+Alt+V", "Ctrl+Shift+Alt+V" } },
        };

        bool TryRegister(int id, string combo)
        {
            Hotkey hk;
            return Hotkey.TryParse(combo, out hk) && Native.RegisterHotKey(messages.Handle, id, hk.Mods | 0x4000, hk.Vk);
        }

        /// <summary>Registers all shortcuts. With allowFallback, a default that another app already
        /// owns is swapped for the next free alternative; notes say what changed.</summary>
        Dictionary<string, string> RegisterHotkeys(bool allowFallback = false, List<string> notes = null)
        {
            var errors = new Dictionary<string, string>();
            for (int i = 0; i < HotkeyNames.Length; i++) Native.UnregisterHotKey(messages.Handle, i + 1);
            if (hotkeysSuspended) return errors;
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < HotkeyNames.Length; i++)
            {
                string name = HotkeyNames[i];
                string combo = Settings.hotkeys[name];
                Hotkey hk;
                if (!Hotkey.TryParse(combo, out hk)) { errors[name] = "That isn't a shortcut Windows can use."; continue; }
                if (used.ContainsKey(combo)) { errors[name] = "Already used for another Chipmunk action."; continue; }
                if (TryRegister(i + 1, combo)) { used[combo] = name; continue; }
                string[] options;
                if (allowFallback && HotkeyFallbacks.TryGetValue(name, out options) && options.Contains(combo))
                {
                    string alt = options.FirstOrDefault(o => o != combo && !used.ContainsKey(o) && TryRegister(i + 1, o));
                    if (alt != null)
                    {
                        if (notes != null) notes.Add(combo + " was taken, so " + HotkeyLabel(name) + " uses " + alt);
                        Settings.hotkeys[name] = alt;
                        used[alt] = name;
                        continue;
                    }
                }
                errors[name] = combo + " is already taken by another app. Pick a different one.";
            }
            return errors;
        }

        static string HotkeyLabel(string name)
        {
            switch (name)
            {
                case "toggle": return "Open Chipmunk";
                case "snip": return "Snip";
                case "grab": return "Grab selection";
                default: return "Send draft";
            }
        }

        public void OnHotkey(int id)
        {
            try
            {
                switch (id)
                {
                    case 1: TogglePanel(); break;
                    case 2: StartSnip(Settings.currentDraft); break;
                    case 3: StartGrab(); break;
                    case 4: PasteHotkey(); break;
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                Notify(e is InvalidOperationException ? e.Message : "Something went wrong: " + e.Message, true);
            }
        }

        // ---------------- notifications ----------------

        void Notify(string message, bool error = false)
        {
            if (PanelIsForeground()) win.Emit("toast", new Dictionary<string, object> { { "text", message }, { "kind", error ? "error" : null } });
            else if (pet.IsShowing) pet.Say(message, error ? 5200 : 3400);
            else toast.Show(message, error);
        }

        void PushDraft(Draft d, bool textChanged = false)
        {
            if (d.id == Settings.currentDraft)
            {
                win.Emit("draftChanged", DraftDto(d, textChanged));
                UpdatePet();
            }
        }

        /// <summary>Something new landed in a draft: refresh the UI and let Chip celebrate.</summary>
        void Collected(Draft d)
        {
            PushDraft(d);
            if (d.id == Settings.currentDraft) pet.Catch();
        }

        static string Quote(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? "your draft" : "“" + (title.Length > 40 ? title.Substring(0, 38) + "…" : title) + "”";
        }

        static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }

        // ---------------- dictation ----------------
        // Chipmunk doesn't do its own speech-to-text: people already have a dictation app they
        // like, and those type into whatever is focused, including Chipmunk's editor. We only
        // detect which one is running so the panel can show its shortcut.

        static readonly Dictionary<string, string> DictationHints = new Dictionary<string, string>
        {
            { "Wispr Flow", "hold Ctrl+Win" }, { "Superwhisper", "use your Superwhisper shortcut" }, { "Aqua", "use your Aqua shortcut" },
            { "Willow", "hold your Willow key" }, { "Spokenly", "use your Spokenly shortcut" }, { "VoiceInk", "use your VoiceInk shortcut" },
        };

        Dictionary<string, object> DictationDto()
        {
            string app = RunningDictationApp();
            string hint;
            return app != null
                ? new Dictionary<string, object> { { "app", app }, { "hint", DictationHints.TryGetValue(app, out hint) ? hint : "use its shortcut" } }
                : new Dictionary<string, object> { { "app", null }, { "hint", "Win+H" } };
        }

        static string RunningDictationApp()
        {
            foreach (var name in DictationApps)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length == 0) continue;
                    if (name.StartsWith("Wispr")) return "Wispr Flow";
                    if (name.StartsWith("Aqua")) return "Aqua";
                    return name;
                }
                catch { }
            }
            return null;
        }

        // ---------------- where to send ----------------

        readonly List<IntPtr> recentWindows = new List<IntPtr>();

        void NoteRecent(IntPtr h)
        {
            recentWindows.Remove(h);
            recentWindows.Insert(0, h);
            if (recentWindows.Count > 30) recentWindows.RemoveAt(recentWindows.Count - 1);
        }

        static readonly Dictionary<string, string> AiApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ChatGPT", "ChatGPT" }, { "claude", "Claude" }, { "Codex", "Codex" }, { "Antigravity", "Antigravity" },
            { "Cursor", "Cursor" }, { "Windsurf", "Windsurf" }, { "Code", "VS Code" }, { "Zed", "Zed" }, { "Kiro", "Kiro" },
            { "Perplexity", "Perplexity" }, { "Copilot", "Copilot" }, { "M365Copilot", "Copilot" },
        };
        static readonly string[] BrowserProcesses = { "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "arc", "zen", "thorium" };
        static readonly string[] WebAis = { "ChatGPT", "Claude", "Gemini", "Perplexity", "Copilot", "Grok", "DeepSeek", "Le Chat", "Mistral", "Poe", "AI Studio", "NotebookLM", "Meta AI", "Kimi", "Qwen" };
        static readonly string[] CliAis = { "Claude Code", "claude", "codex", "gemini", "aider", "opencode", "amp" };

        /// <summary>Names a window for the "Send to" list; ai is true for AI apps and AI tabs.</summary>
        Dictionary<string, object> Describe(IntPtr h)
        {
            string proc = Native.ProcessName(h);
            string title = Native.WindowTitle(h);
            bool terminal = IsTerminal(h);
            string label = null;
            bool ai = false;
            string known;
            if (AiApps.TryGetValue(proc, out known)) { label = known; ai = true; }
            else if (BrowserProcesses.Contains(proc, StringComparer.OrdinalIgnoreCase))
            {
                string hit = WebAis.FirstOrDefault(w => title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) { label = hit; ai = true; }
            }
            else if (terminal)
            {
                string hit = CliAis.FirstOrDefault(w => title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                label = hit == null ? "Terminal" : hit.Equals("claude", StringComparison.OrdinalIgnoreCase) || hit == "Claude Code" ? "Claude Code" : char.ToUpperInvariant(hit[0]) + hit.Substring(1);
                ai = hit != null;
            }
            if (label == null) label = FriendlyApp(h);
            string app = FriendlyApp(h);
            if (string.IsNullOrWhiteSpace(title)) title = app;
            if (title.Length > 80) title = title.Substring(0, 78) + "…";
            return new Dictionary<string, object>
            {
                { "hwnd", h.ToInt64().ToString(CultureInfo.InvariantCulture) }, { "label", label }, { "app", app },
                { "title", title }, { "terminal", terminal }, { "ai", ai },
            };
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        /// <summary>Open windows you could send to: AI apps and AI tabs first (most recently used
        /// first), then other recent windows.</summary>
        List<Dictionary<string, object>> Targets()
        {
            var all = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                if (!IsExternal(h) || Native.Root(h) != h) return true;
                if ((GetWindowLong(h, -20) & 0x80) != 0) return true; // tool windows
                int cloaked;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true; // hidden on another desktop
                if (string.IsNullOrWhiteSpace(Native.WindowTitle(h))) return true;
                all.Add(h);
                return true;
            }, IntPtr.Zero);
            Func<IntPtr, int> rank = h => { int i = recentWindows.IndexOf(h); return i < 0 ? 1000 : i; };
            var described = all.Select(h => Tuple.Create(h, Describe(h))).ToList();
            var ai = described.Where(x => (bool)x.Item2["ai"]).OrderBy(x => rank(x.Item1)).Select(x => x.Item2);
            var other = described.Where(x => !(bool)x.Item2["ai"] && rank(x.Item1) < 1000).OrderBy(x => rank(x.Item1)).Take(6).Select(x => x.Item2);
            return ai.Concat(other).ToList();
        }

        /// <summary>Where the main Send button goes: the most recent AI window, else the last window you used.</summary>
        IntPtr DefaultTarget()
        {
            foreach (var h in recentWindows)
                if (Native.IsWindow(h) && Native.IsWindowVisible(h) && (bool)Describe(h)["ai"]) return h;
            return lastTarget;
        }

        // ---------------- capture: snip ----------------

        public void StartSnipFromUi() { StartSnip(Settings.currentDraft); }

        void StartSnip(string draftId)
        {
            if (!Store.Exists(draftId)) draftId = Settings.currentDraft;
            bool panelVisible = win.Visible;
            snip = new SnipState { DraftId = draftId, Started = DateTime.UtcNow, Until = DateTime.UtcNow.AddSeconds(60), PanelWasVisible = panelVisible };
            if (panelVisible) win.Hide();
            Kbd.WaitModifiersUp(800);
            try
            {
                Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            }
            catch
            {
                Kbd.Chord(Kbd.LWIN, Kbd.SHIFT, 0x53);
            }
            snipTimer.Start();
        }

        void SnipTick()
        {
            if (snip == null) { snipTimer.Stop(); return; }
            var now = DateTime.UtcNow;
            bool overlay = SnipProcesses.Contains(Native.ProcessName(Native.GetForegroundWindow()));
            if (overlay) { snip.SawOverlay = true; snip.OverlayGoneAt = DateTime.MinValue; }
            else if (snip.SawOverlay && snip.OverlayGoneAt == DateTime.MinValue) snip.OverlayGoneAt = now;

            bool cancelled = snip.SawOverlay && snip.OverlayGoneAt != DateTime.MinValue && now - snip.OverlayGoneAt > TimeSpan.FromSeconds(1.5);
            if (cancelled || now > snip.Until) EndSnip();
            else if (!snip.SawOverlay && now - snip.Started > TimeSpan.FromSeconds(4) && snip.PanelWasVisible)
            {
                // Couldn't see the snipping overlay; keep waiting for the image but bring the panel back.
                snip.PanelWasVisible = false;
                ShowPanel();
            }
        }

        void EndSnip()
        {
            snipTimer.Stop();
            var s = snip;
            snip = null;
            if (s != null && s.PanelWasVisible) ShowPanel();
        }

        // ---------------- capture: grab selection ----------------

        void StartGrab()
        {
            var fg = Native.GetForegroundWindow();
            if (!IsExternal(fg))
            {
                Notify("Highlight something in another app, then press " + Settings.hotkeys["grab"] + ".");
                return;
            }
            bool term = IsTerminal(fg);
            Kbd.WaitModifiersUp(1500);
            grab = new GrabState
            {
                DraftId = Settings.currentDraft,
                Source = SourceLabel(fg),
                Seq = Native.GetClipboardSequenceNumber(),
                Until = DateTime.UtcNow.AddMilliseconds(1200),
            };
            // In terminals Ctrl+C would interrupt the running program, so use the terminal copy shortcut.
            if (term) Kbd.Chord(Kbd.CONTROL, Kbd.SHIFT, Kbd.C);
            else Kbd.Chord(Kbd.CONTROL, Kbd.C);
            grabTimer.Start();
        }

        void GrabTick()
        {
            if (grab == null) { grabTimer.Stop(); return; }
            if (Native.GetClipboardSequenceNumber() != grab.Seq) return; // the clipboard handler will finish it
            if (DateTime.UtcNow > grab.Until)
            {
                grabTimer.Stop();
                grab = null;
                Notify("Nothing was selected. Highlight something first, then press " + Settings.hotkeys["grab"] + ".");
            }
        }

        void FinishGrab()
        {
            grabTimer.Stop();
            var g = grab;
            grab = null;
            if (!Store.Exists(g.DraftId)) return;
            var d = Store.Get(g.DraftId);
            string message = AddContent(d, ClipboardService.Read(), g.Source, null);
            Collected(d);
            Notify(message ?? "Nothing to add from that selection.");
        }

        // ---------------- clipboard watching ----------------

        public void OnClipboardUpdate()
        {
            if (ClipboardService.Suppressed) return;
            clipTimer.Stop();
            clipTimer.Start();
        }

        void ProcessClipboard()
        {
            try
            {
                if (ClipboardService.Suppressed) return;
                if (grab != null) { FinishGrab(); return; }
                // Password managers and other private sources mark their clipboard entries; respect that.
                if (ClipboardService.IsFromMonitorExcludedSource()) return;
                if (snip == null && !collect) { OfferImage(); return; }
                var c = ClipboardService.Read();
                if (snip != null)
                {
                    if (c.ImagePng == null) return;
                    string id = snip.DraftId;
                    EndSnip();
                    if (!Store.Exists(id)) return;
                    var d = Store.Get(id);
                    var a = Store.AddImage(d, c.ImagePng, "image/png", Store.ScreenshotName(), "Snip", null);
                    Collected(d);
                    Notify(a == null ? "That screenshot is already in this draft." : "Screenshot added to " + Quote(d.title) + " · " + Plural(d.attachments.Count(x => x.kind == "image"), "image"));
                    return;
                }
                if (collect)
                {
                    var d = Current;
                    string source = "Collected" + (lastTarget != IntPtr.Zero ? " · " + FriendlyApp(lastTarget) : "");
                    if (c.ImagePng != null && (c.Files == null || c.Files.Length == 0))
                    {
                        var a = Store.AddImage(d, c.ImagePng, "image/png", Store.ScreenshotName(), source, null);
                        if (a == null) return;
                        Collected(d);
                        Notify("Collected screenshot into " + Quote(d.title) + " · " + Plural(d.attachments.Count(x => x.kind == "image"), "image"));
                    }
                    else if (Settings.collectText && !string.IsNullOrWhiteSpace(c.Text) && (c.Files == null || c.Files.Length == 0))
                    {
                        var a = Store.AddText(d, c.Text, lastTarget != IntPtr.Zero ? SourceLabel(lastTarget) : "Copied text", c.SourceUrl, null);
                        if (a == null) return;
                        Collected(d);
                        Notify("Collected copied text into " + Quote(d.title));
                    }
                }
            }
            catch (Exception e) { Log.Error(e); }
        }

        void SetCollect(bool on)
        {
            collect = on;
            UpdateTrayText();
            win.Emit("collect", on);
        }

        /// <summary>Adds whatever the clipboard holds. Returns a message for the user.</summary>
        string AddContent(Draft d, ClipContent c, string source, string linkItem)
        {
            if (c.Files != null && c.Files.Length > 0)
            {
                int added = 0, skipped = 0;
                foreach (var f in c.Files)
                {
                    if (Directory.Exists(f)) { skipped++; continue; }
                    if (Store.AddFile(d, f, source ?? "Copied", linkItem) != null) added++;
                    linkItem = null;
                }
                return added > 0 ? "Added " + Plural(added, "file") + " to " + Quote(d.title) + (skipped > 0 ? " (folders are skipped)" : "")
                    : skipped > 0 ? "Folders can't be attached. Copy the files inside instead." : "Those files are already in this draft.";
            }
            if (c.ImagePng != null)
            {
                var a = Store.AddImage(d, c.ImagePng, "image/png", "Copied image " + DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture), source ?? "Clipboard", linkItem);
                return a == null ? "That image is already in this draft." : "Added the image to " + Quote(d.title);
            }
            if (!string.IsNullOrWhiteSpace(c.Text))
            {
                string t = c.Text.Trim();
                bool isUrl = IsUrl(t);
                var a = isUrl ? Store.AddLink(d, t, null, linkItem) : Store.AddText(d, t, source ?? "Clipboard", c.SourceUrl, linkItem);
                return a == null ? "That's already in this draft." : (isUrl ? "Added the link to " : "Added the quote to ") + Quote(d.title);
            }
            return null;
        }

        static bool IsUrl(string t)
        {
            return !t.Contains(" ") && !t.Contains("\n") && t.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.IsWellFormedUriString(t, UriKind.Absolute);
        }

        /// <summary>Things dropped on Chip: files, images, links or text.</summary>
        public void AddFromDrop(IDataObject data, string source)
        {
            try
            {
                var d = Current;
                var c = new ClipContent();
                if (data.GetDataPresent(DataFormats.FileDrop)) c.Files = data.GetData(DataFormats.FileDrop) as string[];
                if ((c.Files == null || c.Files.Length == 0) && data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream ms) c.ImagePng = ms.ToArray();
                if ((c.Files == null || c.Files.Length == 0) && c.ImagePng == null && data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is Bitmap bmp)
                    c.ImagePng = ImageUtil.EncodePng(bmp);
                if (c.Files == null && c.ImagePng == null)
                {
                    string url = data.GetDataPresent("UniformResourceLocatorW") ? ReadUnicodeStream(data.GetData("UniformResourceLocatorW")) : null;
                    string text = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrWhiteSpace(text) && (url == null || text.Trim() != url.Trim())) c.Text = text;
                    else c.Text = url ?? text;
                    if (data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(html, @"^SourceURL:(\S+)", System.Text.RegularExpressions.RegexOptions.Multiline);
                        if (m.Success) c.SourceUrl = m.Groups[1].Value.Trim();
                    }
                }
                string msg = AddContent(d, c, source, null);
                Collected(d);
                if (msg != null) Notify(msg);
            }
            catch (Exception e)
            {
                Log.Error(e);
                Notify(e is InvalidOperationException ? e.Message : "Chip couldn't take that: " + e.Message, true);
            }
        }

        static string ReadUnicodeStream(object o)
        {
            if (o is string s) return s;
            if (o is MemoryStream ms) return Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
            return null;
        }

        // ---------------- browser extension ----------------

        object OnPipe(Dictionary<string, object> req)
        {
            string type = Json.Str(req, "type");
            switch (type)
            {
                case "ping":
                    return new Dictionary<string, object> { { "ok", true }, { "version", Program.Version } };
                case "status":
                {
                    var d = Current;
                    return new Dictionary<string, object>
                    {
                        { "ok", true }, { "title", d.title }, { "count", d.attachments.Count },
                        { "hotkeys", Settings.hotkeys }, { "version", Program.Version },
                    };
                }
                case "open":
                    ShowPanel();
                    return new Dictionary<string, object> { { "ok", true } };
                case "add":
                    return AddFromExtension(Json.Obj(req.ContainsKey("item") ? req["item"] : null));
                case "saveChat":
                {
                    var r = History.SaveFromBrowser(Json.Obj(req.ContainsKey("session") ? req["session"] : null) ?? new Dictionary<string, object>());
                    if (!Settings.usedExtension) { Settings.usedExtension = true; Settings.Save(); }
                    win.Emit("historyChanged", r);
                    string name = History.SourceName((string)r["source"]);
                    Notify("Saved this " + name + " chat · " + Plural((int)r["turns"], "message") + ". Open Past chats to reuse any part of it.");
                    r["ok"] = true;
                    return r;
                }
                default:
                    return new Dictionary<string, object> { { "ok", false }, { "error", "Unknown request" } };
            }
        }

        object AddFromExtension(Dictionary<string, object> item)
        {
            if (item == null) throw new InvalidOperationException("Nothing to add.");
            var d = Current;
            string kind = Json.Str(item, "kind");
            string url = Json.Str(item, "url");
            string title = Json.Str(item, "title");
            string browser = Json.Str(item, "browser") ?? "your browser";
            string source = string.IsNullOrWhiteSpace(title) ? (url ?? browser) : title;
            if (source.Length > 90) source = source.Substring(0, 88) + "…";
            Attachment a;
            string what;
            switch (kind)
            {
                case "text":
                case "page":
                    a = Store.AddText(d, Json.Str(item, "text"), source, url, null);
                    what = kind == "page" ? "the page text" : "the quote";
                    break;
                case "link":
                    a = Store.AddLink(d, url, title, null);
                    what = "the link";
                    break;
                case "image":
                {
                    string data = Json.Str(item, "data") ?? "";
                    int comma = data.IndexOf(',');
                    var bytes = Convert.FromBase64String(comma >= 0 ? data.Substring(comma + 1) : data);
                    string mime = comma > 5 ? data.Substring(5, data.IndexOf(';') - 5) : (Json.Str(item, "mime") ?? "image/png");
                    string name = Json.Str(item, "name") ?? ("Image from " + HostOf(url ?? "the web"));
                    a = Store.AddImage(d, bytes, mime, name, source, null);
                    what = Json.Bool(item, "screenshot") ? "the screenshot" : "the image";
                    break;
                }
                default:
                    throw new InvalidOperationException("Chipmunk doesn't know how to add " + (kind ?? "that") + ".");
            }
            if (!Settings.usedExtension) { Settings.usedExtension = true; Settings.Save(); }
            Collected(d);
            Notify(a == null ? "That's already in " + Quote(d.title) + "." : "Added " + what + " from " + browser + " to " + Quote(d.title));
            return new Dictionary<string, object> { { "ok", true }, { "duplicate", a == null }, { "title", d.title }, { "count", d.attachments.Count } };
        }

        static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.Replace("www.", "") : url;
        }

        // ---------------- sending ----------------

        void PasteHotkey()
        {
            var fg = Native.GetForegroundWindow();
            if (!IsExternal(fg))
            {
                if (PanelIsForeground()) SendFromPanel(Current);
                return;
            }
            var d = Current;
            int missing = d.checklist.Count(c => !c.done);
            if (missing > 0 && !(forceDraft == d.id && DateTime.UtcNow < forceUntil))
            {
                forceDraft = d.id;
                forceUntil = DateTime.UtcNow.AddSeconds(6);
                toast.Show(Plural(missing, "reminder") + " in " + Quote(d.title) + " not ticked off yet. Press " + Settings.hotkeys["paste"] + " again to send anyway.", false, 6000);
                return;
            }
            forceUntil = DateTime.MinValue;
            Kbd.WaitModifiersUp(1500);
            string result = PasteDraft(d, Native.Root(fg));
            if (result != null) toast.Show(result, true, 5000);
        }

        /// <summary>Sends to a chosen window (from the Send menu or the dock), or to the default target.</summary>
        public Dictionary<string, object> SendFromPanel(Draft d, IntPtr chosen = default(IntPtr))
        {
            var target = chosen != IntPtr.Zero && Native.IsWindow(chosen) ? chosen : DefaultTarget();
            if (target == IntPtr.Zero || !Native.IsWindow(target))
                throw new InvalidOperationException("Open the AI app you want to send to, or click into it and press " + Settings.hotkeys["paste"] + ".");
            NoteRecent(target);
            // Switch first, while Windows still lets us hand over focus, then get out of the way.
            Native.Focus(target);
            if (win.Visible) HidePanel();
            string error = PasteDraft(d, target);
            if (error != null) toast.Show(error, true, 5000);
            return new Dictionary<string, object> { { "draft", DraftDto(d) }, { "message", error } };
        }

        public List<Dictionary<string, object>> SendTargets() { return Targets(); }

        public void SendCurrentTo(IntPtr hwnd)
        {
            try { SendFromPanel(Current, hwnd); }
            catch (Exception e) { Notify(e is InvalidOperationException ? e.Message : "Couldn't send: " + e.Message, true); }
        }

        // ---------------- past chats ----------------

        async Task<object> HistoryAddAsync(string source, string sessionId, object[] items, string mode)
        {
            string title = null, url = null;
            var turns = await Task.Run(() =>
            {
                string t, u;
                var r = History.Load(source, sessionId, out t, out u);
                title = t;
                url = u;
                return r;
            });
            string from = History.SourceName(source);
            string cwd = source == "claude-code" ? await Task.Run(() => ClaudeImport.Cwd(sessionId)) : null;
            var d = mode == "new" ? Store.Create(string.IsNullOrWhiteSpace(title) ? "From " + from : title) : Current;
            var prompts = new StringBuilder();
            int added = 0;
            var picks = items.Select(Json.Obj).Where(x => x != null)
                .OrderBy(x => Json.Int(x, "turn")).ThenBy(x => Json.Int(x, "image", -1)).ToList();
            foreach (var p in picks)
            {
                int ti = Json.Int(p, "turn", -1);
                if (ti < 0 || ti >= turns.Count) continue;
                var turn = turns[ti];
                if (Json.Str(p, "kind") == "image")
                {
                    int ii = Json.Int(p, "image", -1);
                    if (ii < 0 || ii >= turn.Images.Count) continue;
                    byte[] bytes;
                    try { bytes = History.ImageBytes(turn.Images[ii]); } catch { continue; }
                    if (Store.AddImage(d, bytes, turn.Images[ii].Mime ?? "image/png", "Image from " + from, from + (title != null ? " · " + title : ""), null) != null) added++;
                }
                else if (turn.Role == "user")
                {
                    // Your own earlier prompts become editable text in the draft. Files a Claude Code
                    // prompt pulled in with @mentions come back as attachments.
                    string text = turn.Text.Trim();
                    if (source == "claude-code")
                    {
                        string rest;
                        foreach (var f in ClaudeImport.ReferencedFiles(text, cwd, out rest))
                            if (Store.AddFile(d, f, from, null) != null) added++;
                        text = rest;
                    }
                    if (text.Length > 0) prompts.Append(prompts.Length > 0 ? "\n\n" : "").Append(text);
                    added++;
                }
                else if (Store.AddText(d, turn.Text, from + " reply" + (title != null ? " · " + title : ""), url, null) != null) added++;
            }
            if (prompts.Length > 0)
            {
                string text = Store.ReadText(d);
                Store.SaveText(d, string.IsNullOrWhiteSpace(text) ? prompts.ToString() : text.TrimEnd() + "\n\n" + prompts);
            }
            if (mode == "new")
            {
                Settings.currentDraft = d.id;
                Settings.Save();
            }
            pet.Catch();
            UpdatePet();
            return new Dictionary<string, object> { { "draft", DraftDto(d, true) }, { "added", added } };
        }

        /// <summary>Imports a ChatGPT or Claude export: the given .zip (dropped on Past chats), or one the user picks.</summary>
        async Task<object> ImportExportAsync(string path)
        {
            if (path != null && !File.Exists(path)) throw new InvalidOperationException("That file isn't there anymore.");
            if (path == null)
                using (var dlg = new OpenFileDialog { Title = "Choose your ChatGPT or Claude data export (.zip)", Filter = "Data export (*.zip)|*.zip" })
                {
                    if (dlg.ShowDialog(win) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
            Notify("Importing your chats. This can take a minute for big exports…");
            var r = await Task.Run(() => History.ImportExport(path, msg => { try { win.BeginInvoke((Action)(() => Notify(msg))); } catch { } }));
            Notify("Imported " + Plural(r.Item2, "chat") + " from " + History.SourceName(r.Item1) + (r.Item3 > 0 ? " with " + Plural(r.Item3, "image") : "") + ".");
            return new Dictionary<string, object> { { "source", r.Item1 }, { "chats", r.Item2 }, { "images", r.Item3 } };
        }

        /// <summary>Pastes the draft into a window. Returns null on success, or an error message.</summary>
        string PasteDraft(Draft d, IntPtr target)
        {
            string text = Store.ReadText(d);
            bool terminal = IsTerminal(target);
            bool asPaths = terminal && Settings.terminalPaths;
            var atts = d.attachments.Where(a => a.include).ToList();
            var job = new Paster.Job
            {
                Target = target,
                Terminal = terminal,
                DelayMs = Settings.pasteDelayMs,
                RestoreClipboard = Settings.restoreClipboard,
                Text = Composer.Build(text, atts, a => Store.FilePath(d, a), asPaths),
            };
            if (!asPaths)
            {
                foreach (var a in atts.Where(a => a.kind == "image"))
                {
                    string p = Store.FilePath(d, a);
                    if (File.Exists(p)) job.Images.Add(ImageUtil.PngForPaste(File.ReadAllBytes(p), Settings.shrinkImages));
                }
                foreach (var a in atts.Where(a => a.kind == "file"))
                {
                    string p = Store.FilePath(d, a);
                    if (File.Exists(p)) job.Files.Add(p);
                }
            }
            if (job.Images.Count == 0 && job.Files.Count == 0 && string.IsNullOrWhiteSpace(job.Text))
                return "This draft is empty, so there's nothing to send.";

            pet.SetMode(2);
            string error;
            try { error = Paster.Run(job); }
            finally { pet.SetMode(0); }
            if (error != null) return error;

            d.status = "sent";
            d.sends.Add(new SendRecord { at = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), target = FriendlyApp(target), items = job.Images.Count + job.Files.Count });
            Store.Touch(d);
            PushDraft(d);
            var parts = new List<string>();
            if (job.Images.Count > 0) parts.Add(Plural(job.Images.Count, "image"));
            if (job.Files.Count > 0) parts.Add(Plural(job.Files.Count, "file"));
            if (!string.IsNullOrWhiteSpace(job.Text)) parts.Add(asPaths && atts.Any(a => a.kind == "image" || a.kind == "file") ? "your text with file paths" : "your text");
            string sent = "Sent " + string.Join(" + ", parts) + " to " + FriendlyApp(target) + ".";
            if (pet.IsShowing) pet.Say(sent, 3000); else toast.Show(sent);
            return null;
        }

        // ---------------- DTOs for the UI ----------------

        string FileUrl(Draft d, Attachment a)
        {
            return "https://" + MainWindow.FilesHost + "/drafts/" + Uri.EscapeDataString(d.id) + "/" + string.Join("/", a.file.Split('/').Select(Uri.EscapeDataString));
        }

        Dictionary<string, object> AttDto(Draft d, Attachment a)
        {
            var x = new Dictionary<string, object>
            {
                { "id", a.id }, { "kind", a.kind }, { "name", a.name }, { "source", a.source }, { "include", a.include },
                { "w", a.w }, { "h", a.h }, { "bytes", a.bytes }, { "ext", a.ext }, { "text", a.text }, { "linked", a.linkPath != null },
                { "url", a.kind == "link" || a.kind == "text" ? a.url : (a.file != null ? FileUrl(d, a) : null) },
            };
            string path = Store.FilePath(d, a);
            if (path != null)
            {
                x["path"] = path;
                if (a.linkPath != null && !File.Exists(path)) x["missing"] = true;
            }
            return x;
        }

        Dictionary<string, object> DraftDto(Draft d, bool textChanged = false)
        {
            return new Dictionary<string, object>
            {
                { "id", d.id }, { "title", d.title }, { "status", d.status }, { "pinned", d.pinned }, { "imported", d.imported },
                { "created", d.created }, { "updated", d.updated }, { "armedItem", d.armedItem }, { "text", Store.ReadText(d) },
                { "folder", Store.Dir(d) }, { "textChanged", textChanged },
                { "checklist", d.checklist.Select(c => new Dictionary<string, object> { { "id", c.id }, { "text", c.text }, { "done", c.done }, { "attId", c.attId } }).ToList() },
                { "attachments", d.attachments.Select(a => AttDto(d, a)).ToList() },
                { "sends", d.sends.Select(s => new Dictionary<string, object> { { "at", s.at }, { "target", s.target } }).ToList() },
            };
        }

        List<Dictionary<string, object>> DraftList()
        {
            return Store.All.OrderByDescending(d => d.updated, StringComparer.Ordinal).Select(d =>
            {
                var images = d.attachments.Where(a => a.kind == "image" && a.file != null).ToList();
                return new Dictionary<string, object>
                {
                    { "id", d.id }, { "title", d.title }, { "status", d.status }, { "pinned", d.pinned }, { "updated", d.updated },
                    { "preview", Store.FirstLine(Store.ReadText(d), 120) }, { "imageCount", images.Count },
                    { "attachmentCount", d.attachments.Count }, { "imported", d.imported },
                    { "thumbs", images.Take(3).Select(a => FileUrl(d, a)).ToList() },
                };
            }).ToList();
        }

        Dictionary<string, object> SettingsDto(Dictionary<string, string> hotkeyErrors = null)
        {
            return new Dictionary<string, object>
            {
                { "hotkeys", Settings.hotkeys }, { "pasteDelayMs", Settings.pasteDelayMs }, { "shrinkImages", Settings.shrinkImages },
                { "terminalPaths", Settings.terminalPaths }, { "restoreClipboard", Settings.restoreClipboard }, { "collectText", Settings.collectText },
                { "alwaysOnTop", Settings.alwaysOnTop }, { "onboarded", Settings.onboarded }, { "theme", Settings.theme },
                { "showPet", Settings.showPet }, { "petHints", Settings.petHints }, { "dictation", DictationDto() },
                { "usedExtension", Settings.usedExtension }, { "dictationApp", RunningDictationApp() },
                { "startWithWindows", Settings.StartWithWindows }, { "dataDir", Store.Root }, { "version", Program.Version },
                { "hotkeyErrors", hotkeyErrors ?? new Dictionary<string, string>() },
            };
        }

        Dictionary<string, object> AgentInfo()
        {
            string exe = Application.ExecutablePath;
            string jsonExe = exe.Replace("\\", "\\\\");
            return new Dictionary<string, object>
            {
                { "exe", exe },
                { "claude", "claude mcp add --scope user chipmunk -- \"" + exe + "\" --mcp" },
                { "codex", "codex mcp add chipmunk -- \"" + exe + "\" --mcp" },
                { "json", "{\n  \"mcpServers\": {\n    \"chipmunk\": {\n      \"command\": \"" + jsonExe + "\",\n      \"args\": [\"--mcp\"]\n    }\n  }\n}" },
            };
        }

        Dictionary<string, object> BrowserInfo()
        {
            string ext = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "extension");
            return new Dictionary<string, object>
            {
                { "folder", ext }, { "exists", Directory.Exists(ext) }, { "id", Bridge.ExtensionId }, { "connected", Settings.usedExtension },
            };
        }

        Dictionary<string, object> StorageInfo()
        {
            long bytes = 0;
            int files = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(Store.Root, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; files++; } catch { }
                }
            }
            catch { }
            int linked = Store.All.Sum(d => d.attachments.Count(a => a.linkPath != null));
            return new Dictionary<string, object> { { "bytes", bytes }, { "files", files }, { "drafts", Store.Count }, { "linked", linked } };
        }

        // ---------------- commands from the UI ----------------

        public object Handle(string cmd, Dictionary<string, object> a, List<string> files)
        {
            var result = HandleCore(cmd, a, files);
            UpdatePet();
            return result;
        }

        object HandleCore(string cmd, Dictionary<string, object> a, List<string> files)
        {
            Draft d = Json.Has(a, "id") && cmd != "undeleteDraft" ? Store.Get(Json.Str(a, "id")) : null;
            switch (cmd)
            {
                case "init":
                    return new Dictionary<string, object>
                    {
                        { "settings", SettingsDto() }, { "drafts", DraftList() }, { "current", DraftDto(Current) },
                        { "target", TargetDto(DefaultTarget()) }, { "collect", collect },
                    };
                case "listDrafts": return DraftList();
                case "getDraft": return DraftDto(d);
                case "setCurrent":
                    Settings.currentDraft = d.id;
                    Settings.Save();
                    return true;
                case "saveText":
                    Store.SaveText(d, Json.Str(a, "text"));
                    return new Dictionary<string, object> { { "updated", d.updated } };
                case "updateDraft": return UpdateDraft(d, Json.Obj(a["patch"]));
                case "armItem":
                    d.armedItem = Json.Str(a, "itemId");
                    Store.SaveMeta(d);
                    return DraftDto(d);
                case "updateAttachment":
                {
                    var att = d.attachments.FirstOrDefault(x => x.id == Json.Str(a, "attId"));
                    var patch = Json.Obj(a["patch"]);
                    if (att != null && Json.Has(patch, "include")) att.include = Json.Bool(patch, "include");
                    if (att != null && Json.Has(patch, "name")) att.name = Json.Str(patch, "name");
                    Store.SaveMeta(d);
                    return DraftDto(d);
                }
                case "removeAttachment":
                    Store.RemoveAttachment(d, Json.Str(a, "attId"));
                    return DraftDto(d);
                case "restoreAttachment":
                    Store.RestoreAttachment(d, Json.Str(a, "attId"));
                    return DraftDto(d);
                case "openAttachment":
                {
                    var att = d.attachments.FirstOrDefault(x => x.id == Json.Str(a, "attId"));
                    if (att == null) return false;
                    string target = att.kind == "link" ? att.url : Store.FilePath(d, att);
                    if (att.kind != "link" && !File.Exists(target)) throw new InvalidOperationException("That file isn't there any more.");
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return true;
                }
                case "revealDraft": OpenFolder(Store.Dir(d)); return true;
                case "openDataFolder": OpenFolder(Store.Root); return true;
                case "snip": StartSnip(d != null ? d.id : Settings.currentDraft); return true;
                case "targets": return Targets();
                case "dictation": return DictationDto();
                case "historySources": return Task.Run(() => (object)History.Summary());
                case "historySessions":
                {
                    string src = Json.Str(a, "source");
                    return Task.Run(() => (object)History.Sessions(src));
                }
                case "historyConversation":
                {
                    string src = Json.Str(a, "source"), sid = Json.Str(a, "sessionId");
                    return Task.Run(() => (object)History.Conversation(src, sid));
                }
                case "historyAdd":
                    return HistoryAddAsync(Json.Str(a, "source"), Json.Str(a, "sessionId"), Json.Arr(a.ContainsKey("items") ? a["items"] : null) ?? new object[0], Json.Str(a, "mode"));
                case "historyImport": return ImportExportAsync(Json.Str(a, "path"));
                case "historyDelete":
                    History.Delete(Json.Str(a, "source"), Json.Str(a, "sessionId"));
                    return true;
                case "pickFiles": return PickFiles(d);
                case "addClipboard":
                {
                    string msg = AddContent(d, ClipboardService.Read(), "Clipboard", null);
                    if (msg != null) Collected(d);
                    return new Dictionary<string, object> { { "draft", DraftDto(d) }, { "message", msg ?? "Your clipboard is empty." } };
                }
                case "addLink":
                    if (Store.AddLink(d, Json.Str(a, "url"), Json.Str(a, "title"), Json.Str(a, "linkItem")) == null) Notify("That link is already in this draft.");
                    else pet.Catch();
                    return DraftDto(d);
                case "addText":
                    if (Store.AddText(d, Json.Str(a, "text"), Json.Str(a, "source"), null, Json.Str(a, "linkItem")) == null) Notify("That text is already in this draft.");
                    else pet.Catch();
                    return DraftDto(d);
                case "addFiles": return AddFiles(d, files, Json.Str(a, "linkItem"));
                case "addData": return AddData(d, Json.Str(a, "name"), Json.Str(a, "data"), Json.Str(a, "linkItem"));
                case "newDraft":
                {
                    var src = Json.Str(a, "duplicateOf");
                    var n = src != null ? Store.Duplicate(Store.Get(src)) : Store.Create("");
                    Settings.currentDraft = n.id;
                    Settings.Save();
                    return DraftDto(n);
                }
                case "deleteDraft":
                {
                    Store.Delete(d);
                    if (Store.Count == 0) Store.Create("");
                    if (!Store.Exists(Settings.currentDraft)) Settings.currentDraft = MostRecent().id;
                    Settings.Save();
                    return new Dictionary<string, object> { { "currentId", Settings.currentDraft } };
                }
                case "undeleteDraft":
                {
                    var r = Store.Undelete(Json.Str(a, "id"));
                    Settings.currentDraft = r.id;
                    Settings.Save();
                    return DraftDto(r);
                }
                case "versions": return Store.Versions(d);
                case "getVersion": return Store.ReadVersion(d, Json.Str(a, "name"));
                case "restoreVersion":
                    Store.RestoreVersion(d, Json.Str(a, "name"));
                    return DraftDto(d, true);
                case "setSettings": return ApplySettings(Json.Obj(a["patch"]));
                case "setCollect": SetCollect(Json.Bool(a, "on")); return true;
                case "send": return Send(d, Json.Str(a, "how"), Json.Str(a, "hwnd"));
                case "agentInfo": return AgentInfo();
                case "browserInfo": return BrowserInfo();
                case "storageInfo": return Task.Run(() => (object)StorageInfo());
                case "copyToClipboard":
                    ClipboardService.SetText(Json.Str(a, "text") ?? "", false);
                    return true;
                case "openExtensionFolder":
                    OpenFolder(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "extension"));
                    return true;
                case "suspendHotkeys":
                    hotkeysSuspended = Json.Bool(a, "on");
                    RegisterHotkeys();
                    return true;
                case "hide": HidePanel(); return true;
                case "flushed": flushed = true; return true;
                default: throw new InvalidOperationException("Unknown command: " + cmd);
            }
        }

        object UpdateDraft(Draft d, Dictionary<string, object> p)
        {
            if (Json.Has(p, "title")) d.title = (Json.Str(p, "title") ?? "").Trim();
            if (Json.Has(p, "pinned")) d.pinned = Json.Bool(p, "pinned");
            if (Json.Has(p, "status")) d.status = Json.Str(p, "status") == "sent" ? "sent" : "draft";
            if (Json.Has(p, "checklist"))
            {
                d.checklist = (Json.Arr(p["checklist"]) ?? new object[0]).Select(Json.Obj).Where(x => x != null).Select(x => new CheckItem
                {
                    id = Json.Str(x, "id") ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                    text = Json.Str(x, "text") ?? "",
                    done = Json.Bool(x, "done"),
                    attId = Json.Str(x, "attId"),
                }).ToList();
                if (d.armedItem != null && !d.checklist.Any(c => c.id == d.armedItem && !c.done)) d.armedItem = null;
            }
            if (Json.Has(p, "order"))
            {
                var ids = (Json.Arr(p["order"]) ?? new object[0]).Select(x => x as string).ToList();
                d.attachments = d.attachments.OrderBy(x => { int i = ids.IndexOf(x.id); return i < 0 ? int.MaxValue : i; }).ToList();
            }
            Store.Touch(d);
            return DraftDto(d);
        }

        object PickFiles(Draft d)
        {
            using (var dlg = new OpenFileDialog { Multiselect = true, Title = "Add files to " + (string.IsNullOrWhiteSpace(d.title) ? "this draft" : d.title) })
            {
                if (dlg.ShowDialog(win) != DialogResult.OK) return null;
                foreach (var f in dlg.FileNames) Store.AddFile(d, f, "Chosen", null);
            }
            Collected(d);
            return DraftDto(d);
        }

        object AddFiles(Draft d, List<string> paths, string linkItem)
        {
            var missing = new List<int>();
            int skipped = 0, added = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string p = paths[i];
                if (string.IsNullOrEmpty(p) || (!File.Exists(p) && !Directory.Exists(p))) { missing.Add(i); continue; }
                if (Directory.Exists(p)) { skipped++; continue; }
                if (Store.AddFile(d, p, "Dropped", linkItem) != null) added++;
                linkItem = null;
            }
            if (skipped > 0) Notify("Folders can't be attached. Drop the files inside instead.");
            if (added > 0) pet.Catch();
            return new Dictionary<string, object> { { "draft", DraftDto(d) }, { "missing", missing } };
        }

        object AddData(Draft d, string name, string dataUrl, string linkItem)
        {
            int comma = dataUrl == null ? -1 : dataUrl.IndexOf(',');
            if (comma < 0) throw new InvalidOperationException("Couldn't read that dropped item.");
            string header = dataUrl.Substring(0, comma);
            byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
            string mediaType = header.Substring(5).Split(';')[0];
            if (mediaType.StartsWith("image/"))
            {
                Store.AddImage(d, bytes, mediaType, string.IsNullOrEmpty(name) ? "Dropped image" : name, "Dropped", linkItem);
            }
            else
            {
                string tmp = Path.Combine(Path.GetTempPath(), "Chipmunk-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                string f = Path.Combine(tmp, string.IsNullOrEmpty(name) ? "dropped-file" : Path.GetFileName(name));
                File.WriteAllBytes(f, bytes);
                Store.AddFile(d, f, "Dropped", linkItem);
                try { Directory.Delete(tmp, true); } catch { }
            }
            pet.Catch();
            return DraftDto(d);
        }

        object Send(Draft d, string how, string hwnd)
        {
            string text = Store.ReadText(d);
            var atts = d.attachments.Where(x => x.include).ToList();
            switch (how)
            {
                case "paste":
                {
                    long h;
                    return SendFromPanel(d, hwnd != null && long.TryParse(hwnd, out h) ? new IntPtr(h) : IntPtr.Zero);
                }
                case "copyText":
                {
                    string t = Composer.Build(text, atts, x => Store.FilePath(d, x), false);
                    if (string.IsNullOrWhiteSpace(t)) throw new InvalidOperationException("There's no text to copy yet.");
                    ClipboardService.SetText(t, false);
                    return new Dictionary<string, object> { { "draft", DraftDto(d) }, { "message", "Copied the text" + (atts.Any(x => x.kind == "text" || x.kind == "link") ? " with your quotes and links" : "") + "." } };
                }
                case "copyFiles":
                {
                    var paths = atts.Where(x => x.kind == "image" || x.kind == "file").Select(x => Store.FilePath(d, x)).Where(File.Exists).ToList();
                    if (paths.Count == 0) throw new InvalidOperationException("There are no images or files to copy.");
                    ClipboardService.SetFiles(paths, false);
                    return new Dictionary<string, object> { { "draft", DraftDto(d) }, { "message", "Copied " + Plural(paths.Count, "file") + ". Paste them into a chat or a folder." } };
                }
                default: throw new InvalidOperationException("Unknown send option.");
            }
        }

        object ApplySettings(Dictionary<string, object> p)
        {
            Dictionary<string, string> errors = null;
            if (Json.Has(p, "hotkeys"))
            {
                var hk = Json.Obj(p["hotkeys"]);
                var before = new Dictionary<string, string>(Settings.hotkeys);
                foreach (var name in HotkeyNames)
                {
                    string v = Json.Str(hk, name);
                    if (!string.IsNullOrWhiteSpace(v)) Settings.hotkeys[name] = v;
                }
                errors = RegisterHotkeys();
                // Keep the old shortcut for anything that couldn't be registered.
                foreach (var name in errors.Keys.ToList())
                    if (Settings.hotkeys[name] != before[name]) Settings.hotkeys[name] = before[name];
                if (errors.Count > 0) RegisterHotkeys();
            }
            if (Json.Has(p, "pasteDelayMs")) Settings.pasteDelayMs = Math.Max(120, Math.Min(3000, Json.Int(p, "pasteDelayMs", 450)));
            if (Json.Has(p, "shrinkImages")) Settings.shrinkImages = Json.Bool(p, "shrinkImages");
            if (Json.Has(p, "terminalPaths")) Settings.terminalPaths = Json.Bool(p, "terminalPaths");
            if (Json.Has(p, "restoreClipboard")) Settings.restoreClipboard = Json.Bool(p, "restoreClipboard");
            if (Json.Has(p, "collectText")) Settings.collectText = Json.Bool(p, "collectText");
            if (Json.Has(p, "onboarded")) Settings.onboarded = Json.Bool(p, "onboarded");
            if (Json.Has(p, "alwaysOnTop")) Settings.alwaysOnTop = Json.Bool(p, "alwaysOnTop");
            if (Json.Has(p, "theme")) Settings.theme = Json.Str(p, "theme") ?? "system";
            if (Json.Has(p, "showPet"))
            {
                Settings.showPet = Json.Bool(p, "showPet");
                pet.SetWanted(Settings.showPet);
            }
            if (Json.Has(p, "petHints")) Settings.petHints = Json.Bool(p, "petHints");
            if (Json.Has(p, "startWithWindows"))
            {
                try { Settings.StartWithWindows = Json.Bool(p, "startWithWindows"); }
                catch (Exception e) { Log.Error(e); }
            }
            Settings.Save();
            win.ApplyTheme();
            UpdateTrayText();
            return SettingsDto(errors);
        }

        static void OpenFolder(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch (Exception e) { Log.Error(e); }
        }

        public void Quit()
        {
            try
            {
                if (win.IsReady)
                {
                    flushed = false;
                    win.Emit("flush", null);
                    var sw = Stopwatch.StartNew();
                    while (!flushed && sw.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(10); }
                }
            }
            catch { }
            for (int i = 0; i < HotkeyNames.Length; i++) Native.UnregisterHotKey(messages.Handle, i + 1);
            messages.Dispose();
            pipe.Stop();
            fgTimer.Stop();
            pet.Hide();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            win.AllowClose = true;
            win.Close();
            Context.ExitThread();
        }
    }
}
