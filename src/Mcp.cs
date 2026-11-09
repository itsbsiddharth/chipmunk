using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chipmunk
{
    /// <summary>
    /// "Chipmunk.exe --mcp": a Model Context Protocol server over stdio, so coding agents
    /// (Claude Code, Codex, Cursor, Claude Desktop) can read your draft directly, screenshots and
    /// all, instead of you pasting it. Read-only; it reads the same files the app writes.
    /// </summary>
    static class McpServer
    {
        static readonly string[] Versions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
        const int MaxImages = 8;

        public static int Run()
        {
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = Console.OpenStandardOutput();
            string line;
            while ((line = input.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                Dictionary<string, object> msg;
                try { msg = Json.Obj(Json.Parse(line)); }
                catch
                {
                    Write(output, Error(null, -32700, "Parse error"));
                    continue;
                }
                if (msg == null || !msg.ContainsKey("id")) continue; // notifications need no reply
                object id = msg["id"];
                string method = Json.Str(msg, "method");
                var p = Json.Obj(msg.ContainsKey("params") ? msg["params"] : null) ?? new Dictionary<string, object>();
                try
                {
                    Write(output, new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", Dispatch(method, p) } });
                }
                catch (MissingMethodException)
                {
                    Write(output, Error(id, -32601, "Method not found: " + method));
                }
                catch (Exception e)
                {
                    Log.Error("MCP " + method + ": " + e);
                    Write(output, Error(id, -32603, e.Message));
                }
            }
            return 0;
        }

        static void Write(Stream output, object payload)
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(payload) + "\n");
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }

        static Dictionary<string, object> Error(object id, int code, string message)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" }, { "id", id },
                { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } },
            };
        }

        static object Dispatch(string method, Dictionary<string, object> p)
        {
            switch (method)
            {
                case "initialize":
                {
                    string asked = Json.Str(p, "protocolVersion");
                    return new Dictionary<string, object>
                    {
                        { "protocolVersion", Versions.Contains(asked) ? asked : Versions[1] },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object> { { "listChanged", false } } } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", "chipmunk" }, { "title", "Chipmunk" }, { "version", Program.Version } } },
                        { "instructions", "Chipmunk is where the user collects context for their prompts: text they wrote or dictated, screenshots, files, quotes and links. When the user mentions their draft, Chipmunk, or things they collected or screenshotted, call get_draft." },
                    };
                }
                case "ping": return new Dictionary<string, object>();
                case "tools/list": return new Dictionary<string, object> { { "tools", Tools() } };
                case "resources/list": return new Dictionary<string, object> { { "resources", new object[0] } };
                case "prompts/list": return new Dictionary<string, object> { { "prompts", new object[0] } };
                case "tools/call": return Call(Json.Str(p, "name"), Json.Obj(p.ContainsKey("arguments") ? p["arguments"] : null) ?? new Dictionary<string, object>());
                default: throw new MissingMethodException();
            }
        }

        static object[] Tools()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    { "name", "get_draft" },
                    { "title", "Get Chipmunk draft" },
                    { "description", "Get the user's current Chipmunk draft: the prompt they wrote or dictated plus the screenshots (returned as images), files (as paths you can read), quotes and links they collected for it. Call this when the user says things like \"use my draft\", \"check Chipmunk\", or refers to screenshots or context they gathered." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "draft_id", new Dictionary<string, object> { { "type", "string" }, { "description", "A specific draft from list_drafts. Leave out for the draft the user has open." } } },
                                    { "include_images", new Dictionary<string, object> { { "type", "boolean" }, { "description", "Return screenshots as images (default true)." } } },
                                }
                            },
                        }
                    },
                },
                new Dictionary<string, object>
                {
                    { "name", "list_drafts" },
                    { "title", "List Chipmunk drafts" },
                    { "description", "List the user's recent Chipmunk drafts (title, id, when it changed, what's attached), newest first." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object> { { "limit", new Dictionary<string, object> { { "type", "integer" }, { "description", "How many drafts (default 10)." } } } } },
                        }
                    },
                },
            };
        }

        static object Text(string s, bool isError = false)
        {
            var r = new Dictionary<string, object> { { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", s } } } } };
            if (isError) r["isError"] = true;
            return r;
        }

        static object Call(string name, Dictionary<string, object> args)
        {
            var settings = Settings.Load();
            var store = new Store(settings.dataDir);
            if (store.Count == 0) return Text("The user has no Chipmunk drafts yet.");
            if (name == "list_drafts")
            {
                int limit = Math.Max(1, Math.Min(50, Json.Int(args, "limit", 10)));
                var sb = new StringBuilder("Chipmunk drafts, newest first:\n");
                foreach (var d in store.All.OrderByDescending(x => x.updated, StringComparer.Ordinal).Take(limit))
                {
                    int images = d.attachments.Count(a => a.kind == "image");
                    sb.Append("\n- ").Append(string.IsNullOrWhiteSpace(d.title) ? "Untitled draft" : d.title)
                      .Append(d.id == settings.currentDraft ? " (open now)" : "")
                      .Append("\n  id: ").Append(d.id)
                      .Append(", changed ").Append(Ago(d.updated))
                      .Append(", ").Append(images).Append(" image(s), ").Append(d.attachments.Count - images).Append(" other attachment(s)");
                    string first = Store.FirstLine(store.ReadText(d), 100);
                    if (first.Length > 0) sb.Append("\n  starts: ").Append(first);
                }
                return Text(sb.ToString());
            }
            if (name != "get_draft") return Text("Unknown tool: " + name, true);

            string id = Json.Str(args, "draft_id") ?? settings.currentDraft;
            if (!store.Exists(id)) id = store.All.OrderByDescending(x => x.updated, StringComparer.Ordinal).First().id;
            var draft = store.Get(id);
            bool withImages = !args.ContainsKey("include_images") || Json.Bool(args, "include_images");
            var atts = draft.attachments.Where(a => a.include).ToList();

            var text = new StringBuilder();
            text.Append("# ").Append(string.IsNullOrWhiteSpace(draft.title) ? "Chipmunk draft" : draft.title).Append("\n\n");
            string body = Composer.Build(store.ReadText(draft), atts, a => store.FilePath(draft, a), false);
            text.Append(body.Length > 0 ? body : "(The draft has no text yet.)");
            var files = atts.Where(a => a.kind == "image" || a.kind == "file").ToList();
            if (files.Count > 0)
            {
                text.Append("\n\n## Attached files\n");
                foreach (var f in files)
                    text.Append("- ").Append(store.FilePath(draft, f)).Append(f.kind == "image" ? (withImages ? " (screenshot, shown below)" : " (image)") : "").Append('\n');
            }
            var open = draft.checklist.Where(c => !c.done).Select(c => c.text).ToList();
            if (open.Count > 0) text.Append("\nThe user still meant to add: ").Append(string.Join("; ", open)).Append('\n');

            var content = new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", text.ToString().Trim() } } };
            if (withImages)
            {
                foreach (var img in files.Where(a => a.kind == "image").Take(MaxImages))
                {
                    try
                    {
                        string path = store.FilePath(draft, img);
                        if (!File.Exists(path)) continue;
                        var png = ImageUtil.PngForPaste(File.ReadAllBytes(path), true, 1568);
                        if (png.Length > 4 * 1024 * 1024) continue;
                        content.Add(new Dictionary<string, object> { { "type", "image" }, { "data", Convert.ToBase64String(png) }, { "mimeType", "image/png" } });
                    }
                    catch (Exception e) { Log.Error("MCP image: " + e.Message); }
                }
            }
            return new Dictionary<string, object> { { "content", content } };
        }

        static string Ago(string iso)
        {
            DateTime t;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return "some time ago";
            var span = DateTime.UtcNow - t;
            if (span.TotalMinutes < 2) return "just now";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " minutes ago";
            if (span.TotalDays < 1) return (int)span.TotalHours + " hours ago";
            return (int)span.TotalDays + " days ago";
        }
    }
}
