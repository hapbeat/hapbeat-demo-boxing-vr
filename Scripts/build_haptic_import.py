"""Flattens the Boxing cue table (Assets/Boxing/Haptics/boxing-cues.json) into Saved/HapticImport.json for
BoxingHapticImport (Unity's JsonUtility reads no dictionaries): one item per game cue (BoxingFeedback.Cues) with
its clips, route gain, clip intensities, sounds with their volumes (sfx volume x sound intensity) and how one is
picked.

The table has one cue per contact kind (punch_glove, punch_body, guard_glove, guard_arm, hit_body) with the
variants soft and hard (DEC-091). The game cue <cue>_<variant> (punch_glove_soft ...) plays the variant
<cue>:<variant>: a variant inherits the cue's sfx / haptics / variation it does not write (shared) and
replaces the ones it writes (separate); sfxVolume / hapticsGain scale what it inherits. By convention the sfx
and the haptics each live either on the cue (shared by both variants) or on every variant (separate); a variant
that writes a field the cue also has simply overrides it (DEC-091), and a field missing from the cue must be on
every variant. This expansion (and the name mapping) lives only here, the game keeps its cue names.

A game cue plays one route: a wrist cue at the acting hand (route at = hand; the import writes it into
left_<cue> and right_<cue>), hit_body at the neck (at = pos_neck). The route names its clip (clip) or several
(clips), its sfx one sound (sound) or several (sounds); per impact the game picks one clip and one sound by
variation.pick (random, the default, never repeats the previous one; roundRobin goes in order). Other v2 extras
(other variation fields, rampTo / rampCurve / rampSteps, levelMap, distanceFalloff, emit) and loops have no
counterpart in the game and are reported as errors rather than dropped silently. sfx null (or not decided yet)
= no sound.
"""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CUES_PATH = ROOT / 'Assets/Boxing/Haptics/boxing-cues.json'
CLIPS_DIR = ROOT / 'Assets/Boxing/Haptics/stream-clips'
SFX_DIR = ROOT / 'Assets/Boxing/Haptics/sounds'
OUT = ROOT / 'Saved' / 'HapticImport.json'
# The table's cues with the position of their route, and their variants.
CUES = {'punch_glove': 'hand', 'punch_body': 'hand', 'guard_glove': 'hand', 'guard_arm': 'hand', 'hit_body': 'pos_neck'}
VARIANTS = ('soft', 'hard')
# BoxingFeedback.Cues: <cue>_<variant>, in this order.
GAME_CUES = [f'{cue}_{variant}' for cue in CUES for variant in VARIANTS]
UNSUPPORTED = ('distanceFalloff', 'emit', 'rampTo', 'rampCurve', 'rampSteps')
PICKS = ('random', 'roundRobin')


def table_cue(game_cue):
    """punch_glove_soft -> punch_glove:soft (the cue table's name of a game cue)."""
    cue, _, variant = game_cue.rpartition('_')
    return f'{cue}:{variant}'


def expand(entry, variant):
    """The variant as the game plays it: the cue's fields it does not write, its own ones, the multipliers
    applied. Returns (resolved entry, errors)."""
    v, err = entry['variants'][variant], []
    resolved = {k: val for k, val in entry.items() if k != 'variants'}
    for key, val in v.items():
        if key not in ('sfxVolume', 'hapticsGain'):
            resolved[key] = val
    if 'sfxVolume' in v:
        if 'sfx' in v:
            err.append('sfxVolume scales an inherited sfx; this variant has its own')
        elif resolved.get('sfx') is not None:
            resolved['sfx'] = dict(resolved['sfx'], volume=float(resolved['sfx'].get('volume', 1)) * float(v['sfxVolume']))
    if 'hapticsGain' in v:
        if 'haptics' in v:
            err.append('hapticsGain scales inherited haptics; this variant has its own')
        elif resolved.get('haptics'):
            resolved['haptics'] = [dict(r, gain=float(r.get('gain', 1)) * float(v['hapticsGain'])) for r in resolved['haptics']]
    return resolved, err


def plan(table, clips_dir=CLIPS_DIR, sfx_dir=SFX_DIR):
    """(items in GAME_CUES order, errors)."""
    err, items = [], []
    cues = table.get('cues', {})
    if sorted(cues) != sorted(CUES):
        err.append(f'cues must be exactly {list(CUES)}')
    for cue, at in CUES.items():
        entry = cues.get(cue)
        if entry is None:
            continue
        if sorted(entry.get('variants') or {}) != sorted(VARIANTS):
            err.append(f'{cue}: variants must be exactly {list(VARIANTS)}')
            continue
        for key in ('sfx', 'haptics'):
            own = [v for v in VARIANTS if key in entry['variants'][v]]
            if key not in entry and len(own) != len(VARIANTS):
                err.append(f'{cue}: {key} is on {", ".join(own) or "no variant"}; separate = on every variant')
        for variant in VARIANTS:
            name = f'{cue}:{variant}'
            resolved, problems = expand(entry, variant)
            err.extend(f'{name}: {p}' for p in problems)
            for key in UNSUPPORTED:
                if resolved.get(key) or entry['variants'][variant].get(key):
                    err.append(f'{name}: {key} is not supported by the Boxing EventMap')
            variation = resolved.get('variation') or {}
            for key in variation:
                if key != 'pick' and variation[key]:
                    err.append(f'{name}: variation.{key} is not supported by the Boxing EventMap (only pick)')
            pick = variation.get('pick', 'random')
            if pick not in PICKS:
                err.append(f'{name}: variation.pick must be one of {list(PICKS)}')
                continue
            routes = resolved.get('haptics')
            if not routes:
                err.append(f'{name}: needs one haptic route (an EventMap entry always plays)')
                continue
            route = routes[0]
            if len(routes) != 1 or route.get('at') != at or ('clip' in route) == ('clips' in route) or route.get('levelMap'):
                err.append(f'{name}: exactly one route with at = {at} naming either clip or clips (no levelMap)')
                continue
            clips = list(route['clips']) if 'clips' in route else [route['clip']]
            if not clips:
                err.append(f'{name}: clips is empty')
                continue
            intensities, bad = [], False
            for clip in clips:
                info = table.get('clips', {}).get(clip)
                if info is None:
                    err.append(f'{name}: clip {clip} is not in clips'); bad = True
                elif info.get('loop'):
                    err.append(f'{name}: clip {clip} loops; every Boxing cue is a one-shot'); bad = True
                elif not (Path(clips_dir) / f'{clip}.wav').is_file():
                    err.append(f'{name}: {Path(clips_dir) / (clip + ".wav")} missing'); bad = True
                else:
                    intensities.append(float(info.get('intensity', 1)))
            if bad:
                continue
            sfx, sounds = resolved.get('sfx'), []
            if sfx is not None:
                if ('sound' in sfx and 'sounds' in sfx) or sfx.get('levelMap'):
                    err.append(f'{name}: sfx names either sound or sounds (no levelMap)')
                    continue
                sounds = list(sfx.get('sounds') or []) if 'sounds' in sfx else [sfx['sound']] if sfx.get('sound') else []
                missing = [s for s in sounds if not (Path(sfx_dir) / f'{s}.wav').is_file()]
                if missing:
                    err.extend(f'{name}: {Path(sfx_dir) / (s + ".wav")} missing' for s in missing)
                    continue
            volumes = [float(sfx.get('volume', 1)) * float(table.get('sounds', {}).get(s, {}).get('intensity', 1)) for s in sounds]
            items.append({'cue': f'{cue}_{variant}', 'clips': clips, 'gain': float(route.get('gain', 1)), 'intensities': intensities,
                          'sounds': sounds, 'volumes': volumes, 'pick': pick})
    return items, err


def main():
    items, err = plan(json.loads(CUES_PATH.read_text(encoding='utf-8')))
    if err:
        print('\n'.join(err), file=sys.stderr)
        raise SystemExit(1)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({'items': items}, ensure_ascii=False, indent=1), encoding='utf-8')
    print(f'HAPTIC_IMPORT_PLAN {len(items)} cues -> {OUT}')


if __name__ == '__main__':
    main()
