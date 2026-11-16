# Chipmunk: guide for working on this project

Chipmunk is a free, open-source Windows app (by Siddharth, bsid.dev). You collect screenshots, files and notes into one draft, then one click pastes all of it into ChatGPT, Claude, Claude Code, Codex, Cursor or Antigravity. Read README.md for features, CHANGELOG.md for history (1.0 to 1.4) and docs/PRODUCT.md for why things are the way they are (research, decisions, what we chose not to build).

## Live things

- Website: https://chipmunk.bsid.dev (Vercel project `chipmunk`, team `bakki`, source in `site/`). Also https://chipmunk-zeta.vercel.app. Short links in `site/vercel.json`: /download, /download/zip, /github, /releases.
- Short link from the personal site: https://bsid.dev/chipmunk (redirect in the bsid.dev repo's vercel.json).
- Repo: https://github.com/itsbsiddharth/chipmunk. Releases carry `Chipmunk-Setup.exe` and `Chipmunk-win-x64.zip`. The website links to `releases/latest/download/Chipmunk-Setup.exe`, so the newest release is always what people get.
- DNS for bsid.dev is at name.com (the user manages it). `chipmunk` is a CNAME to `64d6a008e962d52a.vercel-dns-017.com`.
- The installed app on the user's PC lives in `%LOCALAPPDATA%\Programs\Chipmunk` (put there by the setup). `dist\` is only a build folder.

## How the user wants things

- Product quality bar: feels like a top YC startup. Premium, calm, clean, intuitive for non-programmers. Fast and light (idle CPU near zero). Nothing that feels "old school" or cluttered. Every feature must earn its place; cut what doesn't help most people.
- Look: warm paper palette (bg #f7f6f2, ink #1d1b18, accent #b5602a), serif display headings (Sitka in the app, Instrument Serif on the site), rounded corners, soft shadows, light and dark themes. Chip the chipmunk mascot is the brand; keep it helpful, never annoying, always optional.
- Explanations: plain language, specific steps, like talking to a non-programmer. Concise and structured.
- Git: commit messages are short, plain and human. Never add a Co-Authored-By line or any mention of AI. Don't push to the user's other repos without saying so first.
- Ship to the end: build, test it really works, then release and deploy. Publish GitHub releases only when the user approves that release.
- Use Sonnet subagents for well-defined, simple tasks (docs, small repo edits) and keep design and core code yourself.
- Keep this file, README.md and CHANGELOG.md up to date once at the end of a piece of work (not after every small step).

## Layout

- `src/` C# app (.NET Framework 4.8, WinForms + WebView2). Key files: AppCore.cs (everything the app does), Pet.cs (Chip), Popover.cs (Chip/tray menu + UiKit drawing helpers), Install.cs + InstallInfo.cs (uninstall, updates), History.cs + LocalAgents.cs + ClaudeImport.cs (Past chats), Clipboard.cs (paste engine), Store.cs (drafts on disk), Mcp.cs, Bridge.cs (browser extension bridge).
- `ui/` the panel (vanilla HTML/CSS/JS). `ui/mock-host.js` lets the UI run in a normal browser for design work (`python tools\devserver.py`, then http://localhost:5178).
- `setup/Setup.cs` the installer. The app files get embedded into it at package time.
- `mac/` the Mac app (Swift, menu bar + WKWebView around the same `ui/`). `mac/scripts/build-app.sh` builds Chipmunk.app; `.github/workflows/mac.yml` tests, builds and smoke-tests it on GitHub's Mac runners (there is no Mac here, so CI is the only compiler). The panel picks key names and hidden features from `settings.platform` / `settings.features`.
- `extension/` optional Chrome/Edge extension. `site/` the website. `docs/` README images and `og.html` (source of the social card `site/assets/og.png`). `tools/stats.ps1` downloads/stars/traffic report.

## Build, test, release

- `.\build.cmd` (or `.\build.ps1 -Out <folder>`) builds the app. Needs the VS 2022 Build Tools C# compiler.
- `.\package.ps1` builds `release\Chipmunk-Setup.exe` and `release\Chipmunk-win-x64.zip`. Bump the version in `src/Program.cs` and `extension/manifest.json` first.
- Building into `dist\` fails if a Chipmunk copy runs from there; build to a scratch folder with `-Out`.
- Capture rules (1.4): screenshots go in by themselves only while you work on the draft (panel in front or used in the last 3 minutes) or with "Collect every screenshot" on (auto-off after send). Otherwise Chip or the open panel asks. Chip and toasts are excluded from screen capture; set `CHIPMUNK_SHOW_IN_CAPTURE=1` to see them in screenshots.
- Drive the panel from scripts: start Chipmunk with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`, then use Chrome DevTools Protocol on http://127.0.0.1:9333 (evaluate `(await import('./host.js')).Host.call(...)`). `Chipmunk.exe --harness log.jsonl` opens a window that records every paste it receives. Never touch the clipboard in tests while the user's own Chipmunk panel is open (it would collect the test image).
- Test safely with an isolated profile: `Chipmunk.exe --profile <folder> --background` keeps settings and drafts apart from the user's. Turn Chip off in test profiles (`"showPet":false` in that profile's settings.json) so only one chipmunk shows on screen.
- Setup flags for testing: `--dir <folder> --no-shortcuts --no-launch`. `--update` is what the in-app updater uses.
- Release: `gh release create vX.Y.Z release\Chipmunk-Setup.exe release\Chipmunk-win-x64.zip --title "Chipmunk X.Y" --notes-file release\notes.md`. Installed copies see it within a day and offer the update.
- Website deploy: from `site/`, `npx vercel@latest deploy --prod --yes` (already linked; the custom domain follows automatically).

## Known gaps

- Not code-signed, so Windows SmartScreen warns on first run (site and README explain "More info, Run anyway"). Fixing it needs a paid certificate the user would buy.
- Mac is not supported.
- The bsid.dev redesign (branch `redesign-bakugo`, unreleased, with the user's uncommitted work) gets its Chipmunk entry from the local branch `add-chipmunk-redesign`; merge it when the redesign ships.
