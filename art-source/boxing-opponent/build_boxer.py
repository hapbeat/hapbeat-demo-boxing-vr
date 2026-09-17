"""Original low-poly boxer. Run with Blender --background --python build_boxer.py.
No third-party meshes/textures. Z up, faces -Y, metres. Outputs beside this script.
"""
from pathlib import Path
import math
import json
import bpy
from mathutils import Vector, Quaternion

OUT = Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0

def material(name, color, roughness=.75):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Roughness'].default_value = roughness
    return mat

skin = material('01 Warm clay skin', (.53, .30, .18))
shorts = material('02 Midnight blue satin', (.025, .07, .12), .5)
ivory = material('03 Chalk trim', (.86, .84, .74))
red = material('04 Vermilion leather', (.64, .045, .025), .42)
dark = material('05 Charcoal rubber', (.025, .032, .036))
sole = material('06 Boot panels', (.065, .085, .105))
parts = []
bone_defs = []

def bone(name, a, b, parent=None):
    bone_defs.append((name, Vector(a), Vector(b), parent))

def mesh(name, vertices, faces, mat, weights):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for i, mapping in enumerate(weights):
        for group, value in mapping.items():
            vg = obj.vertex_groups.get(group) or obj.vertex_groups.new(name=group)
            vg.add([i], value, 'REPLACE')
    # Recalculate outward normals on each closed shell.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)
    parts.append(obj)
    return obj

def loft(name, rings, mat, n=8, axis=(0, 0, 1)):
    # ring = centre, half width, half depth, skin weights. All rings share frame.
    direction = Vector(axis).normalized()
    u = Vector((1, 0, 0))
    if abs(direction.dot(u)) > .93:
        u = Vector((0, 1, 0))
    u = (u - direction * direction.dot(u)).normalized()
    v = direction.cross(u).normalized()
    vertices, weights = [], []
    for center, width, depth, weight in rings:
        for j in range(n):
            theta = 2 * math.pi * j / n + math.pi / n
            vertices.append(Vector(center) + u * (math.cos(theta) * width) + v * (math.sin(theta) * depth))
            weights.append(weight)
    faces = [tuple(reversed(range(n)))]
    for r in range(len(rings) - 1):
        for j in range(n):
            a = r*n+j; b = r*n+(j+1)%n
            faces.append((a, b, b+n, a+n))
    faces.append(tuple(range((len(rings)-1)*n, len(rings)*n)))
    return mesh(name, vertices, faces, mat, weights)

def vertical(name, rows, mat, group, n=8):
    return loft(name, [((x, y, z), w, d, {group: 1}) for x, y, z, w, d in rows], mat, n)

bone('root', (0, 0, 0), (0, 0, .18))
bone('pelvis', (0, 0, .90), (0, 0, 1.04), 'root')
bone('spine', (0, 0, 1.04), (0, 0, 1.24), 'pelvis')
bone('chest', (0, 0, 1.24), (0, 0, 1.47), 'spine')
bone('neck', (0, 0, 1.47), (0, 0, 1.57), 'chest')
bone('head', (0, 0, 1.57), (0, 0, 1.82), 'neck')

loft('Torso', [
    ((0, 0, .96), .157, .095, {'pelvis': 1}),
    ((0, 0, 1.07), .15, .098, {'spine': 1}),
    ((0, -.008, 1.18), .177, .112, {'spine': .65, 'chest': .35}),
    ((0, -.01, 1.33), .225, .125, {'chest': 1}),
    ((0, 0, 1.435), .252, .10, {'chest': 1}),
    ((0, .002, 1.485), .155, .075, {'chest': 1}),
    ((0, 0, 1.50), .076, .065, {'neck': .4, 'chest': .6}),
], skin, 12)
vertical('Neck', [(0, 0, 1.47, .068, .061), (0, 0, 1.595, .067, .06)], skin, 'neck')
vertical('Faceless sculpted head', [
    (0, -.022, 1.565, .053, .060),
    (0, -.012, 1.59, .083, .076),
    (0, -.007, 1.65, .102, .092),
    (0, .001, 1.735, .105, .097),
    (0, .006, 1.785, .082, .075),
    (0, .007, 1.808, .044, .042),
], skin, 'head', 10)

# Small readable features, not a realistic face. Rigidly follow the head bone.
mesh('Symbolic nose', [(-.019, -.088, 1.696), (.019, -.088, 1.696),
    (0, -.128, 1.665), (-.017, -.092, 1.65), (.017, -.092, 1.65)],
    [(0, 1, 2), (0, 2, 3), (1, 4, 2), (3, 2, 4), (0, 3, 4, 1)], skin, [{'head': 1}]*5)
for sign in (-1, 1):
    vertical('Symbolic ear '+str(sign), [(sign*.102, .002, 1.637, .012, .018),
        (sign*.117, .002, 1.665, .019, .027), (sign*.112, .002, 1.7, .017, .024),
        (sign*.101, .002, 1.717, .009, .014)], skin, 'head', 6)

vertical('Shorts hip', [(0, 0, .925, .196, .121), (0, 0, .975, .18, .122), (0, 0, 1.027, .162, .105)], shorts, 'pelvis', 12)
vertical('Wide waistband', [(0, 0, .988, .204, .142), (0, 0, 1.035, .177, .123)], ivory, 'pelvis', 12)

for sign, side in [(1, 'L'), (-1, 'R')]:
    shoulder = Vector((sign*.237, 0, 1.428))
    elbow = Vector((sign*.423, -.018, 1.20))
    wrist = Vector((sign*.584, -.052, 1.005))
    fingertip = wrist + (wrist-elbow).normalized()*.16
    hip = Vector((sign*.105, 0, .905))
    knee = Vector((sign*.13, -.018, .515))
    ankle = Vector((sign*.145, 0, .155))
    bone('clavicle.'+side, (0, 0, 1.44), shoulder, 'chest')
    bone('upper_arm.'+side, shoulder, elbow, 'clavicle.'+side)
    bone('forearm.'+side, elbow, wrist, 'upper_arm.'+side)
    bone('hand.'+side, wrist, fingertip, 'forearm.'+side)
    bone('thigh.'+side, hip, knee, 'pelvis')
    bone('shin.'+side, knee, ankle, 'thigh.'+side)
    bone('foot.'+side, ankle, (sign*.145, -.16, .08), 'shin.'+side)
    ua, fa, hand = 'upper_arm.'+side, 'forearm.'+side, 'hand.'+side
    # Connected arm with weighted elbow rings, not separate capsule joints.
    arm_axis = (wrist-shoulder).normalized()
    loft('Arm.'+side, [
        (shoulder + (shoulder-elbow)*.12, .077, .082, {ua: 1}),
        (shoulder.lerp(elbow, .24), .087, .087, {ua: 1}),
        (shoulder.lerp(elbow, .67), .072, .07, {ua: 1}),
        (shoulder.lerp(elbow, .90), .052, .052, {ua: .85, fa: .15}),
        (elbow, .05, .051, {ua: .5, fa: .5}),
        (elbow.lerp(wrist, .13), .063, .062, {ua: .15, fa: .85}),
        (elbow.lerp(wrist, .40), .066, .063, {fa: 1}),
        (elbow.lerp(wrist, .85), .042, .041, {fa: 1}),
        (wrist, .037, .038, {fa: .3, hand: .7}),
    ], skin, 10, arm_axis)
    direction = (fingertip-wrist).normalized()
    glove_start = len(parts)
    loft('Glove cuff.'+side, [(wrist-direction*.025, .053, .047, {hand: 1}), (wrist, .058, .052, {hand: 1}), (wrist+direction*.047, .058, .051, {hand: 1})], red, 16, direction)
    loft('Wrist strap.'+side, [(wrist-direction*.01, .059, .054, {hand: 1}), (wrist+direction*.022, .061, .055, {hand: 1})], ivory, 16, direction)
    loft('Cuff piping.'+side, [(wrist+direction*.038, .060, .053, {hand: 1}), (wrist+direction*.046, .060, .053, {hand: 1})], dark, 16, direction)
    loft('Glove.'+side, [
        (wrist+direction*.038, .057, .055, {hand: 1}),
        (wrist+direction*.075, .077, .069, {hand: 1}),
        (wrist+direction*.145, .09, .075, {hand: 1}),
        (wrist+direction*.18, .085, .073, {hand: 1}),
        (wrist+direction*.205, .065, .059, {hand: 1}),
        (wrist+direction*.221, .039, .036, {hand: 1}),
        (wrist+direction*.229, .008, .008, {hand: 1}),
    ], red, 20, direction)
    # Thumb is attached along the inner side, not a second ball on top.
    inner = Vector((-sign, 0, 0))
    thumb = wrist + direction*.095 + inner*.061 + Vector((0, -.018, 0))
    loft('Thumb.'+side, [(thumb-direction*.025, .018, .017, {hand: 1}), (thumb, .032, .028, {hand: 1}), (thumb+direction*.04, .031, .027, {hand: 1}), (thumb+direction*.069, .020, .018, {hand: 1}), (thumb+direction*.079, .006, .006, {hand: 1})], red, 16, direction)
    # Contrast the palm grip panel and back patch; both are original geometry.
    back = Vector((0, 1, 0)); back = (back-direction*back.dot(direction)).normalized()
    for name, side_sign, mat, width in [('Palm grip', -1, dark, .049), ('Back patch', 1, ivory, .026)]:
        c = wrist+direction*.108+back*(side_sign*.068)
        loft(name+'.'+side, [(c-direction*.028, width*.7, .004, {hand: 1}), (c, width, .005, {hand: 1}), (c+direction*.035, width*.65, .003, {hand: 1})], mat, 12, direction)
    for obj in parts[glove_start:]:
        for polygon in obj.data.polygons: polygon.use_smooth = True
    loft('Leg.'+side, [
        (hip, .09, .10, {'thigh.'+side: 1}),
        (hip.lerp(knee, .3), .10, .10, {'thigh.'+side: 1}),
        (hip.lerp(knee, .85), .064, .065, {'thigh.'+side: 1}),
        (knee, .059, .065, {'thigh.'+side: .5, 'shin.'+side: .5}),
        (knee.lerp(ankle, .23), .069, .072, {'shin.'+side: 1}),
        (knee.lerp(ankle, .62), .052, .057, {'shin.'+side: 1}),
        (ankle, .037, .039, {'shin.'+side: 1}),
    ], skin, 10)
    loft('Shorts leg.'+side, [
        ((sign*.102, 0, .70), .108, .119, {'thigh.'+side: 1}),
        ((sign*.10, 0, .79), .114, .123, {'thigh.'+side: 1}),
        ((sign*.09, 0, .90), .124, .127, {'thigh.'+side: .5, 'pelvis': .5}),
        ((sign*.075, 0, .978), .102, .12, {'pelvis': 1}),
        ((sign*.064, 0, 1.025), .102, .10, {'pelvis': 1}),
    ], shorts, 10)
    vertical('Shorts hem.'+side, [(sign*.102, 0, .7, .109, .12), (sign*.102, 0, .718, .11, .121)], ivory, 'thigh.'+side, 10)
    vertical('Boot shaft.'+side, [(sign*.145, 0, .10, .059, .061), (sign*.145, -.004, .25, .069, .074), (sign*.145, -.01, .325, .076, .081)], sole, 'shin.'+side, 8)
    vertical('Boot collar.'+side, [(sign*.145, -.009, .30, .076, .081), (sign*.145, -.01, .326, .078, .083)], ivory, 'shin.'+side, 8)
    vertical('Boot sole.'+side, [(sign*.145, -.063, .025, .067, .145), (sign*.145, -.063, .048, .069, .148)], dark, 'foot.'+side, 10)
    vertical('Boot foot.'+side, [(sign*.145, -.063, .049, .067, .145), (sign*.145, -.062, .089, .064, .142), (sign*.145, -.013, .145, .052, .084)], sole, 'foot.'+side, 10)

# Union the shorts into one garment surface, avoiding intersecting crotch panels.
garment = [o for o in parts if o.name.startswith(('Shorts hip', 'Shorts leg.'))]
bpy.ops.object.select_all(action='DESELECT')
for obj in garment: obj.select_set(True)
bpy.context.view_layer.objects.active = garment[0]
for obj in garment: parts.remove(obj)
bpy.ops.object.join()
pants = bpy.context.object
pants.data.remesh_voxel_size = .008
bpy.ops.object.voxel_remesh()
smooth = pants.modifiers.new('Garment union smoothing', 'SMOOTH'); smooth.factor=.7; smooth.iterations=3
bpy.ops.object.modifier_apply(modifier=smooth.name)
decimate = pants.modifiers.new('Low-poly garment', 'DECIMATE'); decimate.ratio=.075
bpy.ops.object.modifier_apply(modifier=decimate.name)
pants.vertex_groups.clear()
groups = {name: pants.vertex_groups.new(name=name) for name in ('pelvis', 'thigh.L', 'thigh.R')}
for vertex in pants.data.vertices:
    hip_weight = max(0, min(1, (vertex.co.z-.82)/.14))
    groups['pelvis'].add([vertex.index], hip_weight, 'REPLACE')
    groups['thigh.L' if vertex.co.x >= 0 else 'thigh.R'].add([vertex.index], 1-hip_weight, 'REPLACE')
parts.append(pants)

# Slimmer silhouette at the same eye height. Transform the bind skeleton too.
for obj in parts:
    for vertex in obj.data.vertices:
        vertex.co.x *= .88
        vertex.co.y *= .90
for _, a, b, _ in bone_defs:
    for point in (a, b): point.x *= .88; point.y *= .90

# A single exportable mesh: faceted body with smoother leather gloves.
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = 'Boxer_Mesh'
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for vertex in body.data.vertices: vertex.co.z -= .025

arm_data = bpy.data.armatures.new('Boxer_Skeleton')
rig = bpy.data.objects.new('Boxer_Rig', arm_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
body.select_set(False); rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for name, a, b, parent in bone_defs:
    if name != 'root':
        a.z -= .025; b.z -= .025
    eb = arm_data.edit_bones.new(name); eb.head = a; eb.tail = b
    if parent: eb.parent = arm_data.edit_bones[parent]
    eb.use_deform = name != 'root'
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front = True
modifier = body.modifiers.new('Boxer skin', 'ARMATURE'); modifier.object = rig
body.parent = rig
rig['readme'] = 'Original faceless boxer. FK rig, metre scale. Guard and Hit_Recoil are pose demos, not final gameplay animation.'

def reset_pose():
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()

def aim(name, target):
    pb = rig.pose.bones[name]
    bpy.context.view_layer.update()
    delta = Vector(target)-pb.head
    current = pb.tail-pb.head
    world_rotation = current.rotation_difference(delta)
    matrix = pb.matrix.copy()
    matrix = world_rotation.to_matrix().to_4x4() @ matrix
    matrix.translation = pb.head
    pb.matrix = matrix
    bpy.context.view_layer.update()

def guard():
    reset_pose()
    for side, s in [('L', 1), ('R', -1)]:
        # Fists beside cheeks; forearms incline inward, elbows below shoulders.
        aim('upper_arm.'+side, (s*.29, -.17, 1.165))
        aim('forearm.'+side, (s*.17, -.32, 1.445))
        pb = rig.pose.bones['hand.'+side]
        aim(pb.name, pb.head + Vector((-s*.025, -.03, .15)))

def key_pose(frame):
    for pb in rig.pose.bones:
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
        pb.keyframe_insert('location', frame=frame, group=pb.name)

guard()
rig.animation_data_create()
action = bpy.data.actions.new('Guard_Pose'); rig.animation_data.action = action
key_pose(1); key_pose(30); action.use_fake_user = True
guard()
recoil = bpy.data.actions.new('Hit_Recoil'); rig.animation_data.action = recoil
key_pose(1)
for name, angle in [('spine', -8), ('chest', -12), ('neck', -6), ('head', -9)]:
    rig.pose.bones[name].rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle))
rig.pose.bones['chest'].rotation_quaternion @= Quaternion((0, 0, 1), math.radians(-8))
key_pose(7)
guard(); key_pose(24); key_pose(30); recoil.use_fake_user = True
rig.animation_data.action = action
bpy.context.scene.frame_set(1)

# Dedicated presentation scene objects; excluded from exports.
stage = bpy.data.collections.new('PREVIEW - not exported')
bpy.context.scene.collection.children.link(stage)
def stage_object(obj):
    for collection in list(obj.users_collection): collection.objects.unlink(obj)
    stage.objects.link(obj)
    return obj

floor_mat = material('Preview warm grey', (.16, .185, .19))
bpy.ops.mesh.primitive_plane_add(size=200)
floor = stage_object(bpy.context.object); floor.name = 'Studio floor'; floor.data.materials.append(floor_mat)

def track(obj, target): obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z', 'Y').to_euler()
def area(name, loc, energy, size, color):
    data = bpy.data.lights.new(name, 'AREA'); data.energy=energy; data.shape='DISK'; data.size=size; data.color=color
    obj = bpy.data.objects.new(name, data); stage.objects.link(obj); obj.location=loc; track(obj, (0, 0, 1))
area('Large soft key', (3, -4, 5), 450, 4, (1, .88, .76))
area('Soft fill', (-3, -2, 2.8), 260, 3, (.73, .85, 1))
area('Edge light', (1, 3, 4), 600, 3, (1, .9, .78))
camera_data=bpy.data.cameras.new('Presentation camera')
camera=bpy.data.objects.new('Presentation camera', camera_data); stage.objects.link(camera)
camera_data.type='ORTHO'; camera_data.ortho_scale=2.25
scene=bpy.context.scene; scene.camera=camera
scene.render.engine='CYCLES'; scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.world.color=(.18, .18, .18)
scene.render.resolution_x=1000; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.fps=30; scene.frame_start=1; scene.frame_end=30
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.view_settings.view_transform='AgX'

# Verify skin weights, mesh validity, topology, animation range and dimensions.
assert not body.data.validate(verbose=True), 'Invalid mesh generated'
for vertex in body.data.vertices:
    assert abs(sum(g.weight for g in vertex.groups)-1) < 1e-5, 'Unnormalized skin weight'
    assert vertex.groups, 'Unweighted vertex'
body.data.calc_loop_triangles()
stats = {'vertices': len(body.data.vertices), 'triangles': len(body.data.loop_triangles),
         'bones': len(rig.data.bones), 'materials': len(body.data.materials),
         'height_m': round(max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices), 4),
         'actions': ['Guard_Pose', 'Hit_Recoil'], 'third_party_meshes': False}
assert stats['triangles'] < 5000
assert 1.7 < stats['height_m'] < 1.9

# Export asset only, with both demonstration actions. FBX is separate from Unity Assets.
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); rig.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'boxer-codex.fbx'), use_selection=True,
    object_types={'MESH','ARMATURE'}, add_leaf_bones=False, axis_forward='-Z', axis_up='Y',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0, mesh_smooth_type='FACE')

for label, position in [('hero', (3, -6, 2.9)), ('front', (0, -6, 1.9)), ('side', (6, -.4, 2.0))]:
    camera.location=position; track(camera, (0, -.02, .93))
    scene.render.filepath=str(OUT/('preview-'+label+'.png'))
    bpy.ops.render.render(write_still=True)
camera.location=(3, -6, 2.9); track(camera, (0, -.02, .93))
scene.frame_set(7); rig.animation_data.action=recoil; scene.frame_set(7)
scene.render.filepath=str(OUT/'preview-hit.png'); bpy.ops.render.render(write_still=True)
rig.animation_data.action=action; scene.frame_set(1)
bpy.context.view_layer.objects.active=rig
for screen in bpy.data.screens:
    for area_item in screen.areas:
        if area_item.type=='VIEW_3D':
            area_item.spaces.active.region_3d.view_distance=3
            area_item.spaces.active.region_3d.view_location=(0, 0, .95)
            area_item.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'boxer-codex.blend'))
(OUT/'validation.json').write_text(json.dumps(stats, indent=2)+'\n', encoding='utf-8')
print('BOXER_MODEL_VALID', json.dumps(stats))
