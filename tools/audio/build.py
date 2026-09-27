#!/usr/bin/env python3
"""Front Page Foundry — the whole soundscape, built from arithmetic (M12).

Music: three original ragtime pieces composed here from a seed (AABBACCDD, oom-pah left hand,
syncopated right hand, trio in the subdominant), rendered as three stems each — piano, banjo,
cornet — through an additive synthesiser, then "pressed to shellac": band-limited, with wow,
flutter, crackle and hiss. Original compositions, so nothing to license.

SFX: every effect in GDD §12 synthesised from noise and oscillators.

    uv run --with numpy python3 tools/audio/build.py            # writes assets/audio/*.ogg + manifest.json
    uv run --with numpy python3 tools/audio/build.py --check    # same seed twice → identical bytes

Needs ffmpeg for the Ogg Vorbis encode. Deterministic: the same seeds give the same files.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import random
import subprocess
import sys
import tempfile
import wave
from dataclasses import dataclass

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "assets", "audio")

# ---------------------------------------------------------------------------------------------
# Synthesis
# ---------------------------------------------------------------------------------------------


@dataclass
class Note:
    start: float  # seconds
    dur: float  # seconds held
    midi: int
    vel: float  # 0..1
    pan: float = 0.0  # -1..1


def hz(midi: int) -> float:
    return 440.0 * 2 ** ((midi - 69) / 12)


def _add(buf: np.ndarray, at: float, sig: np.ndarray, pan: float) -> None:
    i = int(at * SR)
    n = min(len(sig), len(buf) - i)
    if n <= 0:
        return
    left = math.sqrt(0.5 * (1 - pan))
    right = math.sqrt(0.5 * (1 + pan))
    buf[i : i + n, 0] += sig[:n] * left
    buf[i : i + n, 1] += sig[:n] * right


def piano(buf: np.ndarray, note: Note, rng: np.random.Generator) -> None:
    f = hz(note.midi)
    tau = min(2.2, max(0.25, 1.1 * (261.6 / f) ** 0.55))
    total = min(note.dur + 0.35, tau * 4)
    n = int(total * SR)
    t = np.arange(n) / SR
    sig = np.zeros(n)
    bright = 0.35 + 0.65 * note.vel
    for k in range(1, 9):
        fk = f * k * (1 + 0.00045 * k * k)
        if fk > SR * 0.45:
            break
        a = (bright ** (k - 1)) / (k**1.15)
        sig += a * np.sin(2 * math.pi * fk * t + rng.uniform(0, 2 * math.pi))
    env = np.exp(-t / tau)
    off = t > note.dur
    env[off] *= np.exp(-(t[off] - note.dur) / 0.06)
    attack = np.minimum(1.0, t / 0.003)
    hammer = rng.standard_normal(n) * np.exp(-t / 0.004) * 0.25
    sig = (sig * env * attack + hammer) * (0.25 + 0.75 * note.vel)
    _add(buf, note.start, sig, note.pan)


def banjo(buf: np.ndarray, note: Note, rng: np.random.Generator) -> None:
    f = hz(note.midi)
    total = min(note.dur + 0.3, 0.9)
    n = int(total * SR)
    t = np.arange(n) / SR
    sig = np.zeros(n)
    for k in range(1, 12):
        fk = f * k
        if fk > SR * 0.45:
            break
        sig += np.sin(2 * math.pi * fk * t + rng.uniform(0, 2 * math.pi)) / (k**0.7)
    env = np.exp(-t / 0.22) * np.minimum(1.0, t / 0.002)
    pluck = rng.standard_normal(n) * np.exp(-t / 0.002) * 0.6
    _add(buf, note.start, (sig * env + pluck) * 0.5 * note.vel, note.pan)


def cornet(buf: np.ndarray, note: Note, rng: np.random.Generator) -> None:
    f = hz(note.midi) * (1 + 0.0002)
    total = note.dur + 0.12
    n = int(total * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.004 * np.sin(2 * math.pi * 5.5 * t) * np.minimum(1.0, t / 0.25)
    phase = 2 * math.pi * f * np.cumsum(vib) / SR
    sig = np.zeros(n)
    for k in range(1, 10):
        if f * k > SR * 0.45:
            break
        sig += np.sin(k * phase) / k * (1.0 if k % 2 else 0.8)
    env = np.minimum(1.0, t / 0.04)
    off = t > note.dur
    env[off] *= np.exp(-(t[off] - note.dur) / 0.05)
    _add(buf, note.start, sig * env * 0.4 * note.vel, note.pan)


def normalize(buf: np.ndarray, peak: float = 0.9) -> np.ndarray:
    m = np.max(np.abs(buf))
    return buf * (peak / m) if m > 0 else buf


def shellac(buf: np.ndarray, rng: np.random.Generator, crackle: float = 1.0) -> np.ndarray:
    """A 1920s record: 250–4500 Hz, wow and flutter, needle crackle, hiss, a little saturation."""
    n = len(buf)
    t = np.arange(n) / SR
    warp = t + 0.0035 * np.sin(2 * math.pi * 0.55 * t) + 0.0005 * np.sin(2 * math.pi * 6.5 * t + 1.0)
    warped = np.stack([np.interp(warp, t, buf[:, c]) for c in range(2)], axis=1)
    spec = np.fft.rfft(warped, axis=0)
    freqs = np.fft.rfftfreq(n, 1 / SR)
    freqs[0] = 1e-3
    h = 1 / np.sqrt(1 + (250 / freqs) ** 4) / np.sqrt(1 + (freqs / 4500) ** 4)
    out = np.fft.irfft(spec * h[:, None], n=n, axis=0)
    pops = np.zeros(n)
    count = int(n / SR * 9 * crackle)
    idx = rng.integers(0, n - 64, size=count)
    amp = rng.uniform(0.05, 0.5, size=count) * rng.choice([-1, 1], size=count)
    for i, a in zip(idx, amp):
        k = np.arange(48)
        pops[i : i + 48] += a * np.exp(-k / 6) * np.cos(k * 0.9)
    hiss = rng.standard_normal(n) * 0.0035
    out += (pops + hiss)[:, None] * 0.6
    return np.tanh(out * 1.4) / 1.4


def write_wav(path: str, buf: np.ndarray) -> None:
    pcm = np.clip(buf, -1, 1)
    data = (pcm * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def encode(wav: str, ogg: str) -> None:
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", ogg], check=True)


# ---------------------------------------------------------------------------------------------
# The composer
# ---------------------------------------------------------------------------------------------

PROGRESSIONS = [
    ["I", "I", "IV", "IV", "I", "V7", "V7", "I", "I", "I7", "IV", "IV", "I", "V7", "I", "I"],
    ["I", "I", "V7", "V7", "V7", "V7", "I", "I", "I", "VI7", "II7", "V7", "I", "V7", "I", "I"],
    ["I", "IV", "I", "V7", "I", "IV", "I", "V7", "I", "I7", "IV", "IV", "I", "V7", "I", "I"],
    ["I", "VI7", "II7", "V7", "I", "VI7", "II7", "V7", "I", "I7", "IV", "IV", "II7", "V7", "I", "I"],
]

RHYTHMS = [  # in eighth notes, summing to 4 (a 2/4 bar)
    [1.5, 1.5, 1],
    [1, 0.5, 1, 1.5],
    [0.5, 1, 0.5, 1, 1],
    [1, 1, 1, 1],
    [2, 1, 1],
    [0.5, 0.5, 1, 0.5, 0.5, 1],
    [1.5, 0.5, 1, 1],
    [0.5, 1.5, 1, 1],
    [1, 0.5, 0.5, 1, 1],
]

SEMITONES = {"I": 0, "II": 2, "IV": 5, "V": 7, "VI": 9}


def chord_tones(symbol: str, key: int) -> list[int]:
    """Pitch classes of a roman-numeral chord in a key (major triads; 7 adds the flat seventh)."""
    degree = symbol.rstrip("7")
    root = (key + SEMITONES[degree]) % 12
    tones = [root, (root + 4) % 12, (root + 7) % 12]
    if symbol.endswith("7"):
        tones.append((root + 10) % 12)
    return tones


def nearest(pc: int, around: int) -> int:
    """The midi note of pitch class pc nearest to `around`."""
    best = None
    for octave in range(0, 11):
        m = octave * 12 + pc
        if best is None or abs(m - around) < abs(best - around):
            best = m
    return best


class Rag:
    def __init__(self, seed: int, bpm: int):
        self.rng = random.Random(seed)
        self.bpm = bpm
        self.piano: list[Note] = []
        self.banjo: list[Note] = []
        self.cornet: list[Note] = []
        self.length = 0.0

    def beat(self) -> float:
        return 60 / self.bpm

    def strain(self, at: float, key: int, prog: list[str], motif: list[list[float]], cornet: bool, bright: bool) -> float:
        """Sixteen bars of rag from `at`; returns the time it ends."""
        eighth = self.beat() / 2
        melody_reg = 74 if bright else 70
        last = melody_reg
        for bar, symbol in enumerate(prog):
            tones = chord_tones(symbol, key)
            bar_at = at + bar * 4 * eighth
            # Left hand: bass on the beats, chord on the off-beats (oom-pah).
            root = nearest(tones[0], 40)
            fifth = nearest(tones[2 % len(tones)], 40)
            for k in range(2):
                bass = root if k == 0 else (fifth if self.rng.random() < 0.7 else root)
                self.piano.append(Note(bar_at + k * 2 * eighth, eighth * 0.9, bass, 0.8, -0.3))
                self.piano.append(Note(bar_at + k * 2 * eighth, eighth * 0.9, bass - 12, 0.5, -0.3))
                chord_at = bar_at + (k * 2 + 1) * eighth
                for pc in tones[:3]:
                    self.piano.append(Note(chord_at, eighth * 0.6, nearest(pc, 55), 0.5, -0.15))
                for pc in tones[:4]:
                    self.banjo.append(Note(chord_at + self.rng.uniform(0, 0.012), eighth * 0.5, nearest(pc, 62), 0.7, 0.35))
            # Right hand: a syncopated line on chord tones and neighbours, a motif that recurs.
            cadence = bar == 15 or bar == 7
            if cadence:
                rhythm = [2, 2] if bar == 15 else [1, 1, 2]
            elif bar % 4 == 0:
                rhythm = motif[(bar // 4) % len(motif)]
            else:
                rhythm = self.rng.choice(RHYTHMS)
            pos = 0.0
            for i, d in enumerate(rhythm):
                on_beat = pos in (0.0, 2.0)
                if on_beat or self.rng.random() < 0.7:
                    pc = self.rng.choice(tones)
                    target = last + self.rng.choice([-2, -1, 0, 1, 2]) * 2
                    m = nearest(pc, max(melody_reg - 7, min(melody_reg + 9, target)))
                else:
                    m = last + self.rng.choice([-2, -1, 1, 2])
                if abs(m - last) > 9:
                    m = last + (3 if m > last else -3)
                last = m
                start = bar_at + pos * eighth
                vel = 0.95 if on_beat else 0.75
                if cadence and i == len(rhythm) - 1:
                    vel = 1.0
                self.piano.append(Note(start, d * eighth * 0.95, m, vel, 0.3))
                if self.rng.random() < 0.35 or cadence:
                    self.piano.append(Note(start, d * eighth * 0.95, m - 12, vel * 0.7, 0.2))
                if cornet:
                    self.cornet.append(Note(start, d * eighth * 0.92, m - 12, vel, 0.1))
                pos += d
        return at + 16 * 4 * eighth

    def compose(self, key: int) -> None:
        motif_a = [self.rng.choice(RHYTHMS) for _ in range(2)]
        motif_b = [self.rng.choice(RHYTHMS) for _ in range(2)]
        motif_c = [self.rng.choice(RHYTHMS) for _ in range(2)]
        motif_d = [self.rng.choice(RHYTHMS) for _ in range(2)]
        pa = self.rng.choice(PROGRESSIONS)
        pb = self.rng.choice(PROGRESSIONS)
        pc = self.rng.choice(PROGRESSIONS)
        pd = self.rng.choice(PROGRESSIONS)
        trio = (key + 5) % 12
        t = 0.0
        # A four-bar introduction: a descending run into the first strain.
        eighth = self.beat() / 2
        run = [key + 12 * 6 + d for d in (12, 11, 9, 7, 5, 4, 2, 0)]
        for i, m in enumerate(run):
            self.piano.append(Note(t + i * eighth, eighth * 0.9, m, 0.9, 0.2))
            self.piano.append(Note(t + i * eighth, eighth * 0.9, m - 12, 0.6, 0.1))
        t += 8 * eighth
        form = [
            ("A", key, pa, motif_a, False, False),
            ("A", key, pa, motif_a, False, True),
            ("B", key, pb, motif_b, False, True),
            ("B", key, pb, motif_b, True, True),
            ("A", key, pa, motif_a, True, True),
            ("C", trio, pc, motif_c, False, False),
            ("C", trio, pc, motif_c, True, True),
            ("D", trio, pd, motif_d, True, True),
            ("D", trio, pd, motif_d, True, True),
        ]
        for _, k, prog, motif, horn, bright in form:
            t = self.strain(t, k, prog, motif, horn, bright)
        # Final chord.
        for pc_ in chord_tones("I", trio):
            self.piano.append(Note(t, 1.6, nearest(pc_, 62), 1.0, 0.0))
            self.piano.append(Note(t, 1.6, nearest(pc_, 50), 0.9, -0.2))
            self.cornet.append(Note(t, 1.2, nearest(pc_, 67), 0.9, 0.1))
        self.length = t + 2.2


def render(notes: list[Note], voice, length: float, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    buf = np.zeros((int(length * SR) + SR, 2))
    for n in notes:
        voice(buf, n, rng)
    return buf


def build_rag(name: str, seed: int, key: int, bpm: int, work: str, manifest: dict) -> None:
    rag = Rag(seed, bpm)
    rag.compose(key)
    rng = np.random.default_rng(seed + 100)
    stems = {
        "piano": normalize(render(rag.piano, piano, rag.length, seed + 1), 0.8),
        "banjo": normalize(render(rag.banjo, banjo, rag.length, seed + 2), 0.55),
        "cornet": normalize(render(rag.cornet, cornet, rag.length, seed + 3), 0.6),
    }
    for stem, buf in stems.items():
        pressed = shellac(buf, rng, crackle=1.0 if stem == "piano" else 0.3)
        # Fade the tail so a looping player can hand over cleanly.
        tail = int(1.5 * SR)
        pressed[-tail:] *= np.linspace(1, 0, tail)[:, None]
        wav = os.path.join(work, f"{name}_{stem}.wav")
        write_wav(wav, pressed)
        encode(wav, os.path.join(OUT, f"{name}_{stem}.ogg"))
    manifest["music"].append({"name": name, "seconds": round(rag.length, 2), "bpm": bpm, "notes": len(rag.piano) + len(rag.banjo) + len(rag.cornet)})
    print(f"{name}: {rag.length:.1f} s, {len(rag.piano)} piano / {len(rag.banjo)} banjo / {len(rag.cornet)} cornet notes")


# ---------------------------------------------------------------------------------------------
# Effects
# ---------------------------------------------------------------------------------------------


def env_exp(n: int, tau: float) -> np.ndarray:
    return np.exp(-np.arange(n) / SR / tau)


def tone(f: float, n: int, shape: str = "sine") -> np.ndarray:
    t = np.arange(n) / SR
    if shape == "saw":
        return sum(np.sin(2 * math.pi * f * k * t) / k for k in range(1, 12))
    if shape == "square":
        return sum(np.sin(2 * math.pi * f * k * t) / k for k in range(1, 12, 2))
    return np.sin(2 * math.pi * f * t)


def lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    spec = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(spec / np.sqrt(1 + (freqs / cutoff) ** 4), n=len(x))


def highpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    spec = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / SR)
    freqs[0] = 1e-3
    return np.fft.irfft(spec / np.sqrt(1 + (cutoff / freqs) ** 4), n=len(x))


def stereo(x: np.ndarray) -> np.ndarray:
    return np.stack([x, x], axis=1)


def sfx(rng: np.random.Generator) -> dict[str, tuple[np.ndarray, bool]]:
    out: dict[str, tuple[np.ndarray, bool]] = {}
    noise = lambda n: rng.standard_normal(n)

    # The press: a heavy platen comes down, twice, with a low thump.
    n = int(0.7 * SR)
    thump = tone(52, n) * env_exp(n, 0.12) + tone(38, n) * env_exp(n, 0.2) * 0.6
    clack = lowpass(noise(n), 2500) * env_exp(n, 0.02)
    second = np.zeros(n)
    second[int(0.16 * SR) :] = (lowpass(noise(n - int(0.16 * SR)), 1800) * env_exp(n - int(0.16 * SR), 0.03))
    out["press"] = (stereo(normalize(thump + clack * 0.8 + second * 0.6, 0.85)), False)

    # Telegraph: a burst of Morse on a sounder, clicks rather than tones.
    n = int(1.7 * SR)
    tele = np.zeros(n)
    t0 = 0.0
    for symbol in "-.-. --.-":
        if symbol == " ":
            t0 += 0.16
            continue
        d = 0.05 if symbol == "." else 0.14
        i = int(t0 * SR)
        m = min(int(d * SR), n - i)
        click = highpass(noise(m), 1200) * env_exp(m, 0.006)
        release = np.zeros(m)
        release[-int(0.004 * SR) :] = 1
        tele[i : i + m] += click + highpass(noise(m), 1500) * env_exp(m, 0.004) * np.roll(release, 0) * 0.7
        t0 += d + 0.07
    out["telegraph"] = (stereo(normalize(tele, 0.7)), False)

    # Typewriter: one key strike, and the carriage bell.
    n = int(0.14 * SR)
    key = highpass(noise(n), 900) * env_exp(n, 0.008) + tone(3100, n) * env_exp(n, 0.012) * 0.4
    out["typewriter"] = (stereo(normalize(key, 0.6)), False)
    n = int(0.9 * SR)
    bell = (tone(2480, n) + tone(2480 * 2.71, n) * 0.4 + tone(2480 * 4.2, n) * 0.15) * env_exp(n, 0.25)
    out["bell"] = (stereo(normalize(bell, 0.5)), False)

    # Pencil on newsprint for a plan; a soft thud for something built; paper turned for the Courier.
    n = int(0.16 * SR)
    scratch = highpass(noise(n), 2000) * (0.5 + 0.5 * np.sin(2 * math.pi * 38 * np.arange(n) / SR)) * env_exp(n, 0.08)
    out["pencil"] = (stereo(normalize(scratch, 0.45)), False)
    n = int(0.25 * SR)
    thud = tone(90, n) * env_exp(n, 0.05) + lowpass(noise(n), 900) * env_exp(n, 0.02) * 0.8
    out["thud"] = (stereo(normalize(thud, 0.7)), False)
    n = int(0.45 * SR)
    rustle = highpass(lowpass(noise(n), 6000), 700) * (np.sin(np.linspace(0, math.pi, n)) ** 0.6) * (1 + 0.6 * np.sin(2 * math.pi * 11 * np.arange(n) / SR))
    out["rustle"] = (stereo(normalize(rustle, 0.5)), False)

    # Loops: belt rattle, a machine chuffing, steam, the wheel creaking in the river.
    n = 2 * SR
    t = np.arange(n) / SR
    ticks = np.zeros(n)
    for i in range(0, n, int(SR / 6)):
        m = min(int(0.02 * SR), n - i)
        ticks[i : i + m] += highpass(noise(m), 800) * env_exp(m, 0.005)
    rumble = lowpass(noise(n), 160) * 2.5
    out["belt"] = (stereo(normalize(ticks * 0.8 + rumble, 0.5)), True)
    chuff = np.zeros(n)
    for i in range(0, n, int(SR / 3)):
        m = min(int(0.12 * SR), n - i)
        chuff[i : i + m] += lowpass(noise(m), 700) * env_exp(m, 0.04)
    hum = tone(55, n) * 0.5 + tone(110, n) * 0.25
    out["machine"] = (stereo(normalize(chuff + hum * 0.6, 0.5)), True)
    n = 3 * SR
    t = np.arange(n) / SR
    hiss = highpass(noise(n), 2500) * (0.7 + 0.3 * np.sin(2 * math.pi * 0.4 * t))
    out["steam"] = (stereo(normalize(hiss, 0.35)), True)
    n = 4 * SR
    t = np.arange(n) / SR
    water = lowpass(noise(n), 1200) * (0.8 + 0.2 * np.sin(2 * math.pi * 0.7 * t))
    creak = np.zeros(n)
    for i in (int(0.4 * SR), int(2.5 * SR)):
        m = int(0.5 * SR)
        f = 240 + 90 * np.sin(np.linspace(0, math.pi, m))
        creak[i : i + m] += np.sin(2 * math.pi * np.cumsum(f) / SR) * np.sin(np.linspace(0, math.pi, m)) * (0.4 + 0.2 * np.sign(np.sin(2 * math.pi * 31 * np.arange(m) / SR)))
    out["wheel"] = (stereo(normalize(water * 0.5 + creak * 0.6, 0.45)), True)

    # A motor truck passing, a locomotive's whistle far off, a barge horn.
    n = int(1.6 * SR)
    t = np.arange(n) / SR
    engine = tone(48, n, "saw") * (0.6 + 0.4 * np.sin(2 * math.pi * 9 * t)) * np.sin(np.linspace(0, math.pi, n)) ** 0.5
    out["truck"] = (stereo(normalize(lowpass(engine, 900), 0.35)), False)
    n = int(1.4 * SR)
    t = np.arange(n) / SR
    whistle = (tone(622, n) + tone(740, n) * 0.8 + tone(932, n) * 0.5) * np.minimum(1, t / 0.1) * np.minimum(1, (n / SR - t) / 0.3)
    out["whistle"] = (stereo(normalize(lowpass(whistle + noise(n) * 0.05, 4000), 0.4)), False)
    n = int(1.2 * SR)
    t = np.arange(n) / SR
    horn = (tone(98, n, "square") + tone(147, n, "square") * 0.6) * np.minimum(1, t / 0.15) * np.minimum(1, (n / SR - t) / 0.25)
    out["horn"] = (stereo(normalize(lowpass(horn, 1500), 0.4)), False)
    return out


# ---------------------------------------------------------------------------------------------


def build(check: bool) -> None:
    os.makedirs(OUT, exist_ok=True)
    manifest: dict = {"music": [], "sfx": []}
    with tempfile.TemporaryDirectory() as work:
        rags = [("rag_courier", 1920, 5, 96), ("rag_carvell", 1921, 10, 88), ("rag_foundry", 1922, 0, 104)]
        if "--sfx-only" in sys.argv:
            manifest["music"] = json.load(open(os.path.join(OUT, "manifest.json")))["music"] if os.path.exists(os.path.join(OUT, "manifest.json")) else [
                {"name": n, "seconds": 0, "bpm": b, "notes": 0} for n, _, _, b in rags]
        else:
            for name, seed, key, bpm in rags:
                build_rag(name, seed, key, bpm, work, manifest)
        rng = np.random.default_rng(7)
        for name, (buf, loop) in sfx(rng).items():
            wav = os.path.join(work, f"sfx_{name}.wav")
            write_wav(wav, buf)
            encode(wav, os.path.join(OUT, f"sfx_{name}.ogg"))
            manifest["sfx"].append({"name": name, "loop": loop, "seconds": round(len(buf) / SR, 3)})
        with open(os.path.join(OUT, "manifest.json"), "w") as f:
            json.dump(manifest, f, indent=2)
    if check:
        digest = hashlib.sha256()
        for fn in sorted(os.listdir(OUT)):
            if fn.endswith(".ogg"):
                digest.update(open(os.path.join(OUT, fn), "rb").read())
        print("sha256 of all ogg:", digest.hexdigest())


if __name__ == "__main__":
    build("--check" in sys.argv)
