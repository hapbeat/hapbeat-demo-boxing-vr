"""Round-trip the delivered blend/FBX and check rig, weights and recoil motion."""
from pathlib import Path
import json
import bpy
from mathutils import Vector

root = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(root/'boxer-codex.blend'))
rig = bpy.data.objects['Boxer_Rig']
mesh = bpy.data.objects['Boxer_Mesh']
assert len(rig.data.bones) == 20
assert any(m.type == 'ARMATURE' and m.object == rig for m in mesh.modifiers)
assert all(v.groups and abs(sum(g.weight for g in v.groups)-1) < 1e-5 for v in mesh.data.vertices)
rig.animation_data.action = bpy.data.actions['Hit_Recoil']
positions=[]
for frame in (1, 7, 30):
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    positions.append(rig.pose.bones['head'].tail.copy())
assert (positions[1]-positions[0]).length > .05, 'Hit does not move head'
assert (positions[2]-positions[0]).length < .001, 'Hit does not return to guard'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root/'boxer-codex.fbx'))
armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(armatures) == 1 and len(meshes) == 1
assert len(armatures[0].data.bones) == 20
assert all(v.groups for v in meshes[0].data.vertices)
assert len(bpy.data.actions) == 2, [a.name for a in bpy.data.actions]
for action in bpy.data.actions:
    assert action.frame_range[1]-action.frame_range[0] >= 29
result={'blend_skin_weights': 'PASS', 'recoil_head_displacement_m': round((positions[1]-positions[0]).length,4),
        'fbx_round_trip': 'PASS', 'fbx_meshes': len(meshes), 'fbx_bones': 20,
        'fbx_actions': [a.name for a in bpy.data.actions]}
(root/'round-trip-validation.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print('BOXER_ROUND_TRIP_PASS',json.dumps(result))
