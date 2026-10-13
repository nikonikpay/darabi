# Makes Mazesta-Art/courtyard-v24-ours.blend from courtyard-v22-ours.blend, with the owner's corrections of the hall (his own edits were made in a
# .blend that is not on this disk, so they are redone here from his pictures):
#   - the three wall plates over the tea counter stand out of the wall instead of sinking into it
#   - the two round poufs on the right of the hall are gone
#   - the pomegranate dish no longer sinks into the books on the coffee table; the cushions no longer sink into the sofas
#   - the books (V9_Book_0..4: plain coloured boxes) take their colour and look from one atlas of ten old bindings (tools/scene/make_v24_textures.py)
#   - a small hidden plane carries the butterfly atlas, so the exporter writes its texture (the wings are drawn by the program: GardenGpu.Wing)
#   blender --background ../Mazesta-Art/courtyard-v22-ours.blend --python tools/scene/prepare_courtyard_v24.py
import bpy, bmesh, os, mathutils
ART = os.path.dirname(bpy.data.filepath); OUT = os.path.join(ART, "courtyard-v24-ours.blend")
V = mathutils.Vector
def family(o):
    """The object, its children and the pieces named after it whose origin is the world's (a seam is a curve of its own)."""
    names = {o.name} | {c.name for c in o.children_recursive}
    names |= {x.name for x in bpy.data.objects if x.name.startswith(o.name + "_") or x.name.startswith(o.name + ".")}
    return [bpy.data.objects[n] for n in names]
def roots(objs): return [x for x in objs if x.parent is None or x.parent not in objs]
def move(objs, d):
    for x in roots(objs): x.location += V(d)
def bbox(o):
    pts = [o.matrix_world @ V(c) for c in o.bound_box]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]

# 1. plates: the wall's face is at x = -9.932, the plates' backs at -9.95
for o in bpy.data.objects:
    if o.name.startswith("V13_Wall_Ceramics__"): o.location.x += 0.04

# 2. the two round poufs on the right of the hall
for base in ("V18_Moroccan_Persian_Leather_Pouf", "V18_Round_Teal_Pouf"):
    for x in [x for x in bpy.data.objects if x.name.startswith(base)]: bpy.data.objects.remove(x, do_unlink=True)

# 3. the fruit dish on the right coffee table goes to the back of the table, the three books stay in front
dish = [bpy.data.objects[n] for n in ("V15_Small_Brass_Dish", "V16_Mina_Fruit_Bowl", "V16_Bowl_Brass_Rim")] + [x for x in bpy.data.objects if x.name.startswith(("V16_Pomegranate"))]
move(dish, (0.0, 0.14, 0.0))

# 4. cushions: rest on the seat (its top is z 1.59) and out of the back (its face is at y 4.37 from z 1.9 up)
SEAT, BACK = 1.59, 4.37
for name in ("V18_Small_Kilim_Cushion", "V18_Indigo_Bolster", "V18_Large_Madder_Termeh", "V18_Round_Ochre_Cushion",
             "V18_Long_Jajim_Lumbar", "V18_Round_Teal_Tufted_Cushion", "V18_Tall_Embroidered_Cushion"):
    o = bpy.data.objects[name]; lo, hi = bbox(o); fam = family(o)
    lift = min(max(0.0, SEAT - lo[2]), 0.25); back = max(0.0, hi[1] - 4.5)
    move(fam, (0.0, -back, lift)); print("CUSHION", name, "lift", round(lift, 3), "out", round(back, 3))
# 5. books: one material (an atlas of ten old bindings) for the boxes of V9_Book_0..4, each box's faces laid on its binding's cell (see make_v24_textures.py)
import random
TEX_DIR = os.path.join(ART, "assets", "v24")
def textured(name, png, alpha=False, rough=0.65):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; p = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    img = bpy.data.images.load(os.path.join(TEX_DIR, png)); img.colorspace_settings.name = 'sRGB'
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Linear'
    nt.links.new(t.outputs['Color'], p.inputs['Base Color'])
    if alpha: nt.links.new(t.outputs['Alpha'], p.inputs['Alpha'])
    p.inputs['Roughness'].default_value = rough
    return m
books_mat = textured("V24_Old_Books", "old-books.png")
objs = [bpy.data.objects[f"V9_Interior_Decor_V9_Book_{n}"] for n in range(5)]
boxes = []   # (object, bmesh, faces of one box) of every book, in the order they stand on a shelf (row, then x), so neighbours get different bindings
bms = {}
for o in objs:
    bm = bmesh.new(); bm.from_mesh(o.data); bms[o] = bm; seen = set()
    for f in bm.faces:
        if f.index in seen: continue
        stack, cur = [f], []; seen.add(f.index)
        while stack:
            x = stack.pop(); cur.append(x)
            for e in x.edges:
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        vs = [v.co for x in cur for v in x.verts]; lo = [min(v[i] for v in vs) for i in range(3)]; hi = [max(v[i] for v in vs) for i in range(3)]
        boxes.append((o, cur, lo, hi))
boxes.sort(key=lambda b: (round(b[2][2] * 2), round(b[2][1] * 2), b[2][0]))
order = []; rnd = random.Random(24)
while len(order) < len(boxes):
    block = list(range(10)); rnd.shuffle(block)
    while order and block[0] == order[-1]: rnd.shuffle(block)
    order += block
for (o, faces, lo, hi), kind in zip(boxes, order):
    size = [hi[i] - lo[i] for i in range(3)]; h = max(range(3), key=lambda i: size[i]); t = min(range(3), key=lambda i: size[i]); d = 3 - h - t
    cx, cy = (kind % 5) * 204, (kind // 5) * 512
    def UV(rect, u, v):   # rect: pixel x0, y0 (top), width, height inside the 1024 atlas; u, v in the box 0..1 (v up)
        x0, y0, w, hh = rect; return ((x0 + 1 + u * (w - 2)) / 1024, 1 - (y0 + 1 + (1 - v) * (hh - 2)) / 1024)
    SPINE, COVER, PAGES = (cx, cy, 136, 512), (cx + 140, cy, 64, 256), (cx + 140, cy + 256, 64, 256)
    layer = bms[o].loops.layers.uv.verify()
    for f in faces:
        a = max(range(3), key=lambda i: abs(f.normal[i])); sgn = f.normal[a] > 0
        for l in f.loops:
            c = l.vert.co; n = lambda i: (c[i] - lo[i]) / max(size[i], 1e-6)
            if a == d and not sgn: l[layer].uv = UV(SPINE, n(t), n(h))
            elif a == t: l[layer].uv = UV(COVER, n(d), n(h))
            elif a == h: l[layer].uv = UV(PAGES, n(t), n(d))
            else: l[layer].uv = UV(PAGES, n(t), n(h))
for o, bm in bms.items():
    bm.to_mesh(o.data); bm.free(); o.data.materials.clear(); o.data.materials.append(books_mat)
    for pgn in o.data.polygons: pgn.material_index = 0
print("BOOKS", len(boxes), "boxes, ten bindings")

# 6. butterflies: the wings are drawn by the program (GardenGpu.Wing), but the exporter only writes a texture some mesh uses: a hidden plane under the hall holds it
wings = textured("V24_Butterfly_Wings", "butterflies.png", alpha=True, rough=0.7)
me = bpy.data.meshes.new("V24_Butterfly_Atlas"); me.from_pydata([(-.05, -.05, 0), (.05, -.05, 0), (.05, .05, 0), (-.05, .05, 0)], [], [(0, 1, 2, 3)])
me.uv_layers.new(name="UVMap"); me.materials.append(wings)
plane = bpy.data.objects.new("V24_Butterfly_Atlas", me); plane.location = (0, 0, -6); bpy.data.collections["V9_Interior_Decor"].objects.link(plane)

if os.environ.get("MZ_SAVE", "1") == "1": bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
