"""Boxing input for Hapbeat Studio's Scene tab (hapbeat-contracts specs/scene-recording.md): turns the PC replay
(Saved/HapticClips/Frames + record.json, written by BoxingHapticRecord) and where the cue table, haptic clips and
sounds live into the recording's session.json (Saved/HapticSession.json), then runs `hapbeat-helper scene build`,
which writes Saved/HapticViewer/ (full_replay.mp4, viewer-data.json, viewer-lib.json). Normally run by
Scripts/record-haptic-clips.ps1.

    haptic_session.py [--ffmpeg ...] [--keep-frames]
    haptic_session.py --viewer-only     # rewrite Saved/HapticViewer/viewer-lib.json only

The cue table (Assets/Boxing/Haptics/boxing-cues.json, hapbeat-contracts specs/cue-table.md) has one cue per
contact kind with the variants soft and hard (DEC-091); the recording names the moments punch_glove:soft etc.
The wrist cues play at the acting hand (left_<cue> / right_<cue> in the EventMap), hit_body at the neck.
Scripts/import-haptic-kit.ps1 applies it to BoxingEventMap and the scene's cue sounds.
"""
import argparse, json, subprocess
import build_haptic_import as bhi

ROOT = bhi.ROOT
RECORD = ROOT / 'Saved' / 'HapticClips'
FRAMES = 'Saved/HapticClips/Frames'  # f<frame>.png of BoxingHapticRecord (the folder holds nothing else)

ap = argparse.ArgumentParser()
ap.add_argument('--ffmpeg', default='ffmpeg', help='ffmpeg executable with libx264')
ap.add_argument('--keep-frames', action='store_true')
ap.add_argument('--engine-root', help='ignored (the launcher passes the Unreal path to every project)')
ap.add_argument('--viewer-only', action='store_true')
args = ap.parse_args()

# Fail early on a broken cue table; Studio reads the table and WAVs from the project.
table = json.loads(bhi.CUES_PATH.read_text(encoding='utf-8'))
err = bhi.spec_problems(table)
if sorted(table.get('cues', {})) != sorted(bhi.CUES):
    err.append(f'cues must be exactly {list(bhi.CUES)}')
if err:
    raise SystemExit('\n'.join(err))

rel = lambda path: path.relative_to(ROOT).as_posix()
scripts = ROOT / 'Scripts'
lib = {
    'schema_version': 1, 'title': 'Boxing', 'project_name': ROOT.name,
    'paths': {'cues': rel(bhi.CUES_PATH), 'clips': rel(bhi.CLIPS_DIR), 'sounds': rel(bhi.SFX_DIR)},
    'at': ['hand', 'pos_neck'],
    'families': [
        {'label': '自分のパンチ', 'color': '#ff8a3d', 'cues': ['punch_glove', 'punch_body']},
        {'label': 'ガード', 'color': '#4ea1ff', 'cues': ['guard_glove', 'guard_arm']},
        {'label': '被弾', 'color': '#ff4d4d', 'cues': ['hit_body']}],
    # Every take is listed (5 cues x 2 variants x 3 takes).
    'moment_rules': {'max': {}, 'min_gap_s': {}, 'default_max': None},
    'import_command': f'powershell -ExecutionPolicy Bypass -File "{scripts / "import-haptic-kit.ps1"}"',
    'record_command': f'powershell -ExecutionPolicy Bypass -File "{scripts / "record-haptic-clips.ps1"}"',
}
write = lambda path, obj: path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
if args.viewer_only:
    out = ROOT / 'Saved' / 'HapticViewer'
    out.mkdir(parents=True, exist_ok=True)
    write(out / 'viewer-lib.json', lib)
    print(f'wrote {out / "viewer-lib.json"}')
    raise SystemExit(0)

record = json.loads((RECORD / 'record.json').read_text(encoding='utf-8'))
# record.json: events [frame, cue, hand, note, gain]; no loop cue, so no level columns (level_columns stays empty).
assert not record.get('level_columns'), 'Boxing records no loop levels'
events = []
for frame, cue, hand, note, gain in record['events']:
    event = {'frame': frame, 'cue': cue, 'hand': hand, 'gain': gain}
    if note:
        event['note'] = note
    events.append(event)
session = {'schema_version': 1, 'project': ROOT.name, 'fps': record.get('fps', 30), 'frames': FRAMES,
           'events': events, 'lib': lib}
path = ROOT / 'Saved' / 'HapticSession.json'
path.parent.mkdir(parents=True, exist_ok=True)
write(path, session)
cmd = [bhi.helper(), 'scene', 'build', '--session', str(path), '--project-root', str(ROOT), '--ffmpeg', args.ffmpeg]
cmd += ['--keep-frames'] if args.keep_frames else []
raise SystemExit(subprocess.run(cmd).returncode)
