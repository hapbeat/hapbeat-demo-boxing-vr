# Original boxing opponent and player forearm

A boxer in sparring headgear (simple eyes, brows, nose and mouth; no realistic face) with padded red boxing gloves, cotton hand wraps, navy boxing shorts, chalk trim and laced boxing boots, plus a separate player forearm. Created in Blender 5.2.1 from project-authored generator code and solid-colour Principled materials; no downloaded mesh, texture or animation data. General direction: a readable, economical boxing NPC, not a reproduction of a particular marketplace character.

## Deliverables

- `boxer-codex.blend`: editable mesh, rig, two pose/animation examples, the player forearm (hidden, own collection) and a studio presentation setup. Opens at the guard pose.
- `boxer-codex.fbx`: only the skinned character and skeleton, with the two actions. No studio floor, camera or lights. The Unity game uses a copy at `unity/boxing-vr/Assets/Boxing/Art/Boxer.fbx` (the copy is updated separately, after review).
- `player-forearm.fbx`: static skin-and-wrap forearm, 0.28 m long, radius 0.036 m at the wrist (wrap) to 0.045 m near the elbow. Axes are baked: mesh +Y is the hand side (open end, hidden inside the glove cuff), -Y the elbow side, origin at the midpoint. Fits `BoxingForearm` (length 0.28, radius 0.045) and `BoxingPresentation.RenderForearm`. Symmetric left/right, so one mesh serves both arms.
- `preview-hero.png`, `preview-front.png`, `preview-side.png`: guard pose. `preview-hit.png`: frame 7 of the recoil example.
- `preview-glove.png`: glove close-up, three-quarter knuckle view (left) and palm view (right).
- `preview-forearm.png`: first-person layout as in Unity (glove width 0.15 m, forearm centre 0.24 m behind the glove centre).
- `preview-before-after.png`: previous delivered hero render (left, commit fb14ffc) beside the current one (right).
- `validation.json`, `round-trip-validation.json`: generated checks, including FBX reimport and the Unity import contracts below.

All previews apply the runtime glove depth squash (see below), so they show the in-game glove shape.

## Unity import contracts (checked by `verify_boxer.py`)

- 20 bones with the previous names, hierarchy and orientation; one skinned mesh; 9 materials. Slots 0-5 keep the previous order (skin, shorts, chalk, glove leather, boot panels, charcoal); slots 6-8 are new (headgear, hand wrap, strap leather), so `BoxingAvatarSetup`'s per-slot colour list needs three more entries when the FBX is imported.
- Glove vertices (and only glove vertices) are weighted 1.0 to `hand.L/R`, which is how `BoxingPlayerGloves` extracts the shared player glove. Anatomy tags (vertex colour `Anatomy`: thumb R, folded fingers G, cuff B) keep the reviewed handedness.
- The gloves are authored at their in-game proportions and stretched along the hand bone's dorsal axis by 1/0.75, because Unity squashes that axis by `GloveDepthRatio` (0.75). In the raw Blender file the gloves therefore look deeper than in game.
- Head, upper-arm and forearm weighted bounds stay within the previous head centre (< 2 cm shift) and limb radii (within 20%) used for hit volumes.

## Rig and limitations

20-bone FK skeleton (forward kinematics): root, pelvis, spine, chest, neck, head, two clavicles and upper-arm/forearm/hand chains, two thigh/shin/foot chains. Blender Z up, character faces -Y, metres. The body stands about 1.78m tall (1.81m to the top of the headgear). FBX exports Y up / -Z forward.

All vertices have normalized bone weights (at most two influences). The torso and each arm/leg are smooth lofts with blended elbow/knee rings and mild muscle relief. Each glove body, the shorts and each boot are voxel-remeshed unions decimated to a triangle budget; headgear, cuffs, straps, piping, wraps and laces are separate shells. Everything is joined into one skinned object, not one watertight anatomical surface. The asset is intended for rendering, not 3D printing.

`Guard_Pose` is a static 30-frame guard sample. `Hit_Recoil` is a short 30fps example: upper body and head recoil around frame 7, then return by frame 24. Neither is a complete combat animation set. No inverse-kinematics controls, finger bones, face rig, walking, root motion or Humanoid Avatar mapping are supplied. Large limb rotations may require weight/garment refinement; the provided guard and recoil are the reviewed poses.

A rig is not essential for a whole-object tilt on impact. It is useful for independently bending the torso, neck and arms without moving the feet; this example demonstrates that. Unity's `BoxingOpponentAvatar` drives these bones from the existing logical attack/guard targets and adds separate head/body hit reactions. The imported Animator is disabled so the example clips do not compete with runtime inverse kinematics. Hit volumes remain unchanged. If the logical strike exceeds anatomical arm length, the visual chain stretches to maintain contact alignment.

Runtime motion uses body scale calibrated at recenter, staggered planted feet with knee flexion, coordinated torso turn and forward weight shift, straight and curved hook trajectories, opposite-hand guard, and forearm-aligned wrist orientation. Opponent head centre matches the HMD reference height captured at recenter; normal ducking does not resize the model. Head and body guards alternate. Glove world size is kept independent of body scale and matches the extracted player glove meshes. The Blender sample actions remain authoring examples; gameplay motion is procedural and belongs to the Unity source. Reference: [IBA Coaches Manual](https://www.iba.sport/documents/iba-coach-manual/), stance and basic punches. No animation data from that source is copied.

## Rebuild / verify

Run in this directory:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python ./build_boxer.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python ./verify_boxer.py
```

The build replaces only this model's named outputs and uses factory startup; it does not touch an existing interactive Blender scene or Unity scene. The before/after image reads the previous hero render from git (`BEFORE_COMMIT`, via Git LFS). Rebuilding does not preserve manual edits to `boxer-codex.blend`: edit the generator or save a distinct artist-authored version before rebuilding.

Original project-authored asset under this repository's license. No third-party asset license is inherited. No claim is made that the result is a complete production animation rig.
