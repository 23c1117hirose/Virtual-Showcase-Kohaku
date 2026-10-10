import os
import sys
import wave

import aud
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
grass_path, wet_path, ref_dir, out_dir = argv
os.makedirs(out_dir, exist_ok=True)
RATE = 44100


def load_mono(path):
    sound = aud.Sound(path)
    rate, _ = sound.specs
    data = sound.data()
    x = data.mean(axis=1) if data.ndim > 1 else data
    x = x.astype(np.float64)
    assert int(rate) == RATE, rate
    return x


def envelope(x, hop=441):  # 10 ms
    n = len(x) // hop
    return np.sqrt((x[: n * hop].reshape(n, hop) ** 2).mean(axis=1))


def onsets(x, thr_ratio=0.3, isolation=0.3):
    env = envelope(x)
    thr = env.max() * thr_ratio
    peaks = []
    for i in range(8, len(env) - 8):
        if env[i] > thr and env[i] == env[i - 8:i + 9].max():
            peaks.append(i)
    return env, peaks


def a_weight(f):
    f2 = f * f
    num = (12194.0 ** 2) * f2 * f2
    den = (f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194.0 ** 2)
    return num / np.maximum(den, 1e-30)


def a_level(x):
    n = 1 << int(np.ceil(np.log2(len(x))))
    spec = np.fft.rfft(x, n)
    f = np.fft.rfftfreq(n, 1.0 / RATE)
    return 10 * np.log10((np.abs(spec * a_weight(f)) ** 2).sum() / n + 1e-30)


def band_shares(x):
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    f = np.fft.rfftfreq(len(x), 1.0 / RATE)
    tot = spec.sum() + 1e-30
    return (spec[f < 300].sum() / tot, spec[(f >= 300) & (f < 1500)].sum() / tot, spec[f >= 1500].sum() / tot,
            (f * spec).sum() / tot)


def highpass(x, cutoff):
    alpha = 1.0 - np.exp(-2 * np.pi * cutoff / RATE)
    low = 0.0
    y = np.empty_like(x)
    for i, v in enumerate(x):
        low += alpha * (v - low)
        y[i] = v - low
    return y


def resample(x, factor):
    """factor > 1: faster and higher (a smaller creature), shorter."""
    n = int(len(x) / factor)
    return np.interp(np.arange(n) * factor, np.arange(len(x)), x)


def fade(x, fade_in, fade_out):
    y = x.copy()
    a = max(1, int(fade_in * RATE))
    b = max(1, int(fade_out * RATE))
    y[:a] *= 0.5 - 0.5 * np.cos(np.pi * np.arange(a) / a)
    y[-b:] *= 0.5 + 0.5 * np.cos(np.pi * np.arange(b) / b)
    return y


def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0


target_land = a_level(read_wav(os.path.join(ref_dir, "frog_pecha.wav")))
target_rustle = a_level(read_wav(os.path.join(ref_dir, "grass_rustle.wav")))
print("target A-levels (synthetic clips at their own scale): land %.1f dB, rustle %.1f dB" % (target_land, target_rustle))


def soft(v):
    return 0.95 * np.tanh(v / 0.95)


def finish(x, target_level, name):
    # gain search: after a soft limiter (transients are rounded, never clipped) the A-weighted level meets the target
    low, high = 0.05, 60.0
    for _ in range(40):
        mid = (low + high) / 2
        if a_level(soft(x * mid)) < target_level:
            low = mid
        else:
            high = mid
    y = soft(x * (low + high) / 2)
    sh = band_shares(y)
    print("  %-28s dur=%.3fs peak=%.2f  <300Hz %4.1f%%  300-1500 %4.1f%%  >1500 %4.1f%%  centroid %5.0fHz  A-level %.1f dB" % (
        name, len(y) / RATE, np.abs(y).max(), sh[0] * 100, sh[1] * 100, sh[2] * 100, sh[3], a_level(y)))
    write_wav(os.path.join(out_dir, name + ".wav"), y)


def denoise(x, noise, alpha=1.3, floor=0.12, n=1024, hop=256):
    """Spectral subtraction: removes the steady rain hiss, keeps the step."""
    win = np.hanning(n)
    nframes = (len(noise) - n) // hop
    prof = np.mean([np.abs(np.fft.rfft(noise[i * hop:i * hop + n] * win)) for i in range(max(1, nframes))], axis=0)
    out = np.zeros(len(x) + n)
    norm = np.zeros(len(x) + n)
    for start in range(0, len(x) - n + 1, hop):
        frame = np.fft.rfft(x[start:start + n] * win)
        mag = np.abs(frame)
        clean = np.maximum(mag - alpha * prof, floor * mag)
        out[start:start + n] += np.fft.irfft(clean * np.exp(1j * np.angle(frame)), n) * win
        norm[start:start + n] += win ** 2
    return out[:len(x)] / np.maximum(norm[:len(x)], 1e-6)


def quietest_segment(x, seconds=0.35):
    env = envelope(x)
    w = int(seconds / 0.01)
    best = min(range(0, len(env) - w), key=lambda i: env[i:i + w].mean())
    start = best * 441
    return x[start:start + int(seconds * RATE)], best * 0.01


# ---------------------------------------------------------------- wet road steps -> landing of a small, damp frog
wet = load_mono(wet_path)
env, peaks = onsets(wet, 0.25)
cands = []
for i in peaks:
    t = i * 0.01
    before = env[max(0, i - 30): max(1, i - 5)]
    snr = env[i] / (np.median(before) + 1e-6)
    near = [j for j in peaks if j != i and abs(j - i) < 30 and env[j] > 0.5 * env[i]]
    if not near and t > 0.15 and t < len(wet) / RATE - 0.4:
        cands.append((snr, t))
cands.sort(reverse=True)
print("wet-road step candidates (snr, time):", ", ".join("%.1f@%.2fs" % c for c in cands))
noise_profile, noise_time = quietest_segment(wet)
print("rain-only part used as the noise profile: %.2fs (%.4f rms)" % (noise_time, np.sqrt((noise_profile ** 2).mean())))
for rank, (snr, t) in enumerate(cands[:4], 1):
    start = int((t - 0.03) * RATE)
    seg = wet[max(0, start - 1024): start + int(0.34 * RATE) + 1024]
    seg = seg - seg.mean()
    seg = denoise(seg, noise_profile)     # take the rain hiss away
    seg = seg[1024 if start >= 1024 else 0: 1024 + int(0.34 * RATE)] if start >= 1024 else seg[: int(0.34 * RATE)]
    seg = highpass(seg, 180.0)            # no heavy footfall rumble: it is a small frog
    seg = resample(seg, 1.15)             # a little higher and quicker: a smaller creature
    seg = fade(seg, 0.002, 0.07)
    finish(seg, target_land, "land_wet_%d" % rank)

# ---------------------------------------------------------------- grass walking -> rustle
grass = load_mono(grass_path)
env, peaks = onsets(grass, 0.30)
cands = []
for i in peaks:
    t = i * 0.01
    if t < 0.2 or t > len(grass) / RATE - 0.7:
        continue
    start = int((t - 0.03) * RATE)
    seg = grass[start: start + int(0.5 * RATE)]
    low, mid, high, centroid = band_shares(seg)
    before = env[max(0, i - 30): max(1, i - 5)]
    snr = env[i] / (np.median(before) + 1e-6)
    cands.append((low + 0.3 * mid - 0.2 * min(snr, 5), t, low, centroid, snr))
cands.sort()
chosen = []
for c in cands:
    if all(abs(c[1] - o[1]) > 0.8 for o in chosen):
        chosen.append(c)
    if len(chosen) == 4:
        break
print("grass rustle candidates (time, low share, centroid, snr):",
      ", ".join("%.2fs/%.1f%%/%.0fHz/%.1f" % (c[1], c[2] * 100, c[3], c[4]) for c in chosen))
for rank, c in enumerate(chosen, 1):
    start = int((c[1] - 0.03) * RATE)
    seg = grass[start: start + int(0.52 * RATE)]
    seg = seg - seg.mean()
    seg = highpass(seg, 600.0)            # no footfall thump, only the swish of blades
    seg = resample(seg, 1.15)
    seg = fade(seg, 0.015, 0.16)
    finish(seg, target_rustle, "rustle_grass_%d" % rank)
