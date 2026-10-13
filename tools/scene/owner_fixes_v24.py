# Run by export_garden.py before it reads the scene: touches the open .blend in memory only (it is never saved), so the owner's own file stays as he made it.
#   - the single burgundy book on the right coffee table (V15_Art_Book_*) takes the old-binding atlas of the other books (V24_Old_Books: made by prepare_courtyard_v24.py)
import bpy, mathutils
V = mathutils.Vector
mat = bpy.data.materials.get("V24_Old_Books")
if mat is not None:
    def planar(o, rect, binding=0):   # rect: pixel x0, y0 (top), w, h of the swatch in the 1024 atlas; the mesh's x and y laid across it
        pts = [o.matrix_world @ V(c) for c in o.bound_box]; lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
        x0, y0, w, h = rect; layer = o.data.uv_layers.active or o.data.uv_layers.new(name="UVMap")
        for poly in o.data.polygons:
            for li in poly.loop_indices:
                p = o.matrix_world @ o.data.vertices[o.data.loops[li].vertex_index].co
                u = (p[0] - lo[0]) / max(hi[0] - lo[0], 1e-6); v = (p[1] - lo[1]) / max(hi[1] - lo[1], 1e-6)
                layer.data[li].uv = ((x0 + 1 + u * (w - 2)) / 1024, 1 - (y0 + 1 + (1 - v) * (h - 2)) / 1024)
        o.data.materials.clear(); o.data.materials.append(mat)
    for name, rect in (("V15_Art_Book_Cover", (140, 0, 64, 256)), ("V15_Art_Book_Cover.001", (140, 0, 64, 256)), ("V15_Art_Book_Pages", (140, 256, 64, 256))):
        if name in bpy.data.objects: planar(bpy.data.objects[name], rect)
    print("OWNER FIXES: the table book is bound")
