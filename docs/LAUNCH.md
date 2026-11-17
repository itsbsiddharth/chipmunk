# Launch kit

Launch day: 17 November 2026, on X. Everything here can be reused for later posts.

## The message

Chipmunk is **where you build an AI prompt before you send it**, not a screenshot clipboard. Lead with the pain everyone has felt, then show the fix:

1. Your AI can't see your screen, so you screenshot, switch, paste and explain, again and again.
2. Long prompts get lost when a chat box reloads.
3. Juggling several tasks means juggling half-written prompts.
4. On Windows, pasting several screenshots into Claude Code, Codex or Cursor attaches one or scrambles the order.

Chipmunk fixes all four: drafts saved as you type (with versions, on your disk), several drafts at once, numbered screenshots with a note on each, and one click that pastes everything into any app in order. Never name competitors in posts.

## Assets

| File | Use |
| --- | --- |
| `docs/video/chipmunk-launch-2026-11-17.mp4` | **Main launch video for X**, 30 s, 1920x1080, with music and sound effects. The old way is shown on a laptop (screenshot, Alt+Tab, paste, the tab reloads), then Chipmunk. Source: `docs/video/launch.html`; render with `node tools/video.mjs`. |
| `docs/video/chipmunk-launch-2026-11-17-silent.mp4` | Same video without sound, for TikTok and Reels (pick a trending sound in the app) or for your own track. |
| `docs/video/chipmunk-launch-music.wav` | The music on its own: 120 BPM warm house, timed to every cut, with a shutter on each screenshot, keys while typing, a click on Send and a chime on each fix. Made by `python tools/music.py`; it's ours, so it's safe on every platform. |
| `docs/video/chipmunk-launch-2026-11-16.mp4` | The first cut (word-pile loop, no music). Kept for reuse. |
| `docs/video/poster-*.png` | Stills from the video (hook, Alt+Tab, explain, fixed, end card), for thumbnails and image posts. |
| `docs/img/panel-light.png`, `send-menu.png`, `past-chats.png`, `hero.png` | Product shots for replies. |
| `site/assets/og.png` | Social card (shown when the link is shared). |
| `docs/archive/` | Earlier headlines and image sets. |

## The X post (one post, then a thread under it)

Post **one** main post with the video uploaded directly (not a YouTube link). Put the link in the first reply, not the main post: X shows posts with outside links to fewer people. Then reply to yourself with the rest, attaching an image to each.

**Main post** (attach the video):

> Your AI can't see your screen.
>
> So you screenshot, switch, paste, explain, repeat. Then the tab reloads and your prompt is gone.
>
> Today I'm launching Chipmunk 🐿️
>
> One draft for screenshots, files and notes. One click pastes it all into ChatGPT, Claude, Claude Code or Cursor.

**Reply 1** (attach `docs/img/hero.png`):

> It's free and open source, for Windows and Mac.
>
> Download: chipmunk.bsid.dev

**Reply 2** (attach `docs/img/panel-light.png`):

> It's not a clipboard manager. It's where you build the prompt before you send it:
>
> • Every draft saves as you type and keeps versions
> • Screenshots are numbered, with a note on each
> • Keep several prompts going at once
> • Pastes into any app in order, even Claude Code

**Reply 3** (attach `docs/img/send-menu.png`):

> Why I built it: on Windows, pasting 4 screenshots into Claude Code would attach one, or scramble the order. And I kept losing long prompts to a refresh.
>
> Now I collect everything while I work and send it once. The AI gets the whole picture, first try.

**Reply 4** (attach `docs/img/past-chats.png`):

> Bonus: pull prompts and screenshots out of your past ChatGPT, Claude, Claude Code or Codex chats into a new draft.
>
> It's v1.4 and I'm shipping fast. What would make it part of your day? I'll build the best idea this week.

**Next morning, quote your own main post** (attach `docs/video/poster-endcard.png` or a new clip) for a second wave:

> Never lose a prompt to a reloading tab again.
>
> Chipmunk keeps every draft on your disk, saved as you type, with a version every minute. Write in it, collect screenshots in it, send from it.
>
> Free for Windows and Mac: chipmunk.bsid.dev

## The first two hours decide reach

- Before posting, message 10-20 friends: "Launching in 10 min, a like or an honest reply in the first half hour would mean a lot." Real replies count far more than likes.
- Reply to every reply within minutes, with a real answer, not "thanks!".
- Pin the post to your profile. Put chipmunk.bsid.dev in your bio.
- Share it in X Communities you're in (Build in Public, AI tools), with a one-line personal note.
- Search X for people complaining about pasting screenshots into Claude Code, Cursor or ChatGPT, and reply helpfully with a link only when it truly answers them.

## The same week

- **Show HN** (Hacker News), Tuesday to Thursday around 8-10 am US Pacific: "Show HN: Chipmunk – collect screenshots and notes, paste them into any AI at once". First comment: what it is, why you built it, that it's open source, what you'd like feedback on.
- **Reddit**: r/ClaudeAI, r/ChatGPT, r/cursor, r/SideProject. Post the video with a short, honest "I built this for myself" note. Read each subreddit's self-promotion rules first.
- **Product Hunt**: launch on a weekday at 12:01 am Pacific with the video, the panel shots and the same story.
- **LinkedIn**: the founder story from reply 3, with the video.

## Music and voice

- The main video already has its own music and sound effects, safe to post anywhere. `node tools/video.mjs --music some-track.mp3` puts any other track on it instead.
- TikTok and Reels: upload the silent video and pick a trending sound inside the app.
- To try a Google-made track instead: in the Gemini app (free for tracks up to 30 seconds), ask it to create music with the prompt below, download it, then run `node tools/video.mjs --music <the file>`.

> Create a 30-second instrumental track at exactly 120 BPM in F major, for a warm, premium tech product launch video. Style: modern feel-good house with a soft Rhodes electric piano, warm analog pads, a round sub bass and crisp but gentle drums. No vocals.
> Structure, to the second: 0 to 3 s, soft Rhodes chords only, airy and hopeful. 3 to 7 s, a kick on every beat and building hi-hats, rising tension, ending in a short riser. At 7.5 s, a satisfying impact hit, then a moment of air. 8 to 16 s, the full groove: four-on-the-floor kick, claps on 2 and 4, offbeat open hats, a bouncy bass, chords Fmaj7, G6, Em7, Am9, one bar each. 16 to 19 s, the same groove under a low-pass filter, softer. At 19 s, a one-beat pause, then at 19.5 s the full groove returns brighter, with a light bell melody on top. At 24 s, the drums stop on a big warm chord, then the chords settle slowly to C major and fade out by 30 s.
> Mood: clean, optimistic, confident, like an Apple or Linear launch film. Not cheesy, not corporate stock music.

- A voice-over isn't needed: most people watch muted and the captions tell the story. Gemini's text-to-speech has a free tier in Google AI Studio if we make a narrated version later.
