using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Chipmunk
{
    static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int SW_RESTORE = 9;
        public const int SW_SHOWNOACTIVATE = 4;
        public const uint GA_ROOTOWNER = 3;

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1;
        const uint KEYEVENTF_KEYUP = 0x2;

        public static void Key(ushort vk, bool up)
        {
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            var input = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        static bool IsExtended(ushort vk)
        {
            return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C || vk == 0xA3 || vk == 0xA5;
        }

        public static string WindowTitle(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string WindowClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr Root(IntPtr h)
        {
            var r = GetAncestor(h, GA_ROOTOWNER);
            return r == IntPtr.Zero ? h : r;
        }

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        static readonly System.Collections.Generic.Dictionary<uint, Tuple<string, DateTime>> procNames = new System.Collections.Generic.Dictionary<uint, Tuple<string, DateTime>>();

        /// <summary>The window's process name without ".exe". Cheap: one handle, cached for a minute.</summary>
        public static string ProcessName(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return "";
            var now = DateTime.UtcNow;
            lock (procNames)
            {
                Tuple<string, DateTime> hit;
                if (procNames.TryGetValue(pid, out hit) && now - hit.Item2 < TimeSpan.FromMinutes(1)) return hit.Item1;
            }
            string name = "";
            IntPtr hp = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (hp != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    if (QueryFullProcessImageName(hp, 0, sb, ref len)) name = Path.GetFileNameWithoutExtension(sb.ToString());
                }
                finally { CloseHandle(hp); }
            }
            lock (procNames)
            {
                if (procNames.Count > 400) procNames.Clear();
                procNames[pid] = Tuple.Create(name, now);
            }
            return name;
        }

        public static uint ProcessId(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        public static string DocumentsFolder()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public static string DownloadsFolder()
        {
            IntPtr p;
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
            {
                string s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>Bring a window to the front, working around Windows' focus-stealing rules.</summary>
        public static bool Focus(IntPtr h)
        {
            if (!IsWindow(h)) return false;
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt == 0)
                {
                    SetForegroundWindow(h);
                }
                else
                {
                    uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                    uint me = GetCurrentThreadId();
                    AttachThreadInput(me, fgThread, true);
                    SetForegroundWindow(h);
                    BringWindowToTop(h);
                    AttachThreadInput(me, fgThread, false);
                }
                for (int i = 0; i < 12; i++)
                {
                    if (Root(GetForegroundWindow()) == Root(h)) return true;
                    Thread.Sleep(25);
                }
            }
            return false;
        }

        public static void SetDarkTitleBar(IntPtr h, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(h, 20, ref v, 4);
            }
            catch { }
        }
    }

    /// <summary>Synthetic keyboard input for pasting into other apps.</summary>
    static class Kbd
    {
        public const ushort SHIFT = 0x10, CONTROL = 0x11, ALT = 0x12, LWIN = 0x5B, RWIN = 0x5C, INSERT = 0x2D, C = 0x43, V = 0x56;
        static readonly ushort[] Modifiers = { SHIFT, CONTROL, ALT, LWIN, RWIN };

        public static bool AnyModifierDown()
        {
            foreach (var m in Modifiers)
                if ((Native.GetAsyncKeyState(m) & 0x8000) != 0) return true;
            return false;
        }

        /// <summary>Waits for the user to let go of the hotkey's modifiers, so a synthetic
        /// Ctrl+V isn't seen as Ctrl+Alt+V.</summary>
        public static void WaitModifiersUp(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (AnyModifierDown() && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(15);
            bool winDown = (Native.GetAsyncKeyState(LWIN) & 0x8000) != 0 || (Native.GetAsyncKeyState(RWIN) & 0x8000) != 0;
            // Releasing Win on its own would open the Start menu; a no-op key in between prevents that.
            if (winDown) { Native.Key(0xE8, false); Native.Key(0xE8, true); }
            foreach (var m in Modifiers)
                if ((Native.GetAsyncKeyState(m) & 0x8000) != 0) Native.Key(m, true);
        }

        public static void Chord(params ushort[] keys)
        {
            foreach (var k in keys) Native.Key(k, false);
            for (int i = keys.Length - 1; i >= 0; i--) Native.Key(keys[i], true);
        }
    }

    static class Log
    {
        static readonly object Gate = new object();
        public static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chipmunk", "log.txt");

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Error(Exception e) { Write("ERROR", e == null ? "(null)" : e.ToString()); }
        public static void Error(string msg) { Write("ERROR", msg); }

        static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
