<p align="center">
  <img src="docs/img/hero.png" alt="Chipmunk: give your AI the whole picture" width="100%">
</p>

<h1 align="center">Chipmunk</h1>

<p align="center">
  <b>Gather context from anywhere. Hand it to any AI in one click.</b><br>
  Snip, grab and drop from any app or website, reuse pieces of past chats, and send every screenshot,<br>
  file and word to ChatGPT, Claude, Codex, Antigravity, Cursor or Claude Code at once.
</p>

<p align="center">
  <a href="https://github.com/itsbsiddharth/chipmunk/releases/latest"><b>Download for Windows</b></a> ·
  <a href="#install">Install</a> ·
  <a href="#how-it-works">How it works</a> ·
  <a href="#past-chats">Past chats</a> ·
  <a href="#use-it-with-coding-agents">Coding agents</a> ·
  <a href="#faq">FAQ</a>
</p>

---

## The problem

Your AI is only as good as the context you give it, and giving it context is still copy-paste chaos:

- You take four screenshots, find the error message, copy a paragraph from the docs and park your prompt in Notepad so the chat box can't eat it.
- Then you attach everything one by one, switching back and forth between apps.
- Next week you want the same prompt and screenshots in another chat, and you dig through old conversations to rebuild it.

**Chipmunk fixes that.** It collects everything into one draft as you work, lets you pull in pieces of any past chat, and hands the whole bundle to the AI you're using in one click.

## How it works

**1. Collect, from anywhere**

| | |
|---|---|
| **Snip** | `Win+Alt+S` snips part of your screen straight into the draft. No desktop full of PNGs. |
| **Grab** | Highlight text in any app and press `Win+Alt+C`. It's saved as a quote that remembers where it came from. |
| **Web** | Right-click any selection, image, link or page and choose **Add to Chipmunk**, or screenshot a whole tab. Works in Chrome, Edge and Brave. |
| **Past chats** | Pick prompts, replies and images from earlier Claude Code, Codex, Antigravity, ChatGPT, Claude or Gemini chats. [More below](#past-chats). |
| **Chip** | Drop files, images, links or text on the chipmunk at the edge of your screen. |
| **Collect mode** | Flip it on and every screenshot you take the usual way (Win+Shift+S, Print Screen, Ditto) is caught automatically. |

**2. Shape it**

- Type, or dictate with the app you already use: **Wispr Flow, Superwhisper, Aqua or Windows voice typing (`Win+H`)** all type straight into the draft. Chipmunk spots which one is running and shows its shortcut.
- The editor **saves on every keystroke**, keeps a version every minute, and deleted drafts go to a trash you can undo.
- Keep a **To attach** list: write "screenshot of the error" and the next matching capture ticks it off. Chipmunk warns you before you send with something missing.
- Reorder attachments, or leave one out without deleting it. You see roughly how many tokens it all costs.

**3. Send, without the back-and-forth**

- **Send** goes to the AI app you used last. The arrow next to it lists every open AI app and window (ChatGPT, Claude, Codex, Antigravity, Cursor, Claude Code in a terminal, a ChatGPT tab in Chrome) so you can pick another one. Chipmunk switches there and pastes every screenshot, file and word, then puts your clipboard back.
- Already in the app? Press **`Win+Alt+V`** and it all pastes right where you are.
- In a terminal running Claude Code, files go in as `@"path"` references, which Claude Code reads directly.
- Or let your coding agent **read the draft itself** ([see below](#use-it-with-coding-agents)).

<p align="center">
  <img src="docs/img/panel-light.png" alt="Chipmunk in light mode" width="32%">
  &nbsp;
  <img src="docs/img/send-menu.png" alt="Pick any open AI app to send to" width="32%">
  &nbsp;
  <img src="docs/img/panel-dark.png" alt="Chipmunk in dark mode" width="32%">
</p>

## Meet Chip

Chip is the chipmunk that sits at the edge of your screen. It's there to save you trips back to the panel:

- **Click Chip to send.** It lists your open AI apps; pick one and your draft goes in.
- **It speaks up when it can help.** Switch to ChatGPT with a draft ready and Chip offers to paste it ("5 items ready for ChatGPT. Click me to paste it in"). Take a screenshot and it offers to keep it. One click, or ignore it and the bubble fades.
- **It tells you what happened** ("Sent 2 images + your text to Claude") instead of popping notifications.
- **Drop anything on it** to collect it. The badge shows how much is in your draft.

Chip hides during fullscreen games, videos and presentations, and uses almost no CPU while idle. Don't want it? Turn off **Settings → Chip**, or just its suggestions.

## Past chats

You've already written great prompts and collected the right screenshots in earlier chats. Chipmunk lets you reuse any part of them:

<p align="center">
  <img src="docs/img/past-chats.png" alt="Pick prompts, replies and images from a past chat" width="40%">
</p>

- **Claude Code, Codex and Antigravity** are read straight from their files on your PC. Nothing to set up.
- **ChatGPT, Claude and Gemini**: open a chat in your browser, click the Chipmunk extension and choose **Save this chat**. Every prompt, reply and image is kept. Or import everything at once from the data export ChatGPT or Claude emails you (`.zip`).
- Open a chat, tick the prompts, replies and images you want (from one chat or several), then **Add to draft** or start a **New draft**. Your prompts come back as editable text, replies as quotes, images as attachments. Claude Code prompts also bring back the files they mentioned with `@`.

Using the **ChatGPT desktop app**? It keeps your chats online, not on your PC, so open the same chat on chatgpt.com (they're synced) and save it there. For single screenshots or text from the desktop app, Snip and Grab work in any app.

## Use cases

- **Report a bug to Claude Code, Codex or Cursor.** Snip the broken screen, grab the error from the console, drop in the CSS file and say what you expected. Click Send and pick the agent.
- **Take a conversation to another AI.** Pull your best prompt and the screenshots from a ChatGPT chat, add Claude's answer from another chat, and send it all to Antigravity.
- **Research with ChatGPT or Gemini.** Right-click the paragraphs that matter on three sites. Each quote keeps its source link, so the AI can cite it.
- **Design feedback.** Collect screenshots of the current UI and the reference you love, then dictate what should change.
- **Write long prompts without fear.** Your draft lives on disk, not in a fragile chat box.

## Use it with coding agents

Chipmunk includes an [MCP](https://modelcontextprotocol.io) server, so agents can read your draft directly: text, screenshots as images, and file paths. Nothing needs pasting.

```bash
# Claude Code
claude mcp add --scope user chipmunk -- "C:\path\to\Chipmunk\Chipmunk.exe" --mcp

# Codex
codex mcp add chipmunk -- "C:\path\to\Chipmunk\Chipmunk.exe" --mcp
```

For Antigravity, Cursor, Claude Desktop and other MCP apps, add this to their MCP config:

```json
{ "mcpServers": { "chipmunk": { "command": "C:\\path\\to\\Chipmunk\\Chipmunk.exe", "args": ["--mcp"] } } }
```

Then just say **"use my Chipmunk draft"**. **Settings → AI agents** shows these with your real path, ready to copy.

## Install

1. Download **`Chipmunk-win-x64.zip`** from the [latest release](https://github.com/itsbsiddharth/chipmunk/releases/latest) and unzip it anywhere, for example `C:\Apps\Chipmunk`.
2. Run **`Chipmunk.exe`**. Windows may say it "protected your PC", because the app isn't code-signed yet. Click **More info → Run anyway**.
3. Chip appears in the corner and a short welcome shows the shortcuts. That's it.

**Browser extension (optional, 30 seconds)**

1. Open `chrome://extensions` (or `edge://extensions`) and turn on **Developer mode**.
2. Click **Load unpacked** and pick the `extension` folder next to `Chipmunk.exe`.
3. Pin Chipmunk to your toolbar. It connects to the app by itself.

Needs Windows 10 or 11 with the Microsoft Edge WebView2 Runtime, which is built into Windows 11.

## Shortcuts

| Shortcut | Does |
|---|---|
| `Win+Alt+P` | Open or hide Chipmunk |
| `Win+Alt+S` | Snip part of the screen into the draft |
| `Win+Alt+C` | Grab the selection from any app |
| `Win+Alt+V` | Send the draft to the app you're in |
| `Ctrl+Enter` / `Ctrl+Shift+H` | In the panel: send / open Past chats |
| `Alt+Shift+A` / `Alt+Shift+S` | In the browser: add the selection / screenshot the tab |

All shortcuts use `Win+Alt`, so they never clash with AltGr characters on international keyboards. If one is taken, Chipmunk picks a free alternative and tells you. Change any of them in **Settings**.

## Privacy

- Everything stays on your PC. Chipmunk makes no network requests and has no account, telemetry or cloud.
- Drafts are plain folders in `Documents\Chipmunk`: `prompt.md`, your files and a `draft.json`. You own them, and OneDrive or Dropbox can back them up.
- Past chats reads Claude Code, Codex and Antigravity history from their files on your PC and never changes them. Browser chats are saved only when you click **Save this chat**.
- Clipboard entries that apps mark as private, such as passwords from a password manager, are never collected.
- Chipmunk's own clipboard use is hidden from Ditto and Windows clipboard history.
- The browser extension only talks to the Chipmunk app on your PC.

## FAQ

**Does it work with Wispr Flow?** Yes. Click in the prompt and use Wispr Flow as you always do; it types into the draft. The same goes for Superwhisper, Aqua and Windows voice typing.

**Can I get chats from the ChatGPT desktop app?** The app stores chats in your OpenAI account, not on your PC. Open the chat on chatgpt.com and click **Save this chat**, or import your ChatGPT data export to bring in every chat at once.

**Can I reuse just one prompt and a few images from an old chat?** Yes, that's what Past chats is for. Tick exactly what you want, even from different chats, and add it to your draft.

**Does it upload my screenshots anywhere?** No. They only go to the AI app you paste into, the same as if you pasted them yourself.

**Do files get duplicated on disk?** Screenshots and small files are copied into the draft, so it still works if you move or delete the originals. Files over 25 MB are linked instead of copied.

**Mac?** Not yet. Chipmunk is Windows-first because that's where tools like this are missing.

## Build from source

```powershell
.\build.cmd        # needs the C# compiler from the free "Build Tools for Visual Studio"
```

The app ends up in `dist\`. To work on the interface in a normal browser with a stand-in for the Windows side, run `python tools\devserver.py` and open http://localhost:5178.

```
src\         C# app: window, tray, shortcuts, clipboard, paste engine, storage, Chip, past chats, MCP server, browser bridge
ui\          the panel (HTML, CSS, JavaScript) rendered by WebView2
extension\   the Chrome / Edge / Brave extension
docs\        images for this README
```

## Roadmap

- Paste straight into web chats from the extension
- Search across every past chat at once
- macOS
- Shared drafts for teams

## License

MIT. Built with Microsoft WebView2 (see `lib/WebView2-LICENSE.txt`).
