# Paints the two atlases the hall's books and the garden's butterflies are drawn with (no source pictures: all of it is made here, from noise and shapes).
#   python tools/scene/make_v24_textures.py [outdir]     (default: ../Mazesta-Art/assets/v24; the pictures are not committed, the exported scene holds them)
# old-books.png    1024 x 1024: ten old bindings in 5 x 2 cells of 204 x 512. In a cell: the spine (x 0-135, the whole height), a cover swatch (x 140-203, upper half)
#                  and a swatch of page edges (x 140-203, lower half). Rows are written top to bottom, as PIL does.
# butterflies.png  512 x 512, RGBA: four right wings (orange, white, blue, yellow) in 2 x 2 cells of 256, each drawn on GardenGpu.Wing's outline (alpha is its shape).
import numpy as np, sys, os
from PIL import Image, ImageDraw, ImageFilter
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Mazesta-Art", "assets", "v24")
os.makedirs(OUT, exist_ok=True); rng = np.random.default_rng(24)

def noise(h, w, scales=((4, .5), (16, .3), (64, .2))):
    """Smooth value noise in 0..1: random grids of coarser and finer cells, stretched to size and summed."""
    out = np.zeros((h, w), np.float32)
    for cells, wt in scales:
        g = rng.random((max(2, h // cells + 2), max(2, w // cells + 2))).astype(np.float32)
        out += wt * np.asarray(Image.fromarray(g).resize((w, h), Image.BICUBIC))
    return np.clip(out / sum(s for _, s in scales), 0, 1)

def leather(h, w, base, grain=0.10, wear=0.18):
    n = noise(h, w, ((2, .5), (6, .3), (24, .2))); big = noise(h, w, ((40, 1.0),))
    img = np.empty((h, w, 3), np.float32)
    for c in range(3): img[..., c] = base[c] * (1 + grain * (n - .5) * 2) * (1 + wear * (big - .5) * 2)
    return img

def gold(img, mask, strength=1.0):
    n = noise(*mask.shape, ((2, .6), (8, .4)))[..., None]; col = np.array([205, 160, 62], np.float32) * (0.75 + 0.5 * n)
    m = (mask * strength)[..., None]; return img * (1 - m) + col * m

def spine(base, panel, trim, bands, cloth=False, worn=1.0):
    W, H = 136, 512; img = leather(H, W, base, grain=0.06 if cloth else 0.12)
    if cloth:   # cloth: a fine weave over the colour
        yy, xx = np.mgrid[0:H, 0:W]; img *= (1 + 0.05 * (np.sin(xx * 1.9) * np.sin(yy * 1.9)))[..., None]
    d = Image.new("L", (W, H), 0); dr = ImageDraw.Draw(d)
    for y in bands:   # raised bands: gold lines each side, a lit ridge between
        yy = int(y * H); dr.rectangle([4, yy - 9, W - 5, yy - 7], fill=255); dr.rectangle([4, yy + 7, W - 5, yy + 9], fill=255)
        img[yy - 6:yy + 6] *= np.linspace(1.12, 0.9, 12)[:, None, None]
    # a label panel between the 2nd and 3rd band, with the title as strokes
    y0, y1 = int(bands[1] * H) + 14, int(bands[2] * H) - 14
    img[y0:y1, 12:W - 12] = np.array(panel, np.float32) * (0.9 + 0.2 * noise(y1 - y0, W - 24, ((3, .6), (12, .4))))[..., None]
    dr.rectangle([14, y0 + 2, W - 15, y0 + 4], fill=255); dr.rectangle([14, y1 - 4, W - 15, y1 - 2], fill=255)
    ly = y0 + 16
    while ly < y1 - 18:
        x = 22
        while x < W - 30:
            seg = int(rng.integers(6, 22)); dr.rectangle([x, ly, min(x + seg, W - 24), ly + 3], fill=255); x += seg + int(rng.integers(5, 9))
        ly += 14
    for a, b in zip(bands[:-1], bands[1:]):   # fleurons in the other compartments
        cy = int((a + b) / 2 * H)
        if abs(cy - (y0 + y1) / 2) < 40: continue
        dr.ellipse([W // 2 - 9, cy - 9, W // 2 + 9, cy + 9], outline=255, width=3); dr.ellipse([W // 2 - 3, cy - 3, W // 2 + 3, cy + 3], fill=255)
    mask = np.asarray(d.filter(ImageFilter.GaussianBlur(0.7)), np.float32) / 255
    img = gold(img, mask, trim)
    for x, s in ((0, 1), (W - 1, -1)):   # joints: dark grooves at both hinges
        for k in range(7): img[:, x + s * k] *= 0.55 + 0.065 * k
    for rows in (slice(0, 22), slice(H - 22, H)):   # head and tail caps rubbed and paler
        img[rows] *= 0.82; img[rows] += 12 * worn * noise(img[rows].shape[0], W, ((3, 1),))[..., None]
    scuff = noise(H, W, ((3, .5), (9, .5))); img += (np.clip(scuff - 0.62, 0, 1) * 150 * worn)[..., None]   # rubbed, pale patches
    img *= (1 - 0.12 * noise(H, W, ((50, 1),)))[..., None]
    return np.clip(img, 0, 255)

def cover(base, border, cloth):
    W, H = 130, 256; img = leather(H, W, base, grain=0.07 if cloth else 0.12)
    d = Image.new("L", (W, H), 0); dr = ImageDraw.Draw(d)
    dr.rectangle([9, 9, W - 10, H - 10], outline=255, width=2); dr.rectangle([15, 15, W - 16, H - 16], outline=255, width=1)
    for cx, cy in ((9, 9), (W - 10, 9), (9, H - 10), (W - 10, H - 10)): dr.ellipse([cx - 5, cy - 5, cx + 5, cy + 5], fill=255)
    img = gold(img, np.asarray(d, np.float32) / 255 * 0.8, border)
    for k in range(6): img[:, k] *= 0.6 + 0.07 * k   # the hinge
    img += (np.clip(noise(H, W, ((3, .5), (9, .5))) - 0.65, 0, 1) * 130)[..., None]
    return np.asarray(Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).resize((64, 256), Image.LANCZOS), np.float32)   # drawn at the cover's own shape, squeezed into its swatch

def pages():
    W, H = 64, 256; n = noise(H, W, ((8, .5), (30, .5))); img = np.empty((H, W, 3), np.float32)
    base = np.array([226, 211, 170], np.float32)
    for c in range(3): img[..., c] = base[c] * (0.86 + 0.18 * n)
    for y in range(0, H, 2): img[y] *= 0.93
    return np.clip(img, 0, 255)

# spine base, label panel, gold strength, bands, cloth
BOOKS = [
    ((104, 36, 34),  (28, 22, 20),    1.0, (.10, .22, .50, .78, .90), False),   # oxblood leather
    ((52, 86, 62),   (150, 36, 32),   0.9, (.08, .24, .52, .80, .92), True),    # faded green cloth
    ((92, 56, 30),   (36, 24, 16),    0.7, (.10, .26, .52, .76, .90), False),   # brown calf
    ((36, 52, 88),   (176, 160, 118), 0.9, (.09, .21, .48, .80, .91), True),    # navy cloth
    ((196, 178, 134), (140, 38, 34),  0.6, (.10, .24, .50, .78, .90), False),   # vellum
    ((30, 28, 30),   (96, 24, 28),    1.0, (.08, .22, .50, .80, .92), False),   # black leather
    ((146, 66, 40),  (34, 30, 28),    0.8, (.10, .22, .48, .76, .90), True),    # rust cloth
    ((86, 88, 44),   (54, 34, 22),    1.0, (.09, .25, .52, .78, .91), False),   # olive leather
    ((86, 24, 40),   (190, 168, 112), 0.9, (.10, .24, .50, .80, .90), False),   # maroon
    ((96, 112, 128), (222, 210, 176), 0.5, (.08, .20, .48, .78, .92), True),    # grey-blue cloth
]
atlas = np.zeros((1024, 1024, 3), np.float32); atlas[:] = (120, 100, 70)
for k, (base, panel, trim, bands, cloth) in enumerate(BOOKS):
    cx, cy = (k % 5) * 204, (k // 5) * 512
    atlas[cy:cy + 512, cx:cx + 136] = spine(base, panel, trim, bands, cloth)
    atlas[cy:cy + 256, cx + 140:cx + 204] = cover(tuple(c * 0.92 for c in base), trim, cloth)
    atlas[cy + 256:cy + 512, cx + 140:cx + 204] = pages()
Image.fromarray(np.clip(atlas, 0, 255).astype(np.uint8)).save(os.path.join(OUT, "old-books.png"))

# ——— butterflies: the wing's outline is GardenGpu.Wing's (x 0-0.05 out from the body, y -0.034..0.04 along it)
OUTLINE = [(0, 0), (0, .022), (.03, .04), (.05, .03), (.046, .008), (.03, -.004), (.036, -.026), (.018, -.034), (0, -.02)]
def to_px(p, S): return ((p[0] / 0.05) * S, (1 - (p[1] + 0.034) / 0.074) * S)   # u to the right, v up -> rows top to bottom
def wing(style, S=256):
    big = S * 4; m = Image.new("L", (big, big), 0); ImageDraw.Draw(m).polygon([to_px(p, big) for p in OUTLINE], fill=255)
    m = m.filter(ImageFilter.MinFilter(9)).filter(ImageFilter.GaussianBlur(5)).resize((S, S), Image.LANCZOS)   # the corners rounded a little, inside the mesh's own edge
    mask = np.asarray(m, np.float32) / 255
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32); root = np.array(to_px((0, 0), S)); dist = np.hypot(xx - root[0], yy - root[1]) / S; ang = np.arctan2(yy - root[1], xx - root[0])
    inside = np.asarray(m.filter(ImageFilter.GaussianBlur(S * .05)), np.float32) / 255   # 1 deep inside, falling to 0 at the rim
    n = noise(S, S, ((6, .5), (24, .5)))
    veins = np.clip(1 - np.abs(np.sin(ang * 14)) * (3 + 10 * dist), 0, 1) * (dist > .06)
    body, rim, spot = [((232, 112, 20), (24, 20, 18), (245, 240, 225)), ((244, 242, 232), (40, 38, 40), (60, 58, 60)),
                       ((42, 98, 214), (14, 18, 40), (170, 200, 250)), ((240, 206, 40), (96, 60, 20), (226, 120, 30))][style]
    img = np.zeros((S, S, 3), np.float32)
    for c in range(3): img[..., c] = body[c] * (0.88 + 0.2 * n) * (1 - 0.3 * np.clip(dist * 1.6 - .3, 0, 1) * (style == 2))
    t = np.clip((0.92 - inside) / 0.45, 0, 1)[..., None] * np.clip(dist * 6, 0, 1)[..., None]   # 0 inside .. 1 at the rim
    img = img * (1 - t) + np.array(rim, np.float32) * t
    if style in (0, 3):   # dark veins
        v = (0.75 * veins)[..., None]; img = img * (1 - v) + np.array(rim, np.float32) * v
    d = Image.new("L", (S, S), 0); dr = ImageDraw.Draw(d)
    for k in range(7 if style != 1 else 2):
        a = 0.2 + k * 0.17 if style != 1 else 0.55 + k * 0.3; r = (0.74 if style != 1 else 0.5 + 0.14 * k) * S
        x, y = root[0] + np.cos(a) * r * 0.7, root[1] + np.sin(a) * r * 0.7; rr = S * (0.022 if style != 1 else 0.05); dr.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)
    sp = np.asarray(d.filter(ImageFilter.GaussianBlur(1.2)), np.float32)[..., None] / 255
    img = img * (1 - sp) + np.array(spot, np.float32) * sp
    return np.concatenate([np.clip(img, 0, 255), (mask * 255)[..., None]], axis=2)
bf = np.zeros((512, 512, 4), np.float32)
for k in range(4): bf[(k // 2) * 256:(k // 2) * 256 + 256, (k % 2) * 256:(k % 2) * 256 + 256] = wing(k)
Image.fromarray(bf.astype(np.uint8), "RGBA").save(os.path.join(OUT, "butterflies.png")); print("written to", os.path.abspath(OUT))
