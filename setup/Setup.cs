using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Chipmunk Setup")]
[assembly: AssemblyProduct("Chipmunk")]
[assembly: AssemblyCompany("Siddharth (bsid.dev)")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(Chipmunk.Setup.BuildInfo.Version)]
[assembly: AssemblyFileVersion(Chipmunk.Setup.BuildInfo.Version)]

namespace Chipmunk.Setup
{
    /// <summary>
    /// Chipmunk-Setup.exe: one file that installs Chipmunk for the current user, with no admin
    /// prompt and nothing to click. It unpacks the app into %LOCALAPPDATA%\Programs\Chipmunk,
    /// adds Start menu and desktop shortcuts and an entry in Settings > Apps, then opens Chipmunk.
    /// Running it again (or from the in-app updater) updates in place.
    /// </summary>
    static class SetupProgram
    {
        public const string Version = BuildInfo.Version;

        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var o = new Options
            {
                Dir = Arg(args, "--dir") ?? InstallInfo.DefaultDir,
                Update = args.Contains("--update"),
                NoLaunch = args.Contains("--no-launch"),
                NoShortcuts = args.Contains("--no-shortcuts"),
                Quiet = args.Contains("--quiet"),
            };
            if (o.Quiet)
            {
                try { new Installer(o).Run(null); return 0; }
                catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
            }
            var w = new SetupWindow(o);
            Application.Run(w);
            return w.Failed ? 1 : 0;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null;
        }
    }

    class Options
    {
        public string Dir;
        public bool Update, NoLaunch, NoShortcuts, Quiet;
    }

    class Installer
    {
        readonly Options o;
        public bool HadWebView2 = true;
        public Installer(Options o) { this.o = o; }

        string Exe { get { return Path.Combine(o.Dir, "Chipmunk.exe"); } }

        public void Run(Action<double, string> progress)
        {
            Action<double, string> p = progress ?? ((a, b) => { });
            bool firstInstall = !File.Exists(Path.Combine(o.Dir, InstallInfo.Marker));

            p(0.05, "Closing Chipmunk…");
            StopRunning();

            p(0.15, "Copying files…");
            Directory.CreateDirectory(o.Dir);
            foreach (var sub in new[] { "ui", "extension" })
            {
                string d = Path.Combine(o.Dir, sub);
                if (Directory.Exists(d)) Retry(() => Directory.Delete(d, true));
            }
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Chipmunk.Payload.zip"))
            {
                if (s == null) throw new InvalidOperationException("This installer is incomplete. Download it again from chipmunk.bsid.dev.");
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                    for (int i = 0; i < files.Count; i++)
                    {
                        var e = files[i];
                        string target = Path.GetFullPath(Path.Combine(o.Dir, e.FullName));
                        if (!target.StartsWith(Path.GetFullPath(o.Dir), StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        Retry(() => e.ExtractToFile(target, true));
                        p(0.15 + 0.7 * (i + 1) / files.Count, "Copying files…");
                    }
                }
            }
            File.WriteAllText(Path.Combine(o.Dir, InstallInfo.Marker),
                "{\"version\":\"" + SetupProgram.Version + "\",\"installed\":\"" + DateTime.UtcNow.ToString("o") + "\"}");

            p(0.88, "Adding shortcuts…");
            if (!o.NoShortcuts)
            {
                Shortcut(InstallInfo.StartMenuShortcut);
                // Only on the first install: if you deleted the desktop icon, updates won't bring it back.
                if (firstInstall) Shortcut(InstallInfo.DesktopShortcut);
                RegisterUninstall();
            }
            HadWebView2 = WebView2Installed();
            p(0.97, o.Update ? "Opening Chipmunk…" : "Opening Chipmunk…");
            if (!o.NoLaunch)
                Process.Start(new ProcessStartInfo(Exe, o.Update ? "--background --updated" : "") { UseShellExecute = true, WorkingDirectory = o.Dir });
            p(1, "Done");
        }

        /// <summary>Asks a running Chipmunk to quit (it saves first), then makes sure it's gone.</summary>
        void StopRunning()
        {
            if (Running().Length == 0) return;
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(InstallInfo.QuitEvent, out ev)) using (ev) ev.Set();
            }
            catch { }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000 && Running().Length > 0) Thread.Sleep(150);
            // Older versions don't listen for the quit signal; drafts save as you type, so this is safe.
            foreach (var proc in Running()) try { proc.Kill(); proc.WaitForExit(3000); } catch { }
        }

        /// <summary>The copies to close: for a normal install every Chipmunk (an older copy elsewhere
        /// would otherwise keep running and own the shortcuts), for a custom folder only that folder's.</summary>
        Process[] Running()
        {
            bool normal = string.Equals(Path.GetFullPath(o.Dir).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(InstallInfo.DefaultDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            return Process.GetProcessesByName("Chipmunk").Where(p =>
            {
                if (normal) return true;
                try { return string.Equals(Path.GetDirectoryName(p.MainModule.FileName), Path.GetFullPath(o.Dir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).ToArray();
        }

        static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (IOException) when (i < 20) { Thread.Sleep(150); }
                catch (UnauthorizedAccessException) when (i < 20) { Thread.Sleep(150); }
            }
        }

        void Shortcut(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = Exe;
                lnk.WorkingDirectory = o.Dir;
                lnk.IconLocation = Exe + ",0";
                lnk.Description = "Collect screenshots, files and notes, then send them to any AI";
                lnk.Save();
                Marshal.FinalReleaseComObject(lnk);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception e) { Log.Error("Shortcut " + path + ": " + e.Message); }
        }

        void RegisterUninstall()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(InstallInfo.UninstallKey))
            {
                k.SetValue("DisplayName", InstallInfo.Product);
                k.SetValue("DisplayVersion", SetupProgram.Version);
                k.SetValue("Publisher", InstallInfo.Publisher);
                k.SetValue("DisplayIcon", Exe + ",0");
                k.SetValue("InstallLocation", o.Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + Exe + "\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", InstallInfo.Website);
                k.SetValue("HelpLink", "https://github.com/" + InstallInfo.Repo);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = 0;
                try { kb = new DirectoryInfo(o.Dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024; } catch { }
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
            }
        }

        /// <summary>Chipmunk draws its panel with Microsoft's WebView2 runtime, built into Windows 11
        /// and installed with Edge on almost every Windows 10 PC.</summary>
        static bool WebView2Installed()
        {
            const string id = @"\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            foreach (var key in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node" + id, @"HKEY_LOCAL_MACHINE\SOFTWARE" + id, @"HKEY_CURRENT_USER\Software" + id })
            {
                try
                {
                    var v = Registry.GetValue(key, "pv", null) as string;
                    if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
                }
                catch { }
            }
            return false;
        }
    }

    /// <summary>The small window you see while Chipmunk installs: Chip, one line of text and a progress bar.</summary>
    class SetupWindow : Form
    {
        readonly Options o;
        readonly UiKit.Palette pal;
        readonly float scale;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 16 };
        readonly DateTime started = DateTime.UtcNow;
        double progress, shown;
        string title, status;
        string error;
        bool done, needsWebView2;
        public bool Failed { get { return error != null; } }
        Rectangle closeBox, button;
        bool hoverClose, hoverButton;
        Point dragFrom;

        public SetupWindow(Options o)
        {
            this.o = o;
            pal = SystemDark() ? UiKit.Dark : UiKit.Light;
            scale = UiKit.ScaleAt(Cursor.Position);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            Text = o.Update ? "Updating Chipmunk" : "Installing Chipmunk";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = pal.Bg;
            title = o.Update ? "Updating Chipmunk" : "Installing Chipmunk";
            status = "Just a few seconds";
            var size = new Size((int)(420 * scale), (int)(300 * scale));
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Top + (wa.Height - size.Height) / 2, size.Width, size.Height);
            anim.Tick += (s, e) => { shown += (progress - shown) * 0.18; Invalidate(); };
        }

        static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && k.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UiKit.Elevate(Handle, pal);
            Native.SetDarkTitleBar(Handle, pal == UiKit.Dark);
            anim.Start();
            Start();
        }

        void Start()
        {
            error = null;
            done = false;
            progress = 0;
            Task.Run(() =>
            {
                var inst = new Installer(o);
                try
                {
                    inst.Run((v, msg) => BeginInvoke((Action)(() => { progress = v; status = msg; })));
                    BeginInvoke((Action)(() => Finish(inst)));
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    BeginInvoke((Action)(() =>
                    {
                        error = ex.Message;
                        title = "Couldn't finish installing";
                        status = ex.Message;
                        Invalidate();
                    }));
                }
            });
        }

        void Finish(Installer inst)
        {
            done = true;
            progress = 1;
            needsWebView2 = !inst.HadWebView2;
            if (needsWebView2)
            {
                title = "One more thing";
                status = "Chipmunk needs Microsoft's free WebView2 runtime. It takes a minute to install.";
                Invalidate();
                return;
            }
            title = o.Update ? "Chipmunk is up to date" : "Chipmunk is ready";
            status = o.Update ? "Back to work." : "Look for Chip in the corner of your screen.";
            var close = new System.Windows.Forms.Timer { Interval = 1600 };
            close.Tick += (s, e) => { close.Stop(); Close(); };
            close.Start();
        }

        float S(float v) { return v * scale; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(pal.Bg);

            // soft warm glow behind Chip
            var glow = new RectangleF(Width / 2f - S(110), S(6), S(220), S(170));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(glow);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(pal == UiKit.Dark ? 60 : 255, pal.AccentSoft), SurroundColors = new[] { Color.FromArgb(0, pal.Bg) } })
                    g.FillPath(b, path);
            }

            double secs = (DateTime.UtcNow - started).TotalSeconds;
            float bob = done ? 0 : (float)Math.Sin(secs * 2 * Math.PI / 1.1) * S(2.5f);
            var pose = new MascotPose { Holding = true, Puff = (float)Math.Min(1, shown), Mode = done ? 3 : 2, Phase = (float)(secs / 0.9 % 1) };
            float m = S(104);
            Mascot.Draw(g, new RectangleF((Width - m) / 2, S(26) - bob, m, m), pose);

            using (var ft = UiKit.Font(S(19), true))
            using (var fs = UiKit.Font(S(13)))
            {
                TextRenderer.DrawText(g, title, ft, new Rectangle(0, (int)S(142), Width, (int)S(28)), pal.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, status, fs, new Rectangle((int)S(36), (int)S(172), Width - (int)S(72), (int)S(40)), error != null ? pal.Danger : pal.Ink3,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            if (error != null || needsWebView2)
            {
                string label = needsWebView2 ? "Get WebView2" : "Try again";
                using (var fb = UiKit.Font(S(13), true))
                {
                    var sz = TextRenderer.MeasureText(label, fb);
                    button = new Rectangle((Width - sz.Width - (int)S(36)) / 2, (int)S(228), sz.Width + (int)S(36), (int)S(36));
                    using (var path = UiKit.Round(button, S(9)))
                    using (var b = new SolidBrush(hoverButton ? pal.Accent : pal.Ink)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, label, fb, button, pal.Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                button = Rectangle.Empty;
                var track = new RectangleF(Width / 2f - S(110), S(238), S(220), S(5));
                using (var path = UiKit.Round(track, S(2.5f)))
                using (var b = new SolidBrush(pal.Line)) g.FillPath(b, path);
                var fill = new RectangleF(track.X, track.Y, Math.Max(S(5), track.Width * (float)shown), track.Height);
                using (var path = UiKit.Round(fill, S(2.5f)))
                using (var b = new SolidBrush(pal.Accent)) g.FillPath(b, path);
            }

            closeBox = new Rectangle(Width - (int)S(40), (int)S(10), (int)S(30), (int)S(30));
            if (hoverClose) using (var path = UiKit.Round(closeBox, S(8))) using (var b = new SolidBrush(pal.Hover)) g.FillPath(b, path);
            using (var p = new Pen(pal.Ink3, S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float c = S(5), cx = closeBox.X + closeBox.Width / 2f, cy = closeBox.Y + closeBox.Height / 2f;
                g.DrawLine(p, cx - c, cy - c, cx + c, cy + c);
                g.DrawLine(p, cx - c, cy + c, cx + c, cy - c);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragFrom != Point.Empty) { Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); return; }
            bool hc = closeBox.Contains(e.Location), hb = button.Contains(e.Location);
            if (hc != hoverClose || hb != hoverButton) { hoverClose = hc; hoverButton = hb; Invalidate(); }
            Cursor = hc || hb ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !closeBox.Contains(e.Location) && !button.Contains(e.Location)) dragFrom = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragFrom = Point.Empty;
            if (closeBox.Contains(e.Location)) { Close(); return; }
            if (!button.Contains(e.Location)) return;
            if (needsWebView2)
            {
                try { Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true }); } catch { }
                Close();
            }
            else if (error != null)
            {
                title = o.Update ? "Updating Chipmunk" : "Installing Chipmunk";
                status = "Just a few seconds";
                Start();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && (done || error != null)) Close();
        }
    }
}
