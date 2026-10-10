# Animated frog (Blender source)

`frog_schlegel_hop.blend` is the source of `src/Assets/Models/FrogAnimated/frog_schlegel_hop.fbx`.

- Model and animation: "CC0 Schlegel's Green Tree Frog" (animated version) by ffish.asia / floraZia.com, CC0
  https://sketchfab.com/3d-models/cc0-schlegels-green-tree-frog-218caec043db49d4b41855ab7e92765d
- The original has one long animation (idle, leg stretch, two leaps). `rebind_and_export.py` cuts it down:
  - `Hop`: one leap (original frames 163-196) followed by the recovery of the second leap (214-223), 44 frames at 24 fps
  - `Idle`: the sitting pose
  - the sitting pose is baked into the mesh and applied as the rest (bind) pose, so that the Deform deformers
    (belly, throat, touch) line up with the visible body
- Run (Blender 5.x, background mode):
  `blender -b --python rebind_and_export.py -- <scene.gltf of the original download> <output folder>`
- In Unity: `Tools > Poke Task > Swap Frog To Animated Model` puts the frog into FrogRoom
  (see `src/Assets/Editor/FrogModelSwap.cs`); `Tools > Poke Task > Run Smoke Test (Play)` checks it automatically.
