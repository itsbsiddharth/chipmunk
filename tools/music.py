"""Composes the launch video's music, timed to docs/video/launch.html (120 BPM, 30 s).

    python tools/music.py            -> docs/video/chipmunk-launch-music.wav

Warm house groove (Fmaj7 - G6 - Em7 - Am9) with a soft electric piano, plus sound effects on the
video's moments: a shutter on each screenshot, keys while the prompt is typed, a click on Send, a
chime when each fix lands. Everything is synthesized here, so it's ours to use anywhere.
"""
import os
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 48000
DUR = 30.0
N = int(SR * DUR)
BEAT = 0.5
rng = np.random.default_rng(7)
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
        if i >= N: return
        s = sig[: N - i] * gain
        self.l[i:i + len(s)] += s * np.sqrt((1 - pan) / 2) * 1.414
        self.r[i:i + len(s)] += s * np.sqrt((1 + pan) / 2) * 1.414
    def st(self): return np.stack([self.l, self.r])


# ---- instruments ----
def ep(m, d, vel=1.0):
    """Soft electric piano (two-operator FM with a fading bell)."""
    t = t_(d + 1.2); f = hz(m)
    idx = 1.6 * np.exp(-t * 5) + .25
    s = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * t))
    s += .25 * np.sin(2 * np.pi * 2 * f * t) * np.exp(-t * 6)
    env = np.minimum(t / .004, 1) * np.exp(-t * 1.6) * np.clip((d + 1.2 - t) / 1.2, 0, 1)
    return s * env * vel * (1 + .06 * np.sin(2 * np.pi * 4.5 * t))


def pad(ms, d):
    t = t_(d); s = np.zeros(len(t))
    for m in ms:
        for det in (-.11, 0, .13):
            ph = 2 * np.pi * hz(m) * (1 + det / 100) * t + rng.random() * 6
            s += 2 * ((ph / (2 * np.pi)) % 1) - 1
    s = lp(s / (len(ms) * 3), 1400)
    env = np.minimum(t / .5, 1) * np.minimum((d - t) / .6, 1).clip(0, 1)
    return s * env


def pluck(m, d=.35, vel=1.0):
    t = t_(d); f = hz(m)
    s = np.sin(2 * np.pi * f * t + 1.2 * np.exp(-t * 18) * np.sin(2 * np.pi * 3 * f * t))
    return s * np.minimum(t / .002, 1) * np.exp(-t * 9) * vel


def bell(m, d=1.6, vel=1.0):
    t = t_(d); f = hz(m)
    s = np.sin(2 * np.pi * f * t + 2.2 * np.exp(-t * 3) * np.sin(2 * np.pi * 3.5 * f * t))
    return s * np.minimum(t / .002, 1) * np.exp(-t * 3.2) * vel


def bass(m, d=.22):
    t = t_(d); f = hz(m)
    s = np.tanh(1.8 * (np.sin(2 * np.pi * f * t) + .35 * np.sin(4 * np.pi * f * t)))
    return lp(s * np.minimum(t / .003, 1) * np.exp(-t * 4) * np.clip((d - t) / .03, 0, 1), 900)


def kick(vel=1.0):
    t = t_(.45)
    ph = 2 * np.pi * np.cumsum(46 + 120 * np.exp(-t * 32)) / SR
    return (np.sin(ph) * np.exp(-t * 7.5) + .3 * np.exp(-t * 300) * rng.standard_normal(len(t))) * vel


def clap(vel=1.0):
    t = t_(.3); n = rng.standard_normal(len(t))
    env = sum(np.exp(-np.clip(t - o, 0, None) * 60) * (t >= o) for o in (0, .011, .022)) + .6 * np.exp(-t * 14)
    return bp(n * env, 900, 3200) * vel * .9


def hat(open_=False, vel=1.0):
    t = t_(.25 if open_ else .05)
    return hp(rng.standard_normal(len(t)), 7500) * np.exp(-t * (14 if open_ else 80)) * vel


def noise_sweep(d, f0, f1, shape='rise'):
    t = t_(d); out = np.zeros(len(t)); seg = int(.02 * SR)
    n = rng.standard_normal(len(t))
    for i in range(0, len(t), seg):
        x = i / len(t); f = f0 * (f1 / f0) ** x
        out[i:i + seg] = bp(n[max(0, i - seg):i + seg], f * .7, min(f * 1.4, SR / 2 - 100))[-len(n[i:i + seg]):]
    env = (t / d) ** 2 if shape == 'rise' else np.sin(np.pi * t / d) ** 2
    return out * env


def boom():
    t = t_(2.4)
    ph = 2 * np.pi * np.cumsum(32 + 60 * np.exp(-t * 6)) / SR
    crash = hp(rng.standard_normal(len(t)), 4000) * np.exp(-t * 1.6) * .35
    return np.sin(ph) * np.exp(-t * 1.5) + crash


def shutter():
    s = np.zeros(int(.12 * SR))
    for o, v in ((0, 1), (.045, .7)):
        t = t_(.03); c = bp(rng.standard_normal(len(t)), 1800, 7000) * np.exp(-t * 160) * v
        i = int(o * SR); s[i:i + len(c)] += c
    return s


def key_click(v=1.0):
    t = t_(.03)
    return bp(rng.standard_normal(len(t)), 2500, 9000) * np.exp(-t * 260) * v


# ---- the arrangement ----
PROG = [  # (pad/ep voicing, bass root)
    ([53, 57, 60, 64], 41),       # Fmaj7
    ([55, 59, 62, 64], 43),       # G6
    ([52, 55, 59, 62], 40),       # Em7
    ([57, 60, 64, 67, 71], 45),   # Am9
]
LEAD = [  # per chord: (beat in bar, midi, beats)
    [(0, 72, .5), (.5, 69, .5), (1, 72, .5), (1.5, 76, 1.5), (3, 74, .5), (3.5, 72, .5)],
    [(0, 71, .5), (.5, 67, .5), (1, 71, .5), (1.5, 74, 1.5), (3, 76, .5), (3.5, 74, .5)],
    [(0, 71, .5), (.5, 67, .5), (1, 71, .5), (1.5, 76, 1), (2.5, 79, 1.5)],
    [(0, 76, 1), (1, 74, .5), (1.5, 72, 1), (2.5, 71, .5), (3, 72, 1)],
]
END = [([53, 57, 60, 64, 67], 41), ([55, 59, 62, 64, 69], 43), ([60, 64, 67, 71, 74], 36)]  # Fmaj9, G6/9, Cmaj9

keys, pads, lead, low, drums, fx = (Bus() for _ in range(6))
kicks = []

def chord_at(bar): return PROG[bar % 4]

# Intro and build: soft piano under the hook (0-3 s), then energy builds with the loop words (3-7.5 s)
for bar in range(4):
    vs, root = chord_at(bar)
    for m in vs: keys.add(bar * 2, ep(m, 1.9, .32 if bar < 2 else .38), pan=(m % 5 - 2) / 6)
    pads.add(bar * 2, pad(vs, 2.0), .75 if bar else .6)
    if bar >= 1:
        for k in range(16 if bar < 3 else 32):           # arp: 8ths, then 16ths as it builds
            step = .25 if bar < 3 else .125
            at = bar * 2 + k * step
            if at < 3 or at >= 7.25: continue
            m = vs[k % len(vs)] + 12
            lead.add(at, pluck(m, .3, .35 + .25 * ((at - 3) / 4.25)), pan=.35 if k % 2 else -.35)
for i in range(9):                                       # a kick under every loop word, 3.0-7.0
    at = 3 + i * .5; kicks.append(at); drums.add(at, kick(.8 + .02 * i))
for i in range(int((7.25 - 3) / .25)):
    at = 3 + i * .25; drums.add(at, hat(False, .25 + .1 * (i % 2 == 0)), pan=.3)
for at in (5.5, 6.5): drums.add(at, clap(.7))
for i in range(12):                                      # snare roll into the drop
    at = 6.5 + i * (.75 / 12); drums.add(at, clap(.25 + .5 * i / 12))
fx.add(5.4, noise_sweep(1.85, 300, 9000), .5)
fx.add(7.5, boom(), .9)                                  # "Meet Chipmunk."
for m in [57, 60, 64, 67, 71]: keys.add(7.5, ep(m, .5, .45))

# Groove: 8-16 s and 19.5-24 s, filtered while the prompt is typed (16-19.5 s)
def groove(t0, t1, with_lead):
    b = t0
    while b < t1 - 1e-6:
        bar = int(b // 2)
        vs, root = chord_at(bar)
        beat = round((b - bar * 2) / .5)
        kicks.append(b); drums.add(b, kick(1.0))
        drums.add(b + .25, hat(True, .32), pan=.25)
        for s16 in range(4): drums.add(b + s16 * .125, hat(False, .14 + .1 * (s16 == 2)), pan=-.2)
        if beat in (1, 3): drums.add(b, clap(.8))
        low.add(b + .25, bass(root + (12 if beat == 3 else 0)), .8)
        if beat == 0:
            for m in vs: keys.add(b, ep(m, 1.0, .34), pan=(m % 5 - 2) / 6)
            pads.add(b, pad(vs, 2.0), .3)
            if with_lead:
                for off, m, dur in LEAD[bar % 4]:
                    if b + off * .5 < t1: lead.add(b + off * .5, bell(m + 12, max(.4, dur * .5 + .3), .32), pan=.15)
        if beat == 2:
            for m in vs[1:]: keys.add(b, ep(m, .45, .22), pan=-(m % 5 - 2) / 6)
        b += .5

groove(8.0, 19.0, with_lead=False)
groove(19.5, 24.0, with_lead=True)
fx.add(18.95, key_click(1.2), .9)                        # the Send click
fx.add(19.0, noise_sweep(.55, 800, 7000, 'swell'), .55)  # whoosh into the payoff
fx.add(19.5, boom() * .5, .6)

# Sound effects on the video's moments
# the old way, on a laptop (3-7.5 s): screenshot, Alt+Tab, paste, type, and the tab reloading
def tock():
    t = t_(.08); s = np.sin(2 * np.pi * 1300 * t) * np.exp(-t * 70) * .6; c = key_click(.5); s[:len(c)] += c * .5; return s
def pop():
    t = t_(.12); return np.sin(2 * np.pi * np.cumsum(500 + 900 * np.exp(-t * 40)) / SR) * np.exp(-t * 30)
for at in (3.31, 5.81): fx.add(at, shutter(), .7, pan=-.2)
for at in (3.5, 5.0, 6.0): fx.add(at, tock(), .5, pan=.1); fx.add(at + .02, noise_sweep(.3, 1500, 5000, 'swell'), .18)
for at in (4.05, 6.55): fx.add(at, pop(), .45)
for a, b in ((4.5, 4.95), (6.62, 6.98)):
    for at in np.arange(a, b, .06): fx.add(at + rng.uniform(-.01, .01), key_click(.5 + .3 * rng.random()), .45, pan=.3)
t_r = t_(.6); fx.add(7.02, np.sin(2 * np.pi * np.cumsum(600 * np.exp(-t_r * 4) + 80) / SR) * np.exp(-t_r * 4) * .5, .5)
for b in (10.0, 12.5, 14.75): fx.add(b + .66, shutter(), .7, pan=-.2)      # each screenshot
for i, at in enumerate(np.arange(16.9, 18.2, .065)):                       # typing the prompt
    fx.add(at + rng.uniform(-.012, .012), key_click(.55 + .3 * rng.random()), .5, pan=.3)
for at, ms in ((22.2, [84, 88]), (23.0, [88, 91])):                         # each "Fixed"
    for k, m in enumerate(ms): fx.add(at + k * .09, bell(m, 1.4, .38), pan=.2)

# End card: big warm chords that resolve home to C as the logo lands, then fade
drums.add(24.0, kick(1.0)); fx.add(24.0, boom() * .45, .6)
for i, (vs, root) in enumerate(END):
    at = 24 + i * 2
    for m in vs: keys.add(at, ep(m, 1.9 if i < 2 else 4.0, .4), pan=(m % 5 - 2) / 6)
    pads.add(at, pad(vs, 2.2 if i < 2 else 6.0), .95)
    low.add(at, bass(root, 1.6 if i < 2 else 3.5) * .7)
for k, m in enumerate([79, 83, 84, 88, 91]):              # sparkle as the brand appears
    lead.add(26.2 + k * .07, bell(m + 12, 1.2, .18), pan=(-.5 + k * .25))

# ---- mix ----
t = np.arange(N) / SR
duck = np.ones(N)
for k in kicks:
    m = t >= k
    duck[m] = np.minimum(duck[m], 1 - .55 * np.exp(-(t[m] - k) / .11))
music = keys.st() * .9 + pads.st() * 1.5 + lead.st() * .8 + low.st() * .75
music = music * duck
mix = music + drums.st() * .75
# low-pass the band while the prompt is typed, so the keys and click stand out
filt = np.stack([lp(mix[0], 700, 4), lp(mix[1], 700, 4)])
w = np.clip((t - 16.3) / .4, 0, 1) * (1 - np.clip((t - 18.95) / .1, 0, 1))
gap = 1 - np.clip((t - 19.05) / .05, 0, 1) * (1 - np.clip((t - 19.5) / .02, 0, 1))   # a breath before the payoff
mix = (mix * (1 - w) + filt * w) * gap
mix = mix + fx.st()
mix *= np.clip((DUR - t) / 2.2, 0, 1) ** 1.5                                          # fade out
mix = np.tanh(mix * 1.1)
mix = mix / np.max(np.abs(mix)) * .89
os.makedirs(os.path.dirname(OUT), exist_ok=True)
wavfile.write(OUT, SR, (mix.T * 32767).astype(np.int16))
print('wrote', os.path.relpath(OUT), f'{DUR:.0f} s')
