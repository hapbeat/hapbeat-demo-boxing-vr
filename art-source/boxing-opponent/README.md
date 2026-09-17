# Original low-poly boxing opponent

A faceless boxer with red gloves, navy boxing shorts, chalk trim and dark boxing boots. Created in Blender 5.2.1, using original geometry and solid-color materials; no downloaded mesh, texture or animation data. General direction: a recognizable, economical boxing NPC, not a reproduction of a particular marketplace character.

## Deliverables

- `boxer-codex.blend`: editable mesh, rig, two pose/animation examples and a studio presentation setup. Opens at the guard pose.
- `boxer-codex.fbx`: only the skinned character and skeleton, with the two actions. No studio floor, camera or lights. Not yet imported or integrated into the Unity game.
- `preview-hero.png`, `preview-front.png`, `preview-side.png`: rendered guard pose.
- `preview-hit.png`: frame 7 of the recoil example.
- `validation.json`, `round-trip-validation.json`: generated checks, including FBX reimport.

## Rig and limitations

20-bone FK skeleton (forward kinematics): root, pelvis, spine, chest, neck, head, two clavicles and upper-arm/forearm/hand chains, two thigh/shin/foot chains. Blender Z up, character faces -Y, metres. Mesh stands about 1.78m tall. FBX exports Y up / -Z forward.

All vertices have normalized bone weights. The torso and each arm/leg have connected local topology with blended elbow/knee rings. Clothing, head, gloves and body are separate intersecting shells joined into one skinned object, not one watertight anatomical surface. The asset is intended for rendering, not 3D printing.

`Guard_Pose` is a static 30-frame guard sample. `Hit_Recoil` is a short 30fps example: upper body and head recoil around frame 7, then return by frame 24. Neither is a complete combat animation set. No inverse-kinematics controls, fingers, face rig, walking, root motion or Humanoid Avatar mapping are supplied. Large limb rotations may require weight/garment refinement; the provided guard and recoil are the reviewed poses.

A rig is not essential for a whole-object tilt on impact. It is useful for independently bending the torso, neck and arms without moving the feet; this example demonstrates that. The existing Unity opponent logic is unchanged. Integration should map the mesh to the existing logical attack/guard transforms rather than letting a new visual model silently change hit volumes.

## Rebuild / verify

Run in this directory:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python ./build_boxer.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python ./verify_boxer.py
```

The build replaces only this model's named outputs and uses factory startup; it does not touch an existing interactive Blender scene or Unity scene. Rebuilding does not preserve manual edits to `boxer-codex.blend`: edit the generator or save a distinct artist-authored version before rebuilding.

Original project-authored asset under this repository's license. No third-party asset license is inherited. No claim is made that the result is a complete production animation rig.
