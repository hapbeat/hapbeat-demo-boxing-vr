"""Original boxer and player forearm. Run with Blender --background --factory-startup --python build_boxer.py.
No third-party meshes/textures. Z up, faces -Y, metres. Outputs beside this script.
"""
from pathlib import Path
import math
import json
import subprocess
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree

OUT = Path(__file__).resolve().parent
# Last commit of the previous (low-poly) look, used for preview-before-after.png.
BEFORE_COMMIT = 'fb14ffc'
# Unity squashes the hand bone's dorsal axis (BoxingOpponentAvatar.GloveDepthRatio).
# Gloves are authored at their in-game shape and pre-stretched by the inverse here.
UNITY_GLOVE_DEPTH_RATIO = .75
DC = 1/UNITY_GLOVE_DEPTH_RATIO
# Player glove as Unity builds it (BoxingPlayerGloves): normalised to 0.15 m wide; its length is set here.
UNITY_GLOVE_WIDTH = .15
PLAYER_GLOVE_LENGTH = .26
# Padded body proportion along the hand (the cuff then fills the rest of PLAYER_GLOVE_LENGTH).
GLOVE_LENGTH_SCALE = .84
# The thumb widens the glove on one side. The padded body is shifted away from it so the glove's
# bounds centre (where Unity places the glove and, behind it, the forearm) stays near the hand bone axis.
GLOVE_BODY_SHIFT = .0089
# Player forearm in the glove frame (BoxingPresentation.RenderForearm, in-game metres): the cuff is centred
# on the glove bounds plus the wrapped forearm's cross-section centre (lateral, dorsal), and its inner wall
# follows the wrap (radii below) with ~2 mm clearance, so no gap shows in first person.
FOREARM_AXIS_OFFSET = (.0008, .0010)
CUFF_INNER = [(.0381, .0330), (.0379, .0330), (.0378, .0328), (.0407, .0360)]
CUFF_PIPING = (.0437, .0393)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0

def material(name, color, roughness=.75, coat=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Roughness'].default_value = roughness
    if coat and 'Coat Weight' in node.inputs:
        node.inputs['Coat Weight'].default_value = coat
        node.inputs['Coat Roughness'].default_value = .28
    return mat

# Slots 0-5 keep the order Unity's BoxingAvatarSetup indexes; 6-8 are new.
skin = material('01 Warm clay skin', (.53, .30, .18), .55)
shorts = material('02 Midnight blue satin', (.025, .07, .12), .36)
ivory = material('03 Chalk trim', (.86, .84, .74), .6)
red = material('04 Vermilion leather', (.64, .045, .025), .3, .35)
dark = material('05 Charcoal rubber', (.025, .032, .036), .8)
sole = material('06 Boot panels', (.065, .085, .105), .5)
headgear_mat = material('07 Headgear leather', (.07, .075, .09), .42, .25)
wrap_mat = material('08 Cotton hand wrap', (.80, .81, .82), .95)
strap_mat = material('09 Matte strap leather', (.36, .028, .02), .72)
MATERIAL_ORDER = [skin, shorts, ivory, red, sole, dark, headgear_mat, wrap_mat, strap_mat]
GLOVE_MATERIALS = {red.name, strap_mat.name, ivory.name}
parts = []
bone_defs = []
glove_frames = {}
THUMB, FINGERS, CUFF, NONE = (1, 0, 0, 1), (0, 1, 0, 1), (0, 0, 1, 1), (0, 0, 0, 1)

def bone(name, a, b, parent=None):
    bone_defs.append((name, Vector(a), Vector(b), parent))

def bell(x, mu, sigma):
    return math.exp(-((x-mu)/sigma)**2)

def front_angle(radial):
    # Degrees from the character's front (-Y); +X side positive.
    return math.degrees(math.atan2(radial.x, -radial.y))

def mesh(name, vertices, faces, mat, weights, tags=None, consistent=True, smooth=True):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    # Non-rendered anatomical tags survive FBX for handedness regression tests.
    tag = THUMB if name.startswith('Thumb.') else FINGERS if name.startswith('Folded fingers.') else CUFF if name.startswith('Glove cuff.') else NONE
    colors = data.color_attributes.new(name='Anatomy', type='FLOAT_COLOR', domain='POINT')
    for i, color in enumerate(colors.data): color.color = tags[i] if tags else tag
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    for i, mapping in enumerate(weights):
        for group, value in mapping.items():
            vg = obj.vertex_groups.get(group) or obj.vertex_groups.new(name=group)
            vg.add([i], value, 'REPLACE')
    if consistent:
        # Recalculate outward normals on each closed shell.
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj.select_set(False)
    for polygon in data.polygons: polygon.use_smooth = smooth
    parts.append(obj)
    return obj

def loft_frame(axis, axis_u=None):
    direction = Vector(axis).normalized()
    u = Vector(axis_u) if axis_u is not None else Vector((1, 0, 0))
    if abs(direction.dot(u)) > .93:
        u = Vector((0, 1, 0))
    u = (u - direction * direction.dot(u)).normalized()
    v = direction.cross(u).normalized()
    return direction, u, v

def ring_point(center, u, v, width, depth, theta, power=2.0):
    c, s = math.cos(theta), math.sin(theta)
    if power != 2.0:
        c = math.copysign(abs(c)**(2/power), c); s = math.copysign(abs(s)**(2/power), s)
    return Vector(center) + u*(c*width) + v*(s*depth)

def loft(name, rings, mat, n=8, axis=(0, 0, 1), axis_u=None, power=2.0, shape=None, caps=(True, True), tags=None):
    # ring = centre, half width, half depth, skin weights. All rings share frame.
    _, u, v = loft_frame(axis, axis_u)
    vertices, weights = [], []
    for i, (center, width, depth, weight) in enumerate(rings):
        center = Vector(center)
        for j in range(n):
            theta = 2*math.pi*j/n + math.pi/n
            p = ring_point(center, u, v, width, depth, theta, power)
            if shape and (p-center).length > 1e-9:
                p = center + (p-center)*shape(i, (p-center).normalized())
            vertices.append(p)
            weights.append(weight)
    faces = [tuple(reversed(range(n)))] if caps[0] else []
    for r in range(len(rings) - 1):
        for j in range(n):
            a = r*n+j; b = r*n+(j+1) % n
            faces.append((a, b, b+n, a+n))
    if caps[1]: faces.append(tuple(range((len(rings)-1)*n, len(rings)*n)))
    return mesh(name, vertices, faces, mat, weights, tags)

def vertical(name, rows, mat, group, n=8, caps=(True, True)):
    return loft(name, [((x, y, z), w, d, {group: 1}) for x, y, z, w, d in rows], mat, n, caps=caps)

def tube(name, points, radius, mat, weights, closed=False, n=8, ref=None):
    # Swept circle along a polyline (piping, laces, straps).
    pts = [Vector(p) for p in points]; m = len(pts)
    radii = list(radius) if isinstance(radius, (list, tuple)) else [radius]*m
    tangents = []
    for k in range(m):
        a = pts[(k-1) % m] if closed or k > 0 else pts[k]
        b = pts[(k+1) % m] if closed or k < m-1 else pts[k]
        tangents.append((b-a).normalized())
    normal = Vector(ref) if ref is not None else tangents[0].orthogonal()
    verts, ws = [], []
    for k in range(m):
        t = tangents[k]
        normal = (normal - t*normal.dot(t)).normalized()
        binormal = t.cross(normal)
        for j in range(n):
            a = 2*math.pi*j/n
            verts.append(pts[k] + (normal*math.cos(a) + binormal*math.sin(a))*radii[k])
            ws.append(weights[k] if isinstance(weights, list) else weights)
    faces = []
    for k in range(m if closed else m-1):
        k2 = (k+1) % m
        for j in range(n):
            faces.append((k*n+j, k*n+(j+1) % n, k2*n+(j+1) % n, k2*n+j))
    if not closed:
        faces.append(tuple(reversed(range(n)))); faces.append(tuple(range((m-1)*n, m*n)))
    return mesh(name, verts, faces, mat, ws)

def slab(name, point, ni, nj, thickness, mat, weight):
    # Closed thin patch: point(i, j, offset) on a curved surface, two layers.
    verts = [point(i, j, layer*thickness) for layer in (0, 1) for i in range(ni) for j in range(nj)]
    idx = lambda layer, i, j: layer*ni*nj + i*nj + j
    faces = []
    for i in range(ni-1):
        for j in range(nj-1):
            faces.append((idx(1, i, j), idx(1, i+1, j), idx(1, i+1, j+1), idx(1, i, j+1)))
            faces.append((idx(0, i, j), idx(0, i, j+1), idx(0, i+1, j+1), idx(0, i+1, j)))
    border = [(i, 0) for i in range(ni-1)] + [(ni-1, j) for j in range(nj-1)] + \
             [(i, nj-1) for i in range(ni-1, 0, -1)] + [(0, j) for j in range(nj-1, 0, -1)]
    for k in range(len(border)):
        a, b = border[k], border[(k+1) % len(border)]
        faces.append((idx(0, *a), idx(0, *b), idx(1, *b), idx(1, *a)))
    return mesh(name, verts, faces, mat, [weight]*len(verts))

def ribbon(name, ring_at, s0, s1, turns, width, mat, mid=.0024, edge=.0005, layer=.0003, per_turn=18):
    # Helical cloth tape over a lofted limb. ring_at(s) -> centre, u, v, half width, half depth, weights.
    count = int(turns*per_turn)+1
    verts, ws, centres = [], [], []
    for k in range(count):
        f = k/(count-1)
        s = s0 + (s1-s0)*f
        theta = 2*math.pi*turns*f
        for ds, off in ((-width/2, edge), (0, mid), (width/2, edge)):
            q = min(max(s+ds, s0), s1)
            center, u, v, w, d, weight = ring_at(q)
            o = off + layer*turns*f
            verts.append(ring_point(center, u, v, w+o, d+o, theta)); ws.append(weight); centres.append(center)
    faces = []
    for k in range(count-1):
        for c in range(2):
            a = k*3+c
            faces.append((a, a+3, a+4, a+1))
    obj = mesh(name, verts, faces, mat, ws, consistent=False)
    orient_outward(obj, lambda p: centres[p.vertices[0]])
    return obj

def orient_outward(obj, centre_of):
    # Open sheets: flip all faces if the average normal points into the limb/head.
    score = 0
    for p in obj.data.polygons:
        score += p.normal.dot(p.center - centre_of(p))
    if score < 0:
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(obj.data); bm.free(); obj.data.update()

def interpolate(table, s):
    # table rows: (param, centre, w, d, weights); linear like the loft between rings.
    if s <= table[0][0]: return table[0][1:]
    for a, b in zip(table, table[1:]):
        if s <= b[0]:
            f = (s-a[0])/(b[0]-a[0])
            weights = {g: a[4].get(g, 0)*(1-f) + b[4].get(g, 0)*f for g in set(a[4]) | set(b[4])}
            return Vector(a[1]).lerp(Vector(b[1]), f), a[2]+(b[2]-a[2])*f, a[3]+(b[3]-a[3])*f, weights
    return table[-1][1:]

def join(objs, name):
    for obj in objs:
        if obj in parts: parts.remove(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objs: obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.object; obj.name = name
    return obj

def remesh(obj, voxel, target_tris, smooth_factor=.5, smooth_iter=3, quads=False):
    # Union of overlapping primitives into one seamless surface, then a triangle budget.
    # quads: even quad layout (QuadriFlow) fitted back onto the union, instead of decimation,
    # whose long uneven triangles show as dents under glossy highlights.
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.data.remesh_voxel_size = voxel
    bpy.ops.object.voxel_remesh()
    if smooth_iter:
        m = obj.modifiers.new('Union smoothing', 'SMOOTH'); m.factor = smooth_factor; m.iterations = smooth_iter
        bpy.ops.object.modifier_apply(modifier=m.name)
    if quads:
        ref = bpy.data.objects.new(obj.name+' union', obj.data.copy()); bpy.context.collection.objects.link(ref)
        # QuadriFlow only approximates the face count; try seeds until it lands within the budget.
        for seed in range(1, 9):
            obj.data = ref.data.copy()
            bpy.ops.object.quadriflow_remesh(target_faces=target_tris//2, use_mesh_symmetry=False, use_preserve_sharp=False,
                                             use_preserve_boundary=False, smooth_normals=False, seed=seed)
            if .85*target_tris <= 2*len(obj.data.polygons) <= 1.03*target_tris: break
        else:
            raise RuntimeError('QuadriFlow missed the triangle budget')
        m = obj.modifiers.new('Even spacing', 'CORRECTIVE_SMOOTH'); m.factor = .5; m.iterations = 4; m.smooth_type = 'LENGTH_WEIGHTED'
        m.use_only_smooth = True
        bpy.ops.object.modifier_apply(modifier=m.name)
        m = obj.modifiers.new('Fit to union', 'SHRINKWRAP'); m.target = ref; m.wrap_method = 'NEAREST_SURFACEPOINT'
        bpy.ops.object.modifier_apply(modifier=m.name)
        m = obj.modifiers.new('Soften', 'LAPLACIANSMOOTH'); m.lambda_factor = .35; m.iterations = 3; m.use_volume_preserve = True
        bpy.ops.object.modifier_apply(modifier=m.name)
        bpy.data.meshes.remove(ref.data)
    obj.data.calc_loop_triangles()
    if not quads and len(obj.data.loop_triangles) > target_tris:
        m = obj.modifiers.new('Triangle budget', 'DECIMATE'); m.ratio = target_tris/len(obj.data.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=m.name)
    for polygon in obj.data.polygons: polygon.use_smooth = True
    obj.vertex_groups.clear()
    return obj

def assign(obj, fn):
    obj.vertex_groups.clear()
    for vertex in obj.data.vertices:
        for group, value in fn(vertex.co).items():
            if value <= 0: continue
            vg = obj.vertex_groups.get(group) or obj.vertex_groups.new(name=group)
            vg.add([vertex.index], value, 'REPLACE')

def set_anatomy(obj, fn):
    attr = obj.data.color_attributes.get('Anatomy') or obj.data.color_attributes.new(name='Anatomy', type='FLOAT_COLOR', domain='POINT')
    for vertex, color in zip(obj.data.vertices, attr.data): color.color = fn(vertex.co)

def bvh(obj):
    return BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons])

bone('root', (0, 0, 0), (0, 0, .18))
bone('pelvis', (0, 0, .90), (0, 0, 1.04), 'root')
bone('spine', (0, 0, 1.04), (0, 0, 1.24), 'pelvis')
bone('chest', (0, 0, 1.24), (0, 0, 1.47), 'spine')
bone('neck', (0, 0, 1.47), (0, 0, 1.57), 'chest')
bone('head', (0, 0, 1.57), (0, 0, 1.82), 'neck')

# ---------------------------------------------------------------- torso
TORSO = [
    (.94, 0, .150, .095, {'pelvis': 1}),
    (.99, 0, .150, .096, {'pelvis': 1}),
    (1.04, 0, .147, .097, {'spine': 1}),
    (1.09, -.002, .150, .099, {'spine': 1}),
    (1.14, -.004, .162, .105, {'spine': .65, 'chest': .35}),
    (1.20, -.008, .182, .113, {'spine': .35, 'chest': .65}),
    (1.26, -.010, .204, .121, {'chest': 1}),
    (1.32, -.012, .221, .125, {'chest': 1}),
    (1.37, -.010, .236, .121, {'chest': 1}),
    (1.41, -.004, .247, .110, {'chest': 1}),
    (1.445, 0, .244, .095, {'chest': 1}),
    (1.47, .002, .190, .080, {'chest': 1}),
    (1.49, .002, .128, .071, {'chest': 1}),
    (1.505, 0, .076, .065, {'neck': .4, 'chest': .6}),
]
def torso_shape(i, radial):
    z = TORSO[i][0]; phi = front_angle(radial); m = 1.0
    pec = bell(z, 1.335, .045)
    m += .06*pec*(bell(phi, 34, 22) + bell(phi, -34, 22)) - .02*pec*bell(phi, 0, 9)
    m -= .02*bell(z, 1.12, .08)*bell(phi, 0, 8)
    m += .035*bell(z, 1.26, .06)*(bell(phi, 122, 24) + bell(phi, -122, 24))
    m -= .025*bell(abs(phi), 180, 11)*bell(z, 1.25, .15)
    return m
loft('Torso', [((0, y, z), w, d, wt) for z, y, w, d, wt in TORSO], skin, 32, shape=torso_shape)
vertical('Neck', [(0, .002, 1.47, .071, .064), (0, .0, 1.53, .062, .058), (0, -.002, 1.595, .060, .056)], skin, 'neck', 18)

# ---------------------------------------------------------------- head, face and headgear
HEAD = [(1.548, -.010, .050, .055), (1.565, -.030, .058, .066), (1.59, -.018, .078, .078), (1.625, -.012, .088, .088),
        (1.66, -.008, .095, .094), (1.70, -.004, .099, .097), (1.735, 0, .100, .098), (1.77, .004, .094, .092),
        (1.795, .006, .077, .075), (1.81, .007, .047, .046), (1.817, .007, .012, .012)]
HC = Vector((0, .002, 1.69))
def head_ring(z):
    z = min(max(z, HEAD[0][0]), HEAD[-1][0])
    for a, b in zip(HEAD, HEAD[1:]):
        if z <= b[0]:
            f = (z-a[0])/(b[0]-a[0])
            return tuple(a[k]+(b[k]-a[k])*f for k in (1, 2, 3))
    return HEAD[-1][1:]
def head_point(phi, z, off=0.0):
    yc, w, d = head_ring(z); a = math.radians(phi)
    p = Vector((w*math.sin(a), yc - d*math.cos(a), z))
    if off:
        r = (p-HC).length; p = HC + (p-HC)*((r+off)/r)
    return p
loft('Head', [((0, y, z), w, d, {'head': 1}) for z, y, w, d in HEAD], skin, 24)

# Simple, non-realistic features: brows, eyes, nose, closed mouth.
for s in (-1, 1):
    c = head_point(s*21, 1.703, -.0016)
    loft('Eye '+str(s), [(c+Vector((0, 0, dz)), w, d, {'head': 1}) for dz, w, d in
         [(-.0056, .0035, .0018), (-.003, .0082, .0036), (0, .0095, .0042), (.003, .0082, .0036), (.0056, .0035, .0018)]], dark, 12)
    tube('Brow '+str(s), [head_point(s*p, 1.722+.0025*math.sin(math.radians(p*5)), .0004) for p in (8, 15, 23, 30, 35)],
         [.0026, .0042, .0046, .0038, .0022], skin, {'head': 1}, n=8)
NOSE = [(1.717, .008, .006, .003), (1.70, .010, .010, .010), (1.684, .013, .014, .016), (1.672, .017, .015, .020),
        (1.663, .015, .011, .013), (1.658, .007, .005, .002)]
loft('Nose', [((0, head_point(0, z).y - p + d, z), w, d, {'head': 1}) for z, w, d, p in NOSE], skin, 14)
tube('Mouth', [head_point(p, 1.626, -.0004) for p in (-11, -6, 0, 6, 11)], [.0010, .0018, .0019, .0018, .0010], dark, {'head': 1}, n=6)

# Sparring headgear: offset head surface with a face opening, padded outward.
HG_Z = [1.598, 1.615, 1.635, 1.66, 1.685, 1.705, 1.722, 1.735, 1.744, 1.751, 1.765, 1.782, 1.798, 1.81, 1.8172]
OPEN = [(1.598, 40), (1.625, 44), (1.66, 50), (1.70, 54), (1.725, 51), (1.738, 43), (1.745, 28), (1.749, 0)]
def open_half(z):
    if z >= OPEN[-1][0]: return 0
    for a, b in zip(OPEN, OPEN[1:]):
        if z <= b[0]: return a[1] + (b[1]-a[1])*(z-a[0])/(b[0]-a[0])
    return OPEN[0][1]
M = 30
hg_verts = []
for r, z in enumerate(HG_Z):
    h = open_half(z)
    for k in range(M+1):
        phi = h + (360-2*h)*k/M
        if r == len(HG_Z)-1:
            hg_verts.append(head_point(0, 1.817, .0075)); continue
        zz = z - .022*((1-math.cos(math.radians(phi)))/2)*max(0, 1-(z-1.598)/.05)
        pad = .0075 + .011*(bell(phi, 90, 30) + bell(phi, 270, 30))*bell(zz, 1.662, .04) \
              + .006*(bell(phi, 0, 45) + bell(phi, 360, 45))*bell(zz, 1.772, .022)
        hg_verts.append(head_point(phi, zz, pad))
hg_faces = [(r*(M+1)+k, r*(M+1)+k+1, (r+1)*(M+1)+k+1, (r+1)*(M+1)+k) for r in range(len(HG_Z)-1) for k in range(M)]
headgear = mesh('Headgear', hg_verts, hg_faces, headgear_mat, [{'head': 1}]*len(hg_verts), consistent=False)
bm = bmesh.new(); bm.from_mesh(headgear.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=.0003)
bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=.00001)
bm.to_mesh(headgear.data); bm.free(); headgear.data.update()
orient_outward(headgear, lambda p: HC)
headgear.data.materials.append(ivory)
solid = headgear.modifiers.new('Headgear padding', 'SOLIDIFY')
solid.thickness = .015; solid.offset = 1; solid.use_rim = True; solid.use_even_offset = True; solid.material_offset_rim = 1
bpy.context.view_layer.objects.active = headgear
bpy.ops.object.modifier_apply(modifier=solid.name)
for polygon in headgear.data.polygons: polygon.use_smooth = True
tube('Chin strap', [head_point(p, z, .0055) for p, z in
     [(102, 1.606), (82, 1.586), (56, 1.566), (28, 1.553), (0, 1.549), (-28, 1.553), (-56, 1.566), (-82, 1.586), (-102, 1.606)]],
     .0055, headgear_mat, {'head': 1}, n=8)

# ---------------------------------------------------------------- shorts
vertical('Shorts hip', [(0, 0, .925, .196, .121), (0, 0, .975, .18, .122), (0, 0, 1.027, .162, .105)], shorts, 'pelvis', 24)

arm_tables = {}
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

    # ------------------------------------------------------------ arm with deltoid, biceps and forearm relief
    arm_axis = (wrist-shoulder).normalized()
    fa_table = [(0, elbow, .050, .051, {ua: .5, fa: .5}), (.10, elbow.lerp(wrist, .10), .060, .060, {ua: .15, fa: .85}),
                (.28, elbow.lerp(wrist, .28), .066, .062, {fa: 1}), (.45, elbow.lerp(wrist, .45), .062, .057, {fa: 1}),
                (.65, elbow.lerp(wrist, .65), .051, .046, {fa: 1}), (.85, elbow.lerp(wrist, .85), .042, .040, {fa: 1}),
                (1.0, wrist, .037, .037, {fa: .3, hand: .7})]
    arm_rings = [
        (shoulder + (shoulder-elbow)*.12, .060, .066, {ua: 1}),
        (shoulder.lerp(elbow, .05), .080, .084, {ua: 1}),
        (shoulder.lerp(elbow, .20), .089, .088, {ua: 1}),
        (shoulder.lerp(elbow, .38), .080, .077, {ua: 1}),
        (shoulder.lerp(elbow, .56), .074, .072, {ua: 1}),
        (shoulder.lerp(elbow, .76), .064, .062, {ua: 1}),
        (shoulder.lerp(elbow, .91), .053, .053, {ua: .85, fa: .15}),
    ] + [(c, w, d, wt) for _, c, w, d, wt in fa_table]
    out_up = Vector((sign*.7, 0, .7)).normalized(); fore_out = Vector((sign*.6, -.6, .5)).normalized()
    def arm_shape(i, radial, out_up=out_up, fore_out=fore_out):
        m = 1.0
        if i in (1, 2): m += .025*max(0, radial.dot(out_up))**2
        if i in (3, 4): m += .065*max(0, -radial.y)**2
        if i in (3, 4, 5): m += .035*max(0, radial.y)**2
        if i in (8, 9): m += .04*max(0, radial.dot(fore_out))**2
        return m
    loft('Arm.'+side, arm_rings, skin, 18, arm_axis, shape=arm_shape)
    _, au, av = loft_frame(arm_axis)
    arm_tables[side] = (fa_table, au, av)
    def arm_ring_at(s, fa_table=fa_table, au=au, av=av):
        c, w, d, wt = interpolate(fa_table, s)
        return c, au, av, w, d, wt
    ribbon('Hand wrap.'+side, arm_ring_at, .56, .98, 4.5, .016/(wrist-elbow).length, wrap_mat)

    # ------------------------------------------------------------ boxing glove (hand bone, weight 1.0)
    direction = (fingertip-wrist).normalized()
    back = Vector((0, 1, 0)); back = (back-direction*back.dot(direction)).normalized()
    across = direction.cross(back).normalized()
    t = -sign  # thumb side, as in the reviewed anatomy (Unity test PlayerGloveAnatomyMatchesTrackedWristFrame)
    def gp(f, x=0.0, z=0.0, wrist=wrist, direction=direction, across=across, back=back):
        # Glove frame in in-game proportions: forward, lateral, dorsal (stretched for Unity's depth squash).
        return wrist + direction*(f*GLOVE_LENGTH_SCALE) + across*x + back*(z*DC)
    body_x = -t*GLOVE_BODY_SHIFT
    W = {hand: 1}
    fist = loft('Glove fist.'+side, [(gp(f, body_x, z), w, d*DC, W) for f, z, w, d in [
        (.030, .000, .051, .046), (.060, .005, .058, .052), (.095, .011, .066, .057), (.135, .013, .072, .062),
        (.175, .010, .075, .066), (.215, .003, .075, .067), (.248, -.004, .069, .059), (.272, -.008, .055, .044),
        (.285, -.009, .034, .026), (.291, -.009, .008, .006)]], red, 28, direction, across, power=2.5)
    def lateral(name, f, z, rf, rz, half, mat=red):
        prof = [(-1.0, .2), (-.94, .62), (-.8, .88), (-.5, .98), (0, 1), (.5, .98), (.8, .88), (.94, .62), (1.0, .2)]
        return loft(name, [(gp(f, body_x+x*half, z), rf*p*GLOVE_LENGTH_SCALE, rz*p*DC, W) for x, p in prof], mat, 18, across, direction)
    knuckles = lateral('Knuckle roll.'+side, .238, .034, .044, .040, .064)
    fingers = lateral('Folded fingers.'+side, .198, -.050, .040, .030, .058)
    thumb = loft('Thumb.'+side, [(gp(f, body_x+t*x, z), w, d*DC, W) for f, x, z, w, d in [
        (.070, .044, -.020, .015, .015), (.098, .060, -.029, .025, .026), (.135, .071, -.033, .028, .030),
        (.170, .071, -.035, .026, .028), (.196, .063, -.040, .021, .023), (.213, .051, -.044, .014, .015),
        (.219, .042, -.045, .004, .004)]], red, 18, direction, across)
    primitives = {'thumb': bvh(thumb), 'fingers': bvh(fingers), 'fist': bvh(fist), 'knuckles': bvh(knuckles)}
    if side == 'L':
        glove = remesh(join([fist, knuckles, fingers, thumb], 'Glove.'+side), .0022, 1150, .6, 8, quads=True)
        left_glove = glove
    else:
        # QuadriFlow is not deterministic; mirror the left glove so both sides have identical bounds
        # (Unity normalises each by its own width, which sets the cuff size around the forearm).
        for obj in (fist, knuckles, fingers, thumb):
            parts.remove(obj); bpy.data.meshes.remove(obj.data)
        data = left_glove.data.copy(); data.transform(Matrix.Scale(-1, 4, (1, 0, 0)))
        bm = bmesh.new(); bm.from_mesh(data); bmesh.ops.reverse_faces(bm, faces=bm.faces); bm.to_mesh(data); bm.free()
        glove = bpy.data.objects.new('Glove.'+side, data); bpy.context.collection.objects.link(glove)
    def glove_tag(co, primitives=primitives):
        nearest = min(primitives, key=lambda k: primitives[k].find_nearest(co)[3])
        return THUMB if nearest == 'thumb' else FINGERS if nearest == 'fingers' else NONE
    set_anatomy(glove, glove_tag)
    assign(glove, lambda co, W=W: W)
    parts.append(glove)
    glove_items = [glove]
    # Cuff: sleeve with a rolled lip at the opening, closed inside the fist. Placed from the padded body's
    # measured bounds so the player glove is PLAYER_GLOVE_LENGTH long at UNITY_GLOVE_WIDTH and centred on the
    # forearm, whatever the remesh did to the body. Inner radii are in-game metres (divided by the glove scale).
    rel = [v.co-wrist for v in glove.data.vertices]
    xs = [r.dot(across) for r in rel]; zs = [r.dot(back)/DC for r in rel]
    scale = UNITY_GLOVE_WIDTH/(max(xs)-min(xs))
    rim = max(r.dot(direction) for r in rel) - PLAYER_GLOVE_LENGTH/scale + .0032  # piping radius behind the rim
    axis_x = (max(xs)+min(xs))/2 + FOREARM_AXIS_OFFSET[0]/scale
    axis_z = (max(zs)+min(zs))/2 + FOREARM_AXIS_OFFSET[1]/scale
    def cp(f, x=0.0, z=0.0, rim=rim, axis_x=axis_x, axis_z=axis_z):
        # Cuff profile authored with the rim at f=-.0598.
        return wrist + direction*(rim+(f+.0598)*GLOVE_LENGTH_SCALE) + across*(axis_x+x) + back*((axis_z+z)*DC)
    inner = [(w/scale, d/scale) for w, d in CUFF_INNER]
    glove_items.append(loft('Glove cuff.'+side, [(cp(f), w, d*DC, W) for f, w, d in [
        (-.015, *inner[0]), (-.045, *inner[1]), (-.0575, *inner[2]), (-.0598, *inner[3]), (-.0585, .0530, .0480),
        (-.050, .0590, .0565), (-.020, .060, .0575), (.020, .060, .0575), (.046, .059, .0565), (.056, .053, .050),
        (.062, .036, .034)]], red, 24, direction, across))
    glove_items.append(loft('Wrist strap.'+side, [(cp(f), w, d*DC, W) for f, w, d in [
        (-.042, .0592, .0567), (-.040, .0640, .0612), (-.006, .0640, .0612), (-.004, .0592, .0567)]],
        strap_mat, 24, direction, across, caps=(False, False)))
    def ellipse_ring(f, w, d, count=20):
        return [cp(f, math.cos(a)*w, math.sin(a)*d) for a in (2*math.pi*k/count for k in range(count))]
    for f, w, d, r in [(-.0597, CUFF_PIPING[0]/scale, CUFF_PIPING[1]/scale, .0032), (.046, .0593, .0568, .0030)]:
        glove_items.append(tube('Glove piping.'+side, ellipse_ring(f, w, d), r, ivory, W, closed=True, n=4, ref=direction))
    # Strap end tab on the back of the wrist, with its stitched edge.
    def tab_point(i, j, off):
        f = -.036 + .027*i/3; a = math.radians(58 + 72*j/6)
        return cp(f, math.cos(a)*(.0640+off), math.sin(a)*(.0612+off))
    glove_items.append(slab('Strap tab.'+side, lambda i, j, layer: tab_point(i, j, .0002+layer), 4, 7, .0042, strap_mat, W))
    glove_items.append(tube('Strap tab edge.'+side, [tab_point(i/2, 6, .0022) for i in range(7)], .0018, ivory, W, n=6))
    for obj in glove_items:
        glove_frames[obj.name] = (wrist.copy(), direction.copy(), across.copy(), back.copy())

    # ------------------------------------------------------------ legs
    th, sh = 'thigh.'+side, 'shin.'+side
    leg_rings = [(hip, .090, .100, {th: 1}), (hip.lerp(knee, .3), .100, .100, {th: 1}), (hip.lerp(knee, .6), .088, .088, {th: 1}),
                 (hip.lerp(knee, .85), .066, .067, {th: 1}), (knee, .060, .064, {th: .5, sh: .5}),
                 (knee.lerp(ankle, .12), .062, .066, {sh: 1}), (knee.lerp(ankle, .30), .066, .070, {sh: 1}),
                 (knee.lerp(ankle, .55), .056, .059, {sh: 1}), (knee.lerp(ankle, .8), .044, .046, {sh: 1}), (ankle, .037, .039, {sh: 1})]
    def leg_shape(i, radial):
        m = 1.0
        if i in (6, 7): m += .10*max(0, radial.y)**2
        if i == 4: m += .05*max(0, -radial.y)**3
        if i == 3: m += .03*max(0, -radial.y)**2
        return m
    loft('Leg.'+side, leg_rings, skin, 18, shape=leg_shape)
    loft('Shorts leg.'+side, [
        ((sign*.104, 0, .70), .115, .124, {th: 1}),
        ((sign*.10, 0, .79), .119, .127, {th: 1}),
        ((sign*.09, 0, .90), .124, .127, {th: .5, 'pelvis': .5}),
        ((sign*.075, 0, .978), .102, .12, {'pelvis': 1}),
        ((sign*.064, 0, 1.025), .102, .10, {'pelvis': 1}),
    ], shorts, 20)
    vertical('Shorts hem.'+side, [(sign*.104, 0, .699, .1155, .1245), (sign*.104, 0, .706, .1185, .1275), (sign*.104, 0, .719, .1175, .1265)], ivory, th, 24, caps=(False, False))

    # ------------------------------------------------------------ boxing boots (floor at z=.025 before the final shift)
    bx = sign*.145
    # The boot's rounded underside reaches into the sole (bottom 9 mm lower, top unchanged): no groove above the sole.
    foot_body = loft('Boot foot.'+side, [((bx, y, zc-.0045), w, d+.0045, {}) for y, zc, w, d in [
        (.080, .087, .035, .042), (.066, .095, .050, .050), (.030, .105, .056, .060), (-.020, .099, .060, .054),
        (-.070, .085, .064, .040), (-.120, .073, .066, .028), (-.165, .068, .058, .023), (-.195, .063, .042, .018),
        (-.208, .059, .020, .012), (-.212, .057, .004, .004)]], sole, 20, (0, -1, 0), (1, 0, 0))
    shaft = vertical('Boot shaft.'+side, [(bx, y, z, w, d) for z, y, w, d in [
        (.075, .008, .050, .056), (.145, .006, .050, .055), (.205, .002, .054, .060), (.265, -.002, .062, .069),
        (.315, -.005, .067, .076), (.347, -.006, .069, .078), (.355, -.006, .063, .072), (.358, -.006, .050, .058)]], sole, 'shin.'+side, 20)
    boot = remesh(join([foot_body, shaft], 'Boot.'+side), .003, 520, .5, 3)
    boot_weight = lambda co, sh=sh, ft='foot.'+side: {sh: min(1, max(0, (co.z-.135)/.06)), ft: 1-min(1, max(0, (co.z-.135)/.06))}
    assign(boot, boot_weight)
    set_anatomy(boot, lambda co: NONE)
    parts.append(boot)
    tree = bvh(boot)
    for zl in (.135, .17, .205, .24, .275, .31):
        hits = []
        for dx in (-.017, 0, .017):
            hit = tree.ray_cast(Vector((bx+dx, -.4, zl)), Vector((0, 1, 0)))
            hits.append(hit[0] + hit[1]*.0018)
        tube('Boot lace.'+side, hits, .0026, ivory, [boot_weight(h) for h in hits], n=4)
    vertical('Boot collar.'+side, [(bx, -.005, .326, .0685, .0775), (bx, -.0055, .338, .0705, .0795), (bx, -.006, .350, .0700, .0790)], ivory, sh, 24, caps=(False, False))
    # Thin sole that follows the boot outline and ends under the rounded heel (a wider, longer plate read as a skate blade).
    loft('Boot sole.'+side, [((bx, y, .035), w, .0100, {'foot.'+side: 1}) for y, w in [
        (.068, .012), (.064, .030), (.052, .046), (.030, .053), (-.020, .057), (-.070, .061), (-.120, .063),
        (-.165, .056), (-.195, .041), (-.212, .022), (-.218, .007)]], dark, 16, (0, -1, 0), (1, 0, 0))

# Waistband over the union of the shorts.
vertical('Wide waistband', [(0, 0, .984, .199, .135), (0, 0, .990, .206, .142), (0, 0, 1.030, .186, .128), (0, 0, 1.037, .176, .120)], ivory, 'pelvis', 40, caps=(False, False))

# Union the shorts into one garment surface, avoiding intersecting crotch panels.
garment = [o for o in parts if o.name.startswith(('Shorts hip', 'Shorts leg.'))]
pants = remesh(join(garment, 'Shorts'), .008, 1250, .7, 3)
pants.vertex_groups.clear()
groups = {name: pants.vertex_groups.new(name=name) for name in ('pelvis', 'thigh.L', 'thigh.R')}
for vertex in pants.data.vertices:
    hip_weight = max(0, min(1, (vertex.co.z-.82)/.14))
    groups['pelvis'].add([vertex.index], hip_weight, 'REPLACE')
    groups['thigh.L' if vertex.co.x >= 0 else 'thigh.R'].add([vertex.index], 1-hip_weight, 'REPLACE')
set_anatomy(pants, lambda co: NONE)
parts.append(pants)

# Slimmer silhouette at the same eye height. Transform the bind skeleton too.
for obj in parts:
    for vertex in obj.data.vertices:
        if obj.name in glove_frames:
            wrist, direction, across, back = glove_frames[obj.name]
            scale = lambda p: Vector((p.x*.88, p.y*.90, p.z))
            new_dir = scale(direction).normalized()
            new_back = Vector((0, 1, 0)); new_back = (new_back-new_dir*new_back.dot(new_dir)).normalized()
            new_x = new_dir.cross(new_back).normalized(); delta = vertex.co-wrist
            vertex.co = scale(wrist)+new_x*delta.dot(across)+new_dir*delta.dot(direction)+new_back*delta.dot(back)
        else:
            vertex.co.x *= .88
            vertex.co.y *= .90
for _, a, b, _ in bone_defs:
    for point in (a, b): point.x *= .88; point.y *= .90

# A single exportable skinned mesh.
body = join(list(parts), 'Boxer_Mesh')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for vertex in body.data.vertices: vertex.co.z -= .025
# Canonical material slot order (Unity indexes slots 0-5).
index_of = {m.name: i for i, m in enumerate(MATERIAL_ORDER)}
old = [index_of[m.name] for m in body.data.materials]
mi = np.zeros(len(body.data.polygons), dtype=np.int32); body.data.polygons.foreach_get('material_index', mi)
mi = np.array(old, dtype=np.int32)[mi]
body.data.materials.clear()
for mat in MATERIAL_ORDER: body.data.materials.append(mat)
body.data.polygons.foreach_set('material_index', mi); body.data.update()

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
    if name.startswith('hand.'): eb.align_roll(Vector((0, 1, 0)))
    if parent: eb.parent = arm_data.edit_bones[parent]
    eb.use_deform = name != 'root'
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front = True
modifier = body.modifiers.new('Boxer skin', 'ARMATURE'); modifier.object = rig
body.parent = rig
rig['readme'] = 'Original boxer with sparring headgear. FK rig, metre scale. Guard and Hit_Recoil are pose demos, not final gameplay animation.'

# ---------------------------------------------------------------- player forearm (separate FBX)
# Built along +Z = hand side; origin at the midpoint; exported with axes baked so Unity mesh +Y = hand.
forearm_parts_start = len(parts)
FA = [(-.140, .010, .009), (-.138, .026, .023), (-.133, .036, .032), (-.124, .0425, .038), (-.108, .0450, .040),
      (-.085, .0450, .0395), (-.055, .0435, .037), (-.020, .0410, .0345), (.020, .0380, .0318), (.060, .0350, .0295),
      (.090, .0335, .0285), (.120, .0325, .0280), (.140, .0322, .0278)]
fa_obj = loft('Player_Forearm', [((0, 0, z), w, d, {}) for z, w, d in FA], skin, 24, caps=(True, False))
FA_TABLE = [(z, (0, 0, z), w, d, {}) for z, w, d in FA]
def forearm_ring_at(s):
    c, w, d, wt = interpolate(FA_TABLE, s)
    return c, Vector((1, 0, 0)), Vector((0, 1, 0)), w, d, wt
fa_wrap = ribbon('Player forearm wrap', forearm_ring_at, .066, .140, 4.5, .016, wrap_mat, per_turn=32)
forearm = join([fa_obj, fa_wrap], 'Player_Forearm')
forearm.vertex_groups.clear()
del parts[forearm_parts_start:]
forearm_collection = bpy.data.collections.new('Player forearm - separate FBX')
bpy.context.scene.collection.children.link(forearm_collection)
for collection in list(forearm.users_collection): collection.objects.unlink(forearm)
forearm_collection.objects.link(forearm)

def reset_pose():
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
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
def area(name, loc, energy, size, color, target=(0, 0, 1)):
    data = bpy.data.lights.new(name, 'AREA'); data.energy = energy; data.shape = 'DISK'; data.size = size; data.color = color
    obj = bpy.data.objects.new(name, data); stage.objects.link(obj); obj.location = loc; track(obj, target)
    return obj
area('Large soft key', (3, -4, 5), 450, 4, (1, .88, .76))
area('Soft fill', (-3, -2, 2.8), 260, 3, (.73, .85, 1))
area('Edge light', (1, 3, 4), 600, 3, (1, .9, .78))
camera_data = bpy.data.cameras.new('Presentation camera')
camera = bpy.data.objects.new('Presentation camera', camera_data); stage.objects.link(camera)
camera_data.type = 'ORTHO'; camera_data.ortho_scale = 2.25
scene = bpy.context.scene; scene.camera = camera
scene.render.engine = 'CYCLES'; scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.world.color = (.18, .18, .18)
scene.render.resolution_x = 1000; scene.render.resolution_y = 1100; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.fps = 30; scene.frame_start = 1; scene.frame_end = 30
scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1
scene.view_settings.view_transform = 'AgX'

# Verify skin weights, mesh validity, topology, animation range and dimensions.
assert not body.data.validate(verbose=True), 'Invalid mesh generated'
for vertex in body.data.vertices:
    assert abs(sum(g.weight for g in vertex.groups)-1) < 1e-5, 'Unnormalized skin weight'
    assert vertex.groups, 'Unweighted vertex'
body.data.calc_loop_triangles()
group_names = {g.index: g.name for g in body.vertex_groups}
def dominant(vertex):
    g = max(vertex.groups, key=lambda g: g.weight); return group_names[g.group], g.weight
glove_tris = {}
for side in 'LR':
    inside = {v.index for v in body.data.vertices if dominant(v)[0] == 'hand.'+side and dominant(v)[1] > .9}
    glove_tris[side] = sum(1 for tri in body.data.loop_triangles if all(i in inside for i in tri.vertices))
forearm.data.calc_loop_triangles()
stats = {'vertices': len(body.data.vertices), 'triangles': len(body.data.loop_triangles),
         'glove_triangles': glove_tris, 'player_forearm_triangles': len(forearm.data.loop_triangles),
         'bones': len(rig.data.bones), 'materials': len(body.data.materials),
         'height_m': round(max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices), 4),
         'actions': ['Guard_Pose', 'Hit_Recoil'], 'third_party_meshes': False}
assert 10000 <= stats['triangles'] <= 15000, stats
assert all(1500 <= n <= 2500 for n in glove_tris.values()), glove_tris
assert stats['materials'] <= 10
assert 1.7 < stats['height_m'] < 1.9

# Export asset only, with both demonstration actions. FBX is separate from Unity Assets.
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'boxer-codex.fbx'), use_selection=True,
    object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False, axis_forward='-Z', axis_up='Y',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0, mesh_smooth_type='FACE')
bpy.ops.object.select_all(action='DESELECT'); forearm.select_set(True)
bpy.context.view_layer.objects.active = forearm
bpy.ops.export_scene.fbx(filepath=str(OUT/'player-forearm.fbx'), use_selection=True, object_types={'MESH'},
    axis_forward='-Z', axis_up='Y', bake_space_transform=True, bake_anim=False, mesh_smooth_type='FACE')

# ---------------------------------------------------------------- previews
# Mimic Unity: the hand bone's dorsal axis is squashed by GloveDepthRatio at runtime.
def unity_glove_scale(on):
    for side in 'LR': rig.pose.bones['hand.'+side].scale = (1, 1, UNITY_GLOVE_DEPTH_RATIO if on else 1)
    bpy.context.view_layer.update()
forearm.hide_render = True
unity_glove_scale(True)
for label, position in [('hero', (3, -6, 2.9)), ('front', (0, -6, 1.9)), ('side', (6, -.4, 2.0))]:
    camera.location = position; track(camera, (0, -.02, .93))
    scene.render.filepath = str(OUT/('preview-'+label+'.png'))
    bpy.ops.render.render(write_still=True)
camera.location = (3, -6, 2.9); track(camera, (0, -.02, .93))
scene.frame_set(7); rig.animation_data.action = recoil; scene.frame_set(7); unity_glove_scale(True)
scene.render.filepath = str(OUT/'preview-hit.png'); bpy.ops.render.render(write_still=True)
rig.animation_data.action = action; scene.frame_set(1); unity_glove_scale(False)

def extract_glove(bone_name, name, width=None):
    # Same rule as Unity BoxingPlayerGloves: hand-dominant > 0.9 vertices in the bone frame,
    # centred on their bounds, depth squashed, optionally normalised to a width.
    inv = rig.data.bones[bone_name].matrix_local.inverted()
    keep = {v.index: inv @ v.co for v in body.data.vertices if dominant(v)[0] == bone_name and dominant(v)[1] > .9}
    lo = Vector([min(p[i] for p in keep.values()) for i in range(3)]); hi = Vector([max(p[i] for p in keep.values()) for i in range(3)])
    centre = (lo+hi)/2; s = width/(hi.x-lo.x) if width else 1
    remap = {}; verts = []
    for i, p in keep.items():
        remap[i] = len(verts); q = (p-centre)*s; verts.append(Vector((q.x, q.y, q.z*UNITY_GLOVE_DEPTH_RATIO)))
    faces, mats = [], []
    for poly in body.data.polygons:
        if all(i in remap for i in poly.vertices):
            faces.append([remap[i] for i in poly.vertices]); mats.append(poly.material_index)
    data = bpy.data.meshes.new(name); data.from_pydata(verts, [], faces)
    for mat in body.data.materials: data.materials.append(mat)
    data.polygons.foreach_set('material_index', mats)
    for poly in data.polygons: poly.use_smooth = True
    data.update()
    obj = bpy.data.objects.new(name, data); stage.objects.link(obj)
    return obj, s

def place(obj, location, forward, dorsal):
    # Bone frame (X lateral, Y forward, Z dorsal) -> world, proper rotation.
    f = Vector(forward).normalized(); d = Vector(dorsal); d = (d - f*d.dot(f)).normalized()
    x = f.cross(d)
    obj.matrix_world = Matrix.Translation(location) @ Matrix((x, f, d)).transposed().to_4x4()

body.hide_render = True; floor.hide_render = True
lights_off = []
# Glove close-up: three-quarter knuckle view and palm view of the same glove.
glove_a, _ = extract_glove('hand.L', 'Preview glove 3/4')
glove_b, _ = extract_glove('hand.L', 'Preview glove palm')
place(glove_a, Vector((-.2, 0, 1)), (.45, -1, .25), (0.1, .3, 1))
place(glove_b, Vector((.2, 0, 1)), (0, -.25, 1), (0, 1, 0))
camera.location = (0, -1.6, 1.05); track(camera, (0, 0, 1)); camera_data.ortho_scale = .78
scene.render.resolution_x = 1400; scene.render.resolution_y = 800
scene.render.filepath = str(OUT/'preview-glove.png'); bpy.ops.render.render(write_still=True)
glove_a.hide_render = glove_b.hide_render = True

# First-person forearms: Unity layout (glove width .15 m, forearm centre .24 m behind the glove centre).
camera_data.type = 'PERSP'; camera_data.lens = 20; camera_data.sensor_width = 36
eye = Vector((0, -1.0, 1.65)); camera.location = eye; track(camera, eye + Vector((0, 1, -.42)))
scene.render.resolution_x = 1400; scene.render.resolution_y = 1000
fpv = []
for side, src, x in (('left', 'hand.R', -1), ('right', 'hand.L', 1)):
    g, _ = extract_glove(src, 'FPV glove '+side, .15)
    centre = eye + Vector((x*.15, .34, -.17))
    fwd = Vector((-x*.2, .9, .38)); dorsal = Vector((x*.55, -.2, .8))
    place(g, centre, fwd, dorsal)
    arm = bpy.data.objects.new('FPV forearm '+side, forearm.data); stage.objects.link(arm)
    f = fwd.normalized(); d = (dorsal - f*dorsal.dot(f)).normalized(); lat = f.cross(d)
    arm.matrix_world = Matrix.Translation(centre - f*(.10+.14)) @ Matrix((lat, f.cross(lat), f)).transposed().to_4x4()
    fpv += [g, arm]
scene.render.filepath = str(OUT/'preview-forearm.png'); bpy.ops.render.render(write_still=True)
for obj in fpv: obj.hide_render = True
body.hide_render = False; floor.hide_render = False
camera_data.type = 'ORTHO'; camera_data.ortho_scale = 2.25
scene.render.resolution_x = 1000; scene.render.resolution_y = 1100

# Before/after: previous delivered hero (git) beside the new hero.
before_png = OUT/'.before-hero.png'
blob = subprocess.run(['git', 'show', BEFORE_COMMIT+':./preview-hero.png'], cwd=OUT, capture_output=True, check=True).stdout
if blob.startswith(b'version https://git-lfs'):  # PNGs are LFS-tracked
    blob = subprocess.run(['git', 'lfs', 'smudge'], cwd=OUT, input=blob, capture_output=True, check=True).stdout
before_png.write_bytes(blob)
before = bpy.data.images.load(str(before_png)); after = bpy.data.images.load(str(OUT/'preview-hero.png'))
assert before.size[0] and after.size[0], 'previous preview could not be read'
def pixels(img):
    a = np.empty(img.size[0]*img.size[1]*4, dtype=np.float32); img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)
pa, pb = pixels(before), pixels(after)
h = max(pa.shape[0], pb.shape[0]); gap = 16
canvas = np.ones((h, pa.shape[1]+gap+pb.shape[1], 4), dtype=np.float32)
canvas[:pa.shape[0], :pa.shape[1]] = pa; canvas[:pb.shape[0], pa.shape[1]+gap:] = pb
combo = bpy.data.images.new('Before after', canvas.shape[1], canvas.shape[0], alpha=True)
combo.colorspace_settings.name = 'sRGB'
combo.pixels.foreach_set(canvas.ravel())
combo.filepath_raw = str(OUT/'preview-before-after.png'); combo.file_format = 'PNG'; combo.save()
for img in (before, after, combo): bpy.data.images.remove(img)
before_png.unlink()

forearm.hide_render = False; forearm.hide_set(True)
bpy.context.view_layer.objects.active = rig
for screen in bpy.data.screens:
    for area_item in screen.areas:
        if area_item.type == 'VIEW_3D':
            area_item.spaces.active.region_3d.view_distance = 3
            area_item.spaces.active.region_3d.view_location = (0, 0, .95)
            area_item.spaces.active.shading.type = 'MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'boxer-codex.blend'))
(OUT/'validation.json').write_text(json.dumps(stats, indent=2)+'\n', encoding='utf-8')
print('BOXER_MODEL_VALID', json.dumps(stats))
