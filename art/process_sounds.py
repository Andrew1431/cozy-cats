# Run headless: blender -b --factory-startup --python art/process_sounds.py
# Gives every clip the same treatment (mono, rumble cut, gentle lo-fi low-pass, peak-normalised) so mixed sources sit together.
import aud, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "sound_candidates")
DST = os.path.join(HERE, "..", "unity", "CozyCatsAssets", "Assets", "Audio")
RATE = 44100

MEOWS = {
    "bvmae_859728": "meow_03", "bvmae_859729": "meow_04", "bvmae_859730": "meow_05", "bvmae_859731": "meow_06",
    "bvmae_859734": "meow_09", "bvmae_859720": "meow_16", "bvmae_859721": "meow_17", "bvmae_859722": "meow_18",
    "bvmae_859723": "meow_19", "tabby_512616": "meow_tabby18", "tabby_512624": "meow_tabby21",
}
PURRS = {"cats_835513": "purr_pearl", "cats_858201": "purr_perran", "cats_855880": "purr_minette"}


def process(name, out, peak_db, lowpass, start=None, length=None):
    s = aud.Sound(os.path.join(SRC, name + ".mp3")).rechannel(1).resample(RATE, True)
    if start is not None:
        s = s.limit(start, start + length)
    s = s.highpass(150).lowpass(lowpass)
    if start is not None:
        s = s.fadein(0, 0.03).fadeout(length - 0.03, 0.03)  # tiny fades: purrs loop while held
    # Render to memory first; aud's streaming writer chokes on some filter chains.
    data = np.ascontiguousarray(s.data(), dtype=np.float32)
    peak = float(np.max(np.abs(data))) or 1.0
    data *= (10 ** (peak_db / 20)) / peak
    aud.Sound.buffer(data, RATE).write(os.path.join(DST, out + ".wav"), rate=RATE, channels=aud.CHANNELS_MONO,
            format=aud.FORMAT_S16, container=aud.CONTAINER_WAV, codec=aud.CODEC_PCM)
    print(f"{out}: {len(data) / RATE:.2f}s")


os.makedirs(DST, exist_ok=True)
for k, v in MEOWS.items():
    process(k, v, -1.0, 7000)
for k, v in PURRS.items():
    process(k, v, -4.0, 5000, start=2.0, length=8.0)
