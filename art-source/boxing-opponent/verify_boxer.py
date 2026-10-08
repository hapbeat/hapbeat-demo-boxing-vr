"""Round-trip the delivered blend/FBX files and check rig, weights, Unity import contracts and recoil motion."""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parent
BONES = ['pelvis', 'spine', 'chest', 'neck', 'head', 'root'] + [b+'.'+s for s in 'LR' for b in
         ('clavicle', 'upper_arm', 'forearm', 'hand', 'thigh', 'shin', 'foot')]
GLOVE_MATERIALS = {'03 Chalk trim', '04 Vermilion leather', '09 Matte strap leather'}
# Bone-frame measurements of the previous model (fb14ffc), read by BoxingOpponentAvatar.CaptureRest.
BASE_HEAD_CENTRE = Vector((0, .1165, .0135))
BASE_RADIUS = {'upper_arm': .0784, 'forearm': .0576}
# Unity: BoxingOpponentAvatar.GloveWidth / GloveDepthRatio, BoxingForearm.WristOffset + Length/2.
UNITY_GLOVE_WIDTH, UNITY_DEPTH, FOREARM_CENTRE_BEHIND = .15, .75, .24
result = {}

def dominant(mesh, names):
    # Unity's boneIndex0 is the largest influence.
    out = []
    for v in mesh.data.vertices:
        g = max(v.groups, key=lambda g: g.weight) if v.groups else None
        out.append((names[g.group], g.weight) if g else (None, 0))
    return out

def bone_points(rig, mesh, dom, bone):
    inv = rig.data.bones[bone].matrix_local.inverted() @ rig.matrix_world.inverted() @ mesh.matrix_world
    return {i: inv @ mesh.data.vertices[i].co for i, (name, w) in enumerate(dom) if name == bone and w > .9}

def bounds(points):
    lo = Vector([min(p[i] for p in points) for i in range(3)]); hi = Vector([max(p[i] for p in points) for i in range(3)])
    return (lo+hi)/2, hi-lo

def r4(v): return [round(x, 4) for x in v]

# ---------------------------------------------------------------- delivered blend
bpy.ops.wm.open_mainfile(filepath=str(root/'boxer-codex.blend'))
rig = bpy.data.objects['Boxer_Rig']
mesh = bpy.data.objects['Boxer_Mesh']
assert sorted(b.name for b in rig.data.bones) == sorted(BONES), [b.name for b in rig.data.bones]
assert any(m.type == 'ARMATURE' and m.object == rig for m in mesh.modifiers)
assert all(v.groups and abs(sum(g.weight for g in v.groups)-1) < 1e-5 for v in mesh.data.vertices)
assert max(len(v.groups) for v in mesh.data.vertices) <= 4
assert len(mesh.data.materials) <= 10
names = {g.index: g.name for g in mesh.vertex_groups}
dom = dominant(mesh, names)
mesh.data.calc_loop_triangles()
result['triangles'] = len(mesh.data.loop_triangles)
assert 10000 <= result['triangles'] <= 15000
result['weight_gt_0_9_vertices'] = {b: sum(1 for n, w in dom if n == b and w > .9) for b in BONES if b != 'root'}
for b in ['head'] + [p+'.'+s for s in 'LR' for p in ('upper_arm', 'forearm', 'hand')]:
    assert result['weight_gt_0_9_vertices'][b] > 0, b

# Gloves: only glove-material vertices are hand > 0.9, and the anatomy tags sit where Unity's test expects.
vertex_materials = {}
for poly in mesh.data.polygons:
    for i in poly.vertices: vertex_materials.setdefault(i, set()).add(mesh.data.materials[poly.material_index].name)
anatomy = mesh.data.color_attributes['Anatomy'].data
result['gloves'] = {}
player_lengths = {}
player_gloves = {}  # side -> BVH of the player glove in its Unity frame (X lateral, Y forward, Z dorsal)
for side, sign in (('L', 1), ('R', -1)):
    pts = bone_points(rig, mesh, dom, 'hand.'+side)
    assert all(vertex_materials[i] <= GLOVE_MATERIALS for i in pts), 'non-glove vertex weighted to hand'
    centre, size = bounds(pts.values())
    tris = sum(1 for t in mesh.data.loop_triangles if all(i in pts for i in t.vertices))
    tags = {}
    for key, channel in (('thumb', 0), ('fingers', 1), ('cuff', 2)):
        sel = [pts[i] for i in pts if anatomy[i].color[channel] > .9]
        assert sel, key
        tags[key] = sum(sel, Vector())/len(sel) - centre
    # Bone frame: X lateral, Y along the hand, Z dorsal.
    assert tags['thumb'].x * -sign > .02, ('thumb side', side, tags['thumb'])
    assert tags['fingers'].z < -.015, ('fingers on palm', side, tags['fingers'])
    assert tags['cuff'].y < -.04, ('cuff behind knuckles', side, tags['cuff'])
    assert 1500 <= tris <= 2500, tris
    # Player glove as BoxingPlayerGloves builds it: centred on the bounds, width 0.15, depth squashed.
    s = UNITY_GLOVE_WIDTH/size.x
    game_length = size.y*s
    assert .25 <= game_length <= .27, ('player glove length', side, game_length)
    # The cuff is centred on the glove bounds; keep that near the hand bone axis, where the opponent's arm enters it.
    assert abs(centre.x)*s < .003 and abs(centre.z)*s*UNITY_DEPTH < .003, ('cuff axis off glove centre', side, centre)
    result['gloves'][side] = {'triangles': tris, 'bounds_size_m': r4(size), 'bounds_centre_m': r4(centre),
                              'width_to_length': round(size.x/size.y, 3), 'player_glove_size_m': r4((UNITY_GLOVE_WIDTH, game_length, size.z*s*UNITY_DEPTH)),
                              'tags_vs_centre_m': {k: r4(v) for k, v in tags.items()}}
    game = {i: Vector(((p.x-centre.x)*s, (p.y-centre.y)*s, (p.z-centre.z)*s*UNITY_DEPTH)) for i, p in pts.items()}
    keys = list(game); index = {k: n for n, k in enumerate(keys)}
    faces = [tuple(index[i] for i in poly.vertices) for poly in mesh.data.polygons if all(i in game for i in poly.vertices)]
    player_lengths[side] = game_length
    player_gloves[side] = BVHTree.FromPolygons([game[k] for k in keys], faces)

# Head centre and limb radii feed the Unity hit volumes.
head_centre, head_size = bounds(bone_points(rig, mesh, dom, 'head').values())
result['head_centre_m'] = r4(head_centre); result['head_centre_shift_m'] = round((head_centre-BASE_HEAD_CENTRE).length, 4)
assert result['head_centre_shift_m'] < .02, result['head_centre_shift_m']
result['limb_radius_m'] = {}
for side in 'LR':
    for part, base in BASE_RADIUS.items():
        _, size = bounds(bone_points(rig, mesh, dom, part+'.'+side).values())
        radius = max(size.x, size.z)/2
        result['limb_radius_m'][part+'.'+side] = {'radius': round(radius, 4), 'vs_previous': round(radius/base, 3)}
        assert .8 <= radius/base <= 1.2, (part, side, radius)

rig.animation_data.action = bpy.data.actions['Hit_Recoil']
positions = []
for frame in (1, 7, 30):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    positions.append(rig.pose.bones['head'].tail.copy())
assert (positions[1]-positions[0]).length > .05, 'Hit does not move head'
assert (positions[2]-positions[0]).length < .001, 'Hit does not return to guard'
result['blend_skin_weights'] = 'PASS'
result['recoil_head_displacement_m'] = round((positions[1]-positions[0]).length, 4)
blend_hand_counts = {s: result['weight_gt_0_9_vertices']['hand.'+s] for s in 'LR'}

# ---------------------------------------------------------------- boxer FBX
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/'boxer-codex.fbx'))
armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(armatures) == 1 and len(meshes) == 1
assert sorted(b.name for b in armatures[0].data.bones) == sorted(BONES)
assert all(v.groups for v in meshes[0].data.vertices)
assert len(bpy.data.actions) == 2, [a.name for a in bpy.data.actions]
for action in bpy.data.actions:
    assert action.frame_range[1]-action.frame_range[0] >= 29
fbx_dom = dominant(meshes[0], {g.index: g.name for g in meshes[0].vertex_groups})
for s in 'LR':
    assert sum(1 for n, w in fbx_dom if n == 'hand.'+s and w > .9) == blend_hand_counts[s]
result.update({'fbx_round_trip': 'PASS', 'fbx_meshes': len(meshes), 'fbx_bones': len(armatures[0].data.bones),
               'fbx_materials': len(meshes[0].data.materials), 'fbx_actions': [a.name for a in bpy.data.actions]})

# ---------------------------------------------------------------- player forearm FBX
# Read with no axis conversion: coordinates as Unity sees them (Unity only mirrors X).
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/'player-forearm.fbx'), use_manual_orientation=True, axis_forward='Y', axis_up='Z')
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(objs) == 1, objs
arm = objs[0]
world = [arm.matrix_world @ v.co for v in arm.data.vertices]
ys = [p.y for p in world]
radial = lambda p: math.hypot(p.x, p.z)
wrist_r = max(radial(p) for p in world if p.y > max(ys)-.02)
elbow_r = max(radial(p) for p in world if p.y < min(ys)+.08)
arm.data.calc_loop_triangles()
fa = {'length_m': round(max(ys)-min(ys), 4), 'y_range_m': [round(min(ys), 4), round(max(ys), 4)],
      'max_radius_m': round(max(radial(p) for p in world), 4), 'wrist_end_radius_m': round(wrist_r, 4),
      'elbow_side_radius_m': round(elbow_r, 4), 'triangles': len(arm.data.loop_triangles),
      # FBX stores centimetres; the only object transform allowed is that uniform unit scale (no axis rotation).
      'object_rotation_identity': arm.matrix_world.to_quaternion().angle < 1e-4 and arm.matrix_world.translation.length < 1e-6,
      'object_unit_scale': round(arm.matrix_world.to_scale().x, 4),
      'materials': [m.name for m in arm.data.materials]}
assert abs(fa['length_m']-.28) < .002 and abs(min(ys)+max(ys)) < .002, fa
assert fa['max_radius_m'] <= .0451, fa
assert wrist_r < elbow_r and .030 <= wrist_r <= .038, fa
assert fa['object_rotation_identity'], fa
fa['orientation'] = 'PASS: +Y = hand (narrow, open end), -Y = elbow, origin at length midpoint'
# Cuff fit as in BoxingPresentation.RenderForearm: forearm centre 0.24 m behind the glove centre.
# Rays go outward from the forearm surface inside the cuff; the first hit must be the cuff's inner wall
# (front face, so the forearm is not through it) within a few millimetres (no visible gap).
# Skin covered by the hand wrap is skipped: only the outermost forearm surface can show a gap.
forearm_tree = BVHTree.FromPolygons(world, [tuple(p.vertices) for p in arm.data.polygons])
fit = {}
for side, tree in player_gloves.items():
    gaps, through = [], []
    for p in world:
        # Unity: mesh x = -FBX x, glove X = mesh x = -bone x; glove Y = -mesh z, glove Y = bone z (dorsal).
        q = Vector((p.x, p.y-FOREARM_CENTRE_BEHIND, -p.z)); ray = Vector((q.x, 0, q.z))
        if ray.length < .02: continue
        if forearm_tree.ray_cast(p+Vector((p.x, 0, p.z)).normalized()*.0003, Vector((p.x, 0, p.z)).normalized(), .05)[0]: continue
        hit, normal, _, dist = tree.ray_cast(q, ray.normalized(), .05)
        if hit is None: continue
        if normal.dot(ray) >= 0: through.append(r4(q)); continue
        gaps.append((q.y, dist))
    assert gaps, ('forearm does not reach into the cuff', side)
    # Mouth: 5-12 mm inside the glove's rear end, past the piped rim (piping radius + rolled lip).
    rear = -player_lengths[side]/2
    mouth = [d for y, d in gaps if rear+.005 < y < rear+.012]
    fit[side] = {'insertion_m': round(max(g[0] for g in gaps)-min(g[0] for g in gaps), 4),
                 'max_gap_m': round(max(d for _, d in gaps), 4), 'mouth_max_gap_m': round(max(mouth), 4),
                 'vertices_through_wall': len(through)}
    assert not through, ('forearm passes through the cuff wall', side, through[:4])
    assert fit[side]['mouth_max_gap_m'] < .004, fit
fa['cuff_fit'] = fit
result['player_forearm_fbx'] = fa
result['verdict'] = 'PASS'
(root/'round-trip-validation.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
print('BOXER_ROUND_TRIP_PASS', json.dumps(result))
