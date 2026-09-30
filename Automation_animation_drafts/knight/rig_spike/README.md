# Knight rig spike (archived 2026-09-30)

A spike that tried to replace the video idle and attack with code-driven animation of a
jointed knight, so that armour, sword and shield could be swapped. The user judged every
result not good enough, and the project went back to video-generated animation.

Four things were tried, all on branch `spike/knight-rig`:
- **Cut-out parts rig** (`RigSpike.unity`, Rig*.cs): code keyframes. Parts were cut from
  one image, then AI parts sheets were used, then each part was generated alone.
- **2D bones** (`KnightShowcases.unity` A, Skin*.cs): Unity sprite skinning of the
  original art, with limb-solver IK on the legs.
- **3D in 2D** (`KnightShowcases.unity` B): TRELLIS mesh → Uthana auto-rig →
  Uthana text-to-motion, Humanoid, glTFast.
- **Video → rig**: RTMPose tracked the user's phone clip and the motion was retargeted
  (`tools/retarget.py`, `tools/pose_track2.py`).

`tools/` holds the Python previews: fk.py, ik.py and smooth.py (monotone-cubic keys),
plus the assemble*, attack* and skin* scripts. `data/` holds the inputs and generated
assets. `renders/` holds what the user saw.
