# Contributing to Chipmunk

Thanks for helping. Bug reports with a screenshot and the app you were pasting into are the most useful thing you can send: the whole product is a promise that pasting works, so a broken paste is a top priority.

## Project layout

```
src/         Windows app in C# (.NET Framework 4.8, WinForms + WebView2): tray, Chip, shortcuts,
             clipboard watching, the paste engine, drafts on disk, Past chats, MCP server
ui/          the panel (plain HTML, CSS, JavaScript), shared by Windows and Mac
mac/         Mac app in Swift (menu bar + WKWebView around ui/)
setup/       the Windows installer; the app is embedded into it when packaging
extension/   the optional Chrome / Edge / Brave extension
site/        the website, https://chipmunk.bsid.dev (static, deployed on Vercel)
docs/        README images and their sources, product notes (PRODUCT.md)
tools/       dev server, screenshot renderer, download stats
```

## Build and run

**Windows** needs the C# compiler from the free *Build Tools for Visual Studio*.

```powershell
.\build.cmd                                  # builds into dist\
.\package.ps1                                # dist\ plus release\Chipmunk-Setup.exe and the portable zip
dist\Chipmunk.exe --profile C:\temp\cm-test  # a separate profile, so your own drafts are untouched
```

**Mac** needs Xcode or its command line tools.

```bash
cd mac && swift test          # unit tests
bash mac/scripts/build-app.sh # builds mac/build/Chipmunk.app and Chipmunk-mac.zip
```

GitHub Actions builds, tests and launches the Mac app on every push that touches `mac/` or `ui/`.

**The panel UI** runs in a normal browser with a stand-in for the native side:

```powershell
python tools\devserver.py    # then open http://localhost:5178
```

## Marketing images

Every image in the README and on the website is rendered from source, so it stays sharp and matches the current UI:

```powershell
node tools\shots.mjs
```

It renders the real panel at 3x with demo content (`docs/shots/scenes.html`), the README hero and social card from `docs/hero.html` and `docs/og.html` at 2x, and records the website's hero animation as `docs/img/demo.webp`. Run it after any visible UI change, and never commit hand-cropped or upscaled screenshots.

## Pull requests

- Keep changes focused, and describe what a user will notice.
- UI text is plain and short, written for people who don't code.
- Test a paste end to end: `Chipmunk.exe --harness log.jsonl` opens a window that records every paste it receives.
