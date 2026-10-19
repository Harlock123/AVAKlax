#!/usr/bin/env python3
"""Synthesises AVAKlax's sound effects and tunes into ../src/AVAKlax/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and noise voices. The title
plays the Rondo alla Turca (Mozart's Piano Sonata No. 11, K. 331, 1783); the tune in play is a new composition. The
voices ("Klax!", and "Put down the phone, and play Klax!") are spoken by the formant voice in speech.py. Run it from
anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAKlax", "Sounds")
RNG = random.Random(1989)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def echo(samples, delay=0.11, feedback=0.35, repeats=3):
    """The voice in a big hall: quieter copies of itself, later and later."""
    d = int(RATE * delay)
    out = samples + [0.0] * (d * repeats)
    for k in range(1, repeats + 1):
        g = feedback ** k
        for i, v in enumerate(samples):
            out[i + d * k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest, a list of notes a chord; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, list):
            out.extend(mix(*[tone(d, hz(n) * mult, kind=kind, release=release, gain=gain / len(note) ** 0.5) for n in note]))
        else:
            out.extend(tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


def kick(seconds, gain=1.0):
    return cat(tone(0.07, 150, 45, "sine", attack=0.001, release=1.0, gain=gain), silence(max(0, seconds - 0.07)))


def hat(seconds, gain=0.3):
    return cat(tone(0.03, 9000, kind="noise", attack=0.001, release=2.0, gain=gain), silence(max(0, seconds - 0.03)))


def snare(seconds, gain=0.6):
    return cat(mix(tone(0.12, 3000, kind="noise", attack=0.001, release=1.6, gain=gain),
                   tone(0.08, 220, 160, "tri", attack=0.001, release=1.0, gain=gain * 0.6)), silence(max(0, seconds - 0.12)))


def rondo_alla_turca():
    """
    The Rondo alla Turca (Mozart, K. 331, 1783): its famous opening, the A minor theme, twice through, the second time
    an octave up, over a left hand of oom-pah chords.
    """
    s, e, q = 0.25, 0.5, 1.0
    theme = [("B4", s), ("A4", s), ("G#4", s), ("A4", s),
             ("C5", e), ("-", e), ("D5", s), ("C5", s), ("B4", s), ("C5", s),
             ("E5", e), ("-", e), ("F5", s), ("E5", s), ("D#5", s), ("E5", s),
             ("B5", s), ("A5", s), ("G#5", s), ("A5", s), ("B5", s), ("A5", s), ("G#5", s), ("A5", s),
             ("C6", q), ("A5", e), ("C6", e),
             ("B5", e), ("A5", e), ("G5", e), ("A5", e),
             ("B5", e), ("A5", e), ("G5", e), ("A5", e),
             ("B5", e), ("A5", e), ("G5", e), ("F#5", e),
             ("E5", q), ("-", q)]
    am, e7, c, g = ["C4", "E4"], ["G#3", "D4"], ["E4", "G4"], ["D4", "G4"]
    bass = [("-", 1.0),
            ("A2", e), (am, e), ("A2", e), (am, e),
            ("A2", e), (am, e), ("A2", e), (am, e),
            ("E2", e), (e7, e), ("E2", e), (e7, e),
            ("A2", e), (am, e), ("A2", e), (am, e),
            ("G2", e), (g, e), ("C3", e), (c, e),
            ("G2", e), (g, e), ("C3", e), (c, e),
            ("G2", e), (g, e), ("B2", e), (e7, e),
            ("E2", e), (e7, e), ("E2", q)]
    first = mix(line(126, theme, "pulse", 1, 0.45, 0.5), line(126, bass, "tri", 1, 0.5, 0.4))
    second = mix(line(126, theme, "square", 2, 0.3, 0.5), line(126, theme, "pulse", 1, 0.25, 0.5), line(126, bass, "tri", 1, 0.5, 0.4))
    ending = mix(line(126, [("B4", s), ("A4", s), ("G#4", s), ("A4", s), ("C5", e), ("A4", e), (["A4", "C5", "E5"], 2)], "pulse", 1, 0.45, 0.4),
                 line(126, [("-", 1.0), ("A2", 1.0), ("A2", 2)], "tri", 1, 0.5, 0.3))
    return cat(first, second, ending)


def play_tune():
    """The tune in play: a tune of the game's own, over a bouncing bass and a drum machine."""
    e, q = 0.5, 1.0
    lead = [("E5", e), ("G5", e), ("A5", q), ("G5", e), ("A5", e), ("C6", e), ("A5", e),
            ("G5", e), ("E5", e), ("D5", q), ("E5", 2),
            ("E5", e), ("G5", e), ("A5", q), ("G5", e), ("A5", e), ("C6", e), ("D6", e),
            ("E6", e), ("D6", e), ("C6", e), ("A5", e), ("A5", 2),
            ("C6", e), ("C6", e), ("B5", e), ("A5", e), ("G5", q), ("E5", q),
            ("F5", e), ("F5", e), ("E5", e), ("D5", e), ("E5", 2),
            ("C6", e), ("C6", e), ("B5", e), ("A5", e), ("G5", q), ("A5", e), ("B5", e),
            ("C6", e), ("B5", e), ("G5", e), ("E5", e), ("A5", 2)]
    # The bass: each bar's root, and the octave above it, bouncing.
    bass = []
    for root in ["A2", "A2", "F2", "G2", "A2", "A2", "F2", "E2", "F2", "F2", "D2", "E2", "F2", "G2", "E2", "A2"]:
        octave = root[0] + str(int(root[-1]) + 1)
        bass += [(root, e), (octave, e), (root, e), (octave, e)]
    beat = 60.0 / 132
    drums = []
    for bar in range(8):
        drums += mix(cat(kick(beat), snare(beat), kick(beat * 0.5), kick(beat * 0.5), snare(beat)),
                     cat(*[hat(beat * 0.5, 0.25 if k % 2 else 0.4) for k in range(8)]))
    melody = line(132, lead, "pulse", 1, 0.45, 0.55)
    return mix(melody, line(132, bass, "tri", 1, 0.6, 0.3), drums)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # The belt: a tile flipping over, a knock of wood on rubber.
    write("Flip.wav", mix(tone(0.05, 260, 170, "sine", attack=0.001, release=2.0), tone(0.02, 2400, kind="noise", attack=0.001, release=2.0, gain=0.3)), 0.2)
    # A tile landing on the paddle; tossed back up the belt; flipped into the bin.
    write("Catch.wav", cat(tone(0.03, 700, kind="square", attack=0.001, release=1.5), tone(0.05, 1050, kind="pulse", release=1.2, gain=0.8)), 0.22)
    write("Toss.wav", mix(tone(0.28, 250, 1100, "square", release=0.8, gain=0.5), tone(0.28, 800, 4000, "noise", release=0.6, gain=0.4)), 0.24)
    write("Thunk.wav", mix(tone(0.16, 140, 55, "sine", attack=0.001, release=1.2), tone(0.06, 900, kind="noise", attack=0.001, release=2.0, gain=0.35)), 0.42)
    # A tile missed: down past the paddle, and a crash at the bottom.
    write("Drop.wav", cat(tone(0.32, 900, 90, "square", release=0.3, gain=0.5), tone(0.3, 600, 80, "noise", attack=0.001, release=1.5)), 0.32)
    write("Blocked.wav", tone(0.16, 98, kind="square", release=0.4), 0.2)
    # A Klax: a bright arpeggio. Two at once, or a chain: a longer one, higher.
    write("Klax.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.6) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.25, hz("C7"), kind="square", vibrato=0.02, release=1.0)), 0.26)
    write("Combo.wav", cat(*[tone(0.045, hz(n), kind="pulse", release=0.6) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "D6", "F#6", "A6", "D7"]],
                          mix(tone(0.4, hz("D7"), kind="square", vibrato=0.03, release=1.0), tone(0.4, hz("A6"), kind="pulse", release=1.0, gain=0.6))), 0.28)
    # The drop meter one short of full; full.
    write("Danger.wav", cat(*[cat(tone(0.12, 880, kind="square", release=0.2), tone(0.12, 660, kind="square", release=0.2)) for _ in range(3)]), 0.22)
    write("Lost.wav", cat(*[tone(0.3, hz(n), hz(n) * 0.94, "square", vibrato=0.02, release=0.6) for n in ["G4", "F#4", "F4"]], tone(0.8, hz("E4"), hz("E4") * 0.9, "square", vibrato=0.03, release=0.9)), 0.3)
    # The wave select: a blip; a warp: a long rush upward.
    write("Select.wav", tone(0.05, 1200, kind="pulse", release=0.8), 0.18)
    write("Warp.wav", mix(tone(1.0, 120, 2400, "square", release=0.4, gain=0.5), tone(1.0, 300, 6000, "noise", release=0.5, gain=0.3),
                          cat(*[tone(0.0625, hz(n), kind="pulse", release=0.5, gain=0.6) for n in ["C4", "G4", "C5", "G5", "C6", "G6", "C7", "G7"] * 2])), 0.3)
    # The empty squares counted off at the end of a wave.
    write("Bonus.wav", tone(0.04, 1568, kind="pulse", release=1.0), 0.16)

    # The voices.
    klax = say("K L AE1 K S !", rate=RATE, pitch=88, speed=0.9, size=1.15, seed=3)
    under = say("K L AE1 K S !", rate=RATE, pitch=58, speed=0.9, size=1.3, seed=5)
    write("SayKlax.wav", echo(mix(klax, [s * 0.5 for s in under]), 0.09, 0.3, 3), 0.55)
    start = say("P UH1 T D AW1 N DH AH F OW1 N , AE N D P L EY1 K L AE1 K S !", rate=RATE, pitch=96, speed=0.95, size=1.1, seed=9)
    write("SayStart.wav", echo(start, 0.08, 0.2, 2), 0.5)

    # Music.
    write("Music.wav", play_tune(), 0.24)
    write("TitleTune.wav", cat(rondo_alla_turca(), silence(1.2)), 0.3)
    write("WaveClear.wav", mix(line(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), (["C6", "E6"], 2)], "pulse"),
                               line(150, [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 2)], "tri")), 0.4)
    write("GameOver.wav", mix(line(72, [("E5", 1), ("C5", 1), ("A4", 1), ("G#4", 1), ("A4", 3)], "tri"), line(72, [("A2", 2), ("E2", 2), ("A1", 3)], "tri")), 0.42)


if __name__ == "__main__":
    main()
