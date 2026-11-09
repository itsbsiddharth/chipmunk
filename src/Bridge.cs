using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Chipmunk
{
    /// <summary>
    /// How the browser extension reaches the app: the browser starts Chipmunk.exe as a native
    /// messaging host, which forwards each message over a private named pipe to the running app.
    /// </summary>
    static class Bridge
    {
        public const string HostName = "com.chipmunk.app";
        public const string ExtensionId = "fcgflimapmefbojakginmcacgegeonii";

        public static string PipeName
        {
            get
            {
                string who = Environment.UserDomainName + "\\" + Environment.UserName + "|" + (Settings.ProfileDir ?? "");
                using (var sha = SHA1.Create())
                    return "chipmunk-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(who.ToLowerInvariant()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        // ---------------- registration ----------------

        static readonly string[] BrowserKeys =
        {
            @"Software\Google\Chrome\NativeMessagingHosts\",
            @"Software\Microsoft\Edge\NativeMessagingHosts\",
            @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\",
            @"Software\Chromium\NativeMessagingHosts\",
            @"Software\Vivaldi\NativeMessagingHosts\",
        };

        /// <summary>Registers Chipmunk as a native messaging host for Chromium browsers (current user only).</summary>
        public static void Register()
        {
            try
            {
                string dir = Settings.LocalDir;
                Directory.CreateDirectory(dir);
                string manifest = Path.Combine(dir, HostName + ".json");
                var json = Json.Serialize(new Dictionary<string, object>
                {
                    { "name", HostName },
                    { "description", "Chipmunk desktop app" },
                    { "path", Application.ExecutablePath },
                    { "type", "stdio" },
                    { "allowed_origins", new[] { "chrome-extension://" + ExtensionId + "/" } },
                });
                if (!File.Exists(manifest) || File.ReadAllText(manifest) != json) Json.WriteAtomic(manifest, json);
                foreach (var k in BrowserKeys)
                    using (var key = Registry.CurrentUser.CreateSubKey(k + HostName))
                        if (!Equals(key.GetValue(null), manifest)) key.SetValue(null, manifest);
            }
            catch (Exception e) { Log.Error("Bridge.Register: " + e.Message); }
        }

        public static void Unregister()
        {
            foreach (var k in BrowserKeys)
                try { Registry.CurrentUser.DeleteSubKeyTree(k + HostName, false); } catch { }
        }

        // ---------------- native messaging host (runs in its own short-lived process) ----------------

        public static int RunNativeHost()
        {
            var input = Console.OpenStandardInput();
            var output = Console.OpenStandardOutput();
            var lenBuf = new byte[4];
            while (true)
            {
                if (!ReadExactly(input, lenBuf, 4)) return 0;
                int len = BitConverter.ToInt32(lenBuf, 0);
                if (len <= 0 || len > 256 * 1024 * 1024) return 1;
                var body = new byte[len];
                if (!ReadExactly(input, body, len)) return 0;
                string reply;
                try { reply = Send(Encoding.UTF8.GetString(body), true); }
                catch (Exception e) { reply = Json.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", e.Message } }); }
                var outBytes = Encoding.UTF8.GetBytes(reply);
                output.Write(BitConverter.GetBytes(outBytes.Length), 0, 4);
                output.Write(outBytes, 0, outBytes.Length);
                output.Flush();
            }
        }

        static bool ReadExactly(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        /// <summary>Sends one JSON request to the running app and returns its JSON reply,
        /// starting the app first if it isn't running.</summary>
        public static string Send(string requestJson, bool launchIfNeeded)
        {
            var deadline = DateTime.UtcNow.AddSeconds(12);
            bool launched = false;
            while (true)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                    {
                        client.Connect(500);
                        var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                        w.WriteLine(requestJson);
                        var r = new StreamReader(client, Encoding.UTF8);
                        string line = r.ReadLine();
                        return line ?? "{\"ok\":false,\"error\":\"Chipmunk closed the connection.\"}";
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { }
                if (!launchIfNeeded || DateTime.UtcNow > deadline)
                    return "{\"ok\":false,\"error\":\"Chipmunk isn't running. Open it and try again.\"}";
                if (!launched)
                {
                    launched = true;
                    LaunchDetached(Application.ExecutablePath, "--background");
                }
                Thread.Sleep(400);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        /// <summary>Starts the app so it outlives this host process, which the browser may run inside a job.</summary>
        static void LaunchDetached(string exe, string args)
        {
            const uint BREAKAWAY = 0x01000000, DETACHED = 0x00000008, NEW_GROUP = 0x00000200;
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)) };
            PROCESS_INFORMATION pi;
            var cmd = new StringBuilder("\"" + exe + "\" " + args);
            if (CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, BREAKAWAY | DETACHED | NEW_GROUP, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out pi))
            {
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
                return;
            }
            // The job doesn't allow breaking away: ask Explorer to start it instead.
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false }); }
            catch (Win32Exception e) { Log.Error("Launch from host failed: " + e.Message); }
        }
    }

    /// <summary>Listens on a named pipe that only the current Windows user can open.</summary>
    class PipeServer
    {
        readonly Control invoker;
        readonly Func<Dictionary<string, object>, object> handler;
        volatile bool stopped;

        public PipeServer(Control invoker, Func<Dictionary<string, object>, object> handler)
        {
            this.invoker = invoker;
            this.handler = handler;
        }

        public void Start()
        {
            new Thread(Loop) { IsBackground = true, Name = "Chipmunk pipe" }.Start();
        }

        public void Stop() { stopped = true; }

        NamedPipeServerStream Create()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(Bridge.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 1 << 16, 1 << 16, security);
        }

        void Loop()
        {
            while (!stopped)
            {
                try
                {
                    using (var server = Create())
                    {
                        server.WaitForConnection();
                        var reader = new StreamReader(server, Encoding.UTF8);
                        string line = reader.ReadLine();
                        object reply;
                        try
                        {
                            var req = Json.Obj(Json.Parse(line ?? "{}")) ?? new Dictionary<string, object>();
                            reply = invoker.Invoke(handler, req);
                        }
                        catch (Exception e)
                        {
                            if (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
                            reply = new Dictionary<string, object> { { "ok", false }, { "error", e.Message } };
                        }
                        var w = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
                        w.WriteLine(Json.Serialize(reply));
                        try { server.WaitForPipeDrain(); } catch { }
                    }
                }
                catch (Exception e)
                {
                    if (stopped) return;
                    Log.Error("Pipe: " + e.Message);
                    Thread.Sleep(300);
                }
            }
        }
    }
}
