using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Chipmunk
{
    /// <summary>
    /// Test target used during development (Chipmunk.exe --harness log.jsonl): a Chromium page
    /// like ChatGPT or Claude that records every paste it receives, so pasting can be checked end to end.
    /// </summary>
    class Harness : Form
    {
        const string Page = @"<!doctype html><html><head><title>Chipmunk paste harness</title></head>
<body style='font:14px Segoe UI;margin:12px'>
<div contenteditable='true' id='box' style='border:1px solid #888;min-height:200px;padding:8px'></div>
<ol id='log'></ol>
<script>
const box = document.getElementById('box');
box.focus();
document.addEventListener('paste', e => {
  const dt = e.clipboardData;
  const files = [...dt.files].map(f => ({ name: f.name, type: f.type, size: f.size }));
  const text = dt.getData('text/plain');
  const entry = { types: [...dt.types], files, text };
  chrome.webview.postMessage(entry);
  const li = document.createElement('li');
  li.textContent = JSON.stringify(entry).slice(0, 300);
  document.getElementById('log').append(li);
  if (files.length) e.preventDefault();
});
</script></body></html>";

        readonly string logPath;
        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill };

        public Harness(string logPath)
        {
            this.logPath = logPath;
            Text = "Chipmunk paste harness";
            Width = 700;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            Controls.Add(web);
            Load += async (s, e) =>
            {
                string udf = Path.Combine(Path.GetTempPath(), "Chipmunk-harness-webview");
                var env = await CoreWebView2Environment.CreateAsync(null, udf, null);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.WebMessageReceived += (o, m) => File.AppendAllText(logPath, m.WebMessageAsJson + Environment.NewLine);
                web.CoreWebView2.NavigationCompleted += (o, n) =>
                {
                    Activate();
                    Native.Focus(Handle);
                    web.Focus();
                    File.AppendAllText(logPath, "{\"ready\":true}" + Environment.NewLine);
                };
                web.CoreWebView2.NavigateToString(Page);
            };
        }
    }
}
