# The gate as the scan has it, nothing reduced: blender -b gate.blend --python export_original.py -- <out dir>
# Same placement as bake_gate.py (1.8 tall on y = 0, front to +z) and the same files out (gate.npz, gate-albedo.png at 4096, a flat gate-normal.png
# so pack_gate.py's format needs no change), but every triangle and the scan's own unwrap kept: the detail is in the geometry, not in a baked map.
import bpy, sys, numpy as np
from mathutils import Matrix

out = sys.argv[sys.argv.index("--") + 1]
SIZE = 4096; HEIGHT = 1.8
src = bpy.data.objects["geometry_0"]
me = src.data.copy(); me.transform(src.matrix_world.copy())
v = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", v); v = v.reshape(-1, 3)
lo_, hi_ = v.min(0), v.max(0); k = HEIGHT / (hi_[2] - lo_[2]); c = (lo_ + hi_) / 2
me.transform(Matrix.Translation((-c[0] * k, -c[1] * k, -lo_[2] * k)) @ Matrix.Scale(k, 4)); me.update()

# the colour picture of the material (the node wired to Base Color), at SIZE
mat = bpy.data.materials["pbr_material"]; bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
img = bsdf.inputs["Base Color"].links[0].from_node.image; print("ALBEDO", img.name, img.size[:])
img.scale(SIZE, SIZE); img.filepath_raw = f"{out}/gate-albedo.png"; img.file_format = "PNG"; img.save()
flat = bpy.data.images.new("flat", SIZE, SIZE, alpha=False); flat.colorspace_settings.name = "Non-Color"; flat.pixels.foreach_set(np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), SIZE * SIZE))
flat.filepath_raw = f"{out}/gate-normal.png"; flat.file_format = "PNG"; flat.save()

me.calc_loop_triangles(); uv = me.uv_layers[0].data
loops = np.array([t.loops for t in me.loop_triangles], np.int64).reshape(-1)
vi = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", vi)
co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
nrm = np.empty(len(me.vertices) * 3, np.float32); me.vertex_normals.foreach_get("vector", nrm); nrm = nrm.reshape(-1, 3)
u = np.empty(len(me.loops) * 2, np.float32); uv.foreach_get("uv", u); u = u.reshape(-1, 2)
# one vertex per (point, uv): packed into one integer key so the unique is fast on millions of corners
key = vi[loops].astype(np.uint64) << np.uint64(40) | (np.round(u[loops, 0] * 8191).astype(np.uint64) << np.uint64(20)) | np.round(u[loops, 1] * 8191).astype(np.uint64)
_, first, inv = np.unique(key, return_index=True, return_inverse=True)
L = loops[first]; P = co[vi[L]]; N = nrm[vi[L]]; T = u[L]
rot = lambda a: np.stack([a[:, 0], a[:, 2], -a[:, 1]], 1)
verts = np.concatenate([rot(P), rot(N), np.stack([T[:, 0], 1 - T[:, 1]], 1)], 1).astype(np.float32)
np.savez(f"{out}/gate.npz", verts=verts, indices=inv.reshape(-1).astype(np.uint32)); print("EXPORT", len(verts), len(inv) // 3)
