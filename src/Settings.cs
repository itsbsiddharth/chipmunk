using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Chipmunk
{
    static class Json
    {
        static JavaScriptSerializer Make()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        }

        public static string Serialize(object o) { return Make().Serialize(o); }
        public static object Parse(string s) { return Make().DeserializeObject(s); }
        public static T Deserialize<T>(string s) { return Make().Deserialize<T>(s); }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static object[] Arr(object o)
        {
            if (o is object[] a) return a;
            if (o is ArrayList al) return al.ToArray();
            return null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v as string ?? Convert.ToString(v) : null;
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool b && b;
        }

        public static bool Has(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key);
        }

        public static int Int(Dictionary<string, object> d, string key, int fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        /// <summary>Write via a temp file and swap it in, so a crash mid-write never leaves a half file.</summary>
        public static void WriteAtomic(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }

    public class Settings
    {
        public Dictionary<string, string> hotkeys = DefaultHotkeys();
        public int pasteDelayMs = 450;
        public bool shrinkImages = true;
        public bool terminalPaths = true;
        public bool restoreClipboard = true;
        public bool collectText = false;
        public bool alwaysOnTop = false;
        public bool onboarded = false;
        public string theme = "system";
        public string dataDir;
        public string currentDraft;
        public int[] bounds;
        public bool showPet = true;
        public int[] petPos;
        public bool petHints = true;              // Chip offers to send drafts and keep screenshots
        public bool usedExtension;

        /// <summary>Win+Alt shortcuts: they never collide with AltGr (Ctrl+Alt) on international keyboards.</summary>
        public static Dictionary<string, string> DefaultHotkeys()
        {
            return new Dictionary<string, string>
            {
                { "toggle", "Win+Alt+P" },
                { "snip", "Win+Alt+S" },
                { "grab", "Win+Alt+C" },
                { "paste", "Win+Alt+V" },
            };
        }

        /// <summary>Set by --profile to keep settings and data in one folder (portable mode, tests).</summary>
        public static string ProfileDir;

        public static string AppDataDir
        {
            get { return ProfileDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chipmunk"); }
        }

        public static string LocalDir
        {
            get { return ProfileDir != null ? Path.Combine(ProfileDir, "local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chipmunk"); }
        }

        static string FilePath { get { return Path.Combine(AppDataDir, "settings.json"); } }

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath)) s = Json.Deserialize<Settings>(File.ReadAllText(FilePath));
            }
            catch (Exception e) { Log.Error(e); }
            s = s ?? new Settings();
            var defaults = DefaultHotkeys();
            if (s.hotkeys == null) s.hotkeys = defaults;
            foreach (var kv in defaults)
                if (!s.hotkeys.ContainsKey(kv.Key)) s.hotkeys[kv.Key] = kv.Value;
            if (string.IsNullOrEmpty(s.dataDir))
            {
                s.dataDir = ProfileDir != null ? Path.Combine(ProfileDir, "data") : Path.Combine(Native.DocumentsFolder(), "Chipmunk");
                MigrateFromPromptpad(s.dataDir);
            }
            s.hotkeys.Remove("talk"); // Talk was removed in 1.1: dictation apps already type into Chipmunk
            s.pasteDelayMs = Math.Max(120, Math.Min(3000, s.pasteDelayMs));
            return s;
        }

        /// <summary>Chipmunk was briefly called Promptpad; carry those drafts over once.</summary>
        static void MigrateFromPromptpad(string dataDir)
        {
            if (ProfileDir != null) return;
            try
            {
                string old = Path.Combine(Native.DocumentsFolder(), "Promptpad");
                if (Directory.Exists(old) && !Directory.Exists(dataDir))
                {
                    Directory.Move(old, dataDir);
                    Log.Info("Moved drafts from " + old + " to " + dataDir);
                }
            }
            catch (Exception e) { Log.Error("Migration: " + e.Message); }
        }

        public void Save()
        {
            try { Json.WriteAtomic(FilePath, Json.Serialize(this)); }
            catch (Exception e) { Log.Error(e); }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("Chipmunk") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("Chipmunk", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --background");
                    else k.DeleteValue("Chipmunk", false);
                }
            }
        }

        public static bool SystemUsesDarkTheme()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && k.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public bool DarkMode
        {
            get { return theme == "dark" || (theme != "light" && SystemUsesDarkTheme()); }
        }
    }

    /// <summary>Parses "Ctrl+Alt+P" style shortcuts into RegisterHotKey arguments.</summary>
    struct Hotkey
    {
        public uint Mods;
        public uint Vk;

        static readonly Dictionary<string, uint> Named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "Space", 0x20 }, { "Enter", 0x0D }, { "Tab", 0x09 }, { "Backspace", 0x08 }, { "Delete", 0x2E }, { "Insert", 0x2D },
            { "Home", 0x24 }, { "End", 0x23 }, { "PageUp", 0x21 }, { "PageDown", 0x22 },
            { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 },
            { "Backquote", 0xC0 }, { "Comma", 0xBC }, { "Period", 0xBE }, { "Slash", 0xBF }, { "Semicolon", 0xBA },
            { "Quote", 0xDE }, { "OpenBracket", 0xDB }, { "CloseBracket", 0xDD }, { "Backslash", 0xDC }, { "Minus", 0xBD }, { "Plus", 0xBB },
        };

        public static bool TryParse(string text, out Hotkey hk)
        {
            hk = new Hotkey();
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var raw in text.Split('+'))
            {
                var p = raw.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) hk.Mods |= 0x2;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) hk.Mods |= 0x1;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) hk.Mods |= 0x4;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) hk.Mods |= 0x8;
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) hk.Vk = char.ToUpperInvariant(p[0]);
                else if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f') && int.TryParse(p.Substring(1), out int f) && f >= 1 && f <= 24) hk.Vk = (uint)(0x70 + f - 1);
                else if (Named.TryGetValue(p, out uint vk)) hk.Vk = vk;
                else return false;
            }
            return hk.Vk != 0 && (hk.Mods & 0xB) != 0;
        }
    }
}
