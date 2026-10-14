"""Boxing input for the haptic authoring viewer (repos-tools/hapbeat-haptic-authoring in the Hapbeat SDK
workspace; Hapbeat Studio's Scene tab): turns the PC replay (Saved/HapticClips/Frames + record.json, written
by BoxingHapticRecord) and where the cue table, haptic clips and sounds live into a session, then runs the
tool's build_viewer.py, which cuts the clips into Saved/HapticViewer/. Normally run by
Scripts/record-haptic-clips.ps1.

    haptic_session.py --tool <dir> [--ffmpeg ...] [--keep-frames]
    haptic_session.py --tool <dir> --viewer-only     # rewrite Saved/HapticViewer/viewer-lib.json only

The cue table (Assets/Boxing/Haptics/boxing-cues.json, cue table v2 of the workspace
docs/haptic-authoring-cue-table.md) has one cue per contact kind with the variants soft and hard (DEC-091);
the recording names the moments punch_glove:soft etc. The wrist cues play at the acting hand (left_<cue> /
right_<cue> in the EventMap), hit_body at the neck. Scripts/import-haptic-kit.ps1 applies it to BoxingEventMap
and the scene's cue sounds.
"""
import argparse, json, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RECORD = ROOT / 'Saved' / 'HapticClips'
CUES_PATH = ROOT / 'Assets/Boxing/Haptics/boxing-cues.json'
CLIPS_DIR = ROOT / 'Assets/Boxing/Haptics/stream-clips'
SFX_DIR = ROOT / 'Assets/Boxing/Haptics/sounds'
# The table's cues (variants soft / hard), all one-shots.
CUES = ['punch_glove', 'punch_body', 'guard_glove', 'guard_arm', 'hit_body']

ap = argparse.ArgumentParser()
ap.add_argument('--tool', default=str(ROOT.parents[2] / 'repos-tools' / 'hapbeat-haptic-authoring'),
                help='hapbeat-haptic-authoring checkout (default: the SDK workspace one)')
ap.add_argument('--ffmpeg', default='ffmpeg', help='ffmpeg executable with libx264')
ap.add_argument('--keep-frames', action='store_true')
ap.add_argument('--engine-root', help='ignored (the tool passes the Unreal path to every project)')
ap.add_argument('--viewer-only', action='store_true')
args = ap.parse_args()
tool = Path(args.tool)
assert (tool / 'build_viewer.py').is_file(), f'hapbeat-haptic-authoring not found at {tool} (pass --tool)'

table = json.loads(CUES_PATH.read_text(encoding='utf-8'))
assert sorted(table['cues']) == sorted(CUES), f'cues must be exactly {CUES}'
for name in table['clips']:
    assert (CLIPS_DIR / f'{name}.wav').is_file(), f'clip {name}: {CLIPS_DIR / (name + ".wav")} missing'
for name in table.get('sounds', {}):
    assert (SFX_DIR / f'{name}.wav').is_file(), f'sound {name}: {SFX_DIR / (name + ".wav")} missing'

record = {'events': [], 'levels': {}, 'level_columns': []}
if not args.viewer_only:
    record = json.loads((RECORD / 'record.json').read_text(encoding='utf-8'))

rel = lambda path: path.relative_to(ROOT).as_posix()
scripts = ROOT / 'Scripts'
lib = {
    'title': 'Boxing', 'project_name': ROOT.name,
    'paths': {'cues': rel(CUES_PATH), 'clips': rel(CLIPS_DIR), 'sounds': rel(SFX_DIR)},
    'at': ['hand', 'pos_neck'], 'loop_cues': [], 'loop_sounds': [],
    'clip_name': r'^[a-z][a-z0-9_-]*$', 'sound_name': r'^[A-Za-z][A-Za-z0-9_]*$',
    'layers': [],
    'families': [
        {'label': '自分のパンチ', 'color': '#ff8a3d', 'cues': ['punch_glove', 'punch_body']},
        {'label': 'ガード', 'color': '#4ea1ff', 'cues': ['guard_glove', 'guard_arm']},
        {'label': '被弾', 'color': '#ff4d4d', 'cues': ['hit_body']}],
    'ticks': [],
    'import_command': f'powershell -ExecutionPolicy Bypass -File "{scripts / "import-haptic-kit.ps1"}"',
    'record_command': f'powershell -ExecutionPolicy Bypass -File "{scripts / "record-haptic-clips.ps1"}"',
}
session = {'project': ROOT.name, 'fps': record.get('fps', 30), 'frames': str(RECORD / 'Frames'),
           'events': record['events'], 'levels': record['levels'], 'level_columns': record['level_columns'],
           # Every take gets a clip (5 cues x 2 variants x 3 takes).
           'clip_rules': {'max': {}, 'min_gap_s': {}, 'default_max': None},
           'lib': lib}
path = ROOT / 'Saved' / 'HapticSession.json'
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(session, ensure_ascii=False), encoding='utf-8')
cmd = [sys.executable, str(tool / 'build_viewer.py'), '--session', str(path), '--project-root', str(ROOT),
       '--ffmpeg', args.ffmpeg]
cmd += ['--keep-frames'] if args.keep_frames else []
cmd += ['--viewer-only'] if args.viewer_only else []
raise SystemExit(subprocess.run(cmd).returncode)
