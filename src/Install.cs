using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Chipmunk
{
    /// <summary>What the app does about being installed: uninstalling itself, and keeping
    /// "Start with Windows" pointed at the copy that's actually running.</summary>
    static class Installation
    {
        public static string Dir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }

        /// <summary>True when this copy was put here by Chipmunk-Setup.exe (and so can update and uninstall itself).</summary>
        public static bool IsInstalled { get { return File.Exists(Path.Combine(Dir, InstallInfo.Marker)); } }

        /// <summary>If Chipmunk moved (say, from a downloaded folder into its install folder),
        /// point the Windows startup entry at the copy that's running now.</summary>
        public static void RepairStartup()
        {
            if (Settings.ProfileDir != null) return;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    var v = k == null ? null : k.GetValue("Chipmunk") as string;
                    if (v == null) return;
                    string want = "\"" + Application.ExecutablePath + "\" --background";
                    if (!string.Equals(v, want, StringComparison.OrdinalIgnoreCase)) k.SetValue("Chipmunk", want);
                }
            }
            catch (Exception e) { Log.Error("RepairStartup: " + e.Message); }
        }

        /// <summary>Removes Chipmunk from this PC. Drafts in Documents\Chipmunk are kept.</summary>
        public static int Uninstall(bool quiet)
        {
            if (!quiet)
            {
                var answer = MessageBox.Show(
                    "Remove Chipmunk from this PC?\n\nYour drafts stay in Documents\\Chipmunk, so nothing you wrote is lost.",
                    "Uninstall Chipmunk", MessageBoxButtons.OKCancel, MessageBoxIcon.None, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.OK) return 1;
            }
            StopRunning(Process.GetCurrentProcess().Id);
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) if (k != null) k.DeleteValue("Chipmunk", false); } catch { }
            try { Bridge.Unregister(); } catch { }
            foreach (var lnk in new[] { InstallInfo.StartMenuShortcut, InstallInfo.DesktopShortcut })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.UninstallKey, false); } catch { }

            // Only ever delete a folder that Setup created, never a folder someone unzipped by hand.
            if (IsInstalled)
            {
                string dir = Dir;
                var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
                };
                try { Process.Start(psi); } catch (Exception e) { Log.Error("Uninstall cleanup: " + e.Message); }
            }
            if (!quiet) MessageBox.Show("Chipmunk was removed. Thanks for trying it.", "Chipmunk", MessageBoxButtons.OK, MessageBoxIcon.None);
            return 0;
        }

        /// <summary>Asks a running Chipmunk to quit (it saves first), then makes sure it's gone.</summary>
        public static void StopRunning(int exceptPid)
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(InstallInfo.QuitEvent, out ev)) using (ev) ev.Set();
            }
            catch { }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000 && Others(exceptPid).Any()) Thread.Sleep(150);
            foreach (var p in Others(exceptPid)) try { p.Kill(); p.WaitForExit(2000); } catch { }
        }

        static IEnumerable<Process> Others(int exceptPid)
        {
            return Process.GetProcessesByName("Chipmunk").Where(p => p.Id != exceptPid);
        }
    }

    /// <summary>Checks GitHub once in a while for a newer Chipmunk and installs it on request.
    /// This is the only network request Chipmunk makes, and it can be turned off in Settings.</summary>
    class Updater
    {
        public string Latest, SetupUrl, PageUrl;
        public bool Available { get { return Latest != null; } }
        public bool CanInstall { get { return Installation.IsInstalled && SetupUrl != null; } }
        public event Action Found;

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        /// <summary>Returns true if a newer version exists.</summary>
        public async Task<bool> Check()
        {
            string json = await Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + InstallInfo.Repo + "/releases/latest");
                req.UserAgent = "Chipmunk/" + Program.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                using (var res = req.GetResponse())
                using (var r = new StreamReader(res.GetResponseStream())) return r.ReadToEnd();
            });
            var o = Json.Obj(Json.Parse(json));
            string tag = (Json.Str(o, "tag_name") ?? "").TrimStart('v', 'V');
            Version remote, local;
            if (!Version.TryParse(tag, out remote) || !Version.TryParse(Program.Version, out local) || remote <= local) return false;
            Latest = tag;
            PageUrl = Json.Str(o, "html_url");
            SetupUrl = null;
            foreach (var a in (Json.Arr(o.ContainsKey("assets") ? o["assets"] : null) ?? new object[0]).Select(Json.Obj))
                if (string.Equals(Json.Str(a, "name"), InstallInfo.SetupAsset, StringComparison.OrdinalIgnoreCase)) SetupUrl = Json.Str(a, "browser_download_url");
            Found?.Invoke();
            return true;
        }

        /// <summary>Downloads the new installer and starts it. The caller quits right after.</summary>
        public async Task<string> DownloadAndRun()
        {
            if (!CanInstall) throw new InvalidOperationException("Download the new version from the website.");
            string path = Path.Combine(Path.GetTempPath(), "Chipmunk-Setup-" + Latest + ".exe");
            await Task.Run(() =>
            {
                var req = (HttpWebRequest)WebRequest.Create(SetupUrl);
                req.UserAgent = "Chipmunk/" + Program.Version;
                req.Timeout = 60000;
                using (var res = req.GetResponse())
                using (var s = res.GetResponseStream())
                using (var f = File.Create(path)) s.CopyTo(f);
            });
            if (new FileInfo(path).Length < 100 * 1024) throw new InvalidOperationException("The download didn't finish. Try again in a moment.");
            Process.Start(new ProcessStartInfo(path, "--update") { UseShellExecute = true });
            return path;
        }
    }
}
