# Builds Mazesta-Art/courtyard-v8.blend: courtyard-v6.blend (its plants, logo, lanterns, both scenes and their light rigs) with the building
# and stonework of the owner's DFM_Courtyard_V8.blend (left untouched): the detailed columns, canopy, carved windcatchers, roof railing,
# the orsi and the open door, the pool's stonework and the beds' coping, in its limestone, kahgel, walnut and turquoise tile.
# V8 has no furniture: the lanterns come down from V6's pedestals onto the pool's stonework, their lights with them. V8's backdrop (the Alborz
# panorama of its world) comes along for the exporter; the scenes keep their own worlds.
# Run headless, then export (tools/scene/export_garden.py):
#   blender --background ../Mazesta-Art/courtyard-v6.blend --python tools/scene/prepare_courtyard_v8.py
import bpy, math, os
from mathutils import Vector
ART = os.path.dirname(os.path.abspath(bpy.data.filepath))
V8 = os.environ.get("MAZESTA_V8") or r"D:\DFM APp\3d-Project-Scene\SourceAssets\CourtyardV6\RevisionV8\DFM_Courtyard_V8.blend"
OUT = os.path.join(ART, "courtyard-v8.blend")
# the collections whose meshes are the old building (instancers of lanterns and urns in them stay)
OLD = ["PV2_01_Shell", "PV2_02_Columns", "PV2_03_Orsi", "PV2_04_Doors", "PV2_05_Tilework", "PV2_06_Canopy", "PV2_07_Roof", "PV2_08_Parapet", "PV2_09_Badgir", "PV2_10_Stairs",
       "CV4_05_Tilework", "CV4_Beds", "CV4_Furniture", "CV4_Ground", "CV4_Pool", "CV4_Walls", "CV5_PoolDetails", "CV5_Soil", "CV5_Terrain", "CV6_Orsi", "CV6_SolidElevations"]
# V8's lights, cameras and props (CV4_Lights, CV4_Cameras, CV5_Props and the assets they place) are not taken: V6's are kept
TAKE = ["PV2_10_Stairs", "CV4_Ground", "CV4_Pool", "CV4_Beds", "CV4_Walls", "CV4_05_Tilework", "CV5_Soil", "CV5_PoolDetails", "CV5_Terrain", "V7_Architecture", "V7_Orsi", "V7_OpenDoor", "V7_Roof",
        "V8_Detailed_Columns", "V8_Detailed_Canopy", "V8_Carved_Windcatchers", "V8_Facade_Details", "V8_Wooden_Roof_Railing", "V8_Pool_Stonework", "V8_Garden_Coping"]
raster, rt = bpy.data.scenes["Garden_Raster"], bpy.data.scenes["Garden_RT"]

for name in OLD:
    c = bpy.data.collections.get(name)
    if c is None: continue
    for o in [o for o in c.objects if o.type == 'MESH']: bpy.data.objects.remove(o)

# V8's collections come in whole (renamed .001 where the name is taken); their meshes move into the collection of that name, a new one is linked into both scenes
with bpy.data.libraries.load(V8, link=False) as (src, dst):
    missing = [n for n in TAKE if n not in src.collections]
    if missing: raise SystemExit(f"{V8} has no collection {missing}")
    dst.collections = list(TAKE); dst.worlds = [n for n in src.worlds if n == "V8_Alborz_Sunset_HDR"]
for w in dst.worlds:   # kept for its backdrop image and how it is laid round the scene; the 96 MB light probe is left behind
    w.use_fake_user = True
    for n in w.node_tree.nodes:
        if n.bl_idname == 'ShaderNodeTexEnvironment' and n.image and n.image.name.lower().endswith(".exr"): img = n.image; n.image = None; bpy.data.images.remove(img)
for c in dst.collections:
    base = c.name.rsplit(".", 1)[0] if c.name.rsplit(".", 1)[0] in TAKE else c.name
    target = bpy.data.collections.get(base) if base != c.name else None
    if target is None:
        c.name = base; raster.collection.children.link(c); rt.collection.children.link(c); continue
    for o in list(c.objects):
        if o.type == 'MESH': target.objects.link(o)
        c.objects.unlink(o)
    bpy.data.collections.remove(c)
for o in [o for o in bpy.data.objects if not o.users_collection]: bpy.data.objects.remove(o)
for name in OLD:
    c = bpy.data.collections.get(name)
    if c is not None and not c.all_objects: bpy.data.collections.remove(c)

# The open door's leaves carry the orsi's stained glass. The orsi's panes are drawn as windows onto a room (the exporter's WINDOW_GLASS);
# the leaves stand open at an angle with the hall behind them, so theirs are plain stained glass under a name of their own.
for o in bpy.data.collections["V7_OpenDoor"].objects:
    for slot in o.material_slots:
        m = slot.material
        if m and m.name.startswith("V7_Stained_"):
            name = "V7_DoorGlass_" + m.name[len("V7_Stained_"):].split(".")[0]
            slot.material = bpy.data.materials.get(name) or m.copy(); slot.material.name = name

# The lanterns stood on V6's pedestals beside the pool (in V8's own file they hang in the air where those were): each comes down onto the
# stone under it, or, where that is the bare paving, onto the pool's edge a step inward; the lamps of both scenes inside it go with it.
EDGE = 0.32
with bpy.context.temp_override(scene=raster, view_layer=raster.view_layers[0]):
    dg = bpy.context.evaluated_depsgraph_get()
    moves = []
    for o in [o for o in bpy.data.collections["CV4_Furniture"].objects if o.instance_collection and o.instance_collection.name == "A_lantern"]:
        p = o.matrix_world.translation.copy()
        def stone(x):
            hit, at, *_ = raster.ray_cast(dg, Vector((x, p.y, p.z - 0.01)), Vector((0, 0, -1)))
            return at.z if hit else None
        here, inward = stone(p.x), stone(p.x - math.copysign(EDGE, p.x))
        x, z = (p.x - math.copysign(EDGE, p.x), inward) if here is not None and inward is not None and here < 0.05 < inward else (p.x, here)
        if z is not None and p.z - z > 0.02: moves.append((o, p, Vector((x - p.x, 0, z - p.z))))
for o, p, by in moves:
    o.location += by
    for l in [l for l in bpy.data.objects if l.type == 'LIGHT' and (Vector(l.location).xy - p.xy).length < 0.3 and 0 <= l.location.z - p.z < 0.6]: l.location += by
    print("LANTERN", o.name, "moved", tuple(round(c, 2) for c in by))

bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)   # V6's stone and walnut images and the old meshes
bpy.ops.wm.save_as_mainfile(filepath=OUT, relative_remap=True, compress=True)
print("SAVED", OUT, "objects", len(raster.objects), len(rt.objects))
