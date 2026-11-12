using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Chipmunk
{
    static class Program
    {
        public const string Version = "1.2.0";

        [STAThread]
        static int Main(string[] args)
        {
            int pi = Array.IndexOf(args, "--profile");
            if (pi >= 0 && pi + 1 < args.Length) Settings.ProfileDir = Path.GetFullPath(args[pi + 1]);
            if (Settings.ProfileDir == null) Settings.ProfileDir = Environment.GetEnvironmentVariable("CHIPMUNK_PROFILE");

            // Started by a browser as a native messaging host (the first argument is the extension's origin).
            if (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                return Bridge.RunNativeHost();
            // Started by an AI agent (Claude Code, Codex, Cursor...) as an MCP server.
            if (args.Contains("--mcp"))
                return McpServer.Run();

            if (args.Length >= 2 && args[0] == "--write-icon")
            {
                Brand.WriteIco(args[1]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--render-art")
            {
                Brand.RenderSheet(args[1]);
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--write-png")
            {
                Brand.WritePng(args[1], int.Parse(args[2]));
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--write-mascot")
            {
                Brand.WriteMascotPng(args[1], int.Parse(args[2]), args[3] == "holding");
                return 0;
            }

            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && args[0] == "--harness")
            {
                Application.Run(new Harness(args[1]));
                return 0;
            }

            string suffix = Settings.ProfileDir == null ? "" : "." + Math.Abs(Settings.ProfileDir.ToLowerInvariant().GetHashCode());
            bool created;
            using (var mutex = new Mutex(true, @"Local\Chipmunk.SingleInstance" + suffix, out created))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Chipmunk.Show" + suffix))
            {
                if (!created)
                {
                    // Already running: let that instance take focus and show its panel.
                    Native.AllowSetForegroundWindow(-1);
                    showEvent.Set();
                    return 0;
                }
                Application.ThreadException += (s, e) => Log.Error(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error(e.ExceptionObject as Exception);
                Log.Info("Starting Chipmunk " + Version + " " + string.Join(" ", args));
                var core = new AppCore(showEvent, args);
                Application.Run(core.Context);
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
