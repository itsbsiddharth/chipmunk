"""Composes the launch video's music, timed to docs/video/launch.html (120 BPM, 54 s).

    python tools/music.py            -> docs/video/chipmunk-launch-music.wav

A catchy house track in F major (Dm7 - Bbmaj7 - F - C) with a sung hook: pitched-up vocal chops,
fitting for a chipmunk, doubled by a marimba. It tells the same story as the video:
  0-4    the hook, teased softly on marimba
  4-16   the old way: a nervous loop that gets busier, then stops dead like a tape when the tab reloads
  16-20  "where does it go?": a rising question
  20     "Meet Chipmunk": the hook arrives, sung
  22-48  the groove, with the hook on the demo, the payoff and "One draft. Every AI."
  48-54  big warm chords that land home on F, then fade
Sound effects sit on every on-screen moment. Everything is synthesized here, so it's ours to use.
"""
import os
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 48000
DUR = 54.0
N = int(SR * DUR)
rng = np.random.default_rng(11)
OUT = os.path.join(os.path.dirname(__file__), '..', 'docs', 'video', 'chipmunk-launch-music.wav')


def hz(m): return 440.0 * 2 ** ((m - 69) / 12)
def t_(d): return np.arange(int(d * SR)) / SR
def lp(x, f, o=2): return sosfilt(butter(o, f, 'low', fs=SR, output='sos'), x)
def hp(x, f, o=2): return sosfilt(butter(o, f, 'high', fs=SR, output='sos'), x)
def bp(x, lo, hi, o=2): return sosfilt(butter(o, [lo, hi], 'band', fs=SR, output='sos'), x)


class Bus:
    def __init__(self): self.l = np.zeros(N); self.r = np.zeros(N)
    def add(self, at, sig, gain=1.0, pan=0.0):
        i = int(at * SR)
        if i >= N or i < 0: return
        s = sig[: N - i] * gain
        self.l[i:i + len(s)] += s * np.sqrt((1 - pan) / 2) * 1.414
        self.r[i:i + len(s)] += s * np.sqrt((1 + pan) / 2) * 1.414
    def st(self): return np.stack([self.l, self.r])


# ---------------- instruments ----------------
def saw(f, t, ph=0.0): return 2 * ((f * t + ph) % 1) - 1

def chop(m, d, vowel=0):
    """A pitched-up sung "ah"/"oh": a sawtooth through vocal formants, with a little scoop and vibrato."""
    t = t_(d + .08); f0 = hz(m)
    f = f0 * (2 ** ((-.6 * np.exp(-t * 40) + .12 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(t / .25, 1)) / 12))
    src = 2 * ((np.cumsum(f) / SR) % 1) - 1
    F = [(1050, 2000, 3100), (720, 1150, 2900)][vowel % 2]
    s = bp(src, F[0] * .8, F[0] * 1.25) + bp(src, F[1] * .85, F[1] * 1.2) * .6 + bp(src, F[2] * .9, F[2] * 1.15) * .25
    env = np.minimum(t / .012, 1) * np.clip((d + .08 - t) / .08, 0, 1) * (1 - .25 * np.minimum(t / d, 1))
    return s * env * 1.6

def marimba(m, d=.5, vel=1.0):
    t = t_(max(d, .5)); f = hz(m)
    s = np.sin(2 * np.pi * f * t) + .35 * np.sin(2 * np.pi * 4 * f * t) * np.exp(-t * 30) + .15 * np.sin(2 * np.pi * 10 * f * t) * np.exp(-t * 60)
    return s * np.minimum(t / .002, 1) * np.exp(-t * 7) * vel

def ep(m, d, vel=1.0):
    t = t_(d + 1.0); f = hz(m)
    s = np.sin(2 * np.pi * f * t + (1.4 * np.exp(-t * 5) + .2) * np.sin(2 * np.pi * f * t))
    env = np.minimum(t / .004, 1) * np.exp(-t * 1.4) * np.clip((d + 1.0 - t) / 1.0, 0, 1)
    return s * env * vel

def pad(ms, d, bright=1400):
    t = t_(d); s = np.zeros(len(t))
    for m in ms:
        for det in (-.12, 0, .14):
            s += saw(hz(m) * (1 + det / 100), t, rng.random())
    s = lp(s / (len(ms) * 3), bright)
    return s * np.minimum(t / .4, 1) * np.clip((d - t) / .5, 0, 1)

def stab(ms, vel=1.0):
    t = t_(.28); s = np.zeros(len(t))
    for m in ms: s += saw(hz(m + 12), t) + saw(hz(m + 12) * 1.004, t)
    return lp(s / len(ms), 2600) * np.minimum(t / .003, 1) * np.exp(-t * 14) * vel

def bass(m, d=.2):
    t = t_(d); f = hz(m)
    s = np.tanh(2.2 * (np.sin(2 * np.pi * f * t) + .4 * np.sin(4 * np.pi * f * t)))
    return lp(s * np.minimum(t / .003, 1) * np.clip((d - t) / .03, 0, 1) * np.exp(-t * 3), 1000)

def kick(vel=1.0):
    t = t_(.42)
    ph = 2 * np.pi * np.cumsum(48 + 125 * np.exp(-t * 30)) / SR
    return (np.sin(ph) * np.exp(-t * 7.5) + .3 * np.exp(-t * 300) * rng.standard_normal(len(t))) * vel

def clap(vel=1.0):
    t = t_(.3); n = rng.standard_normal(len(t))
    env = sum(np.exp(-np.clip(t - o, 0, None) * 60) * (t >= o) for o in (0, .011, .022)) + .6 * np.exp(-t * 13)
    return bp(n * env, 900, 3400) * vel * .9

def hat(open_=False, vel=1.0):
    t = t_(.24 if open_ else .045)
    return hp(rng.standard_normal(len(t)), 7500) * np.exp(-t * (14 if open_ else 85)) * vel

def shaker(vel=1.0):
    t = t_(.07)
    return bp(rng.standard_normal(len(t)), 5000, 12000) * np.minimum(t / .015, 1) * np.exp(-t * 55) * vel

def noise_sweep(d, f0, f1, shape='rise'):
    t = t_(d); out = np.zeros(len(t)); seg = int(.02 * SR); n = rng.standard_normal(len(t))
    for i in range(0, len(t), seg):
        f = f0 * (f1 / f0) ** (i / len(t))
        out[i:i + seg] = bp(n[max(0, i - seg):i + seg], f * .7, min(f * 1.4, SR / 2 - 100))[-len(n[i:i + seg]):]
    return out * ((t / d) ** 2 if shape == 'rise' else np.sin(np.pi * t / d) ** 2)

def boom():
    t = t_(2.4)
    ph = 2 * np.pi * np.cumsum(32 + 62 * np.exp(-t * 6)) / SR
    return np.sin(ph) * np.exp(-t * 1.5) + hp(rng.standard_normal(len(t)), 4000) * np.exp(-t * 1.6) * .35

def shutter():
    s = np.zeros(int(.12 * SR))
    for o, v in ((0, 1), (.045, .7)):
        t = t_(.03); c = bp(rng.standard_normal(len(t)), 1800, 7000) * np.exp(-t * 160) * v
        s[int(o * SR):int(o * SR) + len(c)] += c
    return s

def key_click(v=1.0):
    t = t_(.03); return bp(rng.standard_normal(len(t)), 2500, 9000) * np.exp(-t * 260) * v

def tock():
    t = t_(.08); s = np.sin(2 * np.pi * 1300 * t) * np.exp(-t * 70) * .6
    c = key_click(.5); s[:len(c)] += c * .5; return s

def pop():
    t = t_(.12); return np.sin(2 * np.pi * np.cumsum(500 + 900 * np.exp(-t * 40)) / SR) * np.exp(-t * 30)

def bell(m, d=1.4, vel=1.0):
    t = t_(d); f = hz(m)
    s = np.sin(2 * np.pi * f * t + 2.0 * np.exp(-t * 3) * np.sin(2 * np.pi * 3.5 * f * t))
    return s * np.minimum(t / .002, 1) * np.exp(-t * 3.4) * vel

def thunk():
    t = t_(.15); return np.sin(2 * np.pi * np.cumsum(220 * np.exp(-t * 10) + 60) / SR) * np.exp(-t * 25)


# ---------------- harmony and the hook ----------------
CH = {  # voicing, bass
    'Dm': ([50, 53, 57, 60], 38), 'Bb': ([53, 57, 58, 62], 46),
    'F': ([53, 57, 60, 65], 41), 'C': ([52, 55, 60, 64], 36),
}
BARS = ['Dm', 'Bb'] * 4 + ['Bb', 'C', 'F'] + ['Dm', 'Bb', 'F', 'C'] * 3 + ['Dm', 'Bb', 'C', 'F']   # 27 bars of 2 s
# The hook, per chord: (16th step, midi, length in 16ths). The same rhythm opens every bar, so it sticks.
HOOK = {
    'Dm': [(0, 69, 2), (3, 69, 1), (4, 72, 2), (6, 74, 2), (10, 72, 2), (12, 69, 2), (14, 67, 2)],
    'Bb': [(0, 74, 3), (3, 72, 1), (4, 70, 2), (6, 69, 2), (8, 65, 4), (14, 67, 2)],
    'F':  [(0, 69, 2), (3, 69, 1), (4, 72, 2), (6, 74, 2), (10, 77, 2), (12, 76, 2), (14, 74, 2)],
    'C':  [(0, 76, 3), (3, 74, 1), (4, 72, 2), (6, 67, 2), (8, 72, 6)],
}
S16 = .125

music, drums, hook, fx = Bus(), Bus(), Bus(), Bus()
kicks = []

def play_hook(bar, chops=True, mar=.5, chop_gain=.32):
    name = BARS[bar]
    for k, (st, m, ln) in enumerate(HOOK[name]):
        at = bar * 2 + st * S16
        if chops: hook.add(at, chop(m + 12, ln * S16 * .92, k), chop_gain, pan=(-.15 if k % 2 else .15))
        if mar: hook.add(at, marimba(m, ln * S16), mar, pan=.1)

def chords(bar, ep_gain=.32, pad_gain=.55, bright=1400):
    vs, _ = CH[BARS[bar]]
    for m in vs: music.add(bar * 2, ep(m, 1.8, ep_gain), pan=(m % 5 - 2) / 6)
    music.add(bar * 2, pad(vs, 2.05, bright), pad_gain)

def groove(bar):
    vs, root = CH[BARS[bar]]
    b0 = bar * 2
    for beat in range(4):
        at = b0 + beat * .5
        kicks.append(at); drums.add(at, kick(1.0))
        drums.add(at + .25, hat(True, .3), pan=.25)
        if beat in (1, 3): drums.add(at, clap(.8))
        for s in range(4):
            drums.add(at + s * S16 + (.022 if s % 2 else 0), shaker(.16 + .08 * (s == 2)), pan=-.25)   # swung 16ths
    for st, off, ln in ((0, 0, 2), (3, 0, 1), (6, 12, 2), (8, 0, 2), (11, 7, 1), (14, 12, 2)):
        music.add(b0 + st * S16, bass(root + off, ln * S16 * .9), .75)
    for st in (6, 14): music.add(b0 + st * S16, stab(vs, .5), .55, pan=-.2)
    chords(bar, .26, .4)

# 0-4 s: the hook, teased softly on the marimba over warm chords
for bar in (0, 1):
    chords(bar, .3, .7, 900)
    play_hook(bar, chops=False, mar=.32)

# 4-16 s: the old way. A nervous loop that gets busier, then stops dead when the tab reloads.
for bar in range(2, 8):
    vs, root = CH[BARS[bar]]
    chords(bar, .22, .55, 1100)
    for k in range(16):                                         # ticking 8ths, then 16ths
        if bar < 5 and k % 2: continue
        music.add(bar * 2 + k * S16, marimba(vs[k % 4] + 12, .25, .22 + .03 * (bar - 2)), pan=.35 if k % 2 else -.35)
    for beat in range(4):
        at = bar * 2 + beat * .5
        kicks.append(at); drums.add(at, kick(.75 + .04 * (bar - 2)))
        if bar >= 3: drums.add(at + .25, hat(False, .3))
        if bar >= 5 and beat in (1, 3): drums.add(at, clap(.6))
    if bar >= 4:
        for st, off in ((0, 0), (6, 0), (8, 0), (14, 12)): music.add(bar * 2 + st * S16, bass(root + off, .2), .6)

# 16-20 s: "where does it go?" A questioning lift into the reveal.
chords(8, .3, .6, 1200); chords(9, .32, .65, 1600)
for k in range(8):
    vs, _ = CH[BARS[8 + k // 4]]
    music.add(16 + k * .5, marimba(vs[(k * 2) % 4] + 12, .4, .3), pan=.3 if k % 2 else -.3)
for at in (16.0, 16.5, 17.0, 17.5): drums.add(at + .25, hat(False, .22))
fx.add(18.0, noise_sweep(1.95, 300, 9000), .5)
for i in range(8): drums.add(19.0 + i * .118, clap(.2 + .55 * i / 8))

# 20-22 s: "Meet Chipmunk." The hook, sung.
fx.add(20.0, boom(), .85)
chords(10, .4, .75, 2200)
play_hook(10, chops=True, mar=.45, chop_gain=.36)

# 22-48 s: the groove
for bar in range(11, 24):
    groove(bar)
for bar in (12, 13, 14):            play_hook(bar)                 # the demo, until the prompt is typed
for bar in (16, 17):                play_hook(bar)                 # the payoff
for bar in (21, 22, 23):            play_hook(bar, chop_gain=.36)  # one draft, every AI
for bar in (18, 19, 20):                                           # drafts: a lighter marimba answer
    vs, _ = CH[BARS[bar]]
    for k, st in enumerate((2, 5, 8, 10, 13)):
        hook.add(bar * 2 + st * S16, marimba(vs[(k + 1) % 4] + 24, .3, .3), pan=-.3 if k % 2 else .3)

# 48-54 s: drums stop on a big chord, the hook sings its last phrase, and it lands home on F.
kicks.append(48.0); drums.add(48.0, kick(1.0)); fx.add(48.0, boom() * .5, .6)
for bar in (24, 25, 26):
    vs, root = CH[BARS[bar]]
    d = 2.0 if bar < 26 else 6.0
    for m in vs: music.add(bar * 2, ep(m, d, .36), pan=(m % 5 - 2) / 6)
    music.add(bar * 2, pad(vs + [vs[1] + 12], d + .2, 2000), .85)
    music.add(bar * 2, bass(root, min(d, 3.5)) * .7, .8)
play_hook(24, chop_gain=.3); play_hook(25, chop_gain=.3)
hook.add(52.0, chop(77, 1.2, 0), .3); hook.add(52.0, marimba(65, 1.0), .45)
for k, m in enumerate([77, 81, 84, 89, 93]):
    hook.add(50.2 + k * .07, bell(m, 1.2, .16), pan=-.5 + k * .25)

# ---------------- sound effects on the video's moments ----------------
for at in (4.9, 11.1): fx.add(at, shutter(), .7, pan=-.2)                    # screenshots, the old way
for at in (5.6, 9.6, 11.6): fx.add(at, tock(), .5); fx.add(at + .32, tock() * .6, .4)   # Alt+Tab, then the pick
for at in (6.5, 10.3, 12.2): fx.add(at - .05, noise_sweep(.3, 1500, 5000, 'swell'), .18)
for at in (7.25, 12.35): fx.add(at, pop(), .45)                              # pasted in
for a, b in ((8.15, 9.3), (12.55, 13.45)):
    for at in np.arange(a, b, .07): fx.add(at + rng.uniform(-.012, .012), key_click(.5 + .3 * rng.random()), .45, pan=.3)
fx.add(16.15, bell(84, .9, .4), pan=.4); fx.add(16.27, bell(88, .9, .4), pan=.4)  # the notification
for at in (18.4, 18.8, 19.2): fx.add(at, thunk(), .35)                       # each wrong answer crossed out
for b in (22.5, 25.0, 27.25): fx.add(b + .66, shutter(), .7, pan=-.2)         # screenshots, with Chipmunk
for at in np.arange(29.4, 30.7, .065): fx.add(at + rng.uniform(-.012, .012), key_click(.55 + .3 * rng.random()), .5, pan=.3)
fx.add(31.45, key_click(1.2), .9)                                             # Send
fx.add(31.5, noise_sweep(.5, 800, 7000, 'swell'), .55); fx.add(32.0, boom() * .5, .55)
for at, ms in ((34.7, [84, 88]), (35.5, [88, 91])):                           # each "Fixed"
    for k, m in enumerate(ms): fx.add(at + k * .09, bell(m, 1.4, .38), pan=.2)
fx.add(38.3, key_click(1.0), .7); fx.add(38.55, noise_sweep(.3, 1200, 5000, 'swell'), .2)   # open a draft
for a in (43.0, 44.5, 46.0):                                                  # one draft, three AIs
    fx.add(a, key_click(1.0), .7)
    fx.add(a + .05, noise_sweep(.55, 900, 6000, 'swell'), .3)
    fx.add(a + .62, bell(88, .8, .3), pan=.3)

# ---------------- mix ----------------
t = np.arange(N) / SR
duck = np.ones(N)
for k in kicks:
    i0 = int(k * SR); i1 = min(N, i0 + int(.5 * SR)); tt = t[i0:i1] - k
    duck[i0:i1] = np.minimum(duck[i0:i1], 1 - .5 * np.exp(-tt / .11))
mus = (music.st() + hook.st()) * duck + drums.st() * .78

# filter the band while the prompt is typed (28.8-31.45 s), then a breath before the payoff
filt = np.stack([lp(mus[0], 700, 4), lp(mus[1], 700, 4)])
w = np.clip((t - 28.8) / .4, 0, 1) * (1 - np.clip((t - 31.45) / .1, 0, 1))
gap = 1 - np.clip((t - 31.55) / .05, 0, 1) * (1 - np.clip((t - 32.0) / .02, 0, 1))
mus = (mus * (1 - w) + filt * w) * gap

# the tab reloads at 13.9 s: the music slows to a stop like a tape, then near silence until 16 s
a0, a1 = int(13.9 * SR), int(14.55 * SR)
rate = np.linspace(1, 0, a1 - a0) ** 1.3
pos = a0 + np.cumsum(rate)
stop = np.stack([np.interp(pos, np.arange(N), mus[c]) for c in (0, 1)]) * np.linspace(1, 0, a1 - a0) ** .5
mus[:, a0:a1] = stop
mus[:, a1:int(16 * SR)] = 0
after = Bus()
after.add(14.6, pad([50, 57, 62], 1.5, 700), .5)
for at, m in ((14.7, 69), (15.1, 65), (15.5, 62)): after.add(at, marimba(m, .6, .5), pan=.1)
mus = mus + after.st()

mix = mus + fx.st()
mix *= np.clip((DUR - t) / 2.0, 0, 1) ** 1.5
mix = np.tanh(mix * 1.15)
mix = mix / np.max(np.abs(mix)) * .89
os.makedirs(os.path.dirname(OUT), exist_ok=True)
wavfile.write(OUT, SR, (mix.T * 32767).astype(np.int16))
print('wrote', os.path.relpath(OUT), f'{DUR:.0f} s')
