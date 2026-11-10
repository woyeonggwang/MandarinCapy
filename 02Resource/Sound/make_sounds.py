"""카피바라는 일하고 싶어! 사운드 합성 (44.1kHz, 16bit mono WAV)
 - sfx_step.wav    : 걷기 '뽈' (짧고 둥근 탭)
 - sfx_boing.wav   : 폭발 '띠용' (펑 + 스프링 떨림)
 - sfx_fruit.wav   : 과일 '띠링' (두 음 차임, 코드에서 단계별로 음높이를 올림)
 - bgm_farm.wav    : 농장 BGM (C장조, 108bpm, 16마디 루프, 끊김 없이 이어지게 꼬리를 앞에 겹침)
"""
import sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter

SR = 44100
out = sys.argv[1] if len(sys.argv) > 1 else "."
rng = np.random.default_rng(3)


def t_of(sec):
    return np.arange(int(SR * sec)) / SR


def save(name, x, peak=0.89):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    wavfile.write(f"{out}/{name}", SR, (x * 32767).astype(np.int16))


def lowpass(x, fc, order=2):
    b, a = butter(order, fc / (SR / 2), "low")
    return lfilter(b, a, x)


def highpass(x, fc, order=2):
    b, a = butter(order, fc / (SR / 2), "high")
    return lfilter(b, a, x)


def fade(x, a=0.003, r=0.01):
    n = len(x)
    e = np.ones(n)
    na, nr = int(SR * a), int(SR * r)
    e[:na] = np.linspace(0, 1, na)
    e[n - nr:] = np.linspace(1, 0, nr)
    return x * e


def sweep(f, t):
    """시간에 따라 변하는 주파수 배열 f → 위상 적분 사인"""
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


# ---------- 걷기 '뽈' ----------
t = t_of(0.09)
f = 330 + 260 * np.exp(-t / 0.018)          # 590Hz → 330Hz 로 빠르게 떨어짐 (뽈)
env = (1 - np.exp(-t / 0.002)) * np.exp(-t / 0.028)
step = sweep(f, t) * env
step += 0.25 * lowpass(rng.standard_normal(len(t)), 1800) * np.exp(-t / 0.006)  # 살짝 '톡'
save("sfx_step.wav", fade(step))

# ---------- 폭발 '띠용' ----------
t = t_of(0.85)
pop = lowpass(rng.standard_normal(len(t)), 900, 3) * np.exp(-t / 0.045)        # 퐁(펑) 저음 덩어리
thump = np.sin(2 * np.pi * (60 + 90 * np.exp(-t / 0.03)) * t) * np.exp(-t / 0.08)
# 스프링 떨림: 중심 음이 위로 미끄러진 뒤 큰 비브라토가 점점 줄어듦
center = 260 + 220 * (1 - np.exp(-t / 0.06))
vib = 0.28 * np.exp(-t / 0.35) * np.sin(2 * np.pi * 11 * t)
f = center * (1 + vib)
ph = 2 * np.pi * np.cumsum(f) / SR
boing = np.sin(ph) + 0.45 * np.sin(2 * ph) + 0.25 * np.sin(3 * ph) + 0.12 * np.sin(4 * ph)  # 입 하프 같은 음색
boing *= (1 - np.exp(-t / 0.01)) * np.exp(-t / 0.3)
x = 0.9 * pop + 0.8 * thump + 0.75 * np.concatenate([np.zeros(int(SR * 0.02)), boing[:-int(SR * 0.02)]])
save("sfx_boing.wav", fade(x, r=0.05))


# ---------- 과일 '띠링' ----------
def bell(freq, dur, decay):
    t = t_of(dur)
    s = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2.0 * t) * np.exp(-t / (decay * 0.4)) \
        + 0.18 * np.sin(2 * np.pi * freq * 3.01 * t) * np.exp(-t / (decay * 0.25))
    return s * (1 - np.exp(-t / 0.002)) * np.exp(-t / decay)


t = t_of(0.5)
x = np.zeros(len(t))
a = bell(1318.5, 0.5, 0.16)                 # E6
b = bell(1975.5, 0.43, 0.2)                 # B6 (띠-링)
x += 0.8 * a
o = int(SR * 0.07)
x[o:o + len(b)] += 0.9 * b[:len(x) - o]
save("sfx_fruit.wav", fade(x, r=0.04))

# ---------- BGM ----------
BPM = 108
BEAT = 60 / BPM
BARS = 16
LOOP = int(SR * BEAT * 4 * BARS)
mix = np.zeros(LOOP + SR * 3)   # 꼬리 여유 → 마지막에 앞으로 겹쳐 루프


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def add(x, start_beat, sig, gain):
    s = int(round(start_beat * BEAT * SR))
    mix[s:s + len(sig)] += gain * sig


def marimba(m, dur_beats):
    f = midi(m)
    d = max(0.25, dur_beats * BEAT) + 0.5
    t = t_of(d)
    s = np.sin(2 * np.pi * f * t) + 0.22 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t / 0.05) \
        + 0.08 * np.sin(2 * np.pi * f * 9.9 * t) * np.exp(-t / 0.02)
    return s * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.42)


def bass(m, dur_beats):
    f = midi(m)
    t = t_of(dur_beats * BEAT + 0.15)
    tri = 2 / np.pi * np.arcsin(np.sin(2 * np.pi * f * t))
    s = 0.8 * tri + 0.4 * np.sin(2 * np.pi * f * t)
    e = (1 - np.exp(-t / 0.004)) * np.exp(-t / 0.35)
    return fade(s * e, r=0.03)


def pluck(m):
    f = midi(m)
    t = t_of(0.35)
    sq = np.sign(np.sin(2 * np.pi * f * t))
    s = lowpass(sq, 2200) * 0.5 + np.sin(2 * np.pi * f * t)
    return s * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.09)


def shaker():
    t = t_of(0.07)
    return highpass(rng.standard_normal(len(t)), 6000) * np.exp(-t / 0.018)


def kick():
    t = t_of(0.18)
    return np.sin(2 * np.pi * (55 + 80 * np.exp(-t / 0.02)) * t) * np.exp(-t / 0.08)


R = None
mel = [
    # A
    [(76, 1), (79, .5), (76, .5), (72, 1), (74, 1)],
    [(81, 1.5), (79, .5), (77, 1), (76, 1)],
    [(74, 1), (79, .5), (77, .5), (76, 1), (74, 1)],
    [(76, 1), (72, 1), (67, 1), (R, 1)],
    [(69, .5), (72, .5), (76, 1), (81, 1), (79, 1)],
    [(77, .5), (76, .5), (77, 1), (81, 1), (84, 1)],
    [(83, 1), (81, .5), (79, .5), (77, 1), (74, 1)],
    [(72, 1.5), (76, .5), (72, 2)],
    # B
    [(81, .5), (79, .5), (77, .5), (79, .5), (81, 1), (84, 1)],
    [(83, .5), (81, .5), (79, .5), (81, .5), (83, 1), (86, 1)],
    [(88, 1), (86, .5), (84, .5), (83, 1), (79, 1)],
    [(81, 1.5), (79, .5), (76, 2)],
    [(77, 1), (81, 1), (84, 1), (81, 1)],
    [(79, 1), (83, 1), (86, 1), (83, 1)],
    [(84, 1), (79, .5), (76, .5), (79, 1), (76, 1)],
    [(74, 1), (76, .5), (77, .5), (79, 1), (R, 1)],
]
# 코드: (베이스 루트, 화음 구성음)
C, F, G, Am, Em = (48, [60, 64, 67]), (53, [60, 65, 69]), (43, [59, 62, 67]), (45, [60, 64, 69]), (40, [59, 64, 67])
chords = [C, F, G, C, Am, F, G, C, F, G, Em, Am, F, G, C, G]

for bar in range(BARS):
    b0 = bar * 4
    beat = b0
    for m, d in mel[bar]:
        if m is not None:
            add(mix, beat, marimba(m, d), 0.55)
        beat += d
    root, tones = chords[bar]
    add(mix, b0, bass(root, 1.5), 0.5)
    add(mix, b0 + 2, bass(root + 7, 1.5), 0.45)
    for k in (1, 3):
        for tn in tones:
            add(mix, b0 + k, pluck(tn), 0.12)
    for k in range(8):
        add(mix, b0 + k * 0.5 + 0.5 * (k % 2 == 0) * 0, shaker(), 0.05 if k % 2 == 0 else 0.09)
    add(mix, b0, kick(), 0.35)
    add(mix, b0 + 2, kick(), 0.3)

mix = lowpass(mix, 9000)
loop = mix[:LOOP].copy()
tail = mix[LOOP:]
loop[:len(tail)] += tail           # 끝에서 넘친 소리를 앞에 겹쳐 이음매 없앰
save("bgm_farm.wav", loop, peak=0.8)
print("loop sec", LOOP / SR)

# ======================================================================
# 2026-11-09 추가: 아이템 소리, 날리기 소리 (BGM 뒤에 두어 BGM 난수 순서가 바뀌지 않게)
# ======================================================================
rng2 = np.random.default_rng(11)


def bandnoise(n, lo, hi, order=2):
    b, a = butter(order, [lo / (SR / 2), hi / (SR / 2)], "band")
    return lfilter(b, a, rng2.standard_normal(n))


def modes(freqs, decays, gains, dur):
    t = t_of(dur)
    s = np.zeros(len(t))
    for f, d, g in zip(freqs, decays, gains):
        s += g * np.sin(2 * np.pi * f * t) * np.exp(-t / d)
    return s * (1 - np.exp(-t / 0.0015))


# ---------- 아이템 '뾰로롱' (과일 '띠링'과 다르게: 빠른 상승 아르페지오 + 반짝임) ----------
x = np.zeros(int(SR * 0.6))
for i, m in enumerate([72, 76, 79, 84, 88]):          # C5 E5 G5 C6 E6
    f = midi(m)
    t = t_of(0.3)
    sq = lowpass(np.sign(np.sin(2 * np.pi * f * t)), 3500)
    s = (0.45 * sq + 0.6 * np.sin(2 * np.pi * f * t)) * (1 - np.exp(-t / 0.002)) * np.exp(-t / 0.11)
    o = int(SR * 0.055 * i)
    x[o:o + len(s)] += s[:len(x) - o]
t = t_of(0.6)
x += 0.25 * np.sin(2 * np.pi * 2637 * t) * (0.5 + 0.5 * np.sin(2 * np.pi * 18 * t)) * np.exp(-((t - 0.3) / 0.12) ** 2)
save("sfx_item.wav", fade(x, r=0.05))

# ---------- 휙 (날아가는 바람 소리, 모든 물체 공통) ----------
n = int(SR * 0.32)
t = t_of(0.32)
env = np.sin(np.pi * np.clip(t / 0.32, 0, 1)) ** 2
# 밴드가 위로 쓸려 올라갔다 내려오는 느낌: 두 대역을 섞어 시간에 따라 비율 변경
lo = bandnoise(n, 400, 1400)
hi = bandnoise(n, 1400, 4200)
k = np.sin(np.pi * np.clip(t / 0.32, 0, 1))
save("sfx_whoosh.wav", fade((lo * (1 - k) + hi * k) * env, a=0.01, r=0.03))

# ---------- 재질별 '날릴 때' 타격음 ----------
# 돌: 낮고 단단한 '딱' + 자갈 긁힘
t = t_of(0.25)
stone = modes([210, 540, 1230, 2310], [0.05, 0.03, 0.018, 0.01], [0.8, 0.6, 0.45, 0.3], 0.25)
stone += 0.5 * bandnoise(len(t), 1500, 5000) * np.exp(-t / 0.02)
save("hit_stone.wav", fade(stone))

# 나무: 속이 빈 '톡'
wood = modes([380, 980, 1690], [0.07, 0.035, 0.02], [1.0, 0.5, 0.25], 0.25)
wood += 0.15 * bandnoise(len(t), 800, 3000) * np.exp(-t / 0.006)
save("hit_wood.wav", fade(wood))

# 과일: 말랑한 '뽁' (위로 튀는 음 + 촉촉한 클릭)
t = t_of(0.16)
f = 260 + 700 * (1 - np.exp(-t / 0.02))
fruit_hit = sweep(f, t) * (1 - np.exp(-t / 0.002)) * np.exp(-t / 0.035)
fruit_hit += 0.3 * bandnoise(len(t), 2000, 6000) * np.exp(-t / 0.004)
save("hit_fruit.wav", fade(fruit_hit))

# 건초: '사락' 지푸라기 부스럭 (거친 알갱이)
t = t_of(0.3)
grain = (rng2.random(len(t)) < 0.02).astype(float)
grain = lowpass(grain, 6000) * 4
hay = (bandnoise(len(t), 2500, 7500) * 0.6 + grain) * (1 - np.exp(-t / 0.004)) * np.exp(-t / 0.09)
save("hit_hay.wav", fade(hay))

# 호박: 속 빈 낮은 '퉁'
pump = modes([150, 330, 610], [0.12, 0.06, 0.03], [1.0, 0.55, 0.25], 0.35)
pump += 0.2 * bandnoise(int(SR * 0.35), 300, 1200) * np.exp(-t_of(0.35) / 0.01)
save("hit_pumpkin.wav", fade(pump))

# 금속(폭탄): '팅' 쇳소리 (비정수배 배음)
metal = modes([1180, 1180 * 2.76, 1180 * 5.4, 1180 * 8.9], [0.25, 0.12, 0.06, 0.03], [1.0, 0.5, 0.3, 0.15], 0.5)
save("hit_metal.wav", fade(metal, r=0.05))

# 바구니(등나무): 가벼운 '바스락' + 작은 나무 소리
t = t_of(0.22)
wick = 0.7 * bandnoise(len(t), 1800, 6000) * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.05)
wick += 0.6 * modes([620, 1500], [0.03, 0.015], [1.0, 0.4], 0.22)
save("hit_wicker.wav", fade(wick))

# 시계: 짧은 '따르릉' (종을 빠르게 떨림)
t = t_of(0.45)
bellsig = modes([2350, 2350 * 2.1, 2350 * 3.3], [0.2, 0.1, 0.05], [1.0, 0.4, 0.2], 0.45)
trill = 0.55 + 0.45 * np.sign(np.sin(2 * np.pi * 24 * t))
save("hit_clock.wav", fade(lowpass(bellsig * trill, 8000), r=0.05))
print("extra sounds ok")
