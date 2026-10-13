# Makes Mazesta-Art/courtyard-v22-ours.blend from the owner's courtyard-v22-fuller-seating.blend: everything the owner changed in v22 (the house,
# the windows' coloured panes, the furniture) is kept, but the new tree and the preview camera (V13_ASSET_Zorah_Broadleaf) is not taken: every instance of it is
# pointed back at our own V10_ASSET_Broadleaf, appended from courtyard-v12.blend. The original v22 file is not written to.
#   blender --background ../Mazesta-Art/courtyard-v22-fuller-seating.blend --python tools/scene/prepare_courtyard_v22.py
import bpy, os
ART = os.path.dirname(bpy.data.filepath)
OURS = os.path.join(ART, "courtyard-v12.blend")
OUT = os.path.join(ART, "courtyard-v22-ours.blend")
NEW_TREE, OUR_TREE = "V13_ASSET_Zorah_Broadleaf", "V10_ASSET_Broadleaf"
with bpy.data.libraries.load(OURS, link=False) as (src, dst): dst.collections = [OUR_TREE]
ours = bpy.data.collections[OUR_TREE]
n = 0
for o in bpy.data.objects:
    if o.instance_type == 'COLLECTION' and o.instance_collection and o.instance_collection.name == NEW_TREE: o.instance_collection = ours; n += 1
for sc in bpy.data.scenes: sc.camera = bpy.data.objects["App_Camera"]   # the walk starts at the app's own camera, not the owner's preview one
print("PREPARE v22: trees pointed back to ours:", n)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
