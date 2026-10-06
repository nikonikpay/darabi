# Exports the courtyard scene (Mazesta-Art/courtyard-v10.blend: the owner's DFM_Courtyard_V10 as the app's two scenes, made by tools/scene/prepare_courtyard_v10.py)
# to the file the visual GPU tests draw: src/Mazesta.Diagnostics.Gpu/Scene/garden.mzscene. Best run in a Blender of its own, so an open window is left alone:
#   blender --background ../Mazesta-Art/courtyard-v10.blend --python tools/scene/export_garden.py
# (it also runs from Blender's Text Editor with the .blend open).
# Afterwards the light bounced round the new scene has to be worked out again (Scene/garden.light: GardenLightVolume.cs), on a GPU with
# ray-tracing hardware - build, then with MAZESTA_BAKE_LIGHT set to that file's full path:
#   dotnet test tests/Mazesta.Diagnostics.Gpu.Tests -c Release --filter "FullyQualifiedName~Bakes_the_garden"
# and build again. A test fails while the light is another scene's.
#
# Both of the file's scenes are read: Garden_Raster (the Direct3D test: a low golden sun) and Garden_RT (the ray-traced test: nightfall,
# its own light rig and a mirror sphere). What appears in both is written once, marked for both.
#
# The format is documented in GardenScene.cs, which reads it. In short, gzip over little-endian records:
#   meshes    - 16-byte vertices (position snorm16x4 within the mesh's bounds, normal snorm8x4, uv float16x2), 32-bit indices, one submesh per material
#   instances - a mesh, a 3x4 world matrix, which scene(s) it is in, and flags: the logo, the mirror sphere, how far the wind bends it
#   (vertices, indices and instances are written byte plane by byte plane, the indices as differences, the vertices in the order the
#   indices first use them: the same data, a little over half the size once gzip has been over it)
#   materials - flat, alpha-tested (texture), brick pattern (procedural, as in Blender), water, glass, emissive
#   textures  - 1024x1024 for the building's stone, plaster, wood and tile, the gate and the rugs, 512x512 for the rest:
#               base colour with, in alpha, a leaf card's opacity or else the surface's roughness; or a normal map (x in alpha, y in green)
#               with the material's ambient occlusion in red and its metalness in blue. Stored as JPEG pictures, one or more a texture
#               (colour; then each further channel as a grey picture, a leaf's opacity as one bit a texel, or one value when it is the
#               same all over): a fifth of what the BC3 blocks the GPU wants come to. The reader makes those, and the smaller mips.
#   lights    - sun, point, spot (area lights become wide spots), watts as Blender has them
#   backdrop  - the mountains round the horizon (the world's panorama, laid as the world lays it): one BC3 image, 360 degrees wide
#   features  - the pool's water level, and the fountain: its nozzle, the water in its bowl and the bowl's rim (the renderer draws the jet)
# Coordinates are turned from Blender's (x right, y forward, z up) to Direct3D's (x right, y up, z forward).
import bpy, bmesh, numpy as np, struct, gzip, math, os, io, re, tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", "Mazesta"))
OUT = os.environ.get("MAZESTA_SCENE_OUT") or os.path.join(REPO, "src", "Mazesta.Diagnostics.Gpu", "Scene", "garden.mzscene")
SCENES = (("Garden_Raster", 1), ("Garden_RT", 2))
TEX, SMALL = 1024, 512
# JPEG quality: the colour pictures, a normal map's two directions, and what only shades (roughness, occlusion, metalness)
Q_COLOUR, Q_NORMAL, Q_SHADE = 86, 88, 72
# The stone and soil are lighter than this renderer's sun and tone curve leave room for (Cycles shows them under AgX): their
# base colours are scaled so the paving keeps its joints and the beds read as damp earth, as in the owner's own preview renders.
ALBEDO_SCALE = {"V8_Limestone": 0.8, "V8_Carved_Pale_Limestone": 0.85, "V8_Granular_Garden_Loam": 0.6}
BIG_TEXTURES = ("V8_", "CV5_", "Big_Old_Gates", "carpet")   # the materials whose textures are kept at TEX: the ones the walk passes within arm's reach, or tiled over metres
# Materials Blender builds from layered shaders that have no one Principled node to read, or whose colours are a node group's
# own inputs: what they are, said here (the pots: the middle of the group's two clay colours).
AS_GLASS = ("Multicoated glass",)
STAINED = "V7_Stained_"   # the orsi's coloured panes: the ray tracer colours the light that passes through them
# Stone the .blend shades with noise alone and gives no coordinates of its own: it takes another material's texture and relief, laid by
# world position (so many repeats a metre) and tinted to its own colour.
LAID_LIKE = {"V8_Carved_Pale_Limestone": ("V8_Limestone", 0.42)}
AS_SAID = {"Clay pot": dict(base=(0.5, 0.17, 0.085), rough=0.75)}
DECIMATE_OVER = 6000            # hard-surface meshes above this many triangles are simplified (lantern glass, pots, trunks)
PER_MESH_BUDGET = 400_000       # a mesh placed thousands of times (ivy leaves, blossoms) is simplified until all its copies together stay under this
KEEP_DETAIL = ("V7_", "V8_", "CV4_Walls", "CV5_PoolDetails", "V10_Persian", "V9_Fountain")   # the building: its carving and lattices are what is looked at
# The heaviest meshes, simplified to what the walk can tell apart: the orsi's lattices and the door's panels, which it passes within
# arm's reach, keep most of their carving; the windcatchers' is on the roof, never nearer than 12 m; the furniture is seen from a step away.
BUDGET = {"V7_Orsi_V7_Walnut": 260_000, "V7_OpenDoor_V7_Walnut": 80_000, "V8_Carved_Windcatchers_V8_Carved_Pale_Limestone": 140_000,
          "V9_SOURCE_Armchair": 18_000, "V9_SOURCE_Chair": 16_000, "V9_SOURCE_Sofa": 18_000, "V10_Living_Climbers_V9_Living_Vine_Stems": 40_000,
          "V7_Orsi_V7_Stained_Cobalt": 6_000, "V7_Orsi_V7_Stained_Emerald": 6_000, "V7_Orsi_V7_Stained_Ruby": 6_000, "V7_Orsi_V7_Stained_Amber": 6_000,
          "V9_SOURCE_Blossom_Trunk": 20_000, "CV5_Terrain_CV5_Terrain_Earth": 8_000, "CV5_Soil_CV5_Loamy_Soil": 6_000, "V9_Turf_Bed_Surface": 6_000}
# What all the copies of one plant may come to (the default is PER_MESH_BUDGET): leaves by the thousand need only be leaf-shaped,
# the trees beyond the walls are never nearer than fifteen metres.
COPIES_BUDGET = {"V9_SOURCE_IvyLeaf": 260_000, "V10_SOURCE_VineLeaf": 110_000, "V10_SOURCE_VariegatedLeaf": 60_000, "V10_SOURCE_Broadleaf": 520_000,
                 "V9_SOURCE_Shrub": 520_000, "V10_SOURCE_Alpine": 420_000, "V9_SOURCE_Grass": 330_000, "V9_SOURCE_Daisy": 200_000, "V9_SOURCE_WhiteFlowers": 110_000}
# Bevels are 2 to 4 mm wide and multiply the triangles by up to eight; subdivision smooths what is nearly flat. Neither is exported.
for o in bpy.data.objects:
    if o.type == 'MESH':
        for m in o.modifiers:
            if m.type in ('BEVEL', 'SUBSURF'): m.show_viewport = m.show_render = False
# A lantern's lamp is one asset in both scenes: by day it only glows, at night it lights the paving round it.
LAMP_SCALE = {1: 0.1, 2: 0.45}
# A photograph of leaves is far lighter than a leaf is: under this renderer's sun a crown would bleach. Leaf cards keep this much of it.
LEAF_ALBEDO = 0.6
# The backdrop: all the way round by the tangent of the height above the horizon, 0 to SKY_TAN (20 degrees: the peaks reach 10)
SKY_WORLD, SKY_W, SKY_H, SKY_TAN = "V8_Alborz_Sunset_HDR", 2048, 256, 0.36
MAGIC, VERSION = b"MZSC", 4
K_FLAT, K_CUTOUT, K_BRICK, K_WATER, K_GLASS, K_EMISSIVE = 0, 1, 2, 3, 4, 5
F_LOGO, F_SPHERE = 1, 2
# How far the wind bends each kind of plant: thousandths of a metre sideways for every metre above where it is rooted (bits 8 to 15 of an
# instance's flags). A tree leans a hand's breadth at its top; a sprig, a blade or a leaf on its stalk flutters.
SWAY = {"V10_SOURCE_Broadleaf": 10, "V9_SOURCE_Cypress": 8, "V10_SOURCE_Alpine": 9, "V10_SOURCE_Juniper": 14, "V9_SOURCE_Shrub": 30, "V9_BlossomSprig": 110,
        "V9_SOURCE_Grass": 90, "V9_SOURCE_WildGrass": 90, "V9_SOURCE_Daisy": 70, "V9_SOURCE_WhiteFlowers": 70,
        "V9_SOURCE_IvyLeaf": 150, "V10_SOURCE_VineLeaf": 150, "V10_SOURCE_VariegatedLeaf": 150}

def swap(v): return (v[0], v[2], v[1])   # Blender -> Direct3D axes

# ——— materials and textures ———

def images_feeding(sock, depth=0, channel=None):
    """Every image texture feeding a socket, nearest first: (image, which output - Color or Alpha, which channel when it came through a
    Separate Color node, the image's node). A node group is looked into by the output the link leaves it from, then through what feeds it."""
    if depth > 14 or not sock.is_linked: return []
    found = []
    for link in sock.links:
        n = link.from_node
        if n.bl_idname == 'ShaderNodeTexImage' and n.image: found.append((n.image, link.from_socket.name, channel, n)); continue
        if n.bl_idname == 'ShaderNodeGroup' and n.node_tree:
            inner = next((g for g in n.node_tree.nodes if g.bl_idname == 'NodeGroupOutput'), None)
            s = inner.inputs.get(link.from_socket.name) if inner else None
            if s is not None: found += images_feeding(s, depth + 1, channel)
        ch = {'Red': 0, 'Green': 1, 'Blue': 2}.get(link.from_socket.name) if n.bl_idname in ('ShaderNodeSeparateColor', 'ShaderNodeSeparateRGB') else channel
        for s in n.inputs: found += images_feeding(s, depth + 1, ch)
    return found

def normal_map(sock, depth=0):
    """The Normal Map node a Normal socket is fed from (inside a node group too), as its image and strength; None for a bump from a
    height image or from noise, which is not a normal map."""
    if depth > 8 or not sock.is_linked: return None
    n = sock.links[0].from_node
    if n.bl_idname == 'ShaderNodeNormalMap':
        img = [f for f in images_feeding(n.inputs['Color']) if f[0].colorspace_settings.name != 'sRGB']
        strength = n.inputs['Strength']
        return (img[0][0], 1.0 if strength.is_linked else float(strength.default_value)) if img else None
    if n.bl_idname == 'NodeReroute': return normal_map(n.inputs[0], depth + 1)
    if n.bl_idname == 'ShaderNodeGroup' and n.node_tree:
        inner = next((g for g in n.node_tree.nodes if g.bl_idname == 'NodeGroupOutput'), None)
        s = inner.inputs.get(sock.links[0].from_socket.name) if inner else None
        r = normal_map(s, depth + 1) if s is not None else None
        if r: return r
        return next((r for r in (normal_map(i, depth + 1) for i in n.inputs if i.type == 'VECTOR') if r), None)
    return None

def upstream_image(sock, colour=False):
    """The first image texture feeding a socket and which of its outputs; for a base colour, the first that is a colour image
    (a mix with an occlusion or a mask map puts those on the same socket)."""
    found = images_feeding(sock)
    if colour: found = [f for f in found if f[0].colorspace_settings.name == 'sRGB'] or found
    return found[0] if found else None

def principled(mat):
    """The material's Principled BSDF: the nearest to its output, inside a node group when that is where it is."""
    if not mat or not mat.node_tree: return None
    out = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeOutputMaterial' and n.is_active_output), None)
    def find(node, depth=0):
        if node is None or depth > 6: return None
        if node.bl_idname == 'ShaderNodeBsdfPrincipled': return node
        if node.bl_idname == 'ShaderNodeGroup' and node.node_tree:
            inner = next((g for g in node.node_tree.nodes if g.bl_idname == 'ShaderNodeBsdfPrincipled'), None)
            if inner: return inner
        for s in node.inputs:
            for l in s.links:
                r = find(l.from_node, depth + 1)
                if r: return r
        return None
    return find(out.inputs['Surface'].links[0].from_node) if out and out.inputs['Surface'].is_linked else None

def flat_colour(sock, depth=0):
    """What a procedural base colour comes to on average (noise through colour ramps and mixes: the loam, the turf, the clay pots)."""
    if not sock.is_linked: return tuple(sock.default_value[:3]) if hasattr(sock.default_value, '__len__') else None
    if depth > 8: return None
    n = sock.links[0].from_node
    if n.bl_idname == 'ShaderNodeValToRGB': return tuple(float(np.mean([e.color[i] for e in n.color_ramp.elements])) for i in range(3))
    if n.bl_idname in ('ShaderNodeMixRGB', 'ShaderNodeMix'):
        ins = [s for s in n.inputs if s.type == 'RGBA' and s.enabled][:2]
        a, b = (flat_colour(s, depth + 1) for s in ins) if len(ins) == 2 else (None, None)
        if a is None or b is None: return a or b
        fac = next((s for s in n.inputs if s.name in ('Fac', 'Factor') and s.enabled and s.type == 'VALUE'), None)
        t = 0.5 if fac is None or fac.is_linked else float(fac.default_value)
        if n.blend_type == 'MULTIPLY': return tuple(x * ((1 - t) + t * y) for x, y in zip(a, b))
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    if n.bl_idname in ('ShaderNodeHueSaturation', 'ShaderNodeBrightContrast', 'ShaderNodeRGBCurve', 'ShaderNodeGamma') and 'Color' in n.inputs: return flat_colour(n.inputs['Color'], depth + 1)
    return None

def uv_scale(node):
    """How many times an image repeats across the mesh's own coordinates: the Mapping node its Vector comes through (1 when there is
    none, or the coordinates are not the UV map's)."""
    sock = node.inputs['Vector']
    for _ in range(6):
        if not sock.is_linked: break
        n = sock.links[0].from_node
        if n.bl_idname == 'ShaderNodeMapping':
            src = n.inputs['Vector'].links[0] if n.inputs['Vector'].is_linked else None
            return tuple(n.inputs['Scale'].default_value[:2]) if src is None or src.from_socket.name == 'UV' else (1.0, 1.0)
        if n.bl_idname != 'NodeReroute': break
        sock = n.inputs[0]
    return (1.0, 1.0)

def srgb_to_lin(c): return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)

textures, texture_index = [], {}
def texture_for(color_img, alpha_src, ops=(), rough=1.0, size=SMALL):
    """rough: what a texture without a cut-out keeps in alpha - the roughness image (with the channel it is in, when it shares an
    image with other maps), or the material's one value."""
    rough, rough_channel = rough if isinstance(rough, tuple) else (rough, None)
    rough_key = None if alpha_src else ((rough.name, rough_channel) if hasattr(rough, 'name') else round(rough, 2))
    key = (color_img.name if color_img else None, alpha_src[0].name if alpha_src else None) + (tuple(alpha_src[1:]) if alpha_src else ()) + (ops, rough_key, size)
    if key in texture_index: return texture_index[key]
    rgb = image_pixels(color_img, size)[..., :3] if color_img else np.ones((size, size, 3), np.float32)
    if ops: rgb = grade(rgb, ops)
    if alpha_src:
        ap = image_pixels(alpha_src[0], size)
        a = ap[..., 3] if alpha_src[1] == 'Alpha' else ap[..., :3].mean(axis=2)
        if alpha_src[2]: a = 1 - a
        a = np.clip((a - alpha_src[3]) * 40 + 0.5, 0, 1)   # the mask's own cut (a colour ramp's threshold), kept as a hard edge
        rgb = bleed(rgb, a > 0.5)
    elif hasattr(rough, 'name'): rp = image_pixels(rough, size); a = rp[..., :3].mean(axis=2) if rough_channel is None else rp[..., rough_channel]
    else: a = np.full((size, size), rough, np.float32)
    texture_index[key] = len(textures); textures.append((np.dstack([rgb, a]), alpha_src is not None, False))
    return texture_index[key]

def normal_texture_for(img, size, strength, occlusion=None, metal=0.0):
    """A tangent-space normal map (Blender's: y up the image) as the shaders read it: x in alpha, y in green - the two channels BC3 keeps best.
    The Normal Map node's strength is applied here (the slope is scaled). Red holds the material's ambient occlusion map (1 without one),
    blue its metalness (a map's channel, or the material's one value)."""
    metal, metal_channel = metal if isinstance(metal, tuple) else (metal, None)
    key = ("normal", img.name, size, round(strength, 3), occlusion.name if occlusion else None, (metal.name, metal_channel) if hasattr(metal, 'name') else round(metal, 2))
    if key in texture_index: return texture_index[key]
    px = image_pixels(img, size)
    xy = np.clip((px[..., :2] * 2 - 1) * strength, -1, 1) * 0.5 + 0.5
    ao = image_pixels(occlusion, size)[..., :3].mean(axis=2) if occlusion else np.ones((size, size), np.float32)
    mt = image_pixels(metal, size)[..., metal_channel if metal_channel is not None else 0] if hasattr(metal, 'name') else np.full((size, size), metal, np.float32)
    texture_index[key] = len(textures); textures.append((np.dstack([ao, xy[..., 1], mt, xy[..., 0]]), False, True))
    return texture_index[key]

def bleed(rgb, solid):
    """The cut-away texels take the colour of the nearest kept ones, so filtering and smaller mips draw no background (white) rim round a leaf."""
    if not solid.any() or solid.all(): return rgb
    rgb = rgb.copy(); known = solid.copy()
    for _ in range(solid.shape[0]):
        if known.all(): break
        acc = np.zeros_like(rgb); n = np.zeros(known.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            k = np.roll(known, (dy, dx), (0, 1)); acc += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]; n += k
        grow = ~known & (n > 0)
        rgb[grow] = acc[grow] / n[grow][:, None]; known |= grow
    return rgb

def ramp_cut(sock):
    """Where a colour ramp between the mask image and Alpha cuts (the midpoint of its black-to-white step), and whether it is inverted."""
    n = sock.links[0].from_node if sock.is_linked else None
    if n is None or n.bl_idname != 'ShaderNodeValToRGB' or len(n.color_ramp.elements) != 2: return 0.5, False
    e0, e1 = n.color_ramp.elements
    return (e0.position + e1.position) / 2, e0.color[0] > e1.color[0]

def grade_ops(sock):
    """The Brightness/Contrast and Hue/Saturation/Value nodes between the base image and Base Color (the thuja's), nearest the image first."""
    ops = []
    while sock.is_linked:
        n = sock.links[0].from_node
        if n.bl_idname == 'ShaderNodeBrightContrast': ops.append(('bc', float(n.inputs[1].default_value), float(n.inputs[2].default_value))); sock = n.inputs['Color']
        elif n.bl_idname == 'ShaderNodeHueSaturation': ops.append(('hsv', float(n.inputs['Hue'].default_value), float(n.inputs['Saturation'].default_value), float(n.inputs['Value'].default_value), float(n.inputs['Fac'].default_value))); sock = n.inputs['Color']
        else: break
    return tuple(reversed(ops))

def hsv_shift(rgb, h, sat, val):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(axis=2); mn = rgb.min(axis=2); d = mx - mn; safe = np.where(d > 0, d, 1)
    hue = np.where(mx == r, (g - b) / safe, np.where(mx == g, 2 + (b - r) / safe, 4 + (r - g) / safe)) / 6 % 1.0
    hue = np.where(d > 0, hue, 0); s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    return (hue + h - 0.5) % 1.0, np.clip(s * sat, 0, 1), np.maximum(mx * val, 0)

def hsv_to_rgb(h, s, v):
    i = np.floor(h * 6).astype(int) % 6; f = h * 6 - np.floor(h * 6)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    sel = [(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]
    return np.stack([np.choose(i, [c[k] for c in sel]) for k in range(3)], axis=2)

def grade(rgb, ops):
    """Blender's Brightness/Contrast and Hue/Saturation/Value applied as Cycles does, on linear colour; the result is sRGB-encoded again."""
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    for op in ops:
        if op[0] == 'bc':
            a = 1 + op[2]; lin = np.maximum(a * lin + (op[1] - op[2] * 0.5), 0)
        else:
            _, h, sat, val, fac = op
            out = hsv_to_rgb(*hsv_shift(lin, h, sat, val))
            lin = lin + (out - lin) * fac
    lin = np.clip(lin, 0, 1)
    return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * lin ** (1 / 2.4) - 0.055).astype(np.float32)

def mix_alpha(mat):
    """A cut-out made by mixing with a Transparent BSDF (the factor from a mask image): the image, its output, and whether it is inverted."""
    out = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeOutputMaterial' and n.is_active_output), None)
    if not out or not out.inputs['Surface'].is_linked: return None
    mix = out.inputs['Surface'].links[0].from_node
    if mix.bl_idname != 'ShaderNodeMixShader': return None
    for slot, invert in ((1, False), (2, True)):   # transparent at factor 0 means the factor is the opacity
        if any(l.from_node.bl_idname == 'ShaderNodeBsdfTransparent' for l in mix.inputs[slot].links):
            r = upstream_image(mix.inputs[0])
            return (r[0], r[1], invert, 0.5) if r else None
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

def image_pixels(img, size):
    """The image at size x size, RGBA floats as stored (sRGB colour images stay sRGB-encoded), rows bottom to top as Blender keeps them."""
    c = img.copy()
    try:
        if tuple(c.size) != (size, size): c.scale(size, size)
        px = np.empty(size * size * 4, np.float32); c.pixels.foreach_get(px)
        return px.reshape(size, size, 4)
    finally: bpy.data.images.remove(c)

materials, material_index = [], {}
def material_for(mat):
    name = mat.name if mat else "(none)"
    if name in material_index: return material_index[name]
    size = TEX if name.startswith(BIG_TEXTURES) else SMALL
    rec = dict(kind=K_FLAT, tex=-1, normal=-1, base=(0.6, 0.6, 0.6), alpha=1.0, color2=(0, 0, 0), rough=0.6, mortar=(0, 0, 0), metal=0.0, emit=(0, 0, 0), trans=0.0, pattern=(0, 0, 0, 0))
    p = principled(mat)
    plain = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeBsdfMetallic'), None) if mat and mat.node_tree and not p else None
    if plain: rec.update(base=tuple(plain.inputs['Base Color'].default_value[:3]), rough=0.35, metal=1.0)   # bolts and fittings
    if p:
        bc = p.inputs['Base Color']
        rec['base'] = tuple(bc.default_value[:3]); rec['rough'] = float(p.inputs['Roughness'].default_value); rec['metal'] = float(p.inputs['Metallic'].default_value)
        rough_img = upstream_image(p.inputs['Roughness']); metal_img = upstream_image(p.inputs['Metallic'])
        if p.inputs['Roughness'].is_linked: rec['rough'] = 0.6
        if bc.is_linked and not upstream_image(bc): rec['base'] = flat_colour(bc) or rec['base']
        rec['trans'] = float(p.inputs['Transmission Weight'].default_value)
        es = float(p.inputs['Emission Strength'].default_value); ec = p.inputs['Emission Color'].default_value
        rec['emit'] = (ec[0] * es, ec[1] * es, ec[2] * es)
        rec['alpha'] = float(p.inputs['Alpha'].default_value)
        brick = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeTexBrick'), None)
        img = upstream_image(bc, colour=True); alpha = upstream_image(p.inputs['Alpha'])
        alpha = (alpha[0], alpha[1], *reversed(ramp_cut(p.inputs['Alpha']))) if alpha else mix_alpha(mat)
        if brick is not None:
            mp = next((n for n in mat.node_tree.nodes if n.bl_idname == 'ShaderNodeMapping'), None)
            s = mp.inputs['Scale'].default_value[0] if mp else 1.0
            rec.update(kind=K_BRICK, base=tuple(brick.inputs['Color1'].default_value[:3]), color2=tuple(brick.inputs['Color2'].default_value[:3]), mortar=tuple(brick.inputs['Mortar'].default_value[:3]),
                       pattern=(brick.inputs['Scale'].default_value * s, brick.inputs['Mortar Size'].default_value, brick.inputs['Brick Width'].default_value, brick.inputs['Row Height'].default_value))
        elif img or alpha:
            rec['tex'] = texture_for(img[0] if img else None, alpha, grade_ops(bc) if img else (), (rough_img[0], rough_img[2]) if rough_img else rec['rough'], size)
            if img: rec['base'] = tint(bc); rec['pattern'] = (*uv_scale(img[3]), 0, 0)
            if alpha: rec['kind'] = K_CUTOUT
            if alpha and not name.startswith('carpet'): rec['base'] = tuple(c * LEAF_ALBEDO for c in rec['base'])
            bump = normal_map(p.inputs['Normal'])   # the hard surfaces' own relief (stone joints, plaster, grain, weave); leaves do without
            occlusion = next((f[0] for f in images_feeding(bc) if f[0].colorspace_settings.name != 'sRGB' and (not alpha or f[0] != alpha[0])), None)
            if bump and img and not alpha:
                rec['normal'] = normal_texture_for(bump[0], size, bump[1], occlusion, (metal_img[0], metal_img[2]) if metal_img else rec['metal'])
        base = re.sub(r'\.\d{3}$', '', name)   # an appended copy of a material is named "....001"
        if base in AS_SAID: rec.update(AS_SAID[base])
        if base in AS_GLASS: rec.update(kind=K_GLASS, base=(0.9, 0.95, 0.95), metal=0.0, rough=0.05, trans=1.0)
        elif base.endswith('Water'): rec['kind'] = K_WATER
        elif rec['trans'] > 0.5 or base.endswith('Foam'): rec['kind'] = K_GLASS
        elif max(rec['emit']) > 0.5 and rec['kind'] == K_FLAT: rec['kind'] = K_EMISSIVE
        if base.endswith('Foam'): rec['alpha'] = 0.5
        if base.startswith(STAINED) and rec['kind'] == K_GLASS and max(rec['base']) - min(rec['base']) > 0.2: rec['light_tint'] = 1.0
        if base in LAID_LIKE and rec['tex'] < 0:
            like = materials[material_for(bpy.data.materials[LAID_LIKE[base][0]])]; mean = textures[like['tex']][0][..., :3].mean(axis=(0, 1))
            lin = np.array(srgb_to_lin(tuple(mean))) * np.array(like['base'])   # the borrowed texture's own average colour, as it is shown
            rec.update(tex=like['tex'], normal=like['normal'], base=tuple(float(c / max(m, 1e-3)) for c, m in zip(rec['base'], lin)), pattern=(1.0, 1.0, LAID_LIKE[base][1], 0.0))
        if base in ALBEDO_SCALE: rec['base'] = tuple(c * ALBEDO_SCALE[base] for c in rec['base'])
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
        if name in BUDGET: target = min(tris, BUDGET[name])
        elif tris > DECIMATE_OVER and not cutout and not name.startswith(KEEP_DETAIL): target = DECIMATE_OVER
        copies = counts.get(key, 1); budget = COPIES_BUDGET.get(name, PER_MESH_BUDGET)
        if tris * copies > budget: target = min(target, max(16, budget // copies))
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
    # Water and foam are modelled as slabs a centimetre or two thick: only what faces up or sideways is a surface. Their undersides, seen
    # through the top, would be drawn over it (the pool flickered). Foam lies in the water's own plane: it is lifted clear of it.
    names = [re.sub(r'\.\d{3}$', '', s.name) if s else "" for s in slots]
    wet = np.array([nm.endswith(('Water', 'Foam')) for nm in names] + [False]); foam = np.array([nm.endswith('Foam') for nm in names] + [False])
    slot = np.minimum(mat, len(names))
    if wet.any():
        tn = np.empty(n * 3, np.float32); me.loop_triangles.foreach_get("normal", tn)
        keep = ~(wet[slot] & (tn.reshape(-1, 3)[:, 2] < -0.7))
        if foam.any(): fv = np.unique(lv[loops.reshape(-1, 3)[foam[slot]]]); co = co.copy(); co[fv, 2] += 0.006
        loops = loops.reshape(-1, 3)[keep].ravel(); mat = mat[keep]; n = len(mat)
        if n == 0: return None
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
    used, first_use = np.unique(indices, return_index=True); by_use = used[np.argsort(first_use)]   # vertices in the order the indices first use them
    remap = np.zeros(len(vertices), np.uint32); remap[by_use] = np.arange(len(by_use), dtype=np.uint32)
    vertices = vertices[by_use]; indices = remap[indices]
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
            flags = {"MazestaLogo": F_LOGO, "MirrorSphere": F_SPHERE}.get(o.original.name, 0) | next((v for n, v in SWAY.items() if o.original.name.startswith(n)), 0) << 8
            prev = instances.get(k); instances[k] = (mi, (prev[1] if prev else 0) | bit, flags, rows)
        elif o.type == 'LIGHT':
            d = o.data; pos = swap(mw.translation); aim = mw.to_3x3() @ __import__('mathutils').Vector((0, 0, -1)); aim = swap(aim.normalized())
            kind = {'SUN': 0, 'POINT': 1, 'SPOT': 2, 'AREA': 2}[d.type]
            cone = d.spot_size if d.type == 'SPOT' else (math.radians(160) if d.type == 'AREA' else 0.0)
            blend = d.spot_blend if d.type == 'SPOT' else 1.0
            radius = getattr(d, "shadow_soft_size", 0.1) if d.type != 'AREA' else d.size / 2
            energy = d.energy * (LAMP_SCALE[bit] if inst.is_instance else 1)
            rec = (kind, pos, aim, tuple(d.color), energy, cone, blend, radius, math.degrees(getattr(d, 'angle', 0)))
            k = (kind, tuple(round(x, 3) for x in pos), round(energy, 2))
            prev = lights.get(k); lights[k] = (rec, (prev[1] if prev else 0) | bit)
    cam = sc.camera
    if cam:
        fwd = cam.matrix_world.to_3x3() @ __import__('mathutils').Vector((0, 0, -1))
        cameras[bit] = (swap(cam.matrix_world.translation), swap(cam.matrix_world.translation + fwd * 10), cam.data.angle_y)
if bpy.context.window: bpy.context.window.scene = bpy.data.scenes["Garden_Raster"]
pack_pending()

# ——— the backdrop ———

def backdrop():
    """The world's camera-ray panorama as the shaders look it up (Garden.hlsli Sky): column = the direction round the horizon (the app's
    atan2(x, z)), row = the tangent of the height, lowest first. Each texel's direction goes through the world's own Mapping node
    (scale, then rotation) and Cycles' equirectangular lookup, so the mountains stand where the owner's renders have them."""
    world = bpy.data.worlds.get(SKY_WORLD)
    node = next((n for n in world.node_tree.nodes if n.bl_idname == 'ShaderNodeTexEnvironment' and n.image and not n.image.name.lower().endswith(".exr")), None) if world else None
    if node is None: return None
    mp = node.inputs['Vector'].links[0].from_node if node.inputs['Vector'].is_linked else None
    scale, rot = (tuple(mp.inputs['Scale'].default_value), tuple(mp.inputs['Rotation'].default_value)) if mp and mp.bl_idname == 'ShaderNodeMapping' else ((1, 1, 1), (0, 0, 0))
    img = node.image; iw, ih = img.size
    px = np.empty(iw * ih * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(ih, iw, 4)[..., :3]
    az, tan = np.meshgrid((np.arange(SKY_W) + 0.5) / SKY_W * 2 * math.pi - math.pi, (np.arange(SKY_H) + 0.5) / SKY_H * SKY_TAN)
    x, y, z = np.sin(az) * scale[0], np.cos(az) * scale[1], tan * scale[2]   # the app's (x, height, z) is Blender's (x, z, y)
    c, s = math.cos(rot[2]), math.sin(rot[2]); x, y = c * x - s * y, s * x + c * y
    u = (0.5 - np.arctan2(y, x) / (2 * math.pi)) % 1.0 * iw - 0.5; v = np.clip((0.5 + np.arctan2(z, np.hypot(x, y)) / math.pi) * ih - 0.5, 0, ih - 1)
    u0 = np.floor(u).astype(int); v0 = np.minimum(np.floor(v).astype(int), ih - 2); fu = (u - u0)[..., None]; fv = (v - v0)[..., None]
    sample = lambda uu, vv: px[vv, uu % iw]
    out = (sample(u0, v0) * (1 - fu) + sample(u0 + 1, v0) * fu) * (1 - fv) + (sample(u0, v0 + 1) * (1 - fu) + sample(u0 + 1, v0 + 1) * fu) * fv
    return np.dstack([out, np.ones((SKY_H, SKY_W), np.float32)])

# ——— the pool and the fountain ———

def features():
    """The main pool's water level (its largest water face that looks up), and the fountain: the top of its nozzle, the radius and level
    of the water in its bowl, the radius of the bowl's rim. In Direct3D axes; zeros where the scene has none."""
    level, largest = 0.0, 0.0
    for o in bpy.data.scenes[SCENES[0][0]].objects:
        if o.type != 'MESH' or o.name.startswith("App_") or not any(s.material and re.sub(r'\.\d{3}$', '', s.material.name).endswith('Water') for s in o.material_slots): continue
        for p in o.data.polygons:
            if p.normal.z > 0.9 and p.area > largest: largest = p.area; level = (o.matrix_world @ p.center).z
    nozzle, bowl, stone = bpy.data.objects.get("V9_Fountain_V8_Antique_Bronze"), bpy.data.objects.get("App_Fountain_Bowl_Water"), bpy.data.objects.get("V9_Fountain_V8_Limestone")
    if not (nozzle and bowl and stone): return (level, 0, 0, 0, 0, 0, 0)
    world = lambda o: [o.matrix_world @ v.co for v in o.data.vertices]
    top = max(world(nozzle), key=lambda v: v.z); centre = sum(world(nozzle), top * 0) / len(nozzle.data.vertices)
    water = world(bowl); radius = max(math.hypot(v.x - centre.x, v.y - centre.y) for v in water)
    rim = max(math.hypot(v.x - centre.x, v.y - centre.y) for v in world(stone))
    return (level, centre.x, top.z, centre.y, radius, water[0].z, rim)

# ——— BC3 ———

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

# ——— JPEG ———

def jpeg(planes, quality):
    """An H x W picture (three channels, or one: grey) of 0..1 values, rows bottom to top, as a JPEG file's bytes - written by Blender's own
    writer through a temporary file. The values go in as they are (no colour management: the image's bytes are what is saved)."""
    h, w = planes.shape[:2]; px = np.ones((h, w, 4), np.float32); px[..., :3] = np.clip(planes if planes.ndim == 3 else planes[..., None], 0, 1)
    img = bpy.data.images.new("_jpeg", w, h, alpha=False); path = os.path.join(tempfile.gettempdir(), "mazesta-export.jpg")
    try:
        img.colorspace_settings.name = 'sRGB'; img.pixels.foreach_set(px.ravel()); img.file_format = 'JPEG'; img.filepath_raw = path; img.save(quality=quality)
        with open(path, "rb") as f: return f.read()
    finally:
        bpy.data.images.remove(img)
        if os.path.exists(path): os.remove(path)

P_VALUE, P_JPEG, P_BITS = 0, 1, 2
def texture_parts(img, cutout, normal):
    """A texture as the parts the reader puts together: (which channels - bits 0 to 3 for red, green, blue, alpha, how it is stored, its bytes)."""
    def plane(ch, quality):
        a = img[..., ch]; v = int(round(float(a.flat[0]) * 255))
        if np.all(np.abs(a - a.flat[0]) < 0.5 / 255): return (1 << ch, P_VALUE, bytes([v]))
        return (1 << ch, P_JPEG, jpeg(a, quality))
    if normal: return [plane(0, Q_SHADE), plane(1, Q_NORMAL), plane(2, Q_SHADE), plane(3, Q_NORMAL)]
    colour = (7, P_JPEG, jpeg(img[..., :3], Q_COLOUR))
    if cutout: return [colour, (8, P_BITS, np.packbits(img[::-1, :, 3] > 0.5).tobytes())]   # top row first, as a JPEG has its rows
    return [colour, plane(3, Q_SHADE)]

# ——— write ———

def planes(data, stride):
    """Records of stride bytes as stride runs of one byte each (every record's first byte, then every second...)."""
    return np.ascontiguousarray(np.frombuffer(data, np.uint8).reshape(-1, stride).T).tobytes()

buf = io.BytesIO(); w = buf.write
w(MAGIC); w(struct.pack("<I", VERSION))
w(struct.pack("<6I", len(textures), len(materials), len(meshes), len(instances), len(lights), TEX))
texture_bytes = buf.tell()
for img, cutout, normal in textures:
    parts = texture_parts(img, cutout, normal)
    w(struct.pack("<3I", img.shape[0], (1 if cutout else 0) | (2 if normal else 0), len(parts)))
    for channels, kind, data in parts: w(struct.pack("<2I", channels | kind << 8, len(data))); w(data)
texture_bytes = buf.tell() - texture_bytes
for m in materials:
    w(struct.pack("<Ii", m['kind'], m['tex'])); w(struct.pack("<fi", m.get('light_tint', 0.0), m['normal']))
    w(struct.pack("<4f", *m['base'], m['alpha'])); w(struct.pack("<4f", *m['color2'], m['rough'])); w(struct.pack("<4f", *m['mortar'], m['metal']))
    w(struct.pack("<4f", *m['emit'], m['trans'])); w(struct.pack("<4f", *m['pattern']))
for me in meshes:
    w(struct.pack("<3I", me['vcount'], me['icount'], len(me['subs']))); w(struct.pack("<6f", *me['centre'], *me['extent']))
    for s in me['subs']: w(struct.pack("<3I", *s))
    w(planes(me['vertices'], 16))
    w(planes(np.diff(np.frombuffer(me['indices'], np.uint32).astype(np.int64), prepend=0).astype('<i4').tobytes(), 4))
w(planes(b"".join(struct.pack("<3I12f", mi, mask, flags, *[x for r in rows for x in r]) for mi, mask, flags, rows in instances.values()), 60))
for rec, mask in lights.values():
    kind, pos, aim, color, energy, cone, blend, radius, angle = rec
    w(struct.pack("<2I", kind, mask)); w(struct.pack("<3f3f3f", *pos, *aim, *color)); w(struct.pack("<5f", energy, cone, blend, radius, angle))
for bit in (1, 2):
    pos, target, fov = cameras.get(bit, cameras.get(1))
    w(struct.pack("<7f", *pos, *target, fov))
sky = backdrop()
w(struct.pack("<2I2f", *((SKY_W, SKY_H, 0.0, SKY_TAN) if sky is not None else (0, 0, 0.0, 0.0))))
if sky is not None:
    # The panorama has a sunset painted into it, and the .blend's sun lamp shines from another side: in Cycles' renders the two are
    # never in one picture, on the walk they are. The picture is turned round the horizon until its glow is where the light comes from.
    lum = sky[..., :3].sum(axis=2).sum(axis=0); col = int(np.argmax(np.convolve(np.tile(lum, 3), np.ones(SKY_W // 16), 'same')[SKY_W:2 * SKY_W]))
    sun = next((rec for rec, mask in lights.values() if rec[0] == 0 and mask & 1), None)
    glow = (col + 0.5) / SKY_W * 2 * math.pi - math.pi; at = math.atan2(-sun[2][0], -sun[2][2]) if sun else glow
    sky = np.roll(sky, int(round((at - glow) / (2 * math.pi) * SKY_W)), axis=1)
    w(bc3(sky))
    print("BACKDROP its glow was", round(math.degrees(glow), 1), "degrees round from +z; turned to the raster sun at", round(math.degrees(at), 1), "height", round(math.degrees(math.asin(-sun[2][1])), 1) if sun else None)
feat = features(); w(struct.pack("<7f", *feat)); print("FEATURES water level, fountain nozzle (x, y, z), bowl radius and level, rim radius", tuple(round(x, 3) for x in feat))
raw = buf.getvalue()
with open(OUT, "wb") as f: f.write(gzip.compress(raw, 9, mtime=0))
tris = sum(m['icount'] for m in meshes) // 3
per_frame = sum(meshes[mi]['icount'] // 3 for mi, mask, flags, rows in instances.values() if mask & 1)
for t, c, name in sorted(((m['icount'] // 3, counts.get(pending[i][0], 1), pending[i][4]) for i, m in enumerate(meshes) if m), key=lambda x: -x[0] * x[1])[:45]: print("MESH", t, "x", c, "=", t * c, name)
result = {"raster_tris_per_frame": per_frame, "file": OUT, "raw_mb": round(len(raw) / 1e6, 2), "textures_mb": round(texture_bytes / 1e6, 2), "gz_mb": round(os.path.getsize(OUT) / 1e6, 2), "meshes": len(meshes), "unique_tris": tris, "instances": len(instances),
          "materials": len(materials), "textures": len(textures), "big_textures": sum(1 for t in textures if t[0].shape[0] == TEX), "normal_maps": sum(1 for t in textures if t[2]), "lights": len(lights)}
print(result)
