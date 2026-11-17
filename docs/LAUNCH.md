# Launch kit

Launch on X: **Tuesday 17 November 2026, about 9:30-10 pm India time** (11-11:30 am US East). Then one new post a day, each a different angle, never the same launch post twice:

| When (India time) | Post |
| --- | --- |
| Tue 17 Nov, ~9:30 pm | The launch: main video + thread (below) |
| Wed 18 Nov, 8:00-8:30 pm | Quote the launch post with `clip-old-way.mp4` (the pain) |
| Thu 19 Nov, 8:00-8:30 pm | A new post: "Half-written prompts finally have a home" with `clip-drafts.mp4`, link in a reply. Show HN at ~9:30 pm. |
| Fri 20 Nov | "One draft, every AI" with `clip-every-ai.mp4`; Reddit |

Everything here can be reused for later posts.

## The message

Headline: **Give your AI the whole picture.** One line under it: collect screenshots and notes as you work, keep every prompt in its own draft, and send it to any AI in one click.

Chipmunk is where you build an AI prompt before you send it, not a screenshot clipboard. Every post leads with a pain people have felt, then shows the fix:

1. **Your AI can't see your screen.** Screenshot, switch, paste, explain, switch back, for every screenshot, and the AI still mixes them up. Fix: screenshots land in your draft as you work, numbered, with a note on each.
2. **Half-written prompts have no home.** A chat box that resets, a notes app without your screenshots, a message to yourself you never find. Fix: every prompt gets its own draft, saved as you type, with its screenshots and files.
3. **One prompt, many AIs.** A second opinion means copying the text and uploading every image again. Fix: send the same draft to Claude Code, ChatGPT, Claude or Codex, app or browser, one click each.

Plus, for developers: on Windows, pasting several screenshots into Claude Code, Codex or Cursor often attaches one or scrambles the order. Chipmunk pastes every one, in order. Never name competitors in posts.

## Assets

| File | Use |
| --- | --- |
| `docs/video/chipmunk-launch-2026-11-18.mp4` | **The launch video**, 54 s, 1920x1080, with music. The three pains, then Chipmunk fixing each. Main X post, website, Product Hunt, LinkedIn. |
| `docs/video/chipmunk-launch-2026-11-18-silent.mp4` | Same, without sound, for TikTok and Reels (pick a trending sound in the app). |
| `docs/video/clip-old-way.mp4` | 22 s: "Your AI can't see your screen", the laptop loop, the tab reloading, "Where does your half-written prompt go?", "Meet Chipmunk". The most relatable clip: use it for the day-2 quote post and Reddit. |
| `docs/video/clip-drafts.mp4` | 5.5 s: every prompt gets its own draft. For reply 2. |
| `docs/video/clip-every-ai.mp4` | 6 s: one draft sent to Claude Code, ChatGPT and claude.ai. For reply 3. |
| `docs/video/chipmunk-launch-music.wav` | The music on its own (54 s): a catchy F-major house track with a sung "chipmunk" vocal-chop hook, timed to every cut, with sound effects on every on-screen moment. Made by `python tools/music.py`; ours, so safe on every platform. |
| `docs/video/chipmunk-launch-2026-11-17*.mp4`, `-2026-11-16.mp4` | Earlier 30 s cuts, kept for reuse. |
| `docs/video/poster-*.png` | Stills for thumbnails and image posts. |
| `docs/img/panel-light.png`, `send-menu.png`, `past-chats.png`, `hero.png` | Product shots. |

Render again after any change: `python tools/music.py`, then `node tools/video.mjs`; clips with `node tools/video.mjs --from 0 --to 22 --name clip-old-way` (and `--from 36.5 --to 42 --name clip-drafts`, `--from 42 --to 48 --name clip-every-ai`).

## Before you post (tomorrow, during the day)

- Watch the video once with sound, on your phone. If anything is unclear or the music isn't right, say so and it gets fixed.
- From a different browser, open chipmunk.bsid.dev, download, install, and send one screenshot to an AI. Note anything confusing.
- Message 10-20 friends: "I'm launching at 8 pm, a like or an honest reply in the first half hour would mean a lot." Real replies matter more than likes.
- Put chipmunk.bsid.dev in your X bio.

## The X post: one post, then a thread under it

Upload the video directly (not a link). Keep the link out of the main post: X shows posts with outside links to fewer people. Reply to yourself with the rest, one asset each.

**Main post** (attach `chipmunk-launch-2026-11-18.mp4`):

> Your AI can't see your screen.
>
> So you screenshot, switch, paste, explain. Then a tab reloads and your prompt is gone.
>
> Today I'm launching Chipmunk 🐿️
>
> Collect screenshots and notes as you work, keep every prompt in its own draft, and send it to any AI in one click.

**Reply 1** (attach `docs/img/hero.png`):

> Free and open source, for Windows and Mac.
>
> chipmunk.bsid.dev

**Reply 2** (attach `clip-drafts.mp4`):

> Half-written prompts finally have a home.
>
> Every prompt gets its own draft, saved as you type, with its screenshots and files. Switch to something else and pick it up later.

**Reply 3** (attach `clip-every-ai.mp4`):

> One draft, every AI.
>
> Send the same prompt, screenshots and notes to Claude Code, then ChatGPT, then Claude in the browser. One click each, nothing to re-upload.

**Reply 4** (attach `docs/img/send-menu.png`):

> Why I built it: on Windows, pasting 4 screenshots into Claude Code would attach one, or scramble the order. And I kept losing long prompts to a refresh.
>
> Now I collect everything while I work and send it once.

**Reply 5** (no attachment):

> It's v1.4 and I'm shipping fast. What would make it part of your day? I'll build the best idea this week.

**Wednesday evening, quote your main post** (attach `clip-old-way.mp4`):

> Screenshot. Alt+Tab. Paste. Explain. Repeat.
>
> Then the tab reloads.
>
> That loop is why I built Chipmunk. Free for Windows and Mac: chipmunk.bsid.dev

## The first two hours decide reach

- Reply to every reply within minutes, with a real answer, not "thanks!".
- Pin the post to your profile.
- Share it in X Communities you're in (Build in Public, AI tools), with a one-line personal note.
- Search X for people complaining about pasting screenshots into Claude Code, Cursor or ChatGPT, or about losing prompts, and reply helpfully, with the link only when it truly answers them.

## The rest of the week

- **Thursday ~9:30 pm India time (9 am US Pacific): Show HN.** "Show HN: Chipmunk – drafts for AI prompts, with screenshots, sent to any AI in one click". First comment: what it is, why you built it, that it's open source, what you'd like feedback on.
- **Friday: Reddit**: r/ClaudeAI, r/ChatGPT, r/cursor, r/SideProject, with `clip-old-way.mp4` and an honest "I built this for myself" note. Read each subreddit's self-promotion rules first.
- **Next week: Product Hunt**, on a weekday at 12:01 am Pacific (1:30 pm India time), with the launch video, the product shots and the same three pains.
- **LinkedIn**: the founder story from reply 4, with the launch video.

## Music and voice

- The videos already have their own music and sound effects, safe to post anywhere. `node tools/video.mjs --music some-track.mp3` puts any other track on instead.
- TikTok and Reels: upload the silent video and pick a trending sound in the app.
- To try a Google-made track: the Gemini app makes music for free up to 30 seconds, so use it for the clips, not the 54 s video. Prompt:

> Create a 30-second instrumental at exactly 120 BPM in F major for a premium tech product launch. Catchy, playful, modern house: a sung, pitched-up vocal-chop hook (no words) doubled by a marimba, over Dm7, Bbmaj7, F, C, one bar each. Four-on-the-floor kick, claps on 2 and 4, swung shakers, a bouncy bass. Bright and confident, like an Apple or Linear launch film, never corporate stock music.

- A voice-over isn't needed: most people watch muted and the captions tell the story.
