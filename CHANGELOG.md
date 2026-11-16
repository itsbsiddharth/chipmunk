# Changelog

## Unreleased

- The welcome screen's headline is now "Give your AI the whole picture.", matching the website.
- In dark mode, the Send button is a softer warm gray instead of near-white, so it stays clear without being the brightest thing on screen.

## 1.4.0 (2026-11-16)

- Numbered screenshots with notes. Every screenshot in a draft shows a number (1, 2, 3), and each screenshot or file has a small "Add a note for the AI" field. When you send, images paste first, in order, so the AI app numbers them Image 1, 2, 3 (Claude Code shows `[Image #1]`). Your notes go with them as "Image 2: your note". You can also write "in image 2…" in the prompt.
- Personal screenshots stay personal. Screenshots go into the draft by themselves only while you're working on it: the panel is in front, or you used it in the last few minutes. A panel that's just open behind other windows no longer grabs them. Otherwise Chip asks "Add this screenshot to your draft?" (one click). If Chip is hidden and the panel is open, the panel shows the same question.
- "Keep collecting when hidden" is now "Collect every screenshot". When on, every screenshot from any app goes in, Chip shows a small glowing dot, the tray tooltip says so, and it turns itself off when you send.
- Every screenshot that lands shows a one-line confirmation ("Image 3 added to …") with **Add a note** and **Undo**.
- Windows still saves every screenshot to `Pictures\Screenshots`. Chipmunk only copies from the clipboard, so nothing is taken out of your Screenshots folder.
- Chip changes: click Chip to open your draft (it used to open a menu). Right-click Chip for the menu: Send to any open AI app, Snip, Past chats, Collect every screenshot and Hide Chip. Drop anything on Chip to collect it, and its badge counts items. Chip and its bubbles never appear in your screenshots or screen recordings. Chip is optional, and the welcome screen now asks whether to show it.
- The panel header no longer repeats the logo next to the window title, and the Drafts button now sits on the left of the title.
- Fixed: turning on "Keep the panel on top" (or minimizing, or a display scale change) could leave the panel drawn in only the top part of the window, with a blank area below that couldn't scroll. The page now always fills the window.
- Granola joins the dictation apps Chipmunk recognizes.
- **Chipmunk for Mac (beta).** A menu bar app with the same panel and the same draft format. It collects ⌘⇧3/4/5 screenshots while you work on a draft, numbers them, and pastes everything into your AI app (needs the Accessibility permission). It doesn't have Chip, Past chats, the browser extension or MCP yet. Download `Chipmunk-mac.zip` from the release.
- Welcome screen rewritten: "Show your AI everything." Three steps: screenshot as usual, say what matters (notes, or "in image 2"), send with Ctrl+Enter.

## 1.3.0 (2026-11-16)

- One-file installer: `Chipmunk-Setup.exe`, about 0.5 MB. It installs for the current Windows user with no admin prompt, adds Start menu and desktop shortcuts, and registers in Settings > Apps. Running it again updates in place. The portable zip is still offered.
- Uninstall from Settings > Apps > Installed apps. Drafts in `Documents\Chipmunk` are kept.
- Automatic updates: Chipmunk checks GitHub for a new release about once a day. An "Update to X" pill shows in the panel, Chip offers it and the tray menu lists it. One click updates in a few seconds. Turn it off with **Keep Chipmunk up to date** in Settings.
- New Chip and tray menu: your draft with image thumbnails and counts, **Send to** with each open AI app and its real icon, other windows, Open Chipmunk, Snip into draft, Past chats and Hide Chip. Arrows, Enter and Esc work. The tray menu adds New draft, **Keep collecting when hidden**, **Show Chip**, Settings and Quit.
- Notifications shown while Chip is hidden now follow the light or dark theme.
- New website at https://chipmunk.bsid.dev, with its source in `site/`.

## 1.2.0 (2026-11-12)

- Screenshots land in the draft automatically while the panel is open.
- A fresh draft opens after each send.
- One Send button. The copy options moved into its menu.
- The attach checklist stays hidden until you use it.
- The browser extension is now optional.
- Past chats leads with ChatGPT and Claude export import.
- Fixed a theme flash at startup and a stray "null" in the dictation hint.

## 1.1.0 (2026-11-10)

- Past chats: reuse prompts, replies and images from Claude Code, Codex and Antigravity, read from disk. ChatGPT, Claude and Gemini come in through the extension's **Save this chat** or a data-export import.
- The send picker lists every open AI app and window.
- Chip became an assistant. Click it to send, and it offers to paste when you switch to an AI app, offers to keep screenshots and shows status in a bubble.
- Removed the built-in Talk button. Use the dictation app you already have: Wispr Flow, Superwhisper, Aqua or Win+H.
- Idle CPU dropped from about 8% to about 0.1%.

## 1.0.0 (2026-11-10)

- First release.
- Draft panel that saves as you type, with version history and trash.
- Snip (`Win+Alt+S`), Grab selection (`Win+Alt+C`) and Send (`Win+Alt+V`). Send pastes images, files and text into the app you're in and restores your clipboard. Claude Code terminals get files as `@` paths.
- Chip, the desktop chipmunk you can drop things on.
- Browser extension for adding selections, images, links, page text and tab screenshots.
- MCP server so coding agents can read the draft.
- A to-attach checklist.
