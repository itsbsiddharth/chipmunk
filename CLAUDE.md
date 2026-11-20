# Chipmunk: guide for working on this project

Chipmunk is a free, open-source app for Windows, with a Mac beta (by Siddharth, bsid.dev). You collect screenshots, files and notes into one draft, then one click pastes all of it into ChatGPT, Claude, Claude Code, Codex, Cursor or Antigravity. Read README.md for features, CHANGELOG.md for history (1.0 to 1.4) and docs/PRODUCT.md for why things are the way they are (research, decisions, what we chose not to build).

## Live things

- Website: https://chipmunk.bsid.dev (Vercel project `chipmunk`, team `bakki`, source in `site/`). https://chipmunk-zeta.vercel.app redirects to it, so only the real domain ever shows. All site config (serve `site/`, redirects, short links /download, /download/mac, /download/zip, /github, /releases, headers) lives in the one root `vercel.json`; `.vercelignore` uploads only `site/`. Never add a second vercel.json.
- Short link from the personal site: https://bsid.dev/chipmunk (redirect in the bsid.dev repo's vercel.json).
- Repo: https://github.com/itsbsiddharth/chipmunk. Releases carry `Chipmunk-Setup.exe` and `Chipmunk-win-x64.zip`. The website links to `releases/latest/download/Chipmunk-Setup.exe`, so the newest release is always what people get.
- DNS for bsid.dev is at name.com (the user manages it). `chipmunk` is a CNAME to `64d6a008e962d52a.vercel-dns-017.com`.
- The installed app on the user's PC lives in `%LOCALAPPDATA%\Programs\Chipmunk` (put there by the setup). `dist\` is only a build folder.

## How the user wants things

- Product quality bar: feels like a top YC startup. Premium, calm, clean, intuitive for non-programmers. Fast and light (idle CPU near zero). Nothing that feels "old school" or cluttered. Every feature must earn its place; cut what doesn't help most people.
- Look: warm paper palette (bg #f7f6f2, ink #1d1b18, accent #b5602a), serif display headings (Sitka in the app, Instrument Serif on the site), rounded corners, soft shadows, light and dark themes. Chip the chipmunk mascot is the brand; keep it helpful, never annoying, always optional.
- Pitch: headline "Give your AI the whole picture.", example "Fix my bakery's website" (see docs/PRODUCT.md, "How we pitch it"). Lead with three pains: the AI can't see your screen; half-written prompts have no home; one prompt, many AIs means re-uploading everything. docs/LAUNCH.md has the posts. Examples must be things people really do, on a desktop, that anyone understands.
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
- `extension/` optional Chrome/Edge extension. `site/` the website. `docs/` holds the README images; `hero.html`, `og.html` and `shots/scenes.html` are their sources, and `archive/` keeps earlier marketing sets. `node tools/shots.mjs` re-renders every marketing image (README, site, social card, animated demo) sharp from source; never use real screenshots. Image addresses on the site carry a fingerprint (`?v=…`, added by `tools/stamp.mjs`, which shots.mjs runs) because /assets is cached for a week; run `node tools/stamp.mjs` after changing any file in site/assets by hand. `docs/video/launch.html` is the 30 s launch video (cuts on a 120 BPM grid); `node tools/video.mjs` renders it to `release/chipmunk-launch.mp4` (with the music from `tools/music.py`, AAC) and `chipmunk-launch-silent.mp4` using the browser's own encoders (no ffmpeg); `--music file` swaps the track, and `--stills 3,12,22` saves single frames to check first. `docs/LAUNCH.md` is the launch kit (message, X thread, where else to post); the rendered video and poster frames are kept in `docs/video/`. `tools/stats.ps1` downloads/stars/traffic report.

## Build, test, release

- `.\build.cmd` (or `.\build.ps1 -Out <folder>`) builds the app. Needs the VS 2022 Build Tools C# compiler.
- `.\package.ps1` builds `release\Chipmunk-Setup.exe` and `release\Chipmunk-win-x64.zip`. Bump the version in `src/Program.cs` and `extension/manifest.json` first.
- Building into `dist\` fails if a Chipmunk copy runs from there; build to a scratch folder with `-Out`.
- Capture rules (1.4): screenshots go in by themselves only while you work on the draft (panel in front or used in the last 3 minutes) or with "Collect every screenshot" on (auto-off after send). Otherwise Chip or the open panel asks. Chip and toasts are excluded from screen capture; set `CHIPMUNK_SHOW_IN_CAPTURE=1` to see them in screenshots.
- Drive the panel from scripts: start Chipmunk with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`, then use Chrome DevTools Protocol on http://127.0.0.1:9333 (evaluate `(await import('./host.js')).Host.call(...)`). `Chipmunk.exe --harness log.jsonl` opens a window that records every paste it receives. Never touch the clipboard in tests while the user's own Chipmunk panel is open (it would collect the test image).
- Test safely with an isolated profile: `Chipmunk.exe --profile <folder> --background` keeps settings and drafts apart from the user's. Turn Chip off in test profiles (`"showPet":false` in that profile's settings.json) so only one chipmunk shows on screen.
- Setup flags for testing: `--dir <folder> --no-shortcuts --no-launch`. `--update` is what the in-app updater uses.
- Release: `gh release create vX.Y.Z release\Chipmunk-Setup.exe release\Chipmunk-win-x64.zip --title "Chipmunk X.Y" --notes-file release\notes.md`. Installed copies see it within a day and offer the update.
- Website deploy: automatic. Every push to `main` deploys to production. To deploy by hand, run `npx vercel@latest deploy --prod --yes` from the repo root (linked in `.vercel/`), never from `site/`.

## Known gaps

- Not code-signed, so Windows SmartScreen warns on first run (site and README explain "More info, Run anyway"). Fixing it needs a paid certificate the user would buy.
- The Mac app is a beta, built and tested only in CI. Nobody has tried it on a real Mac yet.
- The bsid.dev redesign (branch `redesign-bakugo`, unreleased, with the user's uncommitted work) gets its Chipmunk entry from the local branch `add-chipmunk-redesign`; merge it when the redesign ships.
