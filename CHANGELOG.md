# Changelog

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
