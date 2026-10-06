# Builds Mazesta-Art/courtyard-v10.blend from the owner's DFM_Courtyard_V10.blend (left untouched): the whole of that scene - the hall and
# its furnished rooms, the taller walls and the gate, the fountain, every plant - as the two scenes the visual GPU tests draw:
#   Garden_Raster  the Direct3D test: V10's own low sun, and three lamps in the hall (its rooms would else be lit by nothing but the door)
#   Garden_RT      the ray-traced test: nightfall, with a light rig made here (moon, the lit hall, lanterns, pool and fountain lights) and a mirror sphere
# Nothing of the earlier garden is carried over but what is the app's own: the Mazesta logo, now smaller and over the pool between the
# fountain and the stairs (the fountain stands where it used to float), and the ray tracer's mirror sphere, which the renderer sends
# gliding round the pool. The fountain's bowl is filled with water; its jet is drawn by the renderer (GardenGpu).
# Run headless, then export (tools/scene/export_garden.py):
#   blender --background <DFM_Courtyard_V10.blend> --python tools/scene/prepare_courtyard_v10.py
import bpy, math, os
from mathutils import Vector
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART = os.environ.get("MAZESTA_ART") or os.path.abspath(os.path.join(REPO, "..", "Mazesta-Art"))
APP = os.path.join(ART, "courtyard-v8.blend")   # where the logo and the mirror sphere are kept
OUT = os.path.join(ART, "courtyard-v10.blend")
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
# the hall is lit from inside: three hanging lamps, whose light falls out through the open door and the orsi onto the terrace
for k, (x, y) in enumerate(((-5.4, 2.4), (0.0, 4.6), (5.4, 2.4))):
    light(f"Hall_{k}", 'POINT', (x, y, 4.7), 260, WARM, radius=0.18)
    light(f"Hall_Day_{k}", 'POINT', (x, y, 4.7), 80, WARM, lights=day)   # the rasteriser's lamps: by day the hall is lit as much by them as by its door and windows
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
