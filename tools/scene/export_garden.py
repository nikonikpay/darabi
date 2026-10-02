# Exports the courtyard scene (Mazesta-Art/courtyard-v6.blend: V4's plants and light rigs around the V6 building, made by Mazesta-Art/scripts/prepare_courtyard_v6.py)
# to the file the visual GPU tests draw: src/Mazesta.Diagnostics.Gpu/Scene/garden.mzscene. Best run in a Blender of its own, so an open window is left alone:
#   blender --background ../Mazesta-Art/courtyard-v6.blend --python tools/scene/export_garden.py
# (it also runs from Blender's Text Editor with the .blend open).
#
# Both of the file's scenes are read: Garden_Raster (the Direct3D test: golden-hour sun) and Garden_RT (the ray-traced test: blue hour,
# its own light rig and a few extra objects). What appears in both is written once, marked for both.
#
# The format is documented in GardenScene.cs, which reads it. In short, gzip over little-endian records:
#   meshes    - 16-byte vertices (position snorm16x4 within the mesh's bounds, normal snorm8x4, uv float16x2), 32-bit indices, one submesh per material
#   instances - a mesh, a 3x4 world matrix, which scene(s) it is in
#   materials - flat, alpha-tested (texture), brick pattern (procedural, as in Blender), water, glass, emissive
#   textures  - 256x256 BC3 (sRGB) with mips down to 4x4, base colour with the opacity in alpha
#   lights    - sun, point, spot (area lights become wide spots), watts as Blender has them
# Coordinates are turned from Blender's (x right, y forward, z up) to Direct3D's (x right, y up, z forward).
import bpy, bmesh, numpy as np, struct, gzip, math, os, io, re

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", "Mazesta"))
OUT = os.environ.get("MAZESTA_SCENE_OUT") or os.path.join(REPO, "src", "Mazesta.Diagnostics.Gpu", "Scene", "garden.mzscene")
SCENES = (("Garden_Raster", 1), ("Garden_RT", 2))
TEX = 256
DECIMATE_OVER = 6000            # hard-surface meshes above this many triangles are simplified (lantern glass, pots, trunks)
PER_MESH_BUDGET = 400_000       # a mesh placed thousands of times (ivy leaves, blossoms) is simplified until all its copies together stay under this
KEEP_DETAIL = ("CypressFoliage",)
MAGIC, VERSION = b"MZSC", 1
K_FLAT, K_CUTOUT, K_BRICK, K_WATER, K_GLASS, K_EMISSIVE = 0, 1, 2, 3, 4, 5

def swap(v): return (v[0], v[2], v[1])   # Blender -> Direct3D axes

# ——— materials and textures ———

def upstream_image(sock, depth=0):
    """The first image texture feeding a socket, and which of its outputs (Color or Alpha)."""
    if depth > 12 or not sock.is_linked: return None
    for link in sock.links:
        n = link.from_node
        if n.bl_idname == 'ShaderNodeTexImage' and n.image: return (n.image, link.from_socket.name)
        for s in n.inputs:
            r = upstream_image(s, depth + 1)
            if r: return r
    return None

def principled(mat):
    if not mat or not mat.node_tree: return None
    out = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeOutputMaterial' and n.is_active_output), None)
    def find(node, depth=0):
        if node is None or depth > 6: return None
        if node.bl_idname == 'ShaderNodeBsdfPrincipled': return node
        for s in node.inputs:
            for l in s.links:
                r = find(l.from_node, depth + 1)
                if r: return r
        return None
    return find(out.inputs['Surface'].links[0].from_node) if out and out.inputs['Surface'].is_linked else None

def srgb_to_lin(c): return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)

textures, texture_index = [], {}
def texture_for(color_img, alpha_src):
    key = (color_img.name if color_img else None, alpha_src[0].name if alpha_src else None, alpha_src[1] if alpha_src else None, alpha_src[2] if alpha_src else None)
    if key in texture_index: return texture_index[key]
    rgb = image_pixels(color_img)[..., :3] if color_img else np.ones((TEX, TEX, 3), np.float32)
    if alpha_src:
        ap = image_pixels(alpha_src[0])
        a = ap[..., 3] if alpha_src[1] == 'Alpha' else ap[..., :3].mean(axis=2)
        if alpha_src[2]: a = 1 - a
        rgb = bleed(rgb, a > 0.5)
    else: a = np.ones((TEX, TEX), np.float32)
    texture_index[key] = len(textures); textures.append((np.dstack([rgb, a]), alpha_src is not None))
    return texture_index[key]

def bleed(rgb, solid):
    """The cut-away texels take the colour of the nearest kept ones, so filtering and smaller mips draw no background (white) rim round a leaf."""
    if not solid.any() or solid.all(): return rgb
    rgb = rgb.copy(); known = solid.copy()
    for _ in range(TEX):
        if known.all(): break
        acc = np.zeros_like(rgb); n = np.zeros(known.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            k = np.roll(known, (dy, dx), (0, 1)); acc += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]; n += k
        grow = ~known & (n > 0)
        rgb[grow] = acc[grow] / n[grow][:, None]; known |= grow
    return rgb

def mix_alpha(mat):
    """A cut-out made by mixing with a Transparent BSDF (the factor from a mask image): the image, its output, and whether it is inverted."""
    out = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeOutputMaterial' and n.is_active_output), None)
    if not out or not out.inputs['Surface'].is_linked: return None
    mix = out.inputs['Surface'].links[0].from_node
    if mix.bl_idname != 'ShaderNodeMixShader': return None
    for slot, invert in ((1, False), (2, True)):   # transparent at factor 0 means the factor is the opacity
        if any(l.from_node.bl_idname == 'ShaderNodeBsdfTransparent' for l in mix.inputs[slot].links):
            r = upstream_image(mix.inputs[0])
            return (r[0], r[1], invert) if r else None
    return None

def tint(sock):
    """A constant colour the base image is multiplied or mixed with on its way to the Base Color (V6's stone and walnut), as one multiplier."""
    if not sock.is_linked: return (1.0, 1.0, 1.0)
    n = sock.links[0].from_node
    if n.bl_idname not in ('ShaderNodeMixRGB', 'ShaderNodeMix'): return (1.0, 1.0, 1.0)
    ins = [s for s in n.inputs if s.type == 'RGBA' and s.enabled]
    fac = next((s for s in n.inputs if s.name in ('Fac', 'Factor') and s.enabled), None)
    const = next((s for s in ins if not s.is_linked), None)
    if fac is None or fac.is_linked or const is None: return (1.0, 1.0, 1.0)
    f, c = float(fac.default_value if not hasattr(fac.default_value, '__len__') else fac.default_value[0]), tuple(const.default_value[:3])
    if n.blend_type == 'MULTIPLY': return tuple((1 - f) + f * x for x in c)
    if n.blend_type == 'MIX': return tuple((1 - f) + f * x / 0.5 for x in c)   # towards the constant, as if the image averaged mid-grey
    return (1.0, 1.0, 1.0)

def image_pixels(img):
    """The image at TEX x TEX, RGBA floats as stored (sRGB colour images stay sRGB-encoded), rows bottom to top as Blender keeps them."""
    c = img.copy()
    try:
        if tuple(c.size) != (TEX, TEX): c.scale(TEX, TEX)
        px = np.empty(TEX * TEX * 4, np.float32); c.pixels.foreach_get(px)
        return px.reshape(TEX, TEX, 4)
    finally: bpy.data.images.remove(c)

materials, material_index = [], {}
def material_for(mat):
    name = mat.name if mat else "(none)"
    if name in material_index: return material_index[name]
    rec = dict(kind=K_FLAT, tex=-1, base=(0.6, 0.6, 0.6), alpha=1.0, color2=(0, 0, 0), rough=0.6, mortar=(0, 0, 0), metal=0.0, emit=(0, 0, 0), trans=0.0, pattern=(0, 0, 0, 0))
    p = principled(mat)
    if p:
        bc = p.inputs['Base Color']
        rec['base'] = tuple(bc.default_value[:3]); rec['rough'] = float(p.inputs['Roughness'].default_value); rec['metal'] = float(p.inputs['Metallic'].default_value)
        if p.inputs['Roughness'].is_linked: rec['rough'] = 0.6
        rec['trans'] = float(p.inputs['Transmission Weight'].default_value)
        es = float(p.inputs['Emission Strength'].default_value); ec = p.inputs['Emission Color'].default_value
        rec['emit'] = (ec[0] * es, ec[1] * es, ec[2] * es)
        rec['alpha'] = float(p.inputs['Alpha'].default_value)
        brick = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeTexBrick'), None)
        img = upstream_image(bc); alpha = upstream_image(p.inputs['Alpha'])
        alpha = (alpha[0], alpha[1], False) if alpha else mix_alpha(mat)
        if brick is not None:
            mp = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeMapping'), None)
            s = mp.inputs['Scale'].default_value[0] if mp else 1.0
            rec.update(kind=K_BRICK, base=tuple(brick.inputs['Color1'].default_value[:3]), color2=tuple(brick.inputs['Color2'].default_value[:3]), mortar=tuple(brick.inputs['Mortar'].default_value[:3]),
                       pattern=(brick.inputs['Scale'].default_value * s, brick.inputs['Mortar Size'].default_value, brick.inputs['Brick Width'].default_value, brick.inputs['Row Height'].default_value))
        elif img or alpha:
            rec['tex'] = texture_for(img[0] if img else None, alpha)
            if img: rec['base'] = tint(bc)
            if alpha: rec['kind'] = K_CUTOUT
        base = re.sub(r'\.\d{3}$', '', name)   # an appended copy of a material is named "....001"
        if base.endswith('Water'): rec['kind'] = K_WATER
        elif rec['trans'] > 0.5 or base == 'Spray': rec['kind'] = K_GLASS
        elif max(rec['emit']) > 0.5 and rec['kind'] == K_FLAT: rec['kind'] = K_EMISSIVE
        if base == 'Spray': rec['alpha'] = 0.5
    material_index[name] = len(materials); materials.append(rec)
    return material_index[name]

# ——— meshes ———

# While the scenes' instances are walked, each new mesh is only copied (a real mesh datablock, safe to keep): simplifying one means
# evaluating a modifier, which re-evaluates the scene and would pull the instances out from under the walk. They are packed afterwards.
meshes, mesh_index, pending = [], {}, []
def mesh_for(key, obj_eval, dg):
    if key in mesh_index: return mesh_index[key]
    me = bpy.data.meshes.new_from_object(obj_eval, preserve_all_data_layers=True, depsgraph=dg)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris == 0: bpy.data.meshes.remove(me); mesh_index[key] = -1; return -1
    slots = [s.material for s in obj_eval.material_slots] or [None]
    mesh_index[key] = len(pending); pending.append((key, me, slots, tris, obj_eval.original.name))
    return mesh_index[key]

def pack_pending():
    for key, me, slots, tris, name in pending:
        cutout = any(materials[material_for(m)]['kind'] == K_CUTOUT for m in slots)
        target = tris
        if tris > DECIMATE_OVER and not cutout and not name.startswith(KEEP_DETAIL): target = DECIMATE_OVER
        copies = counts.get(key, 1)
        if tris * copies > PER_MESH_BUDGET: target = min(target, max(48, PER_MESH_BUDGET // copies))
        data = decimated(me, slots, target / tris) if target < tris * 0.9 else pack(me, slots)
        meshes.append(data)
        bpy.data.meshes.remove(me)

def decimated(me, slots, ratio):
    tmp_me = me.copy(); tmp = bpy.data.objects.new("_dec", tmp_me); bpy.context.scene.collection.objects.link(tmp)
    try:
        m = tmp.modifiers.new("d", 'DECIMATE'); m.ratio = max(0.02, ratio); m.use_collapse_triangulate = True
        dg = bpy.context.evaluated_depsgraph_get(); e = tmp.evaluated_get(dg); me2 = e.to_mesh()
        try: return pack(me2, slots)
        finally: e.to_mesh_clear()
    finally:
        bpy.data.objects.remove(tmp); bpy.data.meshes.remove(tmp_me)

def pack(me, slots):
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    if n == 0: return None
    loops = np.empty(n * 3, np.int32); me.loop_triangles.foreach_get("loops", loops)
    mat = np.empty(n, np.int32); me.loop_triangles.foreach_get("material_index", mat)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get("vertex_index", lv)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    cn = np.empty(len(me.loops) * 3, np.float32); me.corner_normals.foreach_get("vector", cn) if hasattr(me, "corner_normals") else me.loops.foreach_get("normal", cn); cn = cn.reshape(-1, 3)
    uv = np.zeros((len(me.loops), 2), np.float32)
    if me.uv_layers.active: t = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", t); uv = t.reshape(-1, 2)
    p = co[lv[loops]][:, [0, 2, 1]]; nrm = cn[loops][:, [0, 2, 1]]; tuv = uv[loops]
    lo, hi = p.min(axis=0), p.max(axis=0); centre = (lo + hi) / 2; extent = np.maximum((hi - lo) / 2, 1e-4)
    q = np.clip(np.round((p - centre) / extent * 32767), -32767, 32767).astype(np.int16)
    nn = nrm / np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-8)
    qn = np.clip(np.round(nn * 127), -127, 127).astype(np.int8)
    huv = tuv.astype(np.float16)
    rec = np.zeros(len(p), dtype=[('p', '<i2', 4), ('n', 'i1', 4), ('uv', '<f2', 2)])
    rec['p'][:, :3] = q; rec['n'][:, :3] = qn; rec['uv'] = huv
    raw = rec.view(np.dtype((np.void, 16))).ravel()
    uniq, first, inverse = np.unique(raw, return_index=True, return_inverse=True)
    vertices = rec[first]
    idx = inverse.astype(np.uint32).reshape(-1, 3)[:, [0, 2, 1]]   # the axis swap mirrors the mesh: keep the winding counter-clockwise
    subs, order, start = [], [], 0
    for m in np.unique(mat):
        sel = np.nonzero(mat == m)[0]
        subs.append((start, len(sel) * 3, material_for(slots[m] if m < len(slots) else None)))
        order.append(idx[sel]); start += len(sel) * 3
    indices = np.concatenate(order).ravel()
    return dict(centre=centre.astype(np.float32), extent=extent.astype(np.float32), vertices=vertices.tobytes(), vcount=len(vertices), indices=indices.astype(np.uint32).tobytes(), icount=len(indices), subs=subs)

# ——— the scenes ———

def matrix_rows(m):
    """A Blender world matrix as the 3x4 rows of the same transform in Direct3D axes (S M S, S swapping y and z)."""
    S = [0, 2, 1]
    return [[m[S[r]][S[c]] for c in range(3)] + [m[S[r]][3]] for r in range(3)]

def depsgraph_of(sc):
    """The scene evaluated as the viewport shows it, with every particle; works with a window or in the background."""
    for ps in bpy.data.particles: ps.display_percentage = 100
    if bpy.context.window:
        bpy.context.window.scene = sc; return bpy.context.evaluated_depsgraph_get()
    with bpy.context.temp_override(scene=sc, view_layer=sc.view_layers[0]):
        return bpy.context.evaluated_depsgraph_get()

def instance_key(inst):
    o = inst.object
    # instances without modifiers share by mesh data: the realised leaves are thousands of objects over a few meshes
    if inst.is_instance: return ("d:" + o.data.name) if not o.original.modifiers else ("i:" + o.original.name + ":" + o.data.name)
    return "o:" + o.original.name

# how many times each mesh is placed (in the busier of the two scenes), so the ones placed thousands of times can be simplified
counts = {}
for scene_name, bit in SCENES:
    seen = {}
    for inst in depsgraph_of(bpy.data.scenes[scene_name]).object_instances:
        if inst.object.type in ('MESH', 'CURVE'): k = instance_key(inst); seen[k] = seen.get(k, 0) + 1
    for k, n in seen.items(): counts[k] = max(counts.get(k, 0), n)

instances, lights, cameras = {}, {}, {}
for scene_name, bit in SCENES:
    sc = bpy.data.scenes[scene_name]
    dg = depsgraph_of(sc)
    for inst in dg.object_instances:
        o = inst.object
        mw = inst.matrix_world
        if o.type in ('MESH', 'CURVE'):
            key = instance_key(inst)
            mi = mesh_for(key, o, dg)
            if mi < 0: continue
            rows = matrix_rows(mw); k = (mi, tuple(round(x, 4) for r in rows for x in r))
            flags = 1 if o.original.name == "MazestaLogo" else 0
            prev = instances.get(k); instances[k] = (mi, (prev[1] if prev else 0) | bit, flags, rows)
        elif o.type == 'LIGHT':
            d = o.data; pos = swap(mw.translation); aim = mw.to_3x3() @ __import__('mathutils').Vector((0, 0, -1)); aim = swap(aim.normalized())
            kind = {'SUN': 0, 'POINT': 1, 'SPOT': 2, 'AREA': 2}[d.type]
            cone = d.spot_size if d.type == 'SPOT' else (math.radians(160) if d.type == 'AREA' else 0.0)
            blend = d.spot_blend if d.type == 'SPOT' else 1.0
            radius = getattr(d, "shadow_soft_size", 0.1) if d.type != 'AREA' else d.size / 2
            energy = d.energy
            rec = (kind, pos, aim, tuple(d.color), energy, cone, blend, radius, math.degrees(getattr(d, 'angle', 0)))
            k = (kind, tuple(round(x, 3) for x in pos), round(energy, 2))
            prev = lights.get(k); lights[k] = (rec, (prev[1] if prev else 0) | bit)
    cam = sc.camera
    if cam:
        fwd = cam.matrix_world.to_3x3() @ __import__('mathutils').Vector((0, 0, -1))
        cameras[bit] = (swap(cam.matrix_world.translation), swap(cam.matrix_world.translation + fwd * 10), cam.data.angle_y)
if bpy.context.window: bpy.context.window.scene = bpy.data.scenes["Garden_Raster"]
pack_pending()

# ——— BC3 ———

def mips_of(img, cutout):
    levels = [img]; ref = (img[..., 3] > 0.5).mean() if cutout else None
    while levels[-1].shape[0] > 4:
        a = levels[-1]; h = a.shape[0] // 2
        m = a.reshape(h, 2, h, 2, 4).mean(axis=(1, 3))
        if cutout and ref > 0:   # keep the leaves' coverage at a distance: scale alpha so as many texels pass the 0.5 test as at full size
            lo, hi = 0.5, 8.0
            for _ in range(12):
                mid = (lo + hi) / 2
                if (np.clip(m[..., 3] * mid, 0, 1) > 0.5).mean() < ref: lo = mid
                else: hi = mid
            m[..., 3] = np.clip(m[..., 3] * hi, 0, 1)
        levels.append(m)
    return levels

def bc3(img):
    h, w = img.shape[:2]
    b = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.int32).reshape(h // 4, 4, w // 4, 4, 4).transpose(0, 2, 1, 3, 4).reshape(-1, 16, 4)
    rgb, a = b[..., :3].astype(np.float32), b[..., 3]
    # colour: endpoints from the principal axis, inset a little
    mean = rgb.mean(axis=1, keepdims=True); c = rgb - mean
    cov = np.einsum('bij,bik->bjk', c, c)
    axis = np.ones((len(b), 3), np.float32)
    for _ in range(6): axis = np.einsum('bjk,bk->bj', cov, axis); axis /= np.maximum(np.linalg.norm(axis, axis=1, keepdims=True), 1e-6)
    t = np.einsum('bij,bj->bi', c, axis); tmin, tmax = t.min(axis=1), t.max(axis=1)
    inset = (tmax - tmin) / 16
    e0 = mean[:, 0] + axis * (tmax - inset)[:, None]; e1 = mean[:, 0] + axis * (tmin + inset)[:, None]
    def to565(e): e = np.clip(np.round(e), 0, 255).astype(np.int32); return (e[:, 0] >> 3) << 11 | (e[:, 1] >> 2) << 5 | (e[:, 2] >> 3)
    def from565(v): return np.stack([(v >> 11 & 31) * 255 / 31, (v >> 5 & 63) * 255 / 63, (v & 31) * 255 / 31], axis=1).astype(np.float32)
    c0, c1 = to565(e0), to565(e1)
    p0, p1 = from565(c0), from565(c1)
    pal = np.stack([p0, p1, (2 * p0 + p1) / 3, (p0 + 2 * p1) / 3], axis=1)
    ci = np.argmin(((rgb[:, :, None, :] - pal[:, None, :, :]) ** 2).sum(axis=3), axis=2)
    cbits = np.zeros(len(b), np.uint64)
    for i in range(16): cbits |= ci[:, i].astype(np.uint64) << np.uint64(2 * i)
    # alpha: 8-value mode between the block's max and min
    a0, a1 = a.max(axis=1), a.min(axis=1)
    ap = np.stack([a0, a1] + [((7 - k) * a0 + k * a1) / 7 for k in range(1, 7)], axis=1).astype(np.float32)
    ai = np.argmin(np.abs(a[:, :, None] - ap[:, None, :]), axis=2)
    ai[a0 == a1] = 0
    abits = np.zeros(len(b), np.uint64)
    for i in range(16): abits |= ai[:, i].astype(np.uint64) << np.uint64(3 * i)
    out = np.zeros((len(b), 16), np.uint8)
    out[:, 0] = a0; out[:, 1] = a1
    for k in range(6): out[:, 2 + k] = (abits >> np.uint64(8 * k)) & np.uint64(255)
    out[:, 8] = c0 & 255; out[:, 9] = c0 >> 8; out[:, 10] = c1 & 255; out[:, 11] = c1 >> 8
    for k in range(4): out[:, 12 + k] = (cbits >> np.uint64(8 * k)) & np.uint64(255)
    return out.tobytes()

# ——— write ———

buf = io.BytesIO(); w = buf.write
w(MAGIC); w(struct.pack("<I", VERSION))
w(struct.pack("<6I", len(textures), len(materials), len(meshes), len(instances), len(lights), TEX))
for img, cutout in textures:
    for level in mips_of(img, cutout): w(bc3(level))
for m in materials:
    w(struct.pack("<Ii", m['kind'], m['tex'])); w(struct.pack("<2f", 0, 0))
    w(struct.pack("<4f", *m['base'], m['alpha'])); w(struct.pack("<4f", *m['color2'], m['rough'])); w(struct.pack("<4f", *m['mortar'], m['metal']))
    w(struct.pack("<4f", *m['emit'], m['trans'])); w(struct.pack("<4f", *m['pattern']))
for me in meshes:
    w(struct.pack("<3I", me['vcount'], me['icount'], len(me['subs']))); w(struct.pack("<6f", *me['centre'], *me['extent']))
    for s in me['subs']: w(struct.pack("<3I", *s))
    w(me['vertices']); w(me['indices'])
for mi, mask, flags, rows in instances.values():
    w(struct.pack("<3I", mi, mask, flags)); w(struct.pack("<12f", *[x for r in rows for x in r]))
for rec, mask in lights.values():
    kind, pos, aim, color, energy, cone, blend, radius, angle = rec
    w(struct.pack("<2I", kind, mask)); w(struct.pack("<3f3f3f", *pos, *aim, *color)); w(struct.pack("<5f", energy, cone, blend, radius, angle))
for bit in (1, 2):
    pos, target, fov = cameras.get(bit, cameras.get(1))
    w(struct.pack("<7f", *pos, *target, fov))
raw = buf.getvalue()
with open(OUT, "wb") as f: f.write(gzip.compress(raw, 9, mtime=0))
tris = sum(m['icount'] for m in meshes) // 3
per_frame = sum(meshes[mi]['icount'] // 3 for mi, mask, flags, rows in instances.values() if mask & 1)
result = {"raster_tris_per_frame": per_frame, "file": OUT, "raw_mb": round(len(raw) / 1e6, 2), "gz_mb": round(os.path.getsize(OUT) / 1e6, 2), "meshes": len(meshes), "unique_tris": tris, "instances": len(instances),
          "materials": len(materials), "textures": len(textures), "lights": len(lights)}
print(result)
