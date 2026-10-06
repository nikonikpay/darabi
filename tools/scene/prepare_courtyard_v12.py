# Builds Mazesta-Art/courtyard-v12.blend from the owner's DFM_Courtyard_V12.blend (left untouched): the whole of that scene - the hall, its
# plasterwork, chandeliers, paintings and furnished rooms, the walls and the gate, the fountain, every plant and pot - as the two scenes
# the visual GPU tests draw:
#   Garden_Raster  the Direct3D test: the .blend's own low sun, and the hall's lamps (its rooms would else be lit by nothing but the door)
#   Garden_RT      the ray-traced test: nightfall, with a light rig made here (moon, the lit hall, lanterns, pool and fountain lights) and a mirror sphere
# The hall's lamps hang where the .blend's two chandeliers do (it leaves them unlit: Cycles lights the hall through the door), with a
# third between them. A painting wider than the scene file's textures is cut into two panels side by side, so none of it is lost.
# Two things of the owner's scene are put right for the walk: the benches, which stood with their seats against the inner beds, stand
# back against the outer ones, and the pool's coping, whose slabs overhang the pool's wall a hand above the paving, gets a footing.
# Nothing of the earlier garden is carried over but what is the app's own: the Mazesta logo, now smaller and over the pool between the
# fountain and the stairs (the fountain stands where it used to float), and the ray tracer's mirror sphere, which the renderer sends
# gliding round the pool. The fountain's bowl is filled with water; its jet is drawn by the renderer (GardenGpu).
# Run headless, then export (tools/scene/export_garden.py):
#   blender --background <DFM_Courtyard_V12.blend> --python tools/scene/prepare_courtyard_v12.py
import bpy, bmesh, math, os, random, numpy as np
from mathutils import Vector
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART = os.environ.get("MAZESTA_ART") or os.path.abspath(os.path.join(REPO, "..", "Mazesta-Art"))
APP = os.path.join(ART, "courtyard-v8.blend")   # where the logo and the mirror sphere are kept
OUT = os.path.join(ART, "courtyard-v12.blend")
ADDED = os.path.join(ART, "assets", "v13-additions.blend")   # BlenderKit pieces fetched for this revision: the hall's mirror, and the woods of its furniture
WIDEST = 1024   # the scene file's largest texture (export_garden.py TEX)
LOGO_AT, LOGO_SCALE = (0.0, -15.3, 2.9), 0.7
SPHERE_AT = (2.5, -19.0, 1.4)   # GardenGpu.SphereDrift carries it round the fountain from here
FOUNTAIN = (0.0, -19.0)

raster = bpy.context.scene; raster.name = "Garden_Raster"
rt = bpy.data.scenes.new("Garden_RT")
for c in raster.collection.children:
    if c.name not in ("CV4_Lights", "CV4_Cameras"): rt.collection.children.link(c)

def collection(name, *scenes):
    c = bpy.data.collections.new(name)
    for s in scenes: s.collection.children.link(c)
    return c

with bpy.data.libraries.load(APP, link=False) as (src, dst):
    dst.objects = [n for n in ("MazestaLogo", "MirrorSphere") if n in src.objects]
if len(dst.objects) != 2: raise SystemExit(f"{APP} has no MazestaLogo or MirrorSphere")
logo, sphere = bpy.data.objects["MazestaLogo"], bpy.data.objects["MirrorSphere"]
collection("App_Logo", raster, rt).objects.link(logo); logo.location = LOGO_AT; logo.scale = (LOGO_SCALE,) * 3
collection("App_Props_RT", rt).objects.link(sphere); sphere.location = SPHERE_AT

# The fountain's bowl holds water: a disc just under its rim, of the pool's water.
water = bpy.data.materials["V8_Animated_Clear_Pond_Water"]
me = bpy.data.meshes.new("App_Fountain_Bowl_Water"); N, R, Z = 32, 0.8, 1.10
me.from_pydata([(FOUNTAIN[0], FOUNTAIN[1], Z)] + [(FOUNTAIN[0] + R * math.cos(2 * math.pi * k / N), FOUNTAIN[1] + R * math.sin(2 * math.pi * k / N), Z) for k in range(N)], [],
               [(0, 1 + k, 1 + (k + 1) % N) for k in range(N)])
me.materials.append(water)
bpy.data.collections["V9_Fountain"].objects.link(bpy.data.objects.new("App_Fountain_Bowl_Water", me))

# A painting wider than a texture becomes two panels, each with its own half of the picture (cut, not resampled).
for o in list(bpy.data.collections["V12_User_Paintings"].objects):
    mat = o.data.materials[0]; node = next(n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeTexImage'); img = node.image; w, h = img.size
    if w <= WIDEST: continue
    px = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(h, w, 4)
    co = [v.co.copy() for v in o.data.vertices]; uv = [l.uv.copy() for l in o.data.uv_layers.active.data]
    if [tuple(u) for u in uv] != [(0, 0), (1, 0), (1, 1), (0, 1)]: raise SystemExit(f"{o.name} is not one quad over the whole picture")
    mid = [(co[0] + co[1]) / 2, (co[3] + co[2]) / 2]; half = w // 2
    me = bpy.data.meshes.new(o.data.name + "_Panels")
    me.from_pydata([co[0], mid[0], mid[1], co[3], co[1], co[2]], [], [(0, 1, 2, 3), (1, 4, 5, 2)])
    layer = me.uv_layers.new(name="UVMap")
    for k, u in enumerate(((0, 0), (1, 0), (1, 1), (0, 1)) * 2): layer.data[k].uv = u
    for k in range(2):
        part = bpy.data.images.new(f"{img.name}_{k}", half, h, alpha=False); part.colorspace_settings.name = img.colorspace_settings.name
        part.pixels.foreach_set(np.ascontiguousarray(px[:, k * half:(k + 1) * half]).ravel()); part.pack()
        m = mat.copy(); m.name = f"{mat.name}_{k}"; next(n for n in m.node_tree.nodes if n.bl_idname == 'ShaderNodeTexImage').image = part
        me.materials.append(m); me.polygons[k].material_index = k
    o.data = me

# The benches stood 17 cm from the inner beds, seat first: nobody could have sat on one. They stand back, against the outer beds
# (whose kerb is at x 9.92), with the walk in front of them.
BENCH_X = 9.48
for o in bpy.data.collections["V11_Courtyard_Wood_Benches"].objects: o.location.x = math.copysign(BENCH_X, o.location.x)

# The pool's coping slabs (x 3.1 to 3.56, z 0.16 to 0.33) reach past the pool's wall (3.29): under them, down to the paving, was a
# slot a hand high all round the pool, open to the wall's back. A footing of the coping's own stone closes it, a little inside the
# slabs' edge so they still cast their line of shadow. (The far end runs into the upper basin's steps.)
def box(lo, hi):
    v = [(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])]
    return v, [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
verts, faces = [], []
for lo, hi in (((-3.545, -26.515, -0.02), (-3.25, -11.6, 0.17)), ((3.25, -26.515, -0.02), (3.545, -11.6, 0.17)), ((-3.25, -26.515, -0.02), (3.25, -26.25, 0.17))):
    v, f = box(lo, hi); faces += [tuple(i + len(verts) for i in q) for q in f]; verts += v
me = bpy.data.meshes.new("App_Pool_Coping_Footing"); me.from_pydata(verts, [], faces); me.materials.append(bpy.data.materials["V8_Carved_Pale_Limestone"])
bpy.data.objects["V8_Pool_Stonework_V8_Limestone"].users_collection[0].objects.link(bpy.data.objects.new("App_Pool_Coping_Footing", me))

def added(name, verts, faces, material, smooth=False):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.materials.append(bpy.data.materials[material])
    if smooth: me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    bpy.data.objects["V8_Pool_Stonework_V8_Limestone"].users_collection[0].objects.link(bpy.data.objects.new(name, me))
def boxes(*spans):
    verts, faces = [], []
    for lo, hi in spans: v, f = box(lo, hi); faces += [tuple(i + len(verts) for i in q) for q in f]; verts += v
    return verts, faces
def tube(path, radius, sides=10):
    """A tube round a line of points (its ends closed with a point each)."""
    verts, faces = [], []; pts = [Vector(p) for p in path]
    for i, p in enumerate(pts):
        along = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized(); u = along.cross(Vector((1, 0, 0))).normalized(); w = along.cross(u)
        r = radius[i] if isinstance(radius, (list, tuple)) else radius
        verts += [tuple(p + (u * math.cos(a) + w * math.sin(a)) * r) for a in (k * 2 * math.pi / sides for k in range(sides))]
    for i in range(len(pts) - 1):
        faces += [(i * sides + k, i * sides + (k + 1) % sides, (i + 1) * sides + (k + 1) % sides, (i + 1) * sides + k) for k in range(sides)]
    verts += [tuple(pts[0]), tuple(pts[-1])]; n = len(pts) * sides
    faces += [(n, (k + 1) % sides, k) for k in range(sides)] + [(n + 1, (len(pts) - 1) * sides + k, (len(pts) - 1) * sides + (k + 1) % sides) for k in range(sides)]
    return verts, faces

# The upper basin, between the stairs and the pool, stood in the air: its rim (x 1.48, underside at z 0.44, the back piece's at 0.58)
# reaches past its tiled box (x 1.38, from z 0.34), and under the box there was nothing down to the slab at 0.24. A plinth of the
# rim's own stone carries it, a little inside the rim's edge.
added("App_Upper_Basin_Plinth", *boxes(((-1.45, -10.08, 0.2), (1.45, -8.66, 0.445)), ((-1.45, -8.72, 0.2), (1.45, -8.61, 0.585))), "V8_Limestone")

# And nothing fed it: its water ran over into the two basins below from nowhere. A spout stands on the middle of its back rim - a
# short stone post with a cap, a brass pipe out of its face - and a stream of the pool's own water falls from the pipe into the basin
# (0.37 m down at the 0.55 m/s it leaves with: 15 cm out).
added("App_Basin_Spout_Post", *boxes(((-0.24, -8.80, 0.76), (0.24, -8.60, 1.14)), ((-0.28, -8.83, 1.14), (0.28, -8.57, 1.20))), "V8_Limestone")
added("App_Basin_Spout_Pipe", *tube([(0, -8.79, 1.02), (0, -8.93, 1.02), (0, -8.975, 1.005)], 0.028), "V7_Antique_Brass", smooth=True)
fall = [(0, -8.975 - 0.55 * t, 1.0 - 4.905 * t * t) for t in (k * 0.2746 / 11 for k in range(12))]
added("App_Basin_Spout_Water", *tube(fall, [0.017 - 0.006 * k / 11 for k in range(12)]), "V8_Animated_Clear_Pond_Water", smooth=True)

# The orsi's brass pulls stood a hand before the middle of their panes, on nothing (the leaves' stiles are a hand to the side):
# from the garden they read as bars across the glass. They are left out.
bpy.data.objects.remove(bpy.data.objects["V7_Orsi_V7_Antique_Brass"], do_unlink=True)

# The open door's brass pulls stood before the lattice of each leaf, a hand off it, held by nothing. Each is moved onto its leaf's
# free stile (whose face toward the doorway runs from (0.818, -2.057) to (0.796, -1.986) on the plan), three and a half centimetres
# off it, on two brass stand-offs let into the wood.
brass = bpy.data.objects["V7_OpenDoor_V7_Antique_Brass"]; bm = bmesh.new(); bm.from_mesh(brass.data); moved = 0
seen = set()
for v in bm.verts:
    if v in seen: continue
    island, stack = [], [v]; seen.add(v)
    while stack:
        a = stack.pop(); island.append(a)
        for e in a.link_edges:
            b = e.other_vert(a)
            if b not in seen: seen.add(b); stack.append(b)
    xs = [(brass.matrix_world @ a.co).x for a in island]; ys = [(brass.matrix_world @ a.co).y for a in island]
    if len(island) == 16 and max(ys) - min(ys) < 0.06 and -2.3 < min(ys) < -2.2:   # a pull: a bar 29 cm tall
        side = math.copysign(1, sum(xs)); shift = brass.matrix_world.inverted().to_3x3() @ Vector((side * -0.0425, 0.2063, 0))
        for a in island: a.co += shift
        moved += 1
if moved != 2: raise SystemExit(f"the door's two pulls were not found ({moved})")
bm.to_mesh(brass.data); bm.free()
for side in (-1, 1):
    for z in (2.67, 2.86):
        added(f"App_Door_Pull_Standoff_{side}_{z}", *tube([(side * 0.7735, -2.0317, z), (side * 0.8166, -2.0185, z)], 0.009, 8), "V7_Antique_Brass", smooth=True)

# The teapot stood on the middle of the samovar's body; its crown, where a teapot is kept warm, is 3.6 cm off that (the model's
# chimney is not on its axis). It stands on the crown.
bpy.data.objects["V12_Samovar_Teapot"].location.x += 0.004; bpy.data.objects["V12_Samovar_Teapot"].location.y -= 0.0363

# The windcatchers were open to the sky: their cornice is four pieces round a hole, with the vanes' tops and the sky showing in
# it. Each gets a slab of the cornice's stone inside the ring and a low hipped cap of the towers' kahgel over it.
for x0, x1 in ((-9.53, -7.67), (7.67, 9.53)):
    y0, y1, z = -2.41, -0.55, 11.04; xm, ym = (x0 + x1) / 2, (y0 + y1) / 2
    added(f"App_Windcatcher_Roof_Slab_{x0}", *boxes(((x0, y0, 10.86), (x1, y1, z))), "V8_Carved_Pale_Limestone")
    i = 0.12; base = [(x0 + i, y0 + i, z), (x1 - i, y0 + i, z), (x1 - i, y1 - i, z), (x0 + i, y1 - i, z)]; r = 0.28
    ridge = [(xm - r, ym - r, z + 0.42), (xm + r, ym - r, z + 0.42), (xm + r, ym + r, z + 0.42), (xm - r, ym + r, z + 0.42)]
    added(f"App_Windcatcher_Roof_Cap_{x0}", base + ridge, [(k, (k + 1) % 4, 4 + (k + 1) % 4, 4 + k) for k in range(4)] + [(4, 5, 6, 7)], "V8_Kahgel")

# The broadleaf tree's model has, beside its two trunks, two stubs of a metre and a half: beside a bed's young tree, scaled down
# and simplified, each is a grey spike among the shrubs. They are left out.
tree = bpy.data.objects["V10_SOURCE_Broadleaf"]; bm = bmesh.new(); bm.from_mesh(tree.data); slot = [m.name for m in tree.data.materials].index("trunk")
seen, gone = set(), []
for f in bm.faces:
    if f.material_index != slot or f in seen: continue
    island, stack = [], [f]; seen.add(f)
    while stack:
        a = stack.pop(); island.append(a)
        for e in a.edges:
            for b in e.link_faces:
                if b not in seen and b.material_index == slot: seen.add(b); stack.append(b)
    zs = [v.co.z for a in island for v in a.verts]
    if min(zs) < 0.05 and max(zs) < 2.0: gone += island
if not gone: raise SystemExit("the broadleaf's stubs were not found")
bmesh.ops.delete(bm, geom=list({v for a in gone for v in a.verts}), context='VERTS'); bm.to_mesh(tree.data); bm.free()

# ——— what this revision adds (BlenderKit: ADDED) ———
with bpy.data.libraries.load(ADDED, link=False) as (src, dst):
    dst.objects = ["V13_SOURCE_Mirror"]; dst.materials = ["App_Wood_Oak", "App_Wood_Teak", "App_Wood_Rosewood", "App_Wood_Mahogany"]
# Every piece of wood in the scene was the one walnut. The doors, the orsi and the canopy keep it; the tea corner's sideboard is
# mahogany, the bookcases oak, the tables teak, the mirror's frame rosewood.
for name, wood in (("V12_Traditional_Tea_Corner_V8_Walnut", "App_Wood_Mahogany"), ("V9_Interior_Decor_V8_Walnut", "App_Wood_Oak"), ("V9_Furniture_V8_Walnut", "App_Wood_Teak")):
    o = bpy.data.objects[name]; o.data = o.data.copy()
    for k, m in enumerate(o.data.materials):
        if m.name == "V8_Walnut": o.data.materials[k] = bpy.data.materials[wood]
# A tall mirror in a heavy wooden frame hangs on the back wall (y 6.848), facing the door, between the two paintings: with ray
# tracing it shows the room, the door and the garden beyond. Its lit strip and plastic lip become a fillet of the door's brass.
mirror = bpy.data.objects["V13_SOURCE_Mirror"]
for k, m in enumerate(mirror.data.materials):
    if m.name in ("Lamp", "Plastic"): mirror.data.materials[k] = bpy.data.materials["V7_Antique_Brass"]
    elif m.name in ("Metal", "App_Wood_Mahogany"): mirror.data.materials[k] = bpy.data.materials["App_Wood_Rosewood"]
    elif m.name == "Mirror": m.name = "App_Mirror_Glass"
mirror.name = "App_Hall_Mirror"; mirror.scale = (1.6,) * 3; mirror.rotation_euler = (0, 0, -math.pi / 2); mirror.location = (0, 6.846, 1.45 + 1.02 * 1.6)
collection("App_Hall_Mirror", raster, rt).objects.link(mirror)

# The roof between the windcatchers was bare. Seven large terracotta urns stand along it (the lantern pedestals' urn, three
# times its size and more), planted with juniper, geranium and shrubs from the garden's own beds.
planters = collection("App_Roof_Planters", raster, rt); random.seed(12)
def inst(name, what, at, scale, turn=0.0):
    o = bpy.data.objects.new(name, None); o.instance_type = 'COLLECTION'; o.instance_collection = bpy.data.collections[what]
    o.location = at; o.scale = (scale,) * 3; o.rotation_euler = (0, 0, turn); planters.objects.link(o); return o
urn = bpy.data.collections["A_urn"].objects[0]; corners = [urn.matrix_world @ Vector(c) for c in urn.bound_box]
urn_low, urn_high = min(c.z for c in corners), max(c.z for c in corners)
def planter(tag, x, y, z, size, plant):
    inst(f"App_Planter_{tag}_Urn", "A_urn", (x, y, z - urn_low * size), size, random.uniform(0, 6.28)); top = z + (urn_high - urn_low) * size
    r = 0.075 * size; soil = [(x + r * math.cos(k * math.pi / 6), y + r * math.sin(k * math.pi / 6), top - 0.05 * size) for k in range(12)]
    me = bpy.data.meshes.new(f"App_Planter_{tag}_Soil"); me.from_pydata(soil, [], [tuple(range(12))]); me.materials.append(bpy.data.materials["V8_Granular_Garden_Loam"])
    planters.objects.link(bpy.data.objects.new(me.name, me))
    if plant == "juniper": inst(f"App_Planter_{tag}_Juniper", "V10_ASSET_Juniper", (x, y, top - 0.08 * size), 0.7 * size, random.uniform(0, 6.28))
    elif plant == "shrub": inst(f"App_Planter_{tag}_Shrub", "V9_ASSET_Shrub", (x, y, top - 0.1 * size), 0.42 * size, random.uniform(0, 6.28))
    else:
        for k in range(6): inst(f"App_Planter_{tag}_Sprig_{k}", f"V11_ASSET_Geranium{plant}", (x, y, top - 0.07 * size), random.uniform(0.42, 0.52) * size, k * math.pi / 3 + random.uniform(-0.3, 0.3))
        for k in range(14):
            a, d = random.uniform(0, 6.28), random.uniform(0.02, 0.13) * size
            inst(f"App_Planter_{tag}_Bloom_{k}", f"V11_ASSET_Geranium{plant}Bloom", (x + d * math.cos(a), y + d * math.sin(a), top + random.uniform(0.13, 0.21) * size), 1.1 * size, a)
for k, plant in enumerate(("juniper", "Red", "shrub", "juniper", "shrub", "Pink", "juniper")):
    planter(f"Roof_{k}", -6.0 + 2.0 * k, -1.5, 6.58, 3.4 if plant == "juniper" else 3.0, plant)

# The camera both tests start from (the walk itself is GardenCamera's): at the gate, looking up the pool.
cam = bpy.data.objects.new("App_Camera", bpy.data.cameras.new("App_Camera")); cam.location = (0, -31.3, 1.75)
cam.rotation_euler = (Vector((0, -19, 1.9)) - Vector(cam.location)).to_track_quat('-Z', 'Y').to_euler()
collection("App_Cameras", raster, rt).objects.link(cam); raster.camera = rt.camera = cam

# ——— the lights ———
lights = collection("App_Lights_RT", rt); day = collection("App_Lights_Raster", raster)
WARM, LAMP, COOL, WHITE = (1.0, 0.72, 0.42), (1.0, 0.58, 0.25), (0.45, 0.78, 1.0), (1.0, 0.88, 0.72)
def light(name, kind, at, watts, color, aim=None, cone=None, blend=0.5, radius=0.1, size=None, lights=lights):
    d = bpy.data.lights.new(name, kind); d.energy = watts; d.color = color
    if kind == 'SPOT': d.spot_size = cone; d.spot_blend = blend
    if kind == 'AREA': d.size = size
    if kind != 'AREA': d.shadow_soft_size = radius
    o = bpy.data.objects.new(name, d); o.location = at
    if aim is not None: o.rotation_euler = (Vector(aim) - Vector(at)).to_track_quat('-Z', 'Y').to_euler()
    lights.objects.link(o); return o

# the moon: low over the garden's right-hand wall, in front of the hall, so the cypresses and the windcatchers throw long shadows
# across the paving (the renderer carries it slowly along its arc: GardenFrame)
moon = light("Moon", 'SUN', (0, 0, 30), 0.9, (0.62, 0.72, 1.0), aim=(-0.62, 0.42, 30 - 0.52)); moon.data.angle = math.radians(2.0)
# the hall is lit from inside by its two chandeliers and by nothing else (the lamp just under each, so the ceiling's medallion is in its
# light and not in the fixture's shadow): a lamp that hangs from nothing reads as a mistake on the bare ceiling between them
chandeliers = sorted((o.matrix_world.translation.x, o.matrix_world.translation.y, o.matrix_world.translation.z) for o in bpy.data.collections["V12_Two_Decorative_Chandeliers"].objects)
if len(chandeliers) != 2: raise SystemExit("the hall's two chandeliers were not found")
for k, (x, y, z) in enumerate(chandeliers):
    light(f"Hall_{k}", 'POINT', (x, y, z - 0.12), 340, WARM, radius=0.18)
    light(f"Hall_Day_{k}", 'POINT', (x, y, z - 0.12), 105, WARM, lights=day)   # the rasteriser's lamps: by day the hall is lit as much by them as by its door and windows
for x in (-7.2, -2.4, 2.4, 7.2): light(f"Canopy_{x}", 'POINT', (x, -4.7, 4.55), 70, WARM, radius=0.08)
# under the water: along both sides of the pool, and in the upper basins
for x in (-2.3, 2.3):
    for y in (-14.6, -19.0, -23.4): light(f"Pool_{x}_{y}", 'POINT', (x, y, -0.42), 55, COOL)
light("Basin", 'POINT', (0, -10.9, 0.12), 30, COOL)
# the fountain and its jet, lit from the pool's four corners; the logo from the coping either side
for x, y in ((-2.7, -22.0), (2.7, -22.0), (-2.7, -16.0), (2.7, -16.0)): light(f"Fountain_{x}_{y}", 'SPOT', (x, y, 0.05), 130, WHITE, aim=(FOUNTAIN[0], FOUNTAIN[1], 1.7), cone=0.55, blend=0.7)
for x in (-3.3, 3.3): light(f"LogoSpot_{x}", 'SPOT', (x, LOGO_AT[1] - 2.2, 0.5), 900, WHITE, aim=LOGO_AT, cone=0.6, blend=0.6)
# every cypress inside the walls is lit from its foot
for o in list(raster.objects):
    t = o.matrix_world.translation
    if o.instance_collection and o.instance_collection.name == "V9_ASSET_Cypress" and abs(t.x) < 13.5 and -33 < t.y < 6.5:
        at = (t.x - math.copysign(1.0, t.x), t.y, 0.35); light(f"Up_{o.name}", 'SPOT', at, 150, WHITE, aim=(t.x, t.y, 4.0), cone=0.9, blend=0.9)
# the walls: a lamp either side of the gate, and along both side walls
for x in (-2.6, 2.6): light(f"Gate_{x}", 'POINT', (x, -32.45, 2.7), 60, LAMP)
for x in (-13.25, 13.25):
    for y in (-29.0, -21.0, -15.0, -7.5): light(f"Sconce_{x}_{y}", 'POINT', (x, y, 2.35), 40, LAMP)

bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT, relative_remap=True, compress=True)
print("SAVED", OUT, "objects", len(raster.objects), len(rt.objects), "RT lights", len(lights.objects))
