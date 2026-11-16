# Chipmunk: product decisions

Written November 2026, for version 1.4. It covers what we believe, what we built, and what we chose not to build.

## The job

People give AI apps context made of pictures and words: a few screenshots, an error, a file, a sentence about each one. Today that means:

- attaching things one by one,
- pasting that fails in terminal agents on Windows (Ctrl+V vs Alt+V, "No image found in clipboard", only one of several images attaching, images arriving in the wrong order),
- prompts lost when a chat box resets,
- screenshots the model can't tell apart.

Chipmunk's job: **give your AI the whole picture, in one paste, in order, with what you want it to notice.**

## What the research said

- **The biggest, best-documented pain is pasting screenshots into Claude Code, Codex and Cursor on Windows.** There are dozens of GitHub issues (anthropics/claude-code #68021, #9124, #59661, #39416, #51544 and more), and the Cursor forum has a thread titled "Getting screenshots into Cursor is driving me a bit crazy". This is the hero use case.
- **Models only know which image is which from order and nearby text.** Anthropic's vision guide recommends putting a label like "Image 1:" before each image. Claude Code numbers pasted images [Image #1], [Image #2]. So Chipmunk pastes images in draft order and labels notes with the same numbers.
- **Windows already saves every screenshot to Pictures\Screenshots** (Snipping Tool's default). Chipmunk copies from the clipboard, so a user never loses a screenshot to Chipmunk.
- **Desktop companions survive when they are status lights, not mascots.** Codex pets (OpenAI, 2026) are praised as a "passive status monitor" and criticized when they grab hotkeys, are hard to dismiss or take space. Clippy-style novelty gets attention but not daily use.
- **Nobody can capture another app's dictation.** Wispr Flow, Superwhisper, Granola and Win+H all type into whatever text field has focus. The right design is a focused text field, which the editor and each note already are.

## What 1.4 changes, and why

| Change | Why |
|---|---|
| Screenshots are numbered in the draft, and each can have a note. On send: images in order, then "Image 2: <note>". | It answers "how do I tell the AI which screenshot I mean?" without new concepts, and it follows Anthropic's own advice. |
| "Add a note" one click away from every capture (Chip bubble, notification, panel toast). | This removes the friction the founder hit: click Chip, then the card, then type. Now it's one click from the capture to typing or dictating the note. |
| Screenshots go in by themselves only while you're working on the draft (panel in front, or used in the last 3 minutes). Otherwise Chip asks. | This fixes the Focusmate incident, where personal screenshots landed in a work draft because the panel was open in the background. |
| "Collect every screenshot" is explicit, shows a glowing dot on Chip, and turns itself off after you send. | This keeps a power mode for people who want it, so they can never forget it's on. |
| Undo on every capture. | One click to take back a mistake. |
| Chip: click opens the draft; right-click for "Send to"; never appears in screenshots or recordings; optional, chosen on the welcome screen. | Chip's real jobs are status (badge, collecting dot), a drop target and one-click access. Being invisible in captures answers "I don't want it in my screenshots" with no toggle. |
| No logo repeated next to the window title. | The window title bar already shows the icon and name, and real products don't repeat the logo next to it. The space goes to the draft title. |
| Fixed the half-blank panel after "Keep the panel on top". | The window and WebView2 could disagree about the size. The page is now re-fitted on every show, resize, activation and DPI change. |
| Mac version (beta). | Same panel, same draft format, native menu bar app. |

## What we chose not to build (yet)

- **Our own dictation, or "catching" other dictation.** It can't be done safely, and it would risk swallowing a message meant for someone else. A focused field is enough.
- **A shortcut to hide Chip.** Chip is excluded from screenshots, which removes the main reason. Hide and Show stay in the tray and Chip's right-click menu.
- **Agent status on Chip** ("Claude Code finished"). It's useful, but Codex pets and OpenPets already do it. We'll revisit if users ask.
- **Pinned project context** that's attached to every draft. It's a real pain (re-explaining context), but drafts, Duplicate and Past chats cover most of it today.
- **Cleaning up the Screenshots folder.** It's a weak pain, and Windows owns that folder.

## How we pitch it

- **Headline: "Give your AI the whole picture."** We tried three. "Show your AI everything" sounded like a tool that watches your screen, the opposite of what Chipmunk does since 1.4. "Ten screenshots. One paste." was concrete, but it made Chipmunk sound like a screenshot clipboard. "The whole picture" names the outcome (the AI understands your situation, not just one image), plays on "pictures", and is a phrase everyone already knows. The line under it gives the concrete part: screenshots, files and notes, numbered, with a note on each, into the AI app you already use.
- **Example: fixing three things on your bakery's website.** The example has to be something people really do, where a screenshot is the only practical way because the AI can't see the thing otherwise, and where several screenshots with a note each make sense. Fixing problems you spot across a few screens of something you're building is the most common real pattern (the Cursor forum's screenshot threads, Claude Code debugging write-ups, guides on giving agents screenshots). A small business website keeps it clear for people who don't write code, and every shot is a desktop browser window, so nobody reads it as a phone app. Two screenshots show problems ("The button covers the headline", "Total should be $20, not $0"), one shows a café site to copy ("Make our reviews look like this"), and it all goes to Claude Code in one paste.
- **Dropped examples:** a phone sign-in bug (read as a phone app) and picking a laptop (people just ask the AI; nobody screenshots shop pages). Their images are in `docs/archive/`.

## How we'll know it works

- Weekly downloads (`tools/stats.ps1`) and GitHub stars.
- Issues that mention a broken paste into a specific app. These are the product's core promise, so they get fixed first.
- Whether people turn Chip off. The welcome screen asks, so we can learn the default from what people choose.
