using System;
using System.IO;

namespace Chipmunk
{
    /// <summary>Where and how Chipmunk installs. Shared by the app and Chipmunk-Setup.exe.</summary>
    static class InstallInfo
    {
        public const string Product = "Chipmunk";
        public const string Publisher = "Siddharth (bsid.dev)";
        public const string Website = "https://chipmunk.bsid.dev";
        public const string Repo = "itsbsiddharth/chipmunk";
        public const string SetupAsset = "Chipmunk-Setup.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Chipmunk";
        public const string Marker = "install.json";
        public const string QuitEvent = @"Local\Chipmunk.Quit";

        /// <summary>Per-user install, like Slack, Discord or VS Code: no admin rights needed.</summary>
        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Chipmunk"); }
        }

        public static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Chipmunk.lnk"); }
        }

        public static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Chipmunk.lnk"); }
        }
    }
}
