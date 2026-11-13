# Packs export_original.py's output into the one file the temperature test embeds: python pack_gate.py <export dir> <out gate.mzgate> [picture size, default 4096: the 2048 one is only 1.2 MB smaller, the geometry is the weight]
# gzip of "MZG2", then little-endian: vertex count, index count, picture size, colour JPEG length (uint32 each), the bounding box (min xyz, extent xyz: float32),
# then, plane by plane over all vertices (each plane delta-coded, so gzip finds the runs): x, y, z as uint16 across the box (0.03 mm on the 1.8 m gate),
# the normal as two int16 (octahedral), u and v as uint16 (0..1); then the indices as zigzag varint deltas from the one before; then the colour JPEG (4:4:4).
# Nothing is dropped: every triangle and vertex of the scan stays, only its numbers are stored in fewer bytes.
import sys, gzip, io, struct, numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]; side = int(sys.argv[3]) if len(sys.argv) > 3 else 4096
g = np.load(f"{src}/gate.npz"); v = g["verts"]; ix = g["indices"].astype(np.int64); nv = len(v)
assert ix.max() < nv
lo = v[:, :3].min(0); ext = v[:, :3].max(0) - lo
q = np.round((v[:, :3] - lo) / ext * 65535).astype(np.uint16)
n = v[:, 3:6] / np.linalg.norm(v[:, 3:6], axis=1, keepdims=True).clip(1e-9)   # octahedral: fold the lower hemisphere over the upper
p = n[:, :2] / np.abs(n).sum(1, keepdims=True); neg = n[:, 2] < 0
fold = (1 - np.abs(p[:, ::-1])) * np.where(p >= 0, 1, -1); p = np.where(neg[:, None], fold, p)
o = np.round(p * 32767).astype(np.int16)
uv = v[:, 6:8]; assert uv.min() >= -1e-4 and uv.max() <= 1 + 1e-4, "uv outside 0..1"
t = np.round(uv.clip(0, 1) * 65535).astype(np.uint16)
def plane(c): return np.diff(c.astype(np.int32), prepend=0).astype(c.dtype).astype(c.dtype.newbyteorder("<")).tobytes()   # modulo 2^16, so the reader's running sum comes back exact
def planes(*cols): return b"".join(plane(c) for c in cols)
body = planes(q[:, 0], q[:, 1], q[:, 2], o[:, 0], o[:, 1], t[:, 0], t[:, 1])
d = np.diff(ix, prepend=0); z = ((d << 1) ^ (d >> 63)).astype(np.uint64); out = bytearray()
for val in z.tolist():
    while val >= 0x80: out.append(val & 0x7F | 0x80); val >>= 7
    out.append(val)
im = Image.open(f"{src}/gate-albedo.png").convert("RGB"); im = im.resize((side, side), Image.LANCZOS) if im.width != side else im; b = io.BytesIO(); im.save(b, "JPEG", quality=90, subsampling=0, optimize=True); alb = b.getvalue()
with gzip.open(dst, "wb", 9) as f:
    f.write(b"MZG2" + struct.pack("<4I", nv, len(ix), im.width, len(alb)) + struct.pack("<6f", *lo, *ext)); f.write(body); f.write(bytes(out)); f.write(alb)
print("vertices", nv, "triangles", len(ix) // 3, "index bytes", len(out), "albedo", len(alb))
