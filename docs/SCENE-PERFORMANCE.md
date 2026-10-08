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
| level of detail (cheaper meshes far away) | none | **none: the largest remaining gain** |
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
