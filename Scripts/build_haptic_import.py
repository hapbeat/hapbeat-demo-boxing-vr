"""Flattens the Boxing cue table (Assets/Boxing/Haptics/boxing-cues.json) into Saved/HapticImport.json for
BoxingHapticImport (Unity's JsonUtility reads no dictionaries): one item per cue with its clip, route gain,
clip intensity, sound and sound volume (sfx volume x sound intensity).

The EventMap plays one one-shot clip per entry: a wrist cue at the acting hand (route at = hand; the import
writes the same clip / gain / intensity into left_<cue> and right_<cue>), hit_body at the neck (at = pos_neck).
So a cue has exactly one route and one clip, and at most one sound; v2 extras (variants, variation, clips /
sounds lists, levelMap, distanceFalloff, emit) and loops have no counterpart in the game and are reported as
errors rather than dropped silently. sfx null (or not decided yet) = no sound.
"""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CUES_PATH = ROOT / 'Assets/Boxing/Haptics/boxing-cues.json'
CLIPS_DIR = ROOT / 'Assets/Boxing/Haptics/stream-clips'
SFX_DIR = ROOT / 'Assets/Boxing/Haptics/sounds'
OUT = ROOT / 'Saved' / 'HapticImport.json'
# BoxingFeedback.Cues, with the position of each cue's route.
CUES = {'punch_glove_soft': 'hand', 'punch_glove_hard': 'hand', 'punch_body_soft': 'hand', 'punch_body_hard': 'hand',
        'guard_glove_soft': 'hand', 'guard_glove_hard': 'hand', 'guard_arm_soft': 'hand', 'guard_arm_hard': 'hand',
        'hit_body_soft': 'pos_neck', 'hit_body_hard': 'pos_neck'}
UNSUPPORTED = ('variants', 'variation', 'distanceFalloff', 'emit')

table = json.loads(CUES_PATH.read_text(encoding='utf-8'))
err, items = [], []
if sorted(table.get('cues', {})) != sorted(CUES):
    err.append(f'cues must be exactly {list(CUES)}')
for cue, entry in table.get('cues', {}).items():
    if cue not in CUES:
        continue
    for key in UNSUPPORTED:
        if entry.get(key):
            err.append(f'{cue}: {key} is not supported by the Boxing EventMap')
    routes = entry.get('haptics')
    if not routes:
        err.append(f'{cue}: needs one haptic route (an EventMap entry always plays)')
        continue
    if len(routes) != 1 or routes[0].get('at') != CUES[cue] or 'clips' in routes[0] or routes[0].get('levelMap'):
        err.append(f'{cue}: exactly one route with at = {CUES[cue]} and a single clip (no levelMap)')
        continue
    route = routes[0]
    clip = route.get('clip')
    info = table.get('clips', {}).get(clip)
    if info is None:
        err.append(f'{cue}: clip {clip} is not in clips')
        continue
    if info.get('loop'):
        err.append(f'{cue}: clip {clip} loops; every Boxing cue is a one-shot')
        continue
    if not (CLIPS_DIR / f'{clip}.wav').is_file():
        err.append(f'{cue}: {CLIPS_DIR / (clip + ".wav")} missing')
        continue
    sfx, sound, volume = entry.get('sfx'), '', 1.0
    if sfx is not None:
        if 'sounds' in sfx or sfx.get('levelMap'):
            err.append(f'{cue}: sfx must name a single sound (no sounds list / levelMap)')
            continue
        sound = sfx.get('sound') or ''
        if sound and not (SFX_DIR / f'{sound}.wav').is_file():
            err.append(f'{cue}: {SFX_DIR / (sound + ".wav")} missing')
            continue
        volume = float(sfx.get('volume', 1)) * float(table.get('sounds', {}).get(sound, {}).get('intensity', 1))
    items.append({'cue': cue, 'clip': clip, 'gain': float(route.get('gain', 1)), 'intensity': float(info.get('intensity', 1)),
                  'sound': sound, 'volume': volume})
if err:
    print('\n'.join(err), file=sys.stderr)
    raise SystemExit(1)
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps({'items': items}, ensure_ascii=False, indent=1), encoding='utf-8')
print(f'HAPTIC_IMPORT_PLAN {len(items)} cues -> {OUT}')
