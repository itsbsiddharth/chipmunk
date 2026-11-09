using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Chipmunk
{
    /// <summary>The panel window: hosts the HTML UI in WebView2 and relays its messages to AppCore.</summary>
    class MainWindow : Form
    {
        public const string AppHost = "app.chipmunk.local";
        public const string FilesHost = "files.chipmunk.local";

        readonly AppCore core;
        readonly WebView2 web;
        readonly Timer boundsTimer = new Timer { Interval = 600 };
        bool ready;
        public bool AllowClose;

        public MainWindow(AppCore core)
        {
            this.core = core;
            Text = "Chipmunk";
            Icon = core.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = false;
            bool dark = core.Settings.DarkMode;
            BackColor = dark ? Color.FromArgb(20, 20, 23) : Color.FromArgb(246, 246, 248);
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(web);
            PlaceWindow();
            boundsTimer.Tick += (s, e) => { boundsTimer.Stop(); SaveBounds(); };
            Move += (s, e) => { if (Visible) { boundsTimer.Stop(); boundsTimer.Start(); } };
            Resize += (s, e) => { if (Visible) { boundsTimer.Stop(); boundsTimer.Start(); } };
        }

        public bool IsReady { get { return ready; } }

        void PlaceWindow()
        {
            var b = core.Settings.bounds;
            var wa = Screen.PrimaryScreen.WorkingArea;
            float scale = DpiScale();
            MinimumSize = new Size((int)(380 * scale), (int)(480 * scale));
            if (b != null && b.Length == 4)
            {
                var r = new Rectangle(b[0], b[1], b[2], b[3]);
                foreach (var s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(r) && r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height)
                    {
                        Bounds = r;
                        return;
                    }
                }
            }
            int w = (int)(470 * scale), h = Math.Min((int)(800 * scale), wa.Height - (int)(48 * scale));
            Bounds = new Rectangle(wa.Right - w - (int)(24 * scale), wa.Top + (wa.Height - h) / 2, w, h);
        }

        static float DpiScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            core.Settings.bounds = new[] { Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height };
            core.Settings.Save();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            bool dark = core.Settings.DarkMode;
            Native.SetDarkTitleBar(Handle, dark);
            BackColor = dark ? Color.FromArgb(20, 20, 23) : Color.FromArgb(246, 246, 248);
            if (web != null) web.DefaultBackgroundColor = BackColor;
            TopMost = core.Settings.alwaysOnTop;
        }

        public async Task InitAsync(string uiDir)
        {
            string udf = Path.Combine(Settings.LocalDir, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, udf, new CoreWebView2EnvironmentOptions());
            await web.EnsureCoreWebView2Async(env);
            var cw = web.CoreWebView2;
            var s = cw.Settings;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            s.AreDevToolsEnabled = core.DevMode;
            s.IsReputationCheckingRequired = false; // only local pages are ever shown; no SmartScreen lookups
            cw.SetVirtualHostNameToFolderMapping(AppHost, uiDir, CoreWebView2HostResourceAccessKind.Allow);
            cw.SetVirtualHostNameToFolderMapping(FilesHost, core.Store.Root, CoreWebView2HostResourceAccessKind.Allow);
            cw.WebMessageReceived += OnMessage;
            cw.NewWindowRequested += (o, e) => { e.Handled = true; OpenExternal(e.Uri); };
            cw.NavigationStarting += (o, e) =>
            {
                if (!e.Uri.StartsWith("https://" + AppHost + "/", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    OpenExternal(e.Uri);
                }
            };
            cw.NavigationCompleted += (o, e) =>
            {
                ready = e.IsSuccess;
                if (ready && Visible) FocusPage();
            };
            cw.ProcessFailed += (o, e) =>
            {
                Log.Error("WebView2 process failed: " + e.ProcessFailedKind);
                ready = false;
                try { cw.Reload(); } catch { }
            };
            cw.Navigate("https://" + AppHost + "/index.html");
        }

        static void OpenExternal(string uri)
        {
            if (uri != null && (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
            }
        }

        async void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = Json.Obj(Json.Parse(e.WebMessageAsJson)); }
            catch { return; }
            if (msg == null) return;
            object id = msg.ContainsKey("id") ? msg["id"] : null;
            string cmd = Json.Str(msg, "cmd");
            var args = Json.Obj(msg.ContainsKey("args") ? msg["args"] : null) ?? new Dictionary<string, object>();

            // Files dropped onto the page arrive as CoreWebView2File objects that carry their real path.
            var files = new List<string>();
            try
            {
                var extra = e.AdditionalObjects;
                if (extra != null)
                    foreach (var o in extra) files.Add((o as CoreWebView2File)?.Path);
            }
            catch { }

            object result = null;
            string error = null;
            try
            {
                result = core.Handle(cmd, args, files);
                if (result is Task<object> task) result = await task;
            }
            catch (Exception ex)
            {
                if (ex is AggregateException ag && ag.InnerException != null) ex = ag.InnerException;
                error = ex is InvalidOperationException ? ex.Message : "Something went wrong: " + ex.Message;
                Log.Error(cmd + ": " + ex);
            }
            Post(new Dictionary<string, object> { { "replyTo", id }, { "ok", error == null }, { "result", result }, { "error", error } });
        }

        /// <summary>Gives the page keyboard focus so typing goes straight into the prompt.</summary>
        public void FocusPage()
        {
            try { web.Focus(); } catch { }
            Emit("shown", null);
        }

        public void Emit(string evt, object data)
        {
            if (!ready) return;
            Post(new Dictionary<string, object> { { "event", evt }, { "data", data } });
        }

        void Post(object payload)
        {
            try { web.CoreWebView2?.PostWebMessageAsJson(Json.Serialize(payload)); }
            catch (Exception ex) { Log.Error(ex); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                core.HidePanel();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
