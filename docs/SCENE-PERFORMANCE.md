# Why the 3-D scene is heavy, and what was done about it

Measured on the owner's RTX 3090 + Ryzen 9 (32 threads), Full HD, quality 3 (the default), a walk's first six seconds, GPU timestamps between the passes
(`MAZESTA_PROFILE=1 dotnet test tests/Mazesta.Diagnostics.Gpu.Tests --filter GardenProfile`). The renderer is a straightforward, correct Direct3D 12 forward
renderer; what makes it slow is not a bug but how much geometry it draws, how many times, and that most of the techniques a game uses to avoid that were not in it.

## Where a frame goes (rasterised, ms on the card)

| Pass | Before | After | What it is |
|---|---|---|---|
| main colour pass | 5.8 | 5.9 | the frame itself: sky, solids, leaves, water and glass, MSAA 4x |
| pool reflection | 3.0 | 2.9 | the whole garden drawn again from under the water, at the frame's own size |
| depth prepass (MSAA) | 2.4 | 2.4 | depth of solids and leaves, so only the nearest surface is lit |
| depth for the occlusion | 1.7 | - | the same depth again, one sample a pixel, for the ambient occlusion |
| sun shadow map | 1.1 | 1.2 | 4096 squared, drawn every second frame (2.3 ms on the frames it is drawn) |
| surroundings, hall maps, AO, lens | 1.4 | 1.4 | |
| **frame** | **15.5** | **13.9** | |

* **Resolution hardly matters.** 1280x720 took 13.8 ms, 1920x1080 15.5 ms: three quarters of the cost is vertex and triangle work, not pixels. NIS drawn at 77 %
  of the size saves 1.8 ms, not 40 %. Fewer pixels will never make this scene fast; fewer triangles will.
* **The triangles are the cost.** 9.5 M triangles are in the scene (20,524 objects), most of them leaf cards of the plants. A game would draw 1-3 M of them in a
  frame; here every pass that draws the garden draws all of it that it can see, and a frame draws it six times (shadow, hall, one face of the surroundings, the
  mirror image, the depth, the colour): 150-490 M triangles a frame by the earlier count.
* **Ray tracing:** the main pass is 30 of 38 ms. The acceleration structure is built in 0.6 ms (an earlier note said 18 ms: that was the pass's cost mistaken for
  the build's); the time is the shadow, reflection and refraction rays every pixel sends from its shader.

## What a game does that this renderer did not, and where it stands now

| Technique | Before | Now |
|---|---|---|
| instancing (one draw for all copies of a mesh) | yes | yes |
| frustum culling of what the camera cannot see | only the lamps' and surroundings' passes | the camera's passes too (v31), the mirror pass to the part of the picture the pool shows (v32); the picture is bit for bit the same (a render check) |
| one depth prepass, reused | two (one for the occlusion, one for MSAA) | one: the MSAA prepass's nearest sample is the occlusion's depth (v32, -1.7 ms) |
| level of detail (cheaper meshes far away) | none | **v40: leaf cards thinned and grown at distance** (`GardenLod`, below) |
| occlusion culling (not drawing what stands behind a wall) | none | none |
| cascaded or fitted shadow maps | one 4096 map for the whole courtyard (1.6 cm a texel over 66 m) | unchanged |
| GPU-driven rendering / mesh shaders (the card culls and picks) | no | no |
| reflection at a lower resolution, with its own prepass | full size, no prepass (every leaf layer is shaded) | unchanged |

Next, in the order of what each would give: **LOD** (the leaf-card meshes beyond a few metres replaced by decimated ones, which cuts every pass that draws
them); **a prepass for the mirror pass, or half its size**; **a shadow map fitted to what the camera can see** (cascades); **occlusion culling** against the hall's
walls. None changes what the scene is; each changes the picture a little, so each needs a version bump of the benchmark and was not done unasked.

## What was added for the processor and the memory

The processor's score was a recording time of 0.7 ms: the scene gave it almost nothing to do. The garden now has weather and a wind that moves (`GardenWeather`, `GardenWind`), each part simulated the way a game does it - nothing a game would not do, and nothing the ray tracer's structure is made to carry:

* **the air**: a grid of velocities (stable fluids, Stam's method) that the gusts force, that carries itself, that the crowns slow and that a pressure projection makes go round the hall, its
  columns, its walls and the ground (the cells `GardenVoxels` marks hard). 0.67 million cells / 29 MB at the standard level, 2.2 million / 98 MB at the high one; the sweeps are the memory's
  bandwidth as much as the cores' arithmetic, and run on a crew of threads that spin at a barrier between passes (`GardenCrew`);
* **rain** that falls round the camera, is blown by the air, stops on the hall's roof and a tree's crown and splashes on the pool and the paving;
* **leaves and twigs** lifted out of the crowns and off the ground by the gusts, carried and tumbled, tested against the voxels at four points each (a leaf's tip, stalk and edges), turned by
  the corner that strikes, and colliding with one another through a spatial hash, so they settle on the paving, against walls and on the water, and heap up;
* every body is stepped (drag toward the air, gravity, tumbling) and written as the rows of a matrix for the card (the renderers' movers); the bodies are independent, so the frame's step is cut
  into chunks and run on every core, as a game's job system does.

What the processor does per frame is in the CPU score and in its own result line (`Bench_Scene_SimFrame`): at the standard level about 6.8 ms of the frame's 8.8 on a 32-thread Ryzen 9
(the air 4.3, the bodies 0.6, their collisions 0.3); at the high level (three times the bodies, the air at 2.2 million cells and 16 sweeps) about 29 ms.

The **weather option** has three levels: off; on (14,000 drops, 3,600 leaves, 700 twigs and 1,600 splashes, the air 0.45 m cells, 10 sweeps, against the 29 MB grid of solids); high (three
times the bodies, 0.3 m cells and 16 sweeps, against a 233 MB grid). At the high level the working set is far larger than any processor's cache and the reads are random: the speed is the memory's
latency and bandwidth as much as the processor's. The RAM *score* itself is still its own probe (Triad and latency): a scene cannot measure the memory's speed, only be limited by it; the
simulation's time per frame says how much the machine's memory and cores matter to a game of this kind.

A frame shown after another (a live frame) steps the air and the bodies one step of its own length (so the work per frame does not depend on how fast the card is); a frame on its own
(the check frames) uses the plain wind of the fronts, steps each body from the start of its cycle at a fixed 1/60 s, so the same time
is the same picture whatever was drawn before - and the card's check (the same bits before and after the run) still holds. Rain, splashes, leaves and twigs are movers (`WeatherFlag`): they are
not in the ray tracer's top-level structure, so a ray-traced frame does not reflect or shadow them.

## NVIDIA Image Scaling (removed)

NIS was tried as the frame's last step and removed (2026-10-08): its sharpener lifted the ray-traced reflections' and the leaves' grain as much as the detail. The frame is the lens's picture.

## Why fast cards came out close, and what v38 changed (2026-10-11)

An RTX 5090 has 2.9x an RTX 3090's shader arithmetic (105 vs 36 TFLOPS), 1.9x its memory bandwidth and about 2.2x its triangle
setup (11 raster engines at ~2.4 GHz against 7 at ~1.7). Reviews measure it about 2.1x faster at 4K. The garden at Full HD gave
it much less, because the frame was mostly triangles: 9.47 M of them in the Direct3D scene, 41 % smaller than 1 cm² (measured from
`garden.mzscene`), most of them leaf cards, drawn up to six times a frame. Triangle setup scales least between generations, and a
triangle a pixel or smaller shades its whole 2x2 quad, so the card's arithmetic is not what sets the time.

3DMark's tests are built the other way round (the 3DMark Technical Guide): Time Spy at 2560x1440 with 13-14 M triangles but 50-80 M
pixel-shader and ~30 M compute invocations a frame; Time Spy Extreme keeps the geometry and raises the pixel and compute work 2-4x at
4K; Steel Nomad (4K, volumetric sky, fog and lights) separates the 5090 from the 4090 most (+53 %, against +34 % in Time Spy). The
work that grows with a card's power is per pixel: volumetric light, many lights, ambient occlusion, reflections, ray marching.

v38:
- **2560x1440 by default** (as Time Spy).
- **The courtyard's air** (`Air` in GardenRaster.hlsl): every pixel marches its eye ray through the courtyard's shadow map, 0 / 12 /
  24 / 48 steps at quality 1-4, so the sun stands in the haze as beams; the hall's shafts take as many steps. Per-pixel work, as in a
  game's volumetric light.
- **Rough surfaces keep their energy**: the light GGX loses between microfacets is put back (Fdez-Agüera, JCGT 2019).

v40 (measured on an RTX 5070, 1080p, quality 3, the walk's first six seconds; `MAZESTA_PROFILE=1 ... GardenProfile`, which now also prints draw calls and
triangles a pass):
- **Levels of detail for plants** (`GardenLod`, made when the scene loads, no Blender needed): a mesh's cut-out leaf cards are cut into their loose pieces; a level
  keeps a half, a quarter or an eighth of the small ones (picked by a hash, so each level holds the one before) and grows each kept one about its centre by the square root
  of what was dropped, so the foliage keeps its coverage. A plant's level is **its own and never follows the camera** (a level chosen by the eye's distance was seen to change
  leaves as the camera moved, even with steps at 12, 25 and 45 m): within the walls and within 12 m of the walk it is whole; a plant farther from the walk is one level coarser at
  12, 24 and 48 m; one past the courtyard's walls is always thinned (a level at the wall, one more 6 m and 16 m beyond). Only the pictures that are blurred over also follow
  their eye: the pool's mirror image (three levels coarser still, beyond 12 m of the mirrored eye) and the pictures of the surroundings (one). Stonework, trunks and branches are not thinned. The picture differs from the whole meshes' at 2.4/255 on average where trees fill the frame
  (nothing in the sky or the architecture).
- **Big solid meshes in runs** (`GardenChunks`): a single instance of 20,000 triangles or more (the balustrade, carved walls, the hall's shell) has its
  triangles put in Morton order and cut into runs of 2,048, each with a ball; a camera draws only the runs it sees (the hall: -40 % of the frame's triangles).
- Instances inside a mesh are kept in Morton order, and what a view draws is worked out once a frame per view (not once per pass), in fewer, longer runs.
- Result with levels following the eye (dropped): frame (GPU, no weather) 13.3 -> 11.0 ms outdoors and 7.3 -> 6.9 ms in the hall; triangles a frame (all passes) 34 M -> 28 M outdoors;
  the hall's shadow map 6.6 -> 4.6 M triangles. Not thinned: the small solid plants (the potted flowers, 4 M triangles a frame): they are 180-230 triangles each
  and have no loose pieces to thin; they would need whole instances dropped in blocks (a share of each block, the rest grown).
- With the levels fixed to the plant, as it is now: 13.3 -> about 13.7 ms outdoors (the walk comes within 12 m of nearly every plant, so only about 35 stand thinned, the trees beyond the walls; the gain is the mirror's and the big meshes' runs). Stability was chosen over the 2.5 ms.
- The pool's mirror image (2.5 ms, a fifth of the frame) is pixel work (every leaf layer shaded, no prepass), not triangles: fewer triangles hardly changed it.

Still to do, in the order of what each gives the scaling:
- **Level of detail for the leaf cards** (above): the triangles are still the frame's largest cost. Decimated meshes for the plants
  beyond a few metres would have to come from Blender (`export_garden.py` already simplifies; a second, coarser mesh per plant and a
  distance to switch at).
- **Materials**: the shading is already metal-roughness PBR (GGX, Smith, Schlick, split-sum, normal maps with AO and metalness), but
  94 of 121 materials have no texture at all and the rest are 512 or 1024 texels as JPEG. Real-looking surfaces need texture sets of
  2048 (base colour, normal, roughness/AO/metal) on the stone, plaster, wood and tiles - from the .blend's own or CC0 sets (ambientCG,
  Poly Haven) - exported at `TEX = 2048`. That costs scene-file size (the app embeds it) and video memory (the texture array is one
  size: about 0.5 GB at 2048).
- A temporal pass (TAA) in place of MSAA 4x, which would also smooth the air's grain.

## Reference run: RTX 5070 (2026-10-12, commit 483244a, benchmark v40)

Kept so the same build can be compared on another card (RTX 3090 next).

- Profile (`MAZESTA_PROFILE=1`, 1080p, heavy quality, raster): about 13.7-14.0 ms GPU per frame (13.3 before LOD, 11.2 with the popping distance-following LOD).
- Real-app `--selftest=scene`, factory mode: 2902 MHz, 1.090 V, 198 W, 68 C (hot 79 C), **71.8 FPS**, clean=True (90.0 with popping LOD, 75.6 before any LOD).
- Real-app `--selftest=scenert` (ray tracing): 2932 MHz, 134 W, 60 C (hot 68 C), **40.9 FPS**, clean=True.
- Driver 32.0.16.1062.

## Same build on an RTX 3090 (2026-10-12, commit 0dd9294, driver 32.0.16.1062)

| | RTX 5070 | RTX 3090 |
|---|---|---|
| Profile, raster, 1080p heavy (GPU ms) | 13.7-14.0 | **11.3** (pool reflection 2.1, depth prepass 1.8, main pass 4.7) |
| `--selftest=scene` FPS | 71.8 (2902 MHz, 198 W) | **87.4** (1785 MHz, 349 W = power limit, hot 88 C) |
| Profile, ray tracing (GPU ms) | - | 28.7 (main pass 22.6 = 79 %) |
| `--selftest=scenert` FPS | **40.9** (134 W) | 28.3 (1920 MHz, 273 W) |

Both clean=True. Raster favours the 3090 (more SMs/ROPs, 936 GB/s vs 672 GB/s); ray tracing favours the 5070 (newer RT cores). No code difference between the runs: the gap is the hardware.
